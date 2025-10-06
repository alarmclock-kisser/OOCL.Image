using System.Collections.Concurrent;
using System.Drawing;

namespace OOCL.Image.Core
{
	public class AudioCollection
	{
		public ConcurrentDictionary<Guid, AudioObj> tracks { get; private set; } = [];
		public IReadOnlyList<AudioObj> Tracks => this.tracks.Values.OrderBy(t => t.CreatedAt).ToList();

		public string ResourcesPath { get; set; } = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "OOCL.Image.Core", "Resources"));

		public int Count => this.Tracks.Count;
		public string[] Entries => this.Tracks.Select(t => t.Name).ToArray();
		public string[] Playing => this.tracks.Values.Where(t => t.Playing).Select(t => t.Name).ToArray();

		public Color GraphColor { get; set; } = SystemColors.ActiveCaption;
		public Color BackColor { get; set; } = Color.White;
		public int MaxTracks { get; set; } = 1;

		public AudioObj? this[Guid guid]
		{
			get => this.tracks[guid];
		}

		public AudioObj? this[string name]
		{
			get => this.tracks.Values.FirstOrDefault(t => t.Name.Equals(name, StringComparison.CurrentCultureIgnoreCase));
		}

		public AudioObj? this[int index]
		{
			get => index >= 0 && index < this.Count ? this.tracks.Values.ElementAt(index) : null;
		}

		public AudioObj? this[IntPtr pointer]
		{
			get => pointer != IntPtr.Zero ? this.tracks.Values.FirstOrDefault(t => t.Pointer == pointer) : null;
		}

		public AudioCollection()
		{
		}

		public AudioCollection(int? maxracks = 1)
		{
			this.MaxTracks = maxracks ?? 1;

			if (!Directory.Exists(this.ResourcesPath))
			{
				this.ResourcesPath = Path.Combine(AppContext.BaseDirectory, "Resources");
				if (!Directory.Exists(this.ResourcesPath))
				{
					Console.WriteLine($"AudioCollection: Resources directory not found at '{this.ResourcesPath}'.");
				}
				else
				{
					this.ResourcesPath = Path.GetFullPath(this.ResourcesPath);
					Console.WriteLine($"AudioCollection: Using Resources directory at '{this.ResourcesPath}'.");
				}
			}
		}

		public void SetRemoveAfterPlayback(bool remove)
		{
			foreach (var track in this.tracks.Values)
			{
				track.RemoveAfterPlayback = remove;
			}
		}

		private bool ExistsByFileName(string filePath)
		{
			var name = Path.GetFileName(filePath);
			return this.tracks.Values.Any(t => string.Equals(Path.GetFileName(t.FilePath), name, StringComparison.OrdinalIgnoreCase));
		}

		private AudioObj? FindByHash(string? hash)
		{
			if (string.IsNullOrWhiteSpace(hash))
			{
				return null;
			}

			return this.tracks.Values.FirstOrDefault(t => string.Equals(t.ContentHash, hash, StringComparison.OrdinalIgnoreCase));
		}

		public async Task<AudioObj?> ImportAsync(string filePath, bool linearLoad = true)
		{
			if (!File.Exists(filePath))
			{
				return null;
			}

			// Compute hash early for dedupe
			string? hash = AudioObj.ComputeFileHash(filePath);
			var existingByHash = this.FindByHash(hash);
			if (existingByHash != null)
			{
				return existingByHash; // Already imported exact same content
			}

			// Also check by filename (helps when hash could not be computed)
			if (hash == null && this.ExistsByFileName(filePath))
			{
				return this.tracks.Values.First(t => string.Equals(Path.GetFileName(t.FilePath), Path.GetFileName(filePath), StringComparison.OrdinalIgnoreCase));
			}

			AudioObj? obj = null;
			if (linearLoad)
			{
				await Task.Run(() => { obj = new AudioObj(filePath, true); });
			}
			else
			{
				obj = await AudioObj.CreateAsync(filePath);
			}
			if (obj == null)
			{
				return null;
			}

			obj.ContentHash = hash ?? AudioObj.ComputeFileHash(filePath);

			if (!this.tracks.TryAdd(obj.Id, obj))
			{
				obj.Dispose();
				return null;
			}

			obj.RemoveRequested += async (_, __) => { await this.RemoveAsync(obj); };

			return obj;
		}

		public async Task RemoveAsync(AudioObj? obj)
		{
			if (obj == null)
			{
				return;
			}

			// Entfernen aus Dictionary (threadsafe)
			if (this.tracks.TryRemove(obj.Id, out var removed))
			{
				removed.Dispose();
			}
			await Task.CompletedTask;
		}

		public void StopAll(bool remove = false)
		{
			foreach (var track in this.tracks.Values.ToList())
			{
				bool wasPlaying = track.Playing;
				track.Stop();

				if (remove && wasPlaying)
				{
					if (this.tracks.TryRemove(track.Id, out var t))
					{
						t?.Dispose();
					}
				}
			}
		}

		public void SetMasterVolume(float percentage)
		{
			percentage = Math.Clamp(percentage, 0.0f, 1.0f);

			foreach (var track in this.tracks.Values)
			{
				int volume = (int)(track.Volume * percentage);
				track.SetVolume(volume);
			}
		}

		public async Task DisposeAsync()
		{
			var items = this.tracks.Values.ToList();
			this.tracks.Clear();

			foreach (var track in items)
			{
				track.Dispose();
			}
			await Task.CompletedTask;
		}

		public static async Task<AudioObj?> LevelAudioFileAsync(string filePath, float duration = 1.0f, float normalize = 1.0f)
		{
			AudioObj? obj = await AudioObj.CreateAsync(filePath);
			if (obj == null)
			{
				return null;
			}

			await obj.Level(duration, normalize);

			return obj;
		}

		public async Task<int> EnforceTracksLimit(int? limitOverride = null)
					{
			int limit = limitOverride ?? this.MaxTracks;
			if (limit <= 0)
			{
				return 0;
			}

			int removed = 0;
			while (this.Count > limit)
			{
				var oldest = this.tracks.Values.OrderBy(t => t.CreatedAt).FirstOrDefault();
				if (oldest != null)
				{
					await this.RemoveAsync(oldest);
					removed++;
				}
				else
				{
					break;
				}
			}

			return removed;
		}

		public void Clear()
		{
			foreach (var track in this.tracks.Values.ToList())
			{
				if (this.tracks.TryRemove(track.Id, out var t))
				{
					t?.Dispose();
				}
			}
		}

		public static async Task<AudioObj?> CreateFromDataAsync(float[] samples, int sampleRate, int channels, int bitdepth)
		{
			var obj = await Task.Run(() => new AudioObj(samples, sampleRate, channels, bitdepth));
			return obj;
		}

		public async Task<IEnumerable<AudioObj>> LoadFromResources(string? resourcesPath = null)
		{
			resourcesPath ??= this.ResourcesPath;
			if (!Directory.Exists(resourcesPath))
			{
				Console.WriteLine("AudioCollection: Resources directory not found at '" + resourcesPath + "'.");
				return [];
			}

			var supportedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".wav", ".mp3", ".flac"};
			var files = Directory.GetFiles(resourcesPath, "*.*", SearchOption.AllDirectories)
				.Where(f => supportedExtensions.Contains(Path.GetExtension(f)))
				.ToList();

			var imported = new List<AudioObj>();
			foreach (var file in files)
			{
				var obj = await this.ImportAsync(file, linearLoad: false);
				if (obj != null)
				{
					imported.Add(obj);
				}
			}

			return imported;
		}

	}
}
