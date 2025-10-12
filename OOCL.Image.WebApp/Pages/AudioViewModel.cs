using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Radzen;
using OOCL.Image.Client;
using OOCL.Image.Shared;
using Microsoft.AspNetCore.Components.Forms;
using System.IO;
using System.Diagnostics;

namespace OOCL.Image.WebApp.Pages
{
    /// <summary>
    /// ViewModel für Audio-Seite.
    /// Enthält die BPM / Stretch properties die von Audio.razor gebunden werden.
    /// </summary>
    public class AudioViewModel
    {
        public record AudioEntry(Guid Id, string Name, float Bpm, double DurationSeconds, long BytesCount, double LastExecTime);

        private readonly ApiClient api;
        private readonly WebAppConfig config;
        private readonly NotificationService notifications;
        private readonly IJSRuntime js;
        private readonly DialogService dialogs;

		public string StatusSummary => this.openClServiceInfo?.Initialized == true
			? $"{this.openClServiceInfo.DeviceName} [{this.openClServiceInfo.DeviceId}]"
			: "Device not initialized";

		// Collections
		public List<AudioEntry> AudioEntries { get; private set; } = [];
        public List<AudioObjDto> ClientAudioCollection { get; private set; } = [];
        public string DataLocation => this.isServerSidedData.HasValue && this.isServerSidedData.Value == true ? "Server-sided" : "Client-cached [" + this.CacheSize + "]";
        public string CacheSize => this.ClientAudioCollection.Select(dto => dto.Data.SizeInMb).Sum().ToString("F2") + " MB";
        public int MaxTracks => this.config.TracksLimit ?? 0;
        public double MaxUploadSizeMb => this.apiConfig?.MaxUploadSizeMb ?? 0;
		public Dictionary<string, int[]> AvailableDownloadFormats { get; private set; } = new()
		{
			["wav"] = [16, 24, 32],
			["mp3"] = [96, 128, 192, 256, 320]
		};


		// Controls / State
		public decimal InitialBpm { get; set; } = 120m;
        public decimal TargetBpm { get; set; } = 120m;
        public decimal StretchFactor { get; set; } = 1.0m;
        public int ChunkSize { get; set; } = 8192;
        public decimal Overlap { get; set; } = 0.5m;
        public bool EnableStretchControls { get; set; } = true;
        public string DownloadAudioType { get; set; } = "wav";
        public int DownloadAudioBits { get; set; } = 24;
        public string CompressionInfoText => this.config.DefaultCompressionBits > 0 ? $" (default {this.config.DefaultCompressionBits} bits{(this.config.UseCompressionMusLaw ? ", (mu-law)" : "")} compression)" : " (no compression)";
        public string LastRequestDelay { get; set; } = "- ms";


		// Indicates a download is in progress
		public bool IsDownloading { get; set; } = false;
        public bool IsProcessing { get; set; } = false;
		public bool IsUploading { get; set; } = false;

		// Optional cached meta
		private bool? isServerSidedData;
        private WebApiConfig? apiConfig;
		private OpenClServiceInfo? openClServiceInfo;
        private List<OpenClKernelInfo> kernelInfos = [];
        public string SelectedKernelName { get; set; } = "timestretch_double03";
		private AudioEntry? selectedTrack;

		public AudioViewModel(ApiClient api, WebAppConfig config, NotificationService notifications, IJSRuntime js, DialogService dialogs)
        {
            this.api = api ?? throw new ArgumentNullException(nameof(api));
            this.config = config ?? new WebAppConfig();
            this.notifications = notifications;
            this.js = js;
            this.dialogs = dialogs;
        }

        public void InjectPreloadedMeta(OpenClServiceInfo? openClInfo, List<OpenClKernelInfo>? kernels)
        {
            if (openClInfo != null)
			{
				this.openClServiceInfo = openClInfo;
			}

			if (kernels != null)
			{
                this.kernelInfos = kernels.Where(ki => ki.MediaType == "AUD").ToList();
			}
		}

        public async Task InitializeAsync()
        {
            this.apiConfig = await api.GetApiConfigAsync();

            // Lade Track-Infos vom ApiClient falls möglich, sonst belasse Placeholders
            try
            {
                var list = (await this.api.GetAudioListAsync(false))?.ToList() ?? [];
				this.AudioEntries = list.Select(t => new AudioEntry(t.Id, t.Name ?? t.Id.ToString(), t.Bpm, t.DurationSeconds, 0, 0)).ToList();
            }
            catch
            {
                // swallow - View sollte nicht komplett abbrechen
            }

            // Default initial/target from first entry
            var first = this.AudioEntries.FirstOrDefault();
			this.SetSelectedTrack(first);
        }

        public async Task ReloadAsync()
        {
            bool serverSidedData = await this.api.IsServersidedDataAsync();
            this.isServerSidedData = serverSidedData;
            this.apiConfig = await this.api.GetApiConfigAsync();

			if (serverSidedData)
            {
                try
                {
                    var list = (await this.api.GetAudioListAsync(false))?.ToList() ?? [];
                    this.AudioEntries = list.Select(t => new AudioEntry(t.Id, t.Name ?? t.Id.ToString(), t.Bpm, t.DurationSeconds, 0, t.LastExecutionTime ?? 0)).ToList();
                }
                catch
                {
                    // swallow - View sollte nicht komplett abbrechen
                }
            }
            else
            {
                // Client-seitige Collection
                this.AudioEntries = this.ClientAudioCollection.Select(t => new AudioEntry(t.Info.Id, t.Info.Name ?? t.Info.Id.ToString(), t.Info.Bpm, t.Info.DurationSeconds, (t.Data.Samples.LongLength > 0 ? t.Data.Samples.LongLength : -1), t.Info.LastExecutionTime ?? 0)).ToList();
            }

            if (this.selectedTrack == null)
            {
                this.InitialBpm = 120;
                this.TargetBpm = 120;
                this.StretchFactor = 1.0m;
			}
		}

		public void SetSelectedTrack(AudioEntry? track)
        {
            this.selectedTrack = track;

			if (track == null)
            {
				this.InitialBpm = 120m;
				this.TargetBpm = 120m;
				this.StretchFactor = 1.0m;
                return;
            }

			// Track.Bpm ist float -> round/cast
			this.InitialBpm = Math.Round((decimal)track.Bpm, 3);
			// Default target == initial
			this.TargetBpm = this.InitialBpm;
            if (this.InitialBpm != 0m)
            {
				this.StretchFactor = Math.Round(this.TargetBpm / this.InitialBpm, 12);
            }
            else
            {
				this.StretchFactor = 1.0m;
            }
        }

        public void UpdateStretchFromTarget()
        {
			// TargetBpm erwartet bereits gesetzt zu sein
			this.TargetBpm = Math.Round(this.TargetBpm, 3);
            if (this.InitialBpm > 0.001m)
            {
				this.StretchFactor = Math.Round(this.InitialBpm / this.TargetBpm, 12);
			}
            else
            {
				this.StretchFactor = 1.0m;
            }
        }

        public void UpdateTargetFromStretch()
        {
			// StretchFactor assumed to be set
			this.StretchFactor = Math.Round(this.StretchFactor, 12);
			this.TargetBpm = Math.Round(this.InitialBpm / this.StretchFactor, 3);
		}

        public void ToggleChunkSize(decimal value)
        {
            const int MinChunk = 128;
            const int MaxChunk = 65536;

            int intValue = (int)value;
            if (intValue < MinChunk)
			{
				intValue = MinChunk;
			}

			if (intValue > MaxChunk)
			{
				intValue = MaxChunk;
			}

			// Aktuellen ChunkSize in gültigen Bereich bringen
			int current = this.ChunkSize;
            if (current < MinChunk)
			{
				current = MinChunk;
			}

			if (current > MaxChunk)
			{
				current = MaxChunk;
			}

			// Sicherstellen, dass current eine Zweierpotenz ist (ansonsten auf nächstkleinere potenz setzen)
			if (!IsPowerOfTwo(current))
            {
                current = PrevPowerOfTwo(current);
            }

            // Benutzer hat inkrementiert -> eine Stufe (Faktor 2) nach oben
            if (intValue > current)
            {
                long next = (long)current * 2;
                if (next > MaxChunk)
				{
					next = MaxChunk;
				}

				this.ChunkSize = (int)next;
                return;
            }

            // Benutzer hat dekrementiert -> eine Stufe (Faktor 2) nach unten
            if (intValue < current)
            {
                int prev = current / 2;
                if (prev < MinChunk)
				{
					prev = MinChunk;
				}

				this.ChunkSize = prev;
                return;
            }

            // unverändert: nichts tun
        }

        private static bool IsPowerOfTwo(int x) => x > 0 && (x & (x - 1)) == 0;

        private static int PrevPowerOfTwo(int x)
        {
            if (x < 1)
			{
				return 1;
			}

			int p = 1;
            while ((p << 1) <= x)
			{
				p <<= 1;
			}

			return p;
        }

        public async Task EnforceTracksLimit()
        {
            bool serverSidedData = await this.api.IsServersidedDataAsync();
            int? tracksLimit = this.config.TracksLimit;

            if (tracksLimit.HasValue && tracksLimit.Value > 0)
            {
                if (!serverSidedData)
                {
                    this.ClientAudioCollection = this.ClientAudioCollection.OrderByDescending(t => t.Info.CreatedAt).Take(tracksLimit.Value).ToList();
				}
            }

            await this.ReloadAsync();
		}

        public async Task OnUploadAndStretch(InputFileChangeEventArgs e)
        {
            if (e == null)
			{
				return;
			}

			string execTimes = "";
            Stopwatch sw = Stopwatch.StartNew();

            this.IsUploading = true;
			var file = e.File;
			var apiConfig = await api.GetApiConfigAsync();
			using var stream = file.OpenReadStream((long) (apiConfig.MaxUploadSizeMb ?? 32) * 1024 * 1024);
			using var ms = new MemoryStream();
			await stream.CopyToAsync(ms);
			var bytes = ms.ToArray();
			var fileParameter = new FileParameter(new MemoryStream(bytes), file.Name, file.ContentType);

            this.IsProcessing = true;
            var result = await this.api.UploadAndStretchAndDownloadAudioAsync(fileParameter, this.SelectedKernelName, (double)this.StretchFactor, this.ChunkSize, (float)this.Overlap, this.DownloadAudioType, this.DownloadAudioBits);
            this.IsProcessing = false;

			this.IsUploading = false;
            execTimes = execTimes + "Process: "+ sw.Elapsed.TotalMilliseconds.ToString("F1") + " ms";
            sw.Restart();
            this.IsDownloading = true;

            if (result == null)
            {
                sw.Stop();
                execTimes = "Failed!";
                this.IsDownloading = false;
                return;
            }

            await this.DownloadFileResponseAsync(result, fileParameter.FileName + $"_stretched_{(this.StretchFactor.ToString("F5"))}.{this.DownloadAudioType}", "audio/" + this.DownloadAudioType);

            this.IsDownloading = false;
			execTimes = execTimes + ", Download: " + sw.Elapsed.TotalMilliseconds.ToString("F1") + " ms";
            this.LastRequestDelay = execTimes;
		}

		public async Task OnInputFileChange(InputFileChangeEventArgs e)
		{
            this.IsUploading = true;

			var file = e.File;
			var apiConfig = await api.GetApiConfigAsync();
			using var stream = file.OpenReadStream((long) (apiConfig.MaxUploadSizeMb ?? 32) * 1024 * 1024);
			using var ms = new MemoryStream();
			await stream.CopyToAsync(ms);
			var bytes = ms.ToArray();
			var fileParameter = new FileParameter(new MemoryStream(bytes), file.Name, file.ContentType);

            bool isServerSidedData = await this.api.IsServersidedDataAsync();

			var dto = await this.api.UploadAudioAsync(fileParameter, !isServerSidedData, this.config.DefaultCompressionBits, this.config.UseCompressionMusLaw);

            if (dto != null && dto.Info != null)
            {
                if (!isServerSidedData)
                {
                    this.ClientAudioCollection.Add(dto);
                }
                this.AudioEntries.Add(new AudioEntry(dto.Info.Id, dto.Info.Name ?? dto.Info.Id.ToString(), dto.Info.Bpm, dto.Info.DurationSeconds, dto.Data.Samples.LongLength, 0));
			}

            this.IsUploading = false;

            await this.EnforceTracksLimit();
		}

        public async Task RemoveAsync(Guid id)
        {
            try
            {
                bool serverSidedData = await this.api.IsServersidedDataAsync();
                if (serverSidedData)
                {
					await this.api.RemoveAudioAsync(id);
					var e = this.AudioEntries.FirstOrDefault(a => a.Id == id);
					if (e != null)
					{
						this.AudioEntries.Remove(e);
					}
				}
                else
                {
                    var e = this.ClientAudioCollection.FirstOrDefault(a => a.Info.Id == id);
                    if (e != null)
					{
						this.ClientAudioCollection.Remove(e);
					}

					var e2 = this.AudioEntries.FirstOrDefault(a => a.Id == id);
                    if (e2 != null)
					{
						this.AudioEntries.Remove(e2);
					}
				}
			}
            catch (Exception ex)
            {
				this.notifications?.Notify(new NotificationMessage { Severity = NotificationSeverity.Error, Summary = $"Remove failed: {ex.Message}" });
            }
        }

        public async Task PlayAsync(Guid id)
        {
            // Minimal: einfach Notification / JS log; echtes Playback kann später integriert werden
            var e = this.AudioEntries.FirstOrDefault(a => a.Id == id);
            if (e == null)
            {
				this.notifications?.Notify(new NotificationMessage { Severity = NotificationSeverity.Error, Summary = "Track not found" });
                return;
            }
            await this.js.InvokeVoidAsync("console.info", $"Play audio {e.Name} ({e.Id})");
			this.notifications?.Notify(new NotificationMessage { Severity = NotificationSeverity.Info, Summary = $"Play: {e.Name}", Duration = 1200 });
        }

        public async Task ProcessAsync(Guid id)
        {
            AudioObjDto? dto = null;
            bool serverSidedData = await this.api.IsServersidedDataAsync();
            this.IsProcessing = true;

			if (serverSidedData)
            {
                dto = await this.api.ExecuteTimestretchAsync(id, null, this.SelectedKernelName, this.ChunkSize, (float) this.Overlap, (double) this.StretchFactor);
            }
            else
            {
                dto = this.ClientAudioCollection.FirstOrDefault(a => a.Id == id);

                dto = await this.api.ExecuteTimestretchAsync(null, dto, this.SelectedKernelName, this.ChunkSize, (float) this.Overlap, (double) this.StretchFactor);
                if (dto != null)
                {
                    this.ClientAudioCollection.Add(dto);
                }
			}

            this.IsProcessing = false;
            this.LastRequestDelay = $"{(dto?.Info.LastExecutionTime.ToString() ?? "- ")}  ms";
			await this.ReloadAsync();
		}

        public async Task DownloadAudio(Guid id)
        {
            // Ensure only one download at a time by setting flag
            if (this.IsDownloading)
			{
				return;
			}

			this.IsDownloading = true;
            try
            {
                AudioObjDto? dto = null;
                bool serverSidedData = await this.api.IsServersidedDataAsync();
                if (serverSidedData)
                {
                    var file = await this.api.DownloadAudioAsync(id, this.DownloadAudioType, this.DownloadAudioBits);
                    if (file != null)
                    {
                        await this.DownloadFileResponseAsync(file, id.ToString() + $".{this.DownloadAudioType}", "audio/" + this.DownloadAudioType);
                    }
                }
                else
                {
                    dto = this.ClientAudioCollection.FirstOrDefault(a => a.Id == id);
                    if (dto != null)
                    {
                        var file = await this.api.DownloadAudioDataAsync(dto, this.DownloadAudioType, this.DownloadAudioBits);
                        if (file != null)
                        {
                            await this.DownloadFileResponseAsync(file, dto.Info.Name ?? (dto.Info.Id.ToString() + $".{this.DownloadAudioType}"), "audio/" + this.DownloadAudioType);
                        }
                    }
                }
            }
            finally
            {
                this.IsDownloading = false;
            }
        }

        public async Task DownloadFileResponseAsync(FileResponse file, string suggestedFileName, string fallbackMime = "application/octet-stream")
        {
            try
            {
                if (file.Stream == null)
                {
                    return;
                }
                byte[] bytes;
                if (file.Stream.CanSeek)
                {
                    file.Stream.Position = 0;
                    using var ms = new MemoryStream();
                    await file.Stream.CopyToAsync(ms);
                    bytes = ms.ToArray();
                }
                else
                {
                    // Nicht-seekbar -> direkt komplett lesen
                    using var ms = new MemoryStream();
                    await file.Stream.CopyToAsync(ms);
                    bytes = ms.ToArray();
                }

                if (bytes.Length == 0)
                {
                    return;
                }

                string mime = HomeViewModel.DetectMimeFromHeaders(file) ?? fallbackMime;
                string base64 = Convert.ToBase64String(bytes);
                string dataUri = $"data:{mime};base64,{base64}";
                await this.js.InvokeVoidAsync("downloadFileFromDataUri", suggestedFileName, dataUri);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
            finally
            {
                file.Dispose();
            }
        }



		// Helpers
	}
}
