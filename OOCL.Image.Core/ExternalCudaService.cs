using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OOCL.Image.Core
{
	public class ExternalCudaService
	{
		public bool Initialized { get; private set; } = false;



		public void Initialize(int deviceIndex = -1)
		{

		}

		public void Initialize(string deviceName)
		{

		}

		public string? GetKernelName(string kernelCode)
		{
			return null;
		}

		public async Task<object[]?> ExecuteGenericDataKernelAsync(string? kernelName, string? kernelCode, string[] argumentTypes, string[] argumentNames, object[] argumentValues, int workDimension, string? inputDataBase64, string? inputDataType, string? outputDataType, string? outputDataLength, int? deviceIndex = null, string? deviceName = null)
		{
			await Task.Delay(10);
			return null;
		}




	}
}
