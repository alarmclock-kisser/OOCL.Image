using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OOCL.Image.Shared.CUDA
{
	public class KernelCompileResult
	{
		public string DeviceName { get; set; } = string.Empty;
		public string KernelName { get; set; } = string.Empty;

		public bool Success { get; set; } = false;

		public string? CuFilePath { get; set; } = null;
		public string? PtxFilePath { get; set; } = null;
		
		public string? ErrorMessage { get; set; } = null;
		public string? BuildLog { get; set; } = null;

		public double? CompileDurationMs { get; set; } = null;


		public KernelCompileResult()
		{
			// Empty ctor
		}



	}
}
