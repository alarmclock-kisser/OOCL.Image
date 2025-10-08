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

		// ---------- Forward Single Execution (Base64) ----------

		[HttpPost("request-cuda-execute-single-base64")]
		[ProducesResponseType(typeof(KernelExecuteResult), 200)]
		[ProducesResponseType(typeof(ProblemDetails), 400)]
		[ProducesResponseType(typeof(ProblemDetails), 404)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<ActionResult<KernelExecuteResult>> RequestCudaExecuteSingleBase64Async([FromBody] KernelExecuteRequest request, [FromQuery] string? preferredClientApiUrl = null)
		{
			var worker = await this.ResolveOrRegisterWorker(preferredClientApiUrl);
			if (worker == null) return this.NotFound(this.Problem("No CUDA worker registered", "No reachable CUDA worker.", 404));

			if (string.IsNullOrWhiteSpace(request?.KernelCode))
				return this.BadRequest(this.Problem("Invalid request", "Missing Kernel source code", 400));

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
				return this.NotFound(this.Problem("No CUDA worker registered", "No reachable CUDA worker.", 404));

			int len = arraySize.GetValueOrDefault(1024);
			var rnd = new Random();
			var floats = new float[len];
			for (int i = 0; i < len; i++)
				floats[i] = (float) (rnd.NextDouble() * 199.998 - 99.999);

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
				ArgumentNames = new[] { "input", "output", "cutoffMode", "length" },
				ArgumentValues = new[] { "0", "0", "0", len.ToString() }
			};

			var forward = await this.ForwardKernelExecuteBase64Async(worker, req);
			if (!forward.Success)
				return this.StatusCode(forward.StatusCode, forward.Problem!);

			return this.Ok(forward.Result);
		}

		// ---------- Core Forward Logic ----------

		private record ForwardResult(bool Success, int StatusCode, KernelExecuteResult? Result, ProblemDetails? Problem);

		private async Task<ForwardResult> ForwardKernelExecuteBase64Async(string workerBase, KernelExecuteRequest request)
		{
			var normalized = NormalizeWorkerBase(workerBase);

			var urls = BuildCandidateUrls(normalized, "api/Cuda/request-generic-execution-single-base64",
				"Cuda/request-generic-execution-single-base64");

			var query = BuildQuery(new Dictionary<string, string?>
			{
				["kernelCode"] = request.KernelCode,
				["inputDataType"] = request.InputDataType,
				["outputDataLength"] = request.OutputDataLength,
				["outputDataType"] = request.OutputDataType,
				["workDimension"] = request.WorkDimension > 0 ? request.WorkDimension.ToString() : "1",
				["deviceName"] = string.IsNullOrWhiteSpace(request.DeviceName) ? null : request.DeviceName,
				["deviceIndex"] = request.DeviceIndex?.ToString()
			});

			var jsonBody = "\"" + (request.InputDataBase64 ?? "").Replace("\"", "\\\"") + "\"";
			var jsonContent = new StringContent(jsonBody, Encoding.UTF8, "application/json");

			using var http = this.CreateInsecureHttpClient();
			foreach (var baseUrl in urls)
			{
				var finalUrl = baseUrl + query;
				await this.logger.LogAsync($"Forwarding execution to {finalUrl}", nameof(ExternalCudaController));

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
			if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
			var baseUrl = raw.Trim().TrimEnd('/');
			while (baseUrl.EndsWith("/api", StringComparison.OrdinalIgnoreCase))
				baseUrl = baseUrl[..^4].TrimEnd('/');
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

		private HttpClient CreateInsecureHttpClient()
		{
			// Gemeinsame Factory + spezieller Handler (nur solange self-signed)
			var handler = new HttpClientHandler
			{
				ServerCertificateCustomValidationCallback = (_, _, _, _) => true
			};
			var client = new HttpClient(handler)
			{
				Timeout = TimeSpan.FromSeconds(30)
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

		private async Task<bool> IsWorkerOnline(string workerUrl)
		{
			bool result = false;

			try
			{
				// Via HttpClient GET /api/Cuda/status
				using var http = this.CreateInsecureHttpClient();
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

				// Try get status.Index >= 0 ? status.DeviceName : null
				var parts = status.Split(';', 2);
				if (parts.Length == 2 && int.TryParse(parts[0], out int idx) && idx >= 0)
				{
					return parts[1];
				}

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
