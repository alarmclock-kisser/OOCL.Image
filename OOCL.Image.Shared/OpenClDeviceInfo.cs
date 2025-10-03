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

		public string LocalMemoryBytes { get; set; } = "0";
		public string GlobalMemoryBytes { get; set; } = "0";
		public string AddressBits { get; set; } = "0";

		public string CompilerAvailable { get; set; } = "N/A";
		public string DriverVersion { get; set; } = "N/A";

		public string MaxClockFrequency { get; set; } = "0";
		public string MaxComputeUnits { get; set; } = "0";

		public string MaxWorkGroupSize { get; set; } = "0";
		public string MaxWorkItemDimensions { get; set; } = "0";
		public string MaxWorkItemSizes { get; set; } = "N/A";
		
		public string Extensions { get; set; } = "N/A";
		
		public string ImageSupport { get; set; } = "N/A";
		public string Image2DMaxSize { get; set; } = "N/A";
		public string Image3DMaxSize { get; set; } = "N/A";

		public string Vendor { get; set; } = "N/A";
		public string VendorId { get; set; } = "N/A";
		public string Version { get; set; } = "N/A";



		public OpenClDeviceInfo()
		{
			// Empty default ctor
		}

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

			this.LocalMemoryBytes = service.GetDeviceInfo(this.DeviceId, OpenTK.Compute.OpenCL.DeviceInfo.LocalMemorySize) ?? "N/A";
			this.GlobalMemoryBytes = service.GetDeviceInfo(this.DeviceId, OpenTK.Compute.OpenCL.DeviceInfo.GlobalMemorySize) ?? "N/A";
			this.AddressBits = service.GetDeviceInfo(this.DeviceId, OpenTK.Compute.OpenCL.DeviceInfo.AddressBits) ?? "N/A";
			this.CompilerAvailable = service.GetDeviceInfo(this.DeviceId, OpenTK.Compute.OpenCL.DeviceInfo.CompilerAvailable) ?? "N/A";
			this.DriverVersion = service.GetDeviceInfo(this.DeviceId, OpenTK.Compute.OpenCL.DeviceInfo.DriverVersion) ?? "N/A";
			this.MaxClockFrequency = service.GetDeviceInfo(this.DeviceId, OpenTK.Compute.OpenCL.DeviceInfo.MaximumClockFrequency) ?? "N/A";
			this.MaxComputeUnits = service.GetDeviceInfo(this.DeviceId, OpenTK.Compute.OpenCL.DeviceInfo.MaximumComputeUnits) ?? "N/A";
			this.MaxWorkGroupSize = service.GetDeviceInfo(this.DeviceId, OpenTK.Compute.OpenCL.DeviceInfo.MaximumWorkGroupSize) ?? "N/A";
			this.MaxWorkItemDimensions = service.GetDeviceInfo(this.DeviceId, OpenTK.Compute.OpenCL.DeviceInfo.MaximumWorkItemDimensions) ?? "N/A";
			this.MaxWorkItemSizes = service.GetDeviceInfo(this.DeviceId, OpenTK.Compute.OpenCL.DeviceInfo.MaximumWorkItemSizes) ?? "N/A";
			this.Extensions = service.GetDeviceInfo(this.DeviceId, OpenTK.Compute.OpenCL.DeviceInfo.Extensions) ?? "N/A";
			this.ImageSupport = service.GetDeviceInfo(this.DeviceId, OpenTK.Compute.OpenCL.DeviceInfo.ImageSupport) ?? "N/A";
			this.Image2DMaxSize = "[" + (service.GetDeviceInfo(this.DeviceId, OpenTK.Compute.OpenCL.DeviceInfo.Image2DMaximumWidth) ?? "N/A") + " x " +
				(service.GetDeviceInfo(this.DeviceId, OpenTK.Compute.OpenCL.DeviceInfo.Image2DMaximumHeight) ?? "N/A") + "]";
			this.Image3DMaxSize = "[" + (service.GetDeviceInfo(this.DeviceId, OpenTK.Compute.OpenCL.DeviceInfo.Image3DMaximumWidth) ?? "N/A") + " x " +
				(service.GetDeviceInfo(this.DeviceId, OpenTK.Compute.OpenCL.DeviceInfo.Image3DMaximumHeight) ?? "N/A") + " x " +
				(service.GetDeviceInfo(this.DeviceId, OpenTK.Compute.OpenCL.DeviceInfo.Image3DMaximumDepth) ?? "N/A") + "]";
			this.Vendor = service.GetDeviceInfo(this.DeviceId, OpenTK.Compute.OpenCL.DeviceInfo.Vendor) ?? "N/A";
			this.VendorId = service.GetDeviceInfo(this.DeviceId, OpenTK.Compute.OpenCL.DeviceInfo.VendorId) ?? "N/A";
			this.Version = service.GetDeviceInfo(this.DeviceId, OpenTK.Compute.OpenCL.DeviceInfo.Version) ?? "N/A";
		}
	}
}