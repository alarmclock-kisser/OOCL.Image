using OOCL.Image.OpenCl;
using System.Text.Json.Serialization;

namespace OOCL.Image.Shared
{
	public class OpenClDeviceInfo
	{
		public int DeviceId { get; set; } = -1;
		public string DeviceName { get; set; } = string.Empty;
		public string DeviceType { get; set; } = string.Empty;
		public string PlatformName { get; set; } = string.Empty;



		public OpenClDeviceInfo()
		{
			// Empty default ctor
		}

		[JsonConstructor]
		public OpenClDeviceInfo(OpenClService? obj, int index = -1)
		{
			this.DeviceId = index;

			if (obj == null)
			{
				return;
			}

			var service = obj as OpenClService;
			if (service == null)
			{
				return;
			}

			if (index < 0)
			{
				index = service.Index;
				this.DeviceId = index;
			}

			this.DeviceName = service.GetDeviceInfo(index) ?? "N/A";

			this.PlatformName = service.GetPlatformInfo(index) ?? "N/A";

			string deviceCode = service.GetDeviceType(index) ?? "N/A";

			this.DeviceType = deviceCode switch
			{
				"\u0002" => "CPU",
				"\u0004" => "GPU",
				"\u0008" => "ACCELERATOR",
				_ => deviceCode
			};
		}
	}
}