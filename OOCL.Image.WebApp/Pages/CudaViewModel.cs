using Microsoft.JSInterop;
using OOCL.Image.Client;
using OOCL.Image.Shared;
using Radzen;
using static OOCL.Image.WebApp.Pages.AudioViewModel;

namespace OOCL.Image.WebApp.Pages
{
	public class CudaViewModel
	{
		private readonly ApiClient api;
		private readonly WebAppConfig config;
		private readonly NotificationService notifications;
		private readonly IJSRuntime js;
		private readonly DialogService dialogs;

		private WebApiConfig? apiConfig = null;

		public List<string> RegisteredWorkers { get; set; } = [];

		public CudaViewModel(ApiClient api, WebAppConfig config, NotificationService notifications, IJSRuntime js, DialogService dialogs)
		{
			this.api = api ?? throw new ArgumentNullException(nameof(api));
			this.config = config ?? throw new ArgumentNullException(nameof(config));
			this.notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
			this.js = js ?? throw new ArgumentNullException(nameof(js));
			this.dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
		}

		public async Task InitializeAsync()
		{
			this.apiConfig = await api.GetApiConfigAsync();

			this.RegisteredWorkers = (await this.api.RefreshCudaWorkersAsync()).ToList();
		}


	}
}
