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

namespace OOCL.Image.WebApp.Pages
{
    /// <summary>
    /// ViewModel für Audio-Seite.
    /// Enthält die BPM / Stretch properties die von Audio.razor gebunden werden.
    /// </summary>
    public class AudioViewModel
    {
        public record AudioEntry(Guid Id, string Name, float Bpm, double DurationSeconds);

        private readonly ApiClient api;
        private readonly WebAppConfig config;
        private readonly NotificationService notifications;
        private readonly IJSRuntime js;
        private readonly DialogService dialogs;

        // Collections
        public List<AudioEntry> AudioEntries { get; private set; } = [];
        public List<AudioObjDto> ClientAudioCollection { get; private set; } = [];

        // Controls / State
        public decimal InitialBpm { get; set; } = 0m;
        public decimal TargetBpm { get; set; } = 0m;
        public decimal StretchFactor { get; set; } = 1.0m;
        public int ChunkSize { get; set; } = 8192;
        public decimal Overlap { get; set; } = 0.5m;
        public bool EnableStretchControls { get; set; } = true;

        // Optional cached meta
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
            if (openClInfo != null) this.openClServiceInfo = openClInfo;
            if (kernels != null) this.kernelInfos = kernels;
        }

        public async Task InitializeAsync()
        {
            // Lade Track-Infos vom ApiClient falls möglich, sonst belasse Placeholders
            try
            {
                var list = (await this.api.GetAudioListAsync(false))?.ToList() ?? [];
				this.AudioEntries = list.Select(t => new AudioEntry(t.Id, t.Name ?? t.Id.ToString(), t.Bpm, t.DurationSeconds)).ToList();
            }
            catch
            {
                // swallow - View sollte nicht komplett abbrechen
            }

            // Falls keine Einträge, ein Platzhalter
            if (this.AudioEntries.Count == 0)
            {
				this.AudioEntries.Add(new AudioEntry(Guid.NewGuid(), "Sample audio", 120f, 3.2));
            }

            // Default initial/target from first entry
            var first = this.AudioEntries.FirstOrDefault();
			this.SetSelectedTrack(first);
        }

        public async Task ReloadAsync()
        {
            bool serverSidedData = await this.api.IsServersidedDataAsync();
            if (serverSidedData)
            {
                try
                {
                    var list = (await this.api.GetAudioListAsync(false))?.ToList() ?? [];
                    this.AudioEntries = list.Select(t => new AudioEntry(t.Id, t.Name ?? t.Id.ToString(), t.Bpm, t.DurationSeconds)).ToList();
                }
                catch
                {
                    // swallow - View sollte nicht komplett abbrechen
                }
            }
            else
            {
                // Client-seitige Collection
                this.AudioEntries = this.ClientAudioCollection.Select(t => new AudioEntry(t.Info.Id, t.Info.Name ?? t.Info.Id.ToString(), t.Info.Bpm, t.Info.DurationSeconds)).ToList();
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
				this.InitialBpm = 0m;
				this.TargetBpm = 0m;
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
            if (this.InitialBpm != 0m)
            {
				this.StretchFactor = Math.Round(this.TargetBpm / this.InitialBpm, 12);
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
			this.TargetBpm = Math.Round(this.InitialBpm * this.StretchFactor, 3);
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

		public async Task OnInputFileChange(InputFileChangeEventArgs e)
		{
			var file = e.File;
			var apiConfig = await api.GetApiConfigAsync();
			using var stream = file.OpenReadStream((long) (apiConfig.MaxUploadSizeMb ?? 32) * 1024 * 1024);
			using var ms = new MemoryStream();
			await stream.CopyToAsync(ms);
			var bytes = ms.ToArray();
			var fileParameter = new FileParameter(new MemoryStream(bytes), file.Name, file.ContentType);

            bool isServerSidedData = await this.api.IsServersidedDataAsync();

			var dto = await this.api.UploadAudioAsync(fileParameter, !isServerSidedData);

            if (dto != null && dto.Info != null)
            {
                if (!isServerSidedData)
                {
                    this.ClientAudioCollection.Add(dto);
                }
                this.AudioEntries.Add(new AudioEntry(dto.Info.Id, dto.Info.Name ?? dto.Info.Id.ToString(), dto.Info.Bpm, dto.Info.DurationSeconds));
			}

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
					if (e != null) this.AudioEntries.Remove(e);
				}
                else
                {
                    var e = this.ClientAudioCollection.FirstOrDefault(a => a.Info.Id == id);
                    if (e != null) this.ClientAudioCollection.Remove(e);
                    var e2 = this.AudioEntries.FirstOrDefault(a => a.Id == id);
                    if (e2 != null) this.AudioEntries.Remove(e2);
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

            if (serverSidedData)
            {
                dto = await this.api.ExecuteTimestretchAsync(id, null, this.SelectedKernelName, this.ChunkSize, (float) this.Overlap, (double) this.StretchFactor);
            }
            else
            {
                dto = await this.api.ExecuteTimestretchAsync(null, dto, this.SelectedKernelName, this.ChunkSize, (float) this.Overlap, (double) this.StretchFactor);
                if (dto != null)
                {
                    this.ClientAudioCollection.Add(dto);
                }
			}

			await this.ReloadAsync();
		}
    }
}
