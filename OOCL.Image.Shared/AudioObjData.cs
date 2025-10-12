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
				Chunks = []
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
				Overlap = overlap
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

		private async Task<byte[]> GetBytesGzip(int? compromiseBits = null, bool useMuLaw = false, CompressionLevel level = CompressionLevel.Fastest)
		{
			var raw = this.Samples;
			if (raw == null || raw.Length == 0) return [];

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
			if (pcm > CLIP) pcm = CLIP;

			// Segments
			int[] seg_end = { 0xFF, 0x1FF, 0x3FF, 0x7FF, 0xFFF, 0x1FFF, 0x3FFF, 0x7FFF };
			int seg = 0;
			while (seg < 8 && pcm > seg_end[seg]) seg++;

			int aval = seg << 4 | ((pcm >> (seg + 3)) & 0xF);
			byte uval = (byte) (aval ^ mask);
			return uval;
		}
	}
}
