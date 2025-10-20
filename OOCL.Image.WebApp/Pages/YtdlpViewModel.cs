using Microsoft.JSInterop;
using OOCL.Image.Client;
using OOCL.Image.Shared;
using Radzen;

namespace OOCL.Image.WebApp.Pages
{
	public class YtdlpViewModel
	{
		private readonly ApiClient api;
		private readonly WebAppConfig config;
		private readonly NotificationService notifications;
		private readonly IJSRuntime js;
		private readonly DialogService dialogs;


		public YtdlpViewModel(ApiClient api, WebAppConfig config, NotificationService notifications, IJSRuntime js, DialogService dialogs)
		{
			this.api = api;
			this.config = config;
			this.notifications = notifications;
			this.js = js;
			this.dialogs = dialogs;
		}

		public bool IsDownloading { get; set; } = false;
		public string DownloadStatus { get; set; } = string.Empty;

		public string VideoUrl { get; set; } = string.Empty;
		public string SelectedFormat { get; set; } = "mp3";
		public int SelectedBitrate { get; set; } = 256;

		public Dictionary<string, List<int>> AvailableFormats { get; } = new()
		{
			{ "mp3", new List<int> { 96, 128, 192, 256, 320 } },
			{ "m4a", new List<int> { 96, 128, 192, 256, 320 } },
			{ "wav", new List<int> { 1411 } },
			{ "flac", new List<int> { 1411 } }
		};


		public async Task InitializeAsync()
		{
			// Initialization logic here
			await Task.CompletedTask;
		}

		public async Task OnVideoUrlChanged()
		{

		}

		public async Task OnFormatChanged()
		{
			// Reset bitrate when format changes
			if (this.AvailableFormats.TryGetValue(this.SelectedFormat, out var bitrates))
			{
				this.SelectedBitrate = bitrates.First();
			}
			await Task.CompletedTask;
		}

		public async Task OnBitrateChanged()
		{
			await Task.CompletedTask;
		}

		public async Task OnDownloadToServerClicked()
		{
			this.IsDownloading = true;
			// Download to server via api client
			var status = await this.api.YtdlpDownloadServerAsync(this.VideoUrl, this.SelectedFormat, this.SelectedBitrate);

			this.DownloadStatus = status;
			this.IsDownloading = false;
		}

		public async Task OnDownloadToClientClicked()
		{
			this.IsDownloading = true;

			var response = await this.api.YtdlpDownloadClientAsync(this.VideoUrl, this.SelectedFormat, this.SelectedBitrate);
			if (response?.StatusCode == 200)
			{
				var contentDisposition = response.Headers.TryGetValue("Content-Disposition", out var values) ? values.FirstOrDefault() : null;
				var fileName = "download";
				if (contentDisposition != null)
				{
					var parts = contentDisposition.Split(';');
					foreach (var part in parts)
					{
						var trimmedPart = part.Trim();
						if (trimmedPart.StartsWith("filename="))
						{
							fileName = trimmedPart.Substring("filename=".Length).Trim('"');
							break;
						}
					}
				}
				using var memoryStream = new MemoryStream();
				await response.Stream.CopyToAsync(memoryStream);
				var byteArray = memoryStream.ToArray();
				var base64 = Convert.ToBase64String(byteArray);
				await js.InvokeVoidAsync("downloadFileFromBase64", fileName, base64);
			}
			else
			{
				this.notifications.Notify(new NotificationMessage
				{
					Summary = "Download Failed",
					Detail = $"Server returned status code {response?.StatusCode}",
					Severity = NotificationSeverity.Error,
					Duration = 4000
				});
			}

			this.IsDownloading = false;
		}


	}
}
