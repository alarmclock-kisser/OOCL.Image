using OOCL.Image.OpenCl;
using System;
using System.Text.Json.Serialization;

namespace OOCL.Image.Shared
{
	public class OpenClServiceInfo
	{
		public int DeviceId { get; set; } = -1;
		public string DeviceName { get; set; } = string.Empty;
		public string PlatformName { get; set; } = string.Empty;
		public bool Initialized { get; set; } = false;

		public OpenClDeviceInfo CurrentDevice { get; set; } = new();



		public OpenClServiceInfo() : base()
		{
			// Default constructor for serialization
		}

		public OpenClServiceInfo(OpenClService? obj)
		{
			if (obj == null)
			{
				return;
			}

			var service = obj as OpenClService;
			if (service == null)
			{
				return;
			}

			this.Initialized = service.Initialized;
			if (!this.Initialized)
			{
				return;
			}

			this.DeviceId = service.Index;
			this.DeviceName = service.GetDeviceInfo() ?? "N/A";
			this.PlatformName = service.GetPlatformInfo() ?? "N/A";

			this.CurrentDevice = new OpenClDeviceInfo(service, this.DeviceId);
		}
	}
}