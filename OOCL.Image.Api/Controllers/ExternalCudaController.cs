using Microsoft.AspNetCore.Mvc;
using OOCL.Image.Core;
using OOCL.Image.Shared;
using System.Diagnostics;

namespace OOCL.Image.Api.Controllers
{
	public class ExternalCudaController : ControllerBase
	{
		private readonly ExternalCudaService cudaService;

		public ExternalCudaController(ExternalCudaService cudaService)
		{
			this.cudaService = cudaService ?? throw new ArgumentNullException(nameof(cudaService));
		}

		[HttpPost("request-cuda-execute")]
		[ProducesResponseType(typeof(KernelExecuteResult), 200)]
		[ProducesResponseType(typeof(ProblemDetails), 400)]
		[ProducesResponseType(typeof(ProblemDetails), 404)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<ActionResult<KernelExecuteResult>> RequestCudaExecuteAsync([FromBody] KernelExecuteRequest request)
		{
			if (request == null)
			{
				return this.BadRequest(new ProblemDetails { Title = "Invalid request", Detail = "The request body is null." });
			}

			try
			{
				KernelExecuteResult result = new();

				if (string.IsNullOrWhiteSpace(request.KernelName) && string.IsNullOrWhiteSpace(request.KernelCode))
				{
					return this.BadRequest(new ProblemDetails { Title = "Invalid request.", Detail = "Kernel name or Kernel code are required.", Status = 400 });
				}

				// Try initialize OpenCL (if not done yet)
				if (request.DeviceIndex.HasValue)
				{
					this.cudaService.Initialize(request.DeviceIndex.Value);
				}
				else if (!string.IsNullOrWhiteSpace(request.DeviceName))
				{
					this.cudaService.Initialize(request.DeviceName);
				}
				else
				{
					this.cudaService.Initialize();
				}

				if (string.IsNullOrWhiteSpace(request.OutputDataLength) || !int.TryParse(request.OutputDataLength, out int outputLength) || outputLength < 0)
				{
					request.OutputDataLength = "0";
				}

				if (!this.cudaService.Initialized)
				{
					return this.StatusCode(500, new ProblemDetails { Title = "OpenCL service is not initialized.", Detail = "Could not initialize OpenCL service with the specified device.", Status = 500 });
				}

				// If no KernelName given, try get it from code
				if (string.IsNullOrWhiteSpace(request.KernelName) && !string.IsNullOrWhiteSpace(request.KernelCode))
				{
					request.KernelName = this.cudaService.GetKernelName(request.KernelCode);
					if (string.IsNullOrWhiteSpace(request.KernelName))
					{
						return this.BadRequest(new ProblemDetails { Title = "Invalid request.", Detail = "Could not determine kernel name from provided kernel code.", Status = 400 });
					}
				}

				Stopwatch sw = Stopwatch.StartNew();
				object[]? execResult = await this.cudaService.ExecuteGenericDataKernelAsync(request.KernelName, request.KernelCode, request.ArgumentTypes.ToArray(), request.ArgumentNames.ToArray(), request.ArgumentValues.ToArray(), request.WorkDimension, request.InputDataBase64, request.InputDataType, request.OutputDataType, request.OutputDataLength, request.DeviceIndex, request.DeviceName);
				sw.Stop();

				if (execResult == null || execResult.Length == 0)
				{
					result.KernelName = request.KernelName ?? "N/A";
					result.Success = false;
					result.OutputDataBase64 = null;
					result.OutputDataLength = "0";
					result.Message = "Kernel execution failed or returned no data.";
					result.OutputPointer = null;
					result.ExecutionTimeMs = sw.Elapsed.TotalMilliseconds;
					return this.Ok(result);
				}

				result.KernelName = request.KernelName ?? "N/A";
				result.Success = execResult != null;
				result.OutputDataBase64 = execResult != null && execResult.Length > 0 && execResult[0] is byte[] outputBytes ? Convert.ToBase64String(outputBytes) : null;
				result.OutputDataType = request.OutputDataType;
				result.OutputDataLength = execResult != null && execResult.Length > 0 && execResult[0] is byte[] outputBytes2 ? outputBytes2.Length.ToString() : "0";
				result.Message = result.Success ? "Kernel executed successfully." : "Kernel execution failed.";
				result.OutputPointer = execResult != null && execResult.Length > 0 ? execResult[0]?.GetHashCode().ToString() : null;

				return this.Ok(result);
			}
			catch (Exception ex)
			{
				return this.StatusCode(500, new ProblemDetails { Title = "An error occurred while processing the request.", Detail = ex.Message, Status = 500 });
			}
		}

	}
}
