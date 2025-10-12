using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using OOCL.Image.Client;
using OOCL.Image.Shared;
using OpenTK.Compute.OpenCL;
using Radzen;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace OOCL.Image.WebApp.Pages
{
    public class AudioViewModel
    {
        public record AudioEntry(Guid Id, string Name, float Bpm, double DurationSeconds);

        private readonly ApiClient Api;
        private readonly WebAppConfig config;
        private readonly NotificationService notifications;
        private readonly IJSRuntime js;
        private readonly DialogService dialogs;

        public WebApiConfig? ApiConfig { get; private set; }
        public int MaxTracksToKeepNumeric { get; set; } = 0;

		public List<AudioEntry> AudioEntries => this.tracks.Select(t => new AudioEntry(t.Id, t.Name, t.Bpm, t.DurationSeconds)).ToList();
		public List<AudioObjDto> ClientAudioCollection { get; private set; } = [];
		public List<AudioObjInfo> tracks { get; private set; } = [];

		private OpenClServiceInfo? openClServiceInfo;
        private List<OpenClKernelInfo> kernelInfos = [];
        private List<string> kernelNames = [];
		private List<OpenClDeviceInfo> devices = [];

		public string selectedKernelName { get; set; }	= string.Empty;

		public AudioViewModel(ApiClient api, WebAppConfig config, NotificationService notifications, IJSRuntime js, DialogService dialogs)
        {
            this.Api = api;
            this.config = config;
            this.notifications = notifications;
            this.js = js;
            this.dialogs = dialogs;
        }

        public void InjectPreloadedMeta(OpenClServiceInfo? openClInfo, List<OpenClKernelInfo>? kernelInfos)
        {
			if (openClInfo != null)
			{
				this.openClServiceInfo = openClInfo;
			}

			if (kernelInfos != null && kernelInfos.Count > 0)
			{
				this.kernelInfos = kernelInfos.Where(k => k.MediaType == "AUD").ToList();
				this.kernelNames = kernelInfos.Select(k => k.FunctionName).ToList();
			}
		}

        public async Task InitializeAsync()
        {
			this.ApiConfig = await this.Api.GetApiConfigAsync();

			await this.LoadTracks();

			if (this.openClServiceInfo == null || string.IsNullOrEmpty(this.openClServiceInfo.DeviceName))
			{
				await this.LoadOpenClStatus();
			}

			if (this.kernelInfos == null || this.kernelInfos.Count == 0)
			{
				await this.LoadKernels();
			}

			this.MaxTracksToKeepNumeric = this.config?.TracksLimit ?? 1;
		}

		private async Task LoadTracks()
		{
			if (await this.Api.IsServersidedDataAsync())
			{
				this.tracks = this.ClientAudioCollection.Select(dto => dto.Info).ToList();
			}
			else
			{
				this.tracks = (await this.Api.GetAudioListAsync(false)).ToList() ?? [];
			}
		}

		public async Task LoadDevices() => this.devices = (await this.Api.GetOpenClDevicesAsync()).ToList();

		public async Task LoadOpenClStatus()
		{
			this.openClServiceInfo = await this.Api.GetOpenClServiceInfoAsync();
		}

		public async Task LoadKernels()
		{
			try
			{
				this.kernelInfos = (await this.Api.GetOpenClKernelsAsync(true, "AUD")).ToList();
				this.kernelNames = this.kernelInfos.Select(k => k.FunctionName).ToList();
				if (this.kernelNames.Count > 0)
				{
					// Try Select default kernel from config
					if (!string.IsNullOrEmpty(this.config?.DefaultKernel) && this.kernelNames.Contains(this.config.DefaultKernel))
					{
						this.selectedKernelName = this.config.DefaultKernel;
					}
					else
					{
						this.selectedKernelName = this.kernelNames[0];
					}
				}
			}
			catch { }
			finally
			{
				await Task.Yield();
			}
		}

		public async Task UploadAudioAsync(FileParameter file)
		{
			bool isServerSidedData = await this.Api.IsServersidedDataAsync();

			var dto = await this.Api.UploadAudioAsync(file, !isServerSidedData);
			if (dto != null)
			{
				if (isServerSidedData)
				{
					this.ClientAudioCollection.Add(dto);
				}
			}

			await this.LoadTracks();
		}

        public async Task RemoveAsync(Guid id)
        {
            var info = this.tracks.FirstOrDefault(t => t.Id == id);
			
			if (await this.Api.IsServersidedDataAsync())
			{
				var dto = this.ClientAudioCollection.FirstOrDefault(d => d.Id == id);
				if (dto != null)
				{
					this.ClientAudioCollection.Remove(dto);
				}
			}
			else
			{
				await this.Api.RemoveAudioAsync(id);
			}
		}

        public async Task PlayAsync(Guid id)
        {
            var info = this.tracks.FirstOrDefault(t => t.Id == id);
			if (info == null)
			{
				return;
			}

			await Task.CompletedTask;
		}

        public async Task ProcessAsync(Guid id)
        {
            
        }
    }
}
