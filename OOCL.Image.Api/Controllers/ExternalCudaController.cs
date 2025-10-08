using Microsoft.AspNetCore.Mvc;
using OOCL.Image.Core;
using OOCL.Image.Shared;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace OOCL.Image.Api.Controllers
{
	[ApiController]
	[Route("api/[controller]")]
	public class ExternalCudaController : ControllerBase
	{
		private readonly RollingFileLogger logger;
		private readonly WebApiConfig webApiConfig;
		private readonly ExternalCudaService cudaService;

		public ExternalCudaController(RollingFileLogger rollingFileLogger, WebApiConfig webApiConfig, ExternalCudaService cudaService)
		{
			this.logger = rollingFileLogger;
			this.webApiConfig = webApiConfig;
			this.cudaService = cudaService ?? throw new ArgumentNullException(nameof(cudaService));
		}

		[HttpGet("status")]
		[ProducesResponseType(typeof(string), 200)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<ActionResult<string>> GetStatus()
		{
			try
			{
				string status = "OK";

				if (!this.webApiConfig.CudaWorkerAddresses.Any())
				{
					status = "No CUDA worker registered.";
				}
				else
				{
					status += ". (" + this.webApiConfig.CudaWorkerAddresses.Count + " worker(s) registered) ";
				}

				await logger.LogAsync($"Status requested: {status}", nameof(ExternalCudaController));
				return this.Ok(status);
			}
			catch (Exception ex)
			{
				await logger.LogAsync($"Exception in GetStatus: {ex.Message}", nameof(ExternalCudaController));
				return this.StatusCode(500, new ProblemDetails
				{
					Status = 500,
					Title = "Exception",
					Detail = ex.Message
				});
			}
		}

		[HttpPost("register")]
		[ProducesResponseType(typeof(bool), 200)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<ActionResult<bool>> RegisterAddress([FromBody] string workerApiAddress)
		{
			workerApiAddress = workerApiAddress?.Trim().TrimEnd('/') ?? "";
			if (string.IsNullOrWhiteSpace(workerApiAddress))
			{
				await this.logger.LogAsync("Empty workerApiAddress provided for registration.", nameof(ExternalCudaController));
				return this.StatusCode(500, new ProblemDetails
				{
					Status = 500,
					Title = "Worker address missing",
					Detail = "Empty workerApiAddress"
				});
			}

			// Nur für diesen Call Zertifikatsprüfung deaktivieren (self-signed tolerieren)
			var handler = new HttpClientHandler
			{
				ServerCertificateCustomValidationCallback = (_, _, _, _) => true
			};
			using var httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };

			var statusUrl = $"{workerApiAddress}/api/Cuda/status";
			await this.logger.LogAsync($"Probing CUDA worker at {statusUrl}", nameof(ExternalCudaController));

			try
			{
				var response = await httpClient.GetAsync(statusUrl);
				if (response.IsSuccessStatusCode)
				{
					if (this.webApiConfig.CudaWorkerAddresses.Contains(workerApiAddress))
					{
						await this.logger.LogAsync($"CUDA worker already registered: {workerApiAddress}", nameof(ExternalCudaController));
						return this.Ok(true);
					}
					else
					{
						this.webApiConfig.CudaWorkerAddresses.Add(workerApiAddress);
					}
					
					await this.logger.LogAsync($"Registered CUDA worker: {workerApiAddress}", nameof(ExternalCudaController));
					return this.Ok(true);
				}
				else
				{
					await this.logger.LogAsync($"Probe failed {statusUrl} -> {response.StatusCode}", nameof(ExternalCudaController));
					return this.StatusCode(500, new ProblemDetails
					{
						Status = 500,
						Title = "Probe failed",
						Detail = $"GET {statusUrl} returned {(int) response.StatusCode} {response.StatusCode}"
					});
				}
			}
			catch (Exception ex)
			{
				await this.logger.LogAsync($"Exception probing {statusUrl}: {ex.Message}", nameof(ExternalCudaController));
				return this.StatusCode(500, new ProblemDetails
				{
					Status = 500,
					Title = "Exception during probe",
					Detail = ex.Message
				});
			}
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
