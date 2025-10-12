using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace OOCL.Image.Shared.CUDA
{
	public class CudaMemInfo
	{
		public Guid Id { get; set; } = Guid.Empty;
		public IEnumerable<string> Pointers { get; set; } = [];
		public IEnumerable<string> Lengths { get; set; } = [];
		public string ElementType { get; set; } = string.Empty;
		public string Count { get; set; } = "0";
		public string TotalLength { get; set; } = "0";
		public string TotalBytes { get; set; } = "0";



		public CudaMemInfo()
		{
			// Empty constructor
		}


	

	}
}
