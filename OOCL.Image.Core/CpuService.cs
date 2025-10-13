using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OOCL.Image.Core
{
	public class CpuService
	{
		public int MaxProcessors { get; private set; } = 8;

		public CpuService(int maxProcessors = 8)
		{
			this.MaxProcessors = Math.Max(1, Math.Min(Environment.ProcessorCount, maxProcessors));

		}



		// Async method to measure data throughput via stream or idk
		public async Task<double?> MeasureDataThroughputAsync()
		{
			
		}

	}
}
