using alarmclockkisser.KernelDtos;
using OOCL.Image.Shared;
using OOCL.Image.Shared.CUDA;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace OOCL.Image.Client
{
	public class WorkerApiClient
	{
		private readonly internalWorkerClient internalClient;
		private readonly HttpClient httpClient;
		private readonly RollingFileLogger logger;
		private readonly string baseUrl;
		private JsonSerializerOptions jsonSerializerOptions = new() { PropertyNameCaseInsensitive = true };

		public string BaseUrl => this.baseUrl;
		private ApiConfiguration? apiConfigCache = null;


		public bool UseNoCertHttpClient { get; }

		public WorkerApiClient(RollingFileLogger? logger, HttpClient? httpClient, bool initializeApiConfig = true, bool useNoCertHttpClient = false)
		{
			this.logger = logger ?? new RollingFileLogger(1024, false, null, "log_worker-client_");
			this.UseNoCertHttpClient = useNoCertHttpClient;

			if (useNoCertHttpClient)
			{
				var handler = new HttpClientHandler();
				handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true;
				httpClient = new HttpClient(handler) { BaseAddress = httpClient?.BaseAddress ?? throw new InvalidOperationException("HttpClient.BaseAddress is not set. Configure it in DI registration.") };
			}
			else
			{
				this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
			}

			this.baseUrl = this.httpClient?.BaseAddress?.ToString().TrimEnd('/') ?? throw new InvalidOperationException("HttpClient.BaseAddress is not set. Configure it in DI registration.");
			this.internalClient = new internalWorkerClient(this.baseUrl, this.httpClient);

			if (initializeApiConfig)
			{
				_ = Task.Run(async () =>
				{
					this.apiConfigCache = await this.GetApiConfigAsync(true);
				});
			}
		}

		public async Task LogAsync(string message)
		{
			await this.logger.LogAsync("WORKERAPI: " + message, nameof(WorkerApiClient) + "*");
		}


		public async Task<string[]> GetWorkerLogAsync(int maxEntries = 1024, bool showRefreshedLogMessage = true)
		{
			if (showRefreshedLogMessage)
			{
				await this.logger.LogAsync($" - [TICK] - Refreshed Client Logs.", nameof(WorkerApiClient) + "*");
			}
			try
			{
				var entries = await this.logger.GetRecentLogsAsync(maxEntries);
				return entries.ToArray();
			}
			catch (Exception ex)
			{
				await this.logger.LogExceptionAsync(ex, nameof(WorkerApiClient));
				return [];
			}
		}

		public async Task<CudaStatusInfo> GetStatusAsync()
		{
			await this.logger.LogAsync("WORKERAPI: Called GetStatusAsync()", nameof(WorkerApiClient));
			try
			{
				return await this.internalClient.StatusAsync();
			}
			catch (Exception ex)
			{
				await this.logger.LogExceptionAsync(ex, nameof(WorkerApiClient));
				return new CudaStatusInfo
				{
					ErrorMessage = ex.Message
				};
			}
		}


		public async Task<string> RegisterAsync()
		{
			await this.logger.LogAsync("WORKERAPI: Called RegisterAsync()", nameof(WorkerApiClient));
			try
			{
				return await this.internalClient.ConnectToServerAsync("");
			}
			catch (Exception ex)
			{
				await this.logger.LogExceptionAsync(ex, nameof(WorkerApiClient));
				return "Connecting to server failed!";
			}
		}

		public async Task<string> UnregisterAsync()
		{
			try
			{
				return await this.internalClient.UnregisterAsync() ? "Un-register from server success." : "Un-register from server failed!";
			}
			catch (Exception ex)
			{
				await this.logger.LogExceptionAsync(ex, nameof(WorkerApiClient));
				return "Unregistering from server failed!";
			}
		}


		public async Task<ApiConfiguration> GetApiConfigAsync(bool forceReload = false)
		{
			// Logging
			await this.logger.LogAsync($"WORKERAPI: Called GetApiConfigAsync(forceReload: {forceReload})", nameof(WorkerApiClient));
			if (this.apiConfigCache != null && !forceReload)
			{
				return this.apiConfigCache;
			}

			try
			{
				return await this.internalClient.ConfigAsync();
			}
			catch (Exception ex)
			{
				await this.logger.LogExceptionAsync(ex, nameof(WorkerApiClient));
				return new ApiConfiguration();
			}
		}

		public async Task<KernelExecuteResult> RequestKernelExecutionAsync(KernelExecuteRequest request)
		{
			await this.logger.LogAsync($"WORKERAPI: Called RequestKernelExecutionAsync(Kernel: {request.KernelName}, DeviceIndex: {request.DeviceIndex}, InputDataSize: {(request.InputDataBase64 != null ? request.InputDataBase64.Length : 0)}, InputDataChunks: {(request.InputDataBase64Chunks != null ? request.InputDataBase64Chunks.Count() : 0)})", nameof(WorkerApiClient));
			KernelExecuteResult result = new();
			
			Stopwatch sw = Stopwatch.StartNew();
			try
			{
				if (request.InputDataBase64Chunks != null && request.InputDataBase64Chunks.Any() && request.InputDataBase64Chunks.Count() >= 1)
				{
					result = await this.internalClient.RequestGenericExecutionBatchAsync(request);
				}
				else
				{
					result = await this.internalClient.RequestGenericExecutionSingleAsync(request);
				}

				result.ExecutionTimeMs = sw.Elapsed.TotalMilliseconds;

				return result;
			}
			catch (Exception ex)
			{
				await this.logger.LogExceptionAsync(ex, nameof(WorkerApiClient));
				return new KernelExecuteResult
				{
					ErrorMessage = ex.Message
				};
			}
			finally
			{
				sw.Stop();
			}
		}

		public async Task<CuFftResult> RequestCuFftAsync(CuFftRequest request)
		{
			await this.logger.LogAsync($"WORKERAPI: Called RequestCuFftAsync(Batches: {request.Batches}, of Size: {request.Size})", nameof(WorkerApiClient));
			try
			{
				Stopwatch sw = Stopwatch.StartNew();
				var result = await this.internalClient.RequestCufftAsync(request);
				result.ExecutionTimeMs = sw.Elapsed.TotalMilliseconds;
				sw.Stop();

				return result;
			}
			catch (Exception ex)
			{
				await this.logger.LogExceptionAsync(ex, nameof(WorkerApiClient));
				return new CuFftResult
				{
					ErrorMessage = ex.Message
				};
			}
		}


	}
}
