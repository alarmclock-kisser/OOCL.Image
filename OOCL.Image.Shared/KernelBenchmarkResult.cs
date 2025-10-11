using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OOCL.Image.Shared
{
	public class KernelBenchmarkResult
	{
		public string DeviceName { get; set; } = string.Empty;
		public string KernelName { get; set; } = string.Empty;

		public double Score { get; set; } = 0.0;
		public double? RawScore { get; set; } = null;
		public string Unit { get; set; } = "FLOP/s";

		public int? IterationsArg { get; set; } = null;
		public int? OperationsPerIterationArg { get; set; } = null;

		public double ExecutionTimeMs { get; set; } = 0.0;

		public string? ErrorMessage { get; set; } = null;


		public KernelBenchmarkResult()
		{
			// Empty ctor
		}




	}
}
