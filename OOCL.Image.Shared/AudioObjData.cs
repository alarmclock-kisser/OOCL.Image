using OOCL.Image.Core;
using System.IO.Compression;
using System.Runtime.CompilerServices;

namespace OOCL.Image.Shared
{
	public class AudioObjData
	{
		public Guid Id { get; set; } = Guid.Empty;

		public string Length { get; set; } = "0";
		public double SizeInMb { get; set; }
		public int OriginalBits { get; set; } = 0;
		public int? CompresionBits { get; set; } = null;
		public bool? UsedMusLaw { get; set; } = null;

		public int ChunkSize { get; set; } = 0;
		public float Overlap { get; set; } = 0.0f;

		public byte[] Samples { get; set; } = [];
		public IEnumerable<byte[]> Chunks { get; set; } = [];
		

		public AudioObjData()
		{
			// Empty ctor
		}

		public AudioObjData(AudioObj? obj, int compressionBits = 0, bool musLaw = true)
		{
			if (obj == null)
			{
				return;
			}

			this.Id = obj.Id;
			this.ChunkSize = obj.ChunkSize;
			this.Overlap = obj.OverlapSize / (obj.ChunkSize > 0 ? obj.ChunkSize : 1);
			this.OriginalBits = obj.BitDepth;
			this.Samples = obj.GetBytes();
			this.SizeInMb = obj.SizeInMb;
			this.Length = this.Chunks.Any() ? $"[{this.Chunks.Count()}][{this.Chunks.FirstOrDefault()?.LongLength}]" : $"{this.Samples.LongLength}";

			if (compressionBits > 0)
			{
				this.CompressSamplesAsync(compressionBits, musLaw).GetAwaiter().GetResult();
			}
		}


		public static async Task<AudioObjData> FromObjectWithDataAsync(AudioObj? obj, bool keepData = true, int compressionBits = 0, bool musLaw = true)
		{
			if (obj == null)
			{
				return new AudioObjData();
			}

			var data = new AudioObjData(obj)
			{
				Samples = await obj.GetBytesAsync(),
				Chunks = [],
				OriginalBits = obj.BitDepth
			};

			if (compressionBits > 0)
			{
				await data.CompressSamplesAsync(compressionBits, musLaw);
			}

			if (!keepData)
			{
				obj.SetData([]);
			}

			return data;
		}

		public static async Task<AudioObjData> FromObjectWithChunksAsync(AudioObj? obj, int chunkSize = 2048, float overlap = 0.5f, bool keepData = true)
		{
			if (obj == null)
			{
				return new AudioObjData();
			}

			var data = new AudioObjData(obj)
			{
				Samples = [],
				Chunks = await obj.GetChunksBytes(chunkSize, overlap),
				ChunkSize = chunkSize,
				Overlap = overlap,
				OriginalBits = obj.BitDepth
			};

			return data;
		}

		private async Task CompressSamplesAsync(int bits, bool musLaw = true)
		{
			// Async + parallel compress bytes this.Samples in bits and set
			this.Samples = await this.GetBytesGzip(bits, musLaw);
			this.CompresionBits = bits;
			this.UsedMusLaw = musLaw;

			this.SizeInMb = Math.Round((double)this.Samples.Length / (1024 * 1024), 2);

		}

		public async Task DecompressAsync()
		{
			// Ziel: Aus `this.Samples` (ggf. gzipped / mu-law / quantisiert) die
			// rohe PCM-Bytefolge in der ursprünglichen Bit-Tiefe (`OriginalBits`)
			// rekonstruieren und in `this.Samples` zurückschreiben.
			if (this.Samples == null || this.Samples.Length == 0)
			{
				return;
			}

			// 1) Falls gzipped: entpacken
			byte[] data = this.Samples;
			if (data.Length >= 2 && data[0] == 0x1f && data[1] == 0x8b)
			{
				data = await DecompressGzipAsync(data).ConfigureAwait(false);
			}

			// 2) Falls mu-law verwendet wurde: dekodieren (input ist 8-bit µ-law)
			bool musLaw = this.UsedMusLaw.HasValue ? this.UsedMusLaw.Value : false;

			// 3) Falls die komprimierte Bit-Tiefe angegeben ist, konvertiere von compresionBits -> OriginalBits.
			int srcBits = this.CompresionBits ?? this.OriginalBits;
			int dstBits = this.OriginalBits > 0 ? this.OriginalBits : 32;

			// Wenn mu-law, dann die Quelle implizit 8-bit µ-law
			if (musLaw)
			{
				srcBits = 8;
			}

			// Wenn bereits in gewünschter Form, nichts weiter tun
			if (!musLaw && srcBits == dstBits)
			{
				this.Samples = data;
				this.CompresionBits = null;
				this.UsedMusLaw = null;
				this.SizeInMb = Math.Round((double) this.Samples.Length / (1024 * 1024), 2);
				return;
			}

			// Konvertiere via Float-Intermediär (−1..+1)
			int sampleCount = (srcBits == 8) ? data.Length :
							  (srcBits == 16) ? data.Length / 2 :
							  (srcBits == 24) ? data.Length / 3 :
							  (srcBits == 32) ? data.Length / 4 : 0;

			if (sampleCount <= 0)
			{
				// ungültige Eingabe
				this.Samples = Array.Empty<byte>();
				this.CompresionBits = null;
				this.UsedMusLaw = null;
				this.SizeInMb = 0;
				return;
			}

			// Lese alle Samples als Float
			float[] floats = new float[sampleCount];
			if (musLaw)
			{
				for (int i = 0; i < sampleCount; i++)
				{
					byte u = data[i];
					short pcm = MuLawToLinearSample(u);
					floats[i] = pcm / (float) short.MaxValue;
				}
			}
			else
			{
				int idx = 0;
				switch (srcBits)
				{
					case 8:
						for (int i = 0; i < data.Length; i++)
						{
							// historisches Mapping: byte := (byte)(sample*127)
							sbyte sb = unchecked((sbyte) data[i]);
							floats[i] = sb / 127f;
						}
						break;
					case 16:
						for (int i = 0; i + 1 < data.Length; i += 2)
						{
							short s = BitConverter.ToInt16(data, i);
							floats[idx++] = s / (float) short.MaxValue;
						}
						break;
					case 24:
						for (int i = 0; i + 2 < data.Length; i += 3)
						{
							int v = data[i] | (data[i + 1] << 8) | (data[i + 2] << 16);
							if ((v & 0x800000) != 0)
							{
								v |= unchecked((int) 0xFF000000);
							}

							floats[idx++] = v / 8388607f;
						}
						break;
					case 32:
						for (int i = 0; i + 3 < data.Length; i += 4)
						{
							float f = BitConverter.ToSingle(data, i);
							floats[idx++] = Math.Clamp(f, -1f, 1f);
						}
						break;
					default:
						// fallback: versuche 32-bit float
						for (int i = 0; i + 3 < data.Length; i += 4)
						{
							float f = BitConverter.ToSingle(data, i);
							floats[idx++] = Math.Clamp(f, -1f, 1f);
						}
						break;
				}
			}

			// 4) Schreibe floats zurück in die gewünschte Ziel-Bit-Tiefe (OriginalBits)
			using var outMs = new MemoryStream();
			switch (dstBits)
			{
				case 8:
					for (int i = 0; i < floats.Length; i++)
					{
						// mappe −1..+1 -> signed 8-bit (-127..127) wie bisher
						int v = (int) Math.Round(Math.Clamp(floats[i], -1f, 1f) * 127f);
						outMs.WriteByte((byte) (v & 0xFF));
					}
					break;

				case 16:
					for (int i = 0; i < floats.Length; i++)
					{
						short s16 = (short) Math.Round(Math.Clamp(floats[i], -1f, 1f) * short.MaxValue);
						byte[] buf16 = new byte[2];
						BitConverter.TryWriteBytes(buf16, s16);
						outMs.Write(buf16);
					}
					break;

				case 24:
					for (int i = 0; i < floats.Length; i++)
					{
						int s24 = (int) Math.Round(Math.Clamp(floats[i], -1f, 1f) * 8388607f); // 2^23-1
						outMs.WriteByte((byte) (s24 & 0xFF));
						outMs.WriteByte((byte) ((s24 >> 8) & 0xFF));
						outMs.WriteByte((byte) ((s24 >> 16) & 0xFF));
					}
					break;

				case 32:
					// 32-bit float PCM
					for (int i = 0; i < floats.Length; i++)
					{
						byte[] buf32 = new byte[4];
						BitConverter.TryWriteBytes(buf32, floats[i]);
						outMs.Write(buf32);
					}
					break;

				default:
					// Falls unbekannt: nehme 32-bit float als Default
					for (int i = 0; i < floats.Length; i++)
					{
						byte[] bufD = new byte[4];
						BitConverter.TryWriteBytes(bufD, floats[i]);
						outMs.Write(bufD);
					}
					break;
			}

			this.Samples = outMs.ToArray();
			this.CompresionBits = null;
			this.UsedMusLaw = null;
			this.SizeInMb = Math.Round((double) this.Samples.Length / (1024 * 1024), 2);
		}

		/// <summary>
		/// Dekomprimiert ein gzipped Byte-Array (async).
		/// </summary>
		private static async Task<byte[]> DecompressGzipAsync(byte[] gzipped)
		{
			if (gzipped == null || gzipped.Length == 0)
			{
				return Array.Empty<byte>();
			}

			try
			{
				using var inMs = new MemoryStream(gzipped);
				using var gz = new GZipStream(inMs, CompressionMode.Decompress);
				using var outMs = new MemoryStream();
				await gz.CopyToAsync(outMs).ConfigureAwait(false);
				return outMs.ToArray();
			}
			catch
			{
				return Array.Empty<byte>();
			}
		}

		/// <summary>
		/// µ-law Decoding (8-bit µ-law -> 16-bit PCM)
		/// </summary>
		private static short MuLawToLinearSample(byte mulaw)
		{
			// Standard µ-law-Umkehrung (ITU G.711)
			mulaw = (byte) ~mulaw;
			int sign = (mulaw & 0x80);
			int exponent = (mulaw & 0x70) >> 4;
			int mantissa = mulaw & 0x0F;
			int sample = ((mantissa << 3) + 0x84) << exponent;
			if (sign != 0)
			{
				sample = -sample;
			}

			return (short) sample;
		}

		private async Task<byte[]> GetBytesGzip(int? compromiseBits = null, bool useMuLaw = false, CompressionLevel level = CompressionLevel.Fastest)
		{
			var raw = this.Samples;
			if (raw == null || raw.Length == 0)
			{
				return [];
			}

			using var ms = new MemoryStream();
			using (var gz = new GZipStream(ms, level, leaveOpen: true))
			{
				await gz.WriteAsync(raw, 0, raw.Length);
			}

			return ms.ToArray();
		}

		// µ-law helper (konvertiert 16-bit PCM -> 8-bit µ-law)
		private static byte LinearToMuLawSample(short pcmSample)
		{
			const int BIAS = 0x84;      // 132
			const int CLIP = 32635;
			int pcm = pcmSample;

			int mask;
			if (pcm < 0)
			{
				pcm = -pcm;
				mask = 0x7F;
			}
			else
			{
				mask = 0xFF;
			}

			pcm += BIAS;
			if (pcm > CLIP)
			{
				pcm = CLIP;
			}

			// Segments
			int[] seg_end = { 0xFF, 0x1FF, 0x3FF, 0x7FF, 0xFFF, 0x1FFF, 0x3FFF, 0x7FFF };
			int seg = 0;
			while (seg < 8 && pcm > seg_end[seg])
			{
				seg++;
			}

			int aval = seg << 4 | ((pcm >> (seg + 3)) & 0xF);
			byte uval = (byte) (aval ^ mask);
			return uval;
		}
	}
}
