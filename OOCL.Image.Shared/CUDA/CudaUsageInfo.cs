
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace OOCL.Image.Shared.CUDA
{
	public class CudaUsageInfo
	{
		public double TotalMemoryMb { get; set; } = 0;
		public double FreeMemoryMb { get; set; } = 0;
		public double UsedMemoryMb { get; set; } = 0;

		public CudaUsageInfo()
		{
			// Empty constructor
		}

		
	}
}
