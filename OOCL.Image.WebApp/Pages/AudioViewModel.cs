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
        public bool EnableStretchControls { get; set; } = true;

        // Optional cached meta
        private OpenClServiceInfo? openClServiceInfo;
        private List<OpenClKernelInfo> kernelInfos = [];

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

        /// <summary>
        /// Wird aufgerufen, wenn in der UI ein Track selektiert wurde.
        /// InitialBpm wird auf den Track-BPM gesetzt, TargetBpm und StretchFactor werden entsprechend initialisiert.
        /// </summary>
        public void SetSelectedTrack(AudioEntry? track)
        {
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

        /// <summary>
        /// Ziel-BPM wurde verändert -> StretchFactor neu berechnen (12 Dezimalstellen intern, TargetBpm gerundet auf 3).
        /// </summary>
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

        /// <summary>
        /// StretchFactor wurde verändert -> TargetBpm neu berechnen (Target auf 3 Dezimalstellen).
        /// </summary>
        public void UpdateTargetFromStretch()
        {
			// StretchFactor assumed to be set
			this.StretchFactor = Math.Round(this.StretchFactor, 12);
			this.TargetBpm = Math.Round(this.InitialBpm * this.StretchFactor, 3);
        }

        // --- Simple helper actions (placeholders or light implementations) ---

        public async Task UploadAudioAsync(IBrowserFile browserFile)
        {
            bool serverSidedData = await this.api.IsServersidedDataAsync();

			try
            {
                var dto = await this.api.UploadAudioAsync(browserFile, !serverSidedData);
                if (dto != null && dto.Info != null)
                {
					this.ClientAudioCollection.Insert(0, dto);
					this.AudioEntries.Insert(0, new AudioEntry(dto.Info.Id, dto.Info.Name ?? dto.Info.Id.ToString(), dto.Info.Bpm, dto.Info.DurationSeconds));
                }
            }
            catch (Exception ex)
            {
				this.notifications?.Notify(new NotificationMessage { Severity = NotificationSeverity.Error, Summary = $"Upload failed: {ex.Message}" });
            }
        }

        public async Task RemoveAsync(Guid id)
        {
            try
            {
                await this.api.RemoveAudioAsync(id);
                var e = this.AudioEntries.FirstOrDefault(a => a.Id == id);
                if (e != null) this.AudioEntries.Remove(e);
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
            // Placeholder: hier könnte OpenCL/Api-Processing erfolgen
            var e = this.AudioEntries.FirstOrDefault(a => a.Id == id);
            if (e == null) return;
            await Task.Delay(200);
			this.notifications?.Notify(new NotificationMessage { Severity = NotificationSeverity.Success, Summary = $"Processed {e.Name}", Duration = 1400 });
        }
    }
}
