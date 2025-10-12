
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OOCL.Image.Shared.CUDA
{
	public class CudaStatusInfo
	{
		public int DeviceId { get; set; } = -1;
		public string DeviceName { get; set; } = string.Empty;
		public bool Initialized { get; set; } = false;

		public IEnumerable<CudaDeviceInfo> AvailableDevices { get; set; } = [];

		public CudaUsageInfo UsageInfo { get; set; } = new();

		public string? ErrorMessage { get; set; } = null;

		public CudaStatusInfo()
		{
			// Empty constructor
		}

	



	}
}
