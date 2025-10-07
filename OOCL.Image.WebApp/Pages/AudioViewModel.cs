using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;
using OOCL.Image.Client;
using OOCL.Image.Shared;
using Radzen;

namespace OOCL.Image.WebApp.Pages
{
	public class AudioViewModel
	{
		private readonly ApiClient Api;
		private readonly WebAppConfig Config;
		private readonly NotificationService Notifications;
		private readonly IJSRuntime JS;
		private readonly DialogService DialogService;

		public AudioViewModel(ApiClient api, WebAppConfig config, NotificationService notifications, IJSRuntime js, DialogService dialogService)
		{
			this.Api = api ?? throw new ArgumentNullException(nameof(api));
			this.Config = config ?? throw new ArgumentNullException(nameof(config));
			this.Notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
			this.JS = js ?? throw new ArgumentNullException(nameof(js));
			this.DialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
			this.ClientAudioCollection = [];
		}

		public OpenClServiceInfo openClServiceInfo { get; private set; } = new();
		public List<OpenClKernelInfo> KernelInfos { get; private set; } = [];
		public OpenClKernelInfo? SelectedKernel { get; private set; }
		public string SelectedKernelName { get; private set; } = string.Empty;
		public bool IsRendering => this.isRendering;

		public string StatusSummary => this.openClServiceInfo?.Initialized == true
			? $"{this.openClServiceInfo.DeviceName} [{this.openClServiceInfo.DeviceId}]"
			: "Device not initialized";

		public Guid CurrentAudioId { get; private set; }
		public AudioObjData? CurrentAudioData { get; private set; }
		public AudioObjInfo? CurrentAudioInfo => this.CurrentAudioData != null ? this.ClientAudioCollection.Where(d => d.Data.Id == this.CurrentAudioData.Id).Select(d => new AudioObjInfo(null)).FirstOrDefault() : null; // Placeholder
		public Dictionary<Guid, string> AudioCache { get; } = [];

		public List<AudioObjDto> ClientAudioCollection { get; set; } = [];
		public string DownloadFormat { get; set; } = "wav";
		public int DownloadBits { get; set; } = 24;

		private bool isServerSideData = false;
		private bool isRendering = false;
		private int clientTracksLimit = 0;

		// Waveform Anzeige (Base64 Data-URL)
		public string CurrentWaveformBase64 { get; private set; } = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR4nGP4Xw8AAoMBgVwFpI0AAAAASUVORK5CYII=";
		public int WaveformWidth { get; set; } = 800;
		public int WaveformHeight { get; set; } = 200;
		public long WaveformOffset { get; set; } = 0;
		public int WaveformSamplesPerPixel { get; set; } = 128;
		public string GraphColor { get; set; } = "#000000";
		public string BackgroundColor { get; set; } = "#FFFFFF";
		public string WaveformImageType { get; set; } = "jpg";

		// Fallback für Samples (für Seek / Anzeige)
		public long CurrentAudioTotalSamples { get; private set; } = 0;

		public void InjectPreloadedMeta(OpenClServiceInfo? info, List<OpenClKernelInfo>? kernels)
		{
			if (info != null)
			{
				this.openClServiceInfo = info;
			}
			if (kernels != null && kernels.Count > 0)
			{
				this.KernelInfos = kernels.Where(k => k.NeedsImage == false && k.MediaType == "Audio").ToList();
				this.SelectedKernelName = this.Config?.DefaultKernel ??
					this.KernelInfos.FirstOrDefault(k => k.FunctionName.ToLower().Contains("timestretch"))?.FunctionName ??
					this.KernelInfos.FirstOrDefault()?.FunctionName ??
					string.Empty;
				this.SelectedKernel = this.KernelInfos.FirstOrDefault(k => k.FunctionName == this.SelectedKernelName);
			}
		}

		public async Task InitializeAsync()
		{
			this.clientTracksLimit = this.Config?.TracksLimit ?? 0;

			if (this.KernelInfos.Count == 0)
			{
				try { this.KernelInfos = (await this.Api.GetOpenClKernelsAsync(true, "Audio")).ToList(); } catch { }
			}

			if (this.openClServiceInfo?.Initialized != true)
			{
				try
				{
					this.openClServiceInfo = await this.Api.GetOpenClServiceInfoAsync();
					if (!this.openClServiceInfo.Initialized)
					{
						await this.Api.InitializeOpenClIndexAsync(0);
						this.openClServiceInfo = await this.Api.GetOpenClServiceInfoAsync();
					}
				}
				catch { }

				this.isServerSideData = await this.Api.IsServersidedDataAsync();
			}

			if (string.IsNullOrEmpty(this.SelectedKernelName))
			{
				this.SelectedKernelName = this.Config?.DefaultKernel ??
					this.KernelInfos.FirstOrDefault(k => k.FunctionName.ToLower().Contains("timestretch"))?.FunctionName ??
					this.KernelInfos.FirstOrDefault()?.FunctionName ??
					string.Empty;
			}
			this.SelectedKernel = this.KernelInfos.FirstOrDefault(k => k.FunctionName == this.SelectedKernelName);

			if (string.IsNullOrEmpty(this.SelectedKernelName))
			{
				this.Notifications.Notify(new NotificationMessage { Severity = NotificationSeverity.Warning, Summary = "No kernel found", Duration = 3000 });
				return;
			}
		}

		public async Task EnsureDeviceInitializedAsync()
		{
			if (!this.openClServiceInfo.Initialized)
			{
				try
				{
					await this.Api.InitializeOpenClIndexAsync(0);
					this.openClServiceInfo = await this.Api.GetOpenClServiceInfoAsync();
				}
				catch { }
			}
		}

		public async Task SelectTrackAsync(AudioObjDto dto)
		{
			if (dto == null) return;
			this.CurrentAudioId = dto.Data.Id;
			this.CurrentAudioData = dto.Data;
			this.CurrentAudioTotalSamples = long.TryParse(dto.Data.Length.ToString(), out long len) ? len : 0;
			await this.GenerateWaveformAsync(800, 200, 128, 0);
		}

		public async Task UploadAudioAsync(IBrowserFile file, bool includeData = false)
		{
			try
			{
				var dto = await this.Api.UploadAudioAsync(file, includeData);
				if (dto != null)
				{
					this.ClientAudioCollection.Add(dto);
					this.CurrentAudioId = dto.Data.Id;
					this.CurrentAudioData = dto.Data;
					this.CurrentAudioTotalSamples = long.TryParse(dto.Data.Length.ToString(), out long len) ? len : 0;
					await this.GenerateWaveformAsync(800, 200, 128, 0);
				}
				this.TrimLimit();
			}
			catch (Exception ex)
			{
				this.Notifications.Notify(new NotificationMessage { Severity = NotificationSeverity.Error, Summary = "Upload fehlgeschlagen", Detail = ex.Message, Duration = 4000 });
			}
		}

		public async Task DownloadCurrentAsync()
		{
			if (this.CurrentAudioId == Guid.Empty) return;
			try
			{
				if (this.isServerSideData)
				{
					var resp = await this.Api.DownloadAudioDataAsync(this.ClientAudioCollection.FirstOrDefault(c => c.Data.Id == this.CurrentAudioId), this.DownloadFormat, this.DownloadBits);
				}
				else
				{
					var resp = await this.Api.DownloadAudioAsync(this.CurrentAudioId, "wav", 24);
					if (resp != null && resp.Stream != null)
					{
						// Browser Download via JS (Stream->Base64) wäre hier nötig; ausgelassen für Kürze
						this.Notifications.Notify(new NotificationMessage { Severity = NotificationSeverity.Info, Summary = "Download gestartet", Duration = 2500 });
					}
				}
			}
			catch { }
		}

		public async Task RemoveCurrentAsync()
		{
			if (this.CurrentAudioId == Guid.Empty) return;
			try
			{
				await this.Api.RemoveAudioAsync(this.CurrentAudioId);
				this.ClientAudioCollection.RemoveAll(c => c.Data.Id == this.CurrentAudioId);
				this.CurrentAudioId = Guid.Empty;
				this.CurrentAudioData = null;
				this.CurrentWaveformBase64 = TransparentPixel;
				this.CurrentAudioTotalSamples = 0;
			}
			catch { }
		}

		public async Task ClearAllAsync()
		{
			try
			{
				await this.Api.ClearAudiosAsync();
				this.ClientAudioCollection.Clear();
				this.CurrentAudioId = Guid.Empty;
				this.CurrentAudioData = null;
				this.CurrentWaveformBase64 = TransparentPixel;
				this.CurrentAudioTotalSamples = 0;
			}
			catch { }
		}

		public async Task ApplyTracksLimitAsync(int limit)
		{
			this.clientTracksLimit = limit < 0 ? 0 : limit;
			this.TrimLimit();
			await Task.CompletedTask;
		}

		private void TrimLimit()
		{
			if (this.clientTracksLimit > 0 && this.ClientAudioCollection.Count > this.clientTracksLimit)
			{
				int removeCount = this.ClientAudioCollection.Count - this.clientTracksLimit;
				this.ClientAudioCollection.RemoveRange(0, removeCount);
			}
		}

		public async Task GenerateWaveformAsync(int width, int height, int samplesPerPixel, long offset)
		{
			// Platzhalter: Ohne Rohdaten / API für Waveform wird ein Dummy-Bild verwendet.
			// Hier könnte später eine API für /audio/{id}/waveform genutzt werden.
			this.CurrentWaveformBase64 = await this.Api.GetWaveformBase64Async(this.isServerSideData ? this.CurrentAudioId : null, this.isServerSideData ? this.ClientAudioCollection.Where(t => t.Id == this.CurrentAudioId).FirstOrDefault() : null, this.WaveformWidth, this.WaveformHeight, this.WaveformOffset, this.WaveformSamplesPerPixel, this.GraphColor, this.BackgroundColor, this.WaveformImageType);
		}

		public async Task<(List<string> app, List<string> api)> LoadLogsAsync()
		{
			List<string> app = [];
			List<string> api = [];
			try { api = (await this.Api.GetApiLogsAsync()).ToList(); } catch { }
			try { app = (await this.Api.GetWebAppLogs(this.Config.MaxLogLines ?? 500)).ToList(); } catch { }
			return (app, api);
		}

		public async Task UploadAudioAsync(IBrowserFile file) => await this.UploadAudioAsync(file, false);

		private string PlaceholderWave(int w, int h)
		{
			return TransparentPixel;
		}

		private const string TransparentPixel = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR4nGP4Xw8AAoMBgVwFpI0AAAAASUVORK5CYII=";
	}
}
