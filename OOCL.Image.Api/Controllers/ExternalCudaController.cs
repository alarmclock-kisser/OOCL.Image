using alarmclockkisser.KernelDtos;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Mvc;
using OOCL.Image.Core;
using OOCL.Image.Shared;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using static System.Net.WebRequestMethods;

namespace OOCL.Image.Api.Controllers
{
	[ApiController]
	[Route("api/[controller]")]
	public class ExternalCudaController : ControllerBase
	{
		private readonly RollingFileLogger logger;
		private readonly WebApiConfig webApiConfig;
		private readonly IHttpClientFactory httpClientFactory;

		public ExternalCudaController(RollingFileLogger rollingFileLogger, WebApiConfig webApiConfig, IHttpClientFactory httpClientFactory)
		{
			this.logger = rollingFileLogger;
			this.webApiConfig = webApiConfig;
			this.httpClientFactory = httpClientFactory;
		}

		// ---------- Status ----------

		[HttpGet("status")]
		[ProducesResponseType(typeof(string), 200)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<ActionResult<string>> GetStatus()
		{
			try
			{
				var workerUrls = this.webApiConfig.CudaWorkerAddresses;
				List<string> statuses = [];
				foreach (var url in workerUrls)
				{
					var isOnline = await this.IsWorkerOnline(url);
					if (isOnline)
					{
						var deviceName = await this.IsWorkerInitialized(url);
						if (!string.IsNullOrWhiteSpace(deviceName))
						{
							statuses.Add($"1;{deviceName} ({url})");
						}
						else
						{
							statuses.Add($"0;Not initialized ({url})");
						}
					}
					else
					{
						statuses.Add($"-1;Offline ({url})");
					}
				}

				await this.logger.LogAsync($"Status(es) requested: {statuses.Count}", nameof(ExternalCudaController));
				return this.Ok(statuses.Count > 0 ? string.Join('|', statuses) : "No CUDA workers registered.");
			}
			catch (Exception ex)
			{
				await this.logger.LogAsync($"Exception in GetStatus: {ex.Message}", nameof(ExternalCudaController));
				return this.StatusCode(500, this.Problem("Exception", ex.Message, 500));
			}
		}

		[HttpGet("is-worker-registered")]
		[ProducesResponseType(typeof(bool), 200)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<ActionResult<string>> GetWorkerRegistered([FromQuery] string workerUrl)
		{
			workerUrl = (workerUrl ?? "").Trim().TrimEnd('/');
			if (string.IsNullOrWhiteSpace(workerUrl))
			{
				await this.logger.LogAsync("Empty workerUrl provided for is-worker-registered.", nameof(ExternalCudaController));
				return this.StatusCode(500, this.Problem("Worker address missing", "Empty workerUrl", 500));
			}
			var normalized = NormalizeWorkerBase(workerUrl);
			bool isRegistered = this.webApiConfig.CudaWorkerAddresses.Contains(normalized);
			await this.logger.LogAsync($"is-worker-registered {normalized} -> {isRegistered}", nameof(ExternalCudaController));

			return this.Ok(isRegistered);
		}

		[HttpGet("is-worker-online")]
		[ProducesResponseType(typeof(bool), 200)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<ActionResult<string>> GetWorkerOnline([FromQuery] string workerUrl)
		{
			workerUrl = (workerUrl ?? "").Trim().TrimEnd('/');
			if (string.IsNullOrWhiteSpace(workerUrl))
			{
				await this.logger.LogAsync("Empty workerUrl provided for is-worker-online.", nameof(ExternalCudaController));
				return this.StatusCode(500, this.Problem("Worker address missing", "Empty workerUrl", 500));
			}

			var normalized = NormalizeWorkerBase(workerUrl);
			
			bool isOnline = await this.IsWorkerOnline(normalized);
			
			await this.logger.LogAsync($"is-worker-online {normalized} -> {isOnline}", nameof(ExternalCudaController));
			return this.Ok(isOnline);
		}

		// ---------- Registration ----------

		[HttpPost("register")]
		[Consumes("application/json", "text/plain")]
		[ProducesResponseType(typeof(bool), 200)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<ActionResult<bool>> RegisterAddress([FromBody] string workerApiAddress)
		{
			workerApiAddress = (workerApiAddress ?? "").Trim().TrimEnd('/');
			if (string.IsNullOrWhiteSpace(workerApiAddress))
			{
				await this.logger.LogAsync("Empty workerApiAddress provided for registration.", nameof(ExternalCudaController));
				return this.StatusCode(500, this.Problem("Worker address missing", "Empty workerApiAddress", 500));
			}

			var normalized = NormalizeWorkerBase(workerApiAddress);
			var statusUrl = $"{normalized}/api/Cuda/status";

			await this.logger.LogAsync($"Probing CUDA worker at {statusUrl}", nameof(ExternalCudaController));

			try
			{
				using var http = this.CreateInsecureHttpClient(); // temporär TLS relaxed
				var resp = await http.GetAsync(statusUrl);

				if (resp.IsSuccessStatusCode)
				{
					if (!this.webApiConfig.CudaWorkerAddresses.Contains(normalized))
					{
						this.webApiConfig.CudaWorkerAddresses.Add(normalized);
						await this.logger.LogAsync($"Registered CUDA worker: {normalized}", nameof(ExternalCudaController));
					}
					else
					{
						await this.logger.LogAsync($"CUDA worker already registered: {normalized}", nameof(ExternalCudaController));
					}
					return this.Ok(true);
				}

				await this.logger.LogAsync($"Probe failed {statusUrl} -> {resp.StatusCode}", nameof(ExternalCudaController));
				return this.StatusCode(500, this.Problem("Probe failed", $"GET {statusUrl} returned {(int) resp.StatusCode} {resp.StatusCode}", 500));
			}
			catch (Exception ex)
			{
				await this.logger.LogAsync($"Exception probing {statusUrl}: {ex.Message}", nameof(ExternalCudaController));
				return this.StatusCode(500, this.Problem("Exception during probe", ex.Message, 500));
			}
		}

		[HttpDelete("unregister")]
		[Consumes("application/json", "text/plain")]
		[ProducesResponseType(typeof(bool), 200)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<ActionResult<bool>> UnregisterAddress([FromBody] string workerApiAddress)
		{
			workerApiAddress = (workerApiAddress ?? "").Trim().TrimEnd('/');
			if (string.IsNullOrWhiteSpace(workerApiAddress))
			{
				await this.logger.LogAsync("Empty workerApiAddress provided for unregistration.", nameof(ExternalCudaController));
				return this.StatusCode(500, this.Problem("Worker address missing", "Empty workerApiAddress", 500));
			}

			var normalized = NormalizeWorkerBase(workerApiAddress);
			if (this.webApiConfig.CudaWorkerAddresses.Contains(normalized))
			{
				this.webApiConfig.CudaWorkerAddresses.Remove(normalized);
				await this.logger.LogAsync($"Unregistered CUDA worker: {normalized}", nameof(ExternalCudaController));
				return this.Ok(true);
			}
			else
			{
				await this.logger.LogAsync($"CUDA worker not found for unregistration: {normalized}", nameof(ExternalCudaController));
				return this.Ok(false);
			}
		}

		[HttpGet("refresh-workers")]
		[ProducesResponseType(typeof(IEnumerable<string>), 200)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<ActionResult<IEnumerable<string>>> RefreshWorkers()
		{
			var workerUrls = this.webApiConfig.CudaWorkerAddresses.ToList();
			List<string> validWorkers = [];
			foreach (var url in workerUrls)
			{
				if (await this.IsWorkerOnline(url))
				{
					validWorkers.Add(url);
				}
				else
				{
					validWorkers.Add("");
				}
			}

			this.webApiConfig.CudaWorkerAddresses = validWorkers.Where(u => !string.IsNullOrWhiteSpace(u)).ToList();
			await this.logger.LogAsync($"Refreshed CUDA workers. Valid count: {this.webApiConfig.CudaWorkerAddresses.Count}", nameof(ExternalCudaController));

			return this.Ok(validWorkers);
		}

		// ---------- Forward Single Execution (Base64) ----------

		[HttpPost("request-cuda-execute-single-base64")]
		[ProducesResponseType(typeof(KernelExecuteResult), 200)]
		[ProducesResponseType(typeof(ProblemDetails), 400)]
		[ProducesResponseType(typeof(ProblemDetails), 404)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<ActionResult<KernelExecuteResult>> RequestCudaExecuteSingleBase64Async([FromBody] KernelExecuteRequest request, [FromQuery] string? preferredClientApiUrl = null)
		{
			var worker = await this.ResolveOrRegisterWorker(preferredClientApiUrl);
			if (worker == null)
			{
				return this.NotFound(this.Problem("No CUDA worker registered", "No reachable CUDA worker.", 404));
			}

			if (string.IsNullOrWhiteSpace(request?.KernelCode))
			{
				return this.BadRequest(this.Problem("Invalid request", "Missing Kernel source code", 400));
			}

			var sw = Stopwatch.StartNew();
			var forwardResp = await this.ForwardKernelExecuteBase64Async(worker, request);
			sw.Stop();

			if (forwardResp == null)
			{
				return this.StatusCode(500, this.Problem("Internal error", "Forwarding returned null", 500));
			}

			if (!forwardResp.Success)
			{
				return this.StatusCode(forwardResp.StatusCode, forwardResp.Problem!);
			}

			if (forwardResp.Result != null)
			{
				forwardResp.Result.ExecutionTimeMs = sw.Elapsed.TotalMilliseconds;
			}

			return this.Ok(forwardResp.Result);
		}

		[HttpPost("request-cuda-execute-batch-base64")]
		[ProducesResponseType(typeof(KernelExecuteResult), 200)]
		[ProducesResponseType(typeof(ProblemDetails), 400)]
		[ProducesResponseType(typeof(ProblemDetails), 404)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<ActionResult<KernelExecuteResult>> RequestCudaExecuteBatchBase64Async([FromBody] KernelExecuteRequest request, [FromQuery] string? preferredClientApiUrl = null)
		{
			var worker = await this.ResolveOrRegisterWorker(preferredClientApiUrl);
			if (worker == null)
			{
				return this.NotFound(this.Problem("No CUDA worker registered", "No reachable CUDA worker.", 404));
			}

			if (string.IsNullOrWhiteSpace(request?.KernelCode))
			{
				return this.BadRequest(this.Problem("Invalid request", "Missing Kernel source code", 400));
			}

			var sw = Stopwatch.StartNew();
			var forwardResp = await this.ForwardKernelExecuteBase64Async(worker, request);
			sw.Stop();

			if (forwardResp == null)
			{
				return this.StatusCode(500, this.Problem("Internal error", "Forwarding returned null", 500));
			}

			if (!forwardResp.Success)
			{
				return this.StatusCode(forwardResp.StatusCode, forwardResp.Problem!);
			}

			if (forwardResp.Result != null)
			{
				forwardResp.Result.ExecutionTimeMs = sw.Elapsed.TotalMilliseconds;
			}

			return this.Ok(forwardResp.Result);
		}


		// ---------- Test Endpoint (Generates Random Data & Calls Worker) ----------

		[HttpGet("test-cuda-execute-single-base64")]
		[ProducesResponseType(typeof(KernelExecuteResult), 200)]
		[ProducesResponseType(typeof(ProblemDetails), 400)]
		[ProducesResponseType(typeof(ProblemDetails), 404)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<ActionResult<KernelExecuteResult>> TestCudaExecuteSingleBase64Async([FromQuery] int? arraySize = 1024, [FromQuery] string? specificClientApiUrl = null, [FromQuery] string? specificCudaDevice = null)
		{
			var worker = await this.ResolveOrRegisterWorker(specificClientApiUrl);
			if (worker == null)
			{
				return this.NotFound(this.Problem("No CUDA worker registered", "No reachable CUDA worker.", 404));
			}

			int len = arraySize.GetValueOrDefault(1024);
			var rnd = new Random();
			var floats = new float[len];
			for (int i = 0; i < len; i++)
			{
				floats[i] = (float) (rnd.NextDouble() * 199.998 - 99.999);
			}

			string inputBase64 = Convert.ToBase64String(floats.SelectMany(BitConverter.GetBytes).ToArray());

			string kernelCode = @"
					extern ""C"" __global__ void to_int_round_or_cut_f(
						const float* __restrict__ input,
						int* __restrict__ output,
						int cutoffMode,
						int length)
					{
						int gid = blockIdx.x * blockDim.x + threadIdx.x;
						if (gid >= length) return;

						float v = input[gid];
						int result;
						if (cutoffMode != 0) {
							result = (int)(v);
						} else {
							result = (v >= 0.0f)
								? (int)floorf(v + 0.5f)
								: (int)ceilf(v - 0.5f);
						}
						output[gid] = result;
					}";

			var req = new KernelExecuteRequest
			{
				DeviceName = specificCudaDevice ?? "",
				KernelCode = kernelCode,
				KernelName = "to_int_round_or_cut_f",
				InputDataBase64 = inputBase64,
				InputDataType = "float",
				OutputDataType = "int",
				OutputDataLength = len.ToString(),
				WorkDimension = 1,
				ArgumentNames = ["input", "output", "cutoffMode", "length"],
				ArgumentValues = ["0", "0", "0", len.ToString()],
				ArgumentTypes = ["float*", "int*", "int", "int"]
			};

			var forward = await this.ForwardKernelExecuteBase64Async(worker, req);
			if (!forward.Success)
			{
				return this.StatusCode(forward.StatusCode, forward.Problem!);
			}

			return this.Ok(forward.Result);
		}

		[HttpGet("test-cuda-execute-batch-base64")]
		[ProducesResponseType(typeof(KernelExecuteResult), 200)]
		[ProducesResponseType(typeof(ProblemDetails), 400)]
		[ProducesResponseType(typeof(ProblemDetails), 404)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<ActionResult<KernelExecuteResult>> TestCudaExecuteBatchBase64Async([FromQuery] int? arraySize = 1024, [FromQuery] int? arrayStride = 2, [FromQuery] string? specificClientApiUrl = null, [FromQuery] string? specificCudaDevice = null)
		{
			var worker = await this.ResolveOrRegisterWorker(specificClientApiUrl);
			if (worker == null)
			{
				return this.NotFound(this.Problem("No CUDA worker registered", "No reachable CUDA worker.", 404));
			}

			int len = arraySize ?? 1024;
			int stride = arrayStride ?? 2;
			var rnd = new Random();
			var floats = new float[len * stride];
			for (int i = 0; i < len * stride; i++)
			{
				floats[i] = (float)(rnd.NextDouble() * 199.998 - 99.999);
			}

			string[] inputBase64Chunks = new string[stride];
			for (int s = 0; s < stride; s++)
			{
				var chunk = new float[len];
				for (int i = 0; i < len; i++)
				{
					chunk[i] = floats[i * stride + s];
				}
				inputBase64Chunks[s] = Convert.ToBase64String(chunk.SelectMany(BitConverter.GetBytes).ToArray());
			}

			string kernelCode = @"
					extern ""C"" __global__ void to_int_round_or_cut_chunks_f(
						const float* __restrict__ input,
						int* __restrict__ output,
						int cutoffMode,
						int length)
					{
						int gid = blockIdx.x * blockDim.x + threadIdx.x;
						if (gid >= length) return;
						float v = input[gid];
						int result;
						if (cutoffMode != 0) {
							result = (int)(v);
						} else {
							result = (v >= 0.0f)
								? (int)floorf(v + 0.5f)
								: (int)ceilf(v - 0.5f);
						}
						output[gid] = result;
					}";
			var req = new KernelExecuteRequest
			{
				DeviceName = specificCudaDevice ?? "",
				KernelCode = kernelCode,
				KernelName = "to_int_round_or_cut_chunks_f",
				InputDataBase64 = null,
				InputDataBase64Chunks = inputBase64Chunks,
				InputDataType = "float",
				OutputDataType = "int",
				OutputDataLength = len.ToString(),
				WorkDimension = 1,
				ArgumentNames = ["input", "output", "cutoffMode", "length"],
				ArgumentValues = ["0", "0", " 0", len.ToString()],
				ArgumentTypes = ["float*", "int*", "int", "int"]
			};

			var forward = await this.ForwardKernelExecuteBase64Async(worker, req);
			if (!forward.Success)
			{
				return this.StatusCode(forward.StatusCode, forward.Problem!);
			}

			return this.Ok(forward.Result);
		}

		[HttpGet("test-cuda-execute-single-dto")]
		public async Task<ActionResult<KernelExecuteResult>> TestCudaExecuteSingleDtoAsync([FromQuery] int? arraySize = 1024, [FromQuery] string? specificClientApiUrl = null, [FromQuery] string? specificCudaDevice = null)
		{
			var worker = await this.ResolveOrRegisterWorker(specificClientApiUrl);
			if (worker == null)
			{
				return this.NotFound(this.Problem("No CUDA worker registered", "No reachable CUDA worker.", 404));
			}

			int len = arraySize ?? 1024;
			var rnd = new Random();
			var floats = new float[len];
			for (int i = 0; i < len; i++)
			{
				floats[i] = (float)(rnd.NextDouble() * 199.998 - 99.999);
			}

			string inputBase64 = Convert.ToBase64String(floats.SelectMany(BitConverter.GetBytes).ToArray());
			var dto = new KernelExecuteRequest
			{
				DeviceName = specificCudaDevice ?? "",
				KernelCode = @" ... ",
				KernelName = "to_int_round_or_cut_f",
				InputDataBase64 = inputBase64,
				InputDataType = "float",
				OutputDataType = "int",
				OutputDataLength = len.ToString(),
				WorkDimension = 1,
				ArgumentNames = new[] { "input", "output", "cutoffMode", "length" },
				ArgumentValues = new[] { "0", "0", "0", len.ToString() },
				ArgumentTypes = new[] { "float*", "int*", "int", "int" }
			};

			var sw = Stopwatch.StartNew();
			using var httpClient = this.CreateInsecureHttpClient();
			Console.WriteLine($"{worker}/api/ExternalCuda/request-cuda-execute-single");
			var resp = await httpClient.PostAsJsonAsync($"{worker}/api/api/ExternalCuda/request-cuda-execute-single", dto);
			sw.Stop();

			if (!resp.IsSuccessStatusCode)
			{
				var body = await resp.Content.ReadAsStringAsync();
				return this.StatusCode((int) resp.StatusCode, this.Problem("Error from forwarding", body, (int) resp.StatusCode));
			}

			var result = await resp.Content.ReadFromJsonAsync<KernelExecuteResult>();
			if (result != null)
			{
				result.ExecutionTimeMs = sw.Elapsed.TotalMilliseconds;
			}

			return this.Ok(result);
		}

		[HttpGet("test-cuda-execute-batch-dto")]
		[ProducesResponseType(typeof(KernelExecuteResult), 200)]
		[ProducesResponseType(typeof(ProblemDetails), 400)]
		[ProducesResponseType(typeof(ProblemDetails), 404)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<ActionResult<KernelExecuteResult>> TestCudaExecuteBatchDtoAsync([FromQuery] int? arraySize = 1024, [FromQuery] int? arrayStride = 2, [FromQuery] string? specificClientApiUrl = null, [FromQuery] string? specificCudaDevice = null)
		{
			var worker = await this.ResolveOrRegisterWorker(specificClientApiUrl);
			if (worker == null)
			{
				return this.NotFound(this.Problem("No CUDA worker registered", "No reachable CUDA worker.", 404));
			}
			int len = arraySize ?? 1024;
			int stride = arrayStride ?? 2;
			var rnd = new Random();
			var floats = new float[len * stride];
			for (int i = 0; i < len * stride; i++)
			{
				floats[i] = (float)(rnd.NextDouble() * 199.998 - 99.999);
			}
			string[] inputBase64Chunks = new string[stride];
			for (int s = 0; s < stride; s++)
			{
				var chunk = new float[len];
				for (int i = 0; i < len; i++)
				{
					chunk[i] = floats[i * stride + s];
				}
				inputBase64Chunks[s] = Convert.ToBase64String(chunk.SelectMany(BitConverter.GetBytes).ToArray());
			}
			var dto = new KernelExecuteRequest
			{
				DeviceName = specificCudaDevice ?? "",
				KernelCode = @"
					extern ""C"" __global__ void to_int_round_or_cut_chunks_f(
						const float* __restrict__ input,
						int* __restrict__ output,
						int cutoffMode,
						int length)
					{
						int gid = blockIdx.x * blockDim.x + threadIdx.x;
						if (gid >= length) return;
						float v = input[gid];
						int result;
						if (cutoffMode != 0) {
							result = (int)(v);
						} else {
							result = (v >= 0.0f)
								? (int)floorf(v + 0.5f)
								: (int)ceilf(v - 0.5f);
						}
						output[gid] = result;
					}",
				KernelName = "to_int_round_or_cut_chunks_f",
				InputDataBase64 = null,
				InputDataBase64Chunks = inputBase64Chunks,
				InputDataType = "float",
				OutputDataType = "int",
				OutputDataLength = len.ToString(),
				WorkDimension = 1,
				ArgumentNames = ["input", "output", "cutoffMode", "length"],
				ArgumentValues = ["0", "0", "0", len.ToString()],
				ArgumentTypes = ["float*", "int*", "int", "int"]
				};

			var sw = Stopwatch.StartNew();
			using var httpClient = this.CreateInsecureHttpClient();
			Console.WriteLine($"{worker}/api/api/ExternalCuda/request-cuda-execute-batch");
			var resp = await httpClient.PostAsJsonAsync($"{worker}/api/ExternalCuda/request-cuda-execute-batch", dto);
			sw.Stop();
			if (!resp.IsSuccessStatusCode)
			{
				var body = await resp.Content.ReadAsStringAsync();
				return this.StatusCode((int)resp.StatusCode, this.Problem("Error from forwarding", body, (int)resp.StatusCode));
			}

			var result = await resp.Content.ReadFromJsonAsync<KernelExecuteResult>();
			if (result != null)
			{
				result.ExecutionTimeMs = sw.Elapsed.TotalMilliseconds;
			}

			return this.Ok(result);
		}


		// ---------- Core Forward Logic ----------

		private record ForwardResult(bool Success, int StatusCode, KernelExecuteResult? Result, ProblemDetails? Problem);

		private async Task<ForwardResult> ForwardKernelExecuteBase64Async(string workerBase, KernelExecuteRequest request)
		{
			var normalized = NormalizeWorkerBase(workerBase);

			// Korrigierte URL-Auswahl + Body-Bau + Logging vor dem Senden
			IEnumerable<string> urls = [];
			if (request.InputDataBase64Chunks != null && request.InputDataBase64Chunks.Any())
			{
				// Wenn CHUNKS vorhanden → batch endpoints (Array im Body)
				urls = BuildCandidateUrls(normalized,
					"api/Cuda/request-generic-execution-batch-base64",
					"Cuda/request-generic-execution-batch-base64");
			}
			else
			{
				// KEINE chunks → single endpoints (ein JSON-String im Body)
				urls = BuildCandidateUrls(normalized,
					"api/Cuda/request-generic-execution-single-base64",
					"Cuda/request-generic-execution-single-base64");
			}

			// Basis-Query-Parameter (ohne argNames/argValues)
			var baseParams = new Dictionary<string, string?>
			{
				["kernelCode"] = request.KernelCode,
				["inputDataType"] = request.InputDataType,
				["outputDataLength"] = request.OutputDataLength,
				["outputDataType"] = request.OutputDataType,
				["deviceName"] = string.IsNullOrWhiteSpace(request.DeviceName) ? null : request.DeviceName,
				["deviceIndex"] = request.DeviceIndex?.ToString(),
				["workDimension"] = request.WorkDimension > 0 ? request.WorkDimension.ToString() : "1"
			};

			// Build base query (may be empty or start with '?')
			var baseQuery = BuildQuery(baseParams); // e.g. "?kernelCode=...&..."

			// Normalize into list of key=value fragments (without leading '?')
			var fragments = new List<string>();
			if (!string.IsNullOrWhiteSpace(baseQuery))
			{
				var trimmed = baseQuery.TrimStart('?');
				if (!string.IsNullOrEmpty(trimmed))
				{
					fragments.Add(trimmed);
				}
			}

			// Append argNames/argValues as repeated query params (paired)
			if (request.ArgumentNames != null)
			{
				// If argument values shorter, use empty string for missing ones
				int nameCount = request.ArgumentNames.Count();
				for (int i = 0; i < nameCount; i++)
				{
					var name = request.ArgumentNames.ElementAt(i) ?? string.Empty;
					// defensive: if caller accidentally passed a CSV in a single element, try to split it
					if (nameCount == 1 && name.Contains(',') && (request.ArgumentValues == null || request.ArgumentValues.Count() == 1 && request.ArgumentValues.FirstOrDefault()?.Contains(',') == true))
					{
						// split both names and values if both are CSV
						var splitNames = name.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToArray();
						var splitVals = (request.ArgumentValues != null ? request.ArgumentValues.FirstOrDefault() ?? string.Empty : string.Empty)
							.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToArray();

						int n = Math.Max(splitNames.Length, splitVals.Length);
						for (int j = 0; j < n; j++)
						{
							var nm = j < splitNames.Length ? splitNames[j] : string.Empty;
							var val = j < splitVals.Length ? splitVals[j] : string.Empty;
							fragments.Add($"argNames={Uri.EscapeDataString(nm)}");
							fragments.Add($"argValues={Uri.EscapeDataString(val)}");
						}
						// done with CSV special-case
						break;
					}
					else
					{
						var val = request.ArgumentValues?.ElementAtOrDefault(i) ?? string.Empty;
						fragments.Add($"argNames={Uri.EscapeDataString(name)}");
						fragments.Add($"argValues={Uri.EscapeDataString(val)}");
					}
				}
			}
			else if (request.ArgumentValues != null)
			{
				// No names but values exist -> still send values with empty names (edge-case)
				foreach (var v in request.ArgumentValues)
				{
					fragments.Add($"argNames=");
					fragments.Add($"argValues={Uri.EscapeDataString(v ?? string.Empty)}");
				}
			}

			// Rebuild final query string
			var query = fragments.Count > 0 ? "?" + string.Join("&", fragments) : string.Empty;

			// JSON-Body: Array wenn chunks, sonst einzelner JSON-String
			string jsonBody;
			if (request.InputDataBase64Chunks != null && request.InputDataBase64Chunks.Any())
			{
				try
				{
					jsonBody = JsonSerializer.Serialize(request.InputDataBase64Chunks);
				}
				catch (Exception ex)
				{
					await this.logger.LogAsync($"Failed to serialize InputDataBase64Chunks: {ex.Message}", nameof(ExternalCudaController));
					return new(false, 500, null, this.Problem("Serialization failed", ex.Message, 500));
				}
			}
			else
			{
				jsonBody = JsonSerializer.Serialize(request.InputDataBase64 ?? string.Empty);
			}

			// Log Preview (gekürzt) + Ziele für Diagnose
			await this.logger.LogAsync($"Forwarding to candidates: {string.Join(',', urls)} | Query: {query} | BodyPreview: {(jsonBody.Length > 2000 ? jsonBody.Substring(0,2000) + "..." : jsonBody)}", nameof(ExternalCudaController));

			var jsonContent = new StringContent(jsonBody, Encoding.UTF8, "application/json");

			// Sende an Kandidaten
			using var http = this.CreateInsecureHttpClient();
			foreach (var baseUrl in urls)
			{
				var finalUrl = baseUrl + query;
				await this.logger.LogAsync($"POST -> {finalUrl}", nameof(ExternalCudaController));
				HttpResponseMessage resp;
				try
				{
					resp = await http.PostAsync(finalUrl, jsonContent);
				}
				catch (Exception ex)
				{
					await this.logger.LogAsync($"Forward EX {finalUrl}: {ex.Message}", nameof(ExternalCudaController));
					continue;
				}

				if (resp.StatusCode == HttpStatusCode.NotFound)
				{
					continue; // nächste Variante
				}

				string body = await resp.Content.ReadAsStringAsync();

				if (resp.IsSuccessStatusCode)
				{
					KernelExecuteResult? workerResult = null;
					try
					{
						workerResult = JsonSerializer.Deserialize<KernelExecuteResult>(body, new JsonSerializerOptions
						{
							PropertyNameCaseInsensitive = true
						});
					}
					catch (Exception ex)
					{
						await this.logger.LogAsync($"Deserialize fail {finalUrl}: {ex.Message}", nameof(ExternalCudaController));
						return new(false, 500, null, this.Problem("Deserialization failed", ex.Message, 500));
					}

					if (workerResult == null)
					{
						return new(false, 500, null, this.Problem("Empty worker result", "Worker returned empty or invalid JSON.", 500));
					}

					workerResult.KernelName ??= request.KernelName ?? "UnnamedKernel";
					return new(true, (int) resp.StatusCode, workerResult, null);
				}

				// Fehler zurückgeben
				return new(false, (int) resp.StatusCode,
					null,
					this.Problem("Error from CUDA worker", body, (int) resp.StatusCode));
			}

			return new(false, 404, null, this.Problem("All variants failed", "All candidate URLs returned 404 or errors.", 404));
		}


		// ---------- Helpers ----------

		private async Task<string?> ResolveOrRegisterWorker(string? candidate)
		{
			if (!string.IsNullOrWhiteSpace(candidate))
			{
				var normalized = NormalizeWorkerBase(candidate);
				if (!this.webApiConfig.CudaWorkerAddresses.Contains(normalized))
				{
					var reg = await this.RegisterAddress(normalized);
					// Fix: ActionResult<bool> prüfen, ob OkResult mit Value true
					if (reg.Value is not true)
					{
						return null;
					}
				}
				return normalized;
			}

			// Fallback: ersten registrierten nehmen
			return this.webApiConfig.CudaWorkerAddresses.FirstOrDefault();
		}

		private static string NormalizeWorkerBase(string raw)
		{
			if (string.IsNullOrWhiteSpace(raw))
			{
				return string.Empty;
			}

			var baseUrl = raw.Trim().TrimEnd('/');
			while (baseUrl.EndsWith("/api", StringComparison.OrdinalIgnoreCase))
			{
				baseUrl = baseUrl[..^4].TrimEnd('/');
			}

			return baseUrl;
		}

		private static IEnumerable<string> BuildCandidateUrls(string baseUrl, params string[] relPaths)
		{
			var list = new List<string>();
			foreach (var rel in relPaths)
			{
				list.Add($"{baseUrl}/{rel.TrimStart('/')}");
				// Doppelt verschachtelte Variante falls PathBase intern erneut /api anhängt
				list.Add($"{baseUrl}/api/{rel.TrimStart('/')}");
			}
			return list.Distinct();
		}

		private static string BuildQuery(Dictionary<string, string?> kv)
		{
			var sb = new StringBuilder();
			foreach (var pair in kv.Where(p => !string.IsNullOrWhiteSpace(p.Value)))
			{
				sb.Append(sb.Length == 0 ? '?' : '&');
				sb.Append(Uri.EscapeDataString(pair.Key));
				sb.Append('=');
				sb.Append(Uri.EscapeDataString(pair.Value!));
			}
			return sb.ToString();
		}

		private HttpClient CreateInsecureHttpClient(int maxTimeout = 30)
		{
			// Gemeinsame Factory + spezieller Handler (nur solange self-signed)
			var handler = new HttpClientHandler
			{
				ServerCertificateCustomValidationCallback = (_, _, _, _) => true
			};
			var client = new HttpClient(handler)
			{
				Timeout = TimeSpan.FromSeconds(maxTimeout)
			};
			client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
			return client;
		}

		private ProblemDetails Problem(string title, string? detail, int status)
			=> new()
			{
				Title = title,
				Detail = detail,
				Status = status
			};

		private async Task<bool> IsWorkerOnline(string workerUrl, int maxTimeout = 5)
		{
			bool result = false;

			try
			{
				// Via HttpClient GET /api/Cuda/status
				using var http = this.CreateInsecureHttpClient(maxTimeout);
				var resp = await http.GetAsync($"{workerUrl}/api/Cuda/status");
				result = resp.IsSuccessStatusCode;
			}
			catch (Exception ex)
			{
				Console.WriteLine($"IsWorkerOnline Exception: {ex.Message}");
			}

			return result;
		}

		private async Task<string?> IsWorkerInitialized(string workerUrl)
		{
			string? status = null;
			try
			{
				// Via HttpClient GET /api/Cuda/status
				using var http = this.CreateInsecureHttpClient();
				var resp = await http.GetAsync($"{workerUrl}/api/Cuda/status");
				if (resp.IsSuccessStatusCode)
				{
					status = await resp.Content.ReadAsStringAsync();
				}
				if (status == null)
				{
					return null;
				}

				var content = await resp.Content.ReadAsStringAsync();
				if (string.IsNullOrWhiteSpace(content))
				{
					return null;
				}

				// Try JSON parse
				try
				{
					using var doc = JsonDocument.Parse(content);
					var root = doc.RootElement;
					if (root.TryGetProperty("Initialized", out var pInit) && pInit.GetBoolean()
						&& root.TryGetProperty("DeviceName", out var pName) && pName.ValueKind == JsonValueKind.String)
					{
						return pName.GetString();
					}
				}
				catch (JsonException) { /* invalid json — already tried legacy format */ }

				return null;
			}
			catch (Exception ex)
			{
				Console.WriteLine($"IsWorkerInitialized Exception: {ex.Message}");
			}

			return status;
		}
	}
}
