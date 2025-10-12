using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OOCL.Image.Shared.CUDA
{
	public class CudaKernelInfo
	{
		public string Name { get; set; } = string.Empty;

		public IEnumerable<string> ArgumentTypes { get; set; } = [];
		public IEnumerable<string> ArgumentNames { get; set; } = [];

		public string InputType { get; set; } = "void*";
		public string ReturnType { get; set; } = "void*";

		public bool SuccessfullyCompiled { get; set; } = false;
		public string CompilationLog { get; set; } = string.Empty;


		public string? ErrorMessage { get; set; } = null;



		public CudaKernelInfo()
		{
			// Parameterless constructor for serialization
		}

		

	}
}
