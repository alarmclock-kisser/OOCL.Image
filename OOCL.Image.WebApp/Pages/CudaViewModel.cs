using alarmclockkisser.KernelDtos;
using ManagedCuda.CudaFFT;
using Microsoft.JSInterop;
using OOCL.Image.Client;
using OOCL.Image.Shared;
using Radzen;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace OOCL.Image.WebApp.Pages
{
	public class CudaViewModel
	{
		private readonly ApiClient api;
		private WorkerApiClient? workerApi;
		private readonly WebAppConfig config;
		private readonly NotificationService notifications;
		private readonly IJSRuntime js;
		private readonly DialogService dialogs;

		private WebApiConfig? apiConfig = null;
		private DateTime lastConfigFetch = DateTime.MinValue;

		public List<string> RegisteredWorkers { get; set; } = [];

		// Registration UI
		public string NewWorkerUrl { get; set; } = string.Empty;
		public string RegistrationMessage { get; private set; } = string.Empty;
		public string RegistrationColor { get; private set; } = "color:gray"; // gray default

		// CuFFT test UI
		public int FftSize { get; set; } = 4096;
		public int BatchSize { get; set; } = 8;
		public bool DoInverseAfterwards { get; set; } = false;
		public string ForceDeviceName { get; set; } = string.Empty;
		public string PreferredClientApiUrl { get; set; } = string.Empty;
		public double ExecutionTimeMs { get; set; } = 0.0;
		public string PreviewText { get; private set; } = string.Empty;
		public bool SerializeAsBase64 { get; set; } = false;
		public bool RequestTestMode { get; set; } = false;
		public string? PreferredClientSwaggerLink => this.RegisteredWorkers != null && this.RegisteredWorkers.Count > 0
			? (this.PreferredClientApiUrl.TrimEnd('/') + ":32141/api/swagger")
			: null;

		public List<string> WorkerApiLog { get; private set; } = [];
		public bool HasWorkerApi => this.workerApi != null;
		public bool ShowLogRefreshedMsg { get; set; } = true;
		public bool UseClientApiHttpNoCert => this.config.UseClientApiHttpNoCert;
		public bool SuppressWorkerApiClient { get; set; } = true;

		private readonly JsonSerializerOptions jsonOptions = new() { PropertyNameCaseInsensitive = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

		public CudaViewModel(ApiClient api, WorkerApiClient? workerApi, WebAppConfig config, NotificationService notifications, IJSRuntime js, DialogService dialogs)
		{
			this.api = api ?? throw new ArgumentNullException(nameof(api));
			this.workerApi = workerApi ?? null;
			this.config = config ?? throw new ArgumentNullException(nameof(config));
			this.notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
			this.js = js ?? throw new ArgumentNullException(nameof(js));
			this.dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
		}

		public async Task InitializeAsync()
		{
			this.apiConfig = await this.api.GetApiConfigAsync();

			var workers = (await this.api.RefreshCudaWorkersAsync());
			this.RegisteredWorkers = workers.ToList();

			// default preferred client api url to first available
			this.PreferredClientApiUrl = this.RegisteredWorkers.FirstOrDefault() ?? string.Empty;
			await this.CreateWorkerApiClient(this.PreferredClientApiUrl, this.config.UseClientApiHttpNoCert);
		}

		public Task ToggleSuppressWorkerApiClientAsync(bool v)
		{
			this.SuppressWorkerApiClient = v;

			if (v)
			{
				this.workerApi = null;
			}
			else
			{
				_ = Task.Run(async () =>
				{
					await this.CreateWorkerApiClient(this.PreferredClientApiUrl, this.config.UseClientApiHttpNoCert);
					if (this.workerApi != null)
					{
						await this.RefreshWorkerApiLog();
					}
				});
			}

			return Task.CompletedTask;
		}

		public async Task RefreshRegisteredWorkersAsync()
		{
			this.RegisteredWorkers = (await this.api.RefreshCudaWorkersAsync()).ToList();
			this.PreferredClientApiUrl = this.RegisteredWorkers.FirstOrDefault() ?? string.Empty;

			await this.CreateWorkerApiClient(this.PreferredClientApiUrl);
			if (this.workerApi != null)
			{
				await this.RefreshWorkerApiLog();
			}

			await this.LogAsync("Refreshed registered workers", true);
		}

		public async Task LogAsync(string message, bool bothClients = false)
		{
			if (this.workerApi != null)
			{
				await this.workerApi.LogAsync(message);
			}
			if (bothClients || this.workerApi == null)
			{
				await this.api.LogAsync(message);
			}
		}

		public async Task RefreshWorkerApiLog(bool tryInitialize = true)
		{
			if (this.SuppressWorkerApiClient)
			{
				return;
			}

			if (this.workerApi == null && tryInitialize)
			{
				this.PreferredClientApiUrl = this.RegisteredWorkers.FirstOrDefault() ?? string.Empty;
				await this.CreateWorkerApiClient(this.PreferredClientApiUrl, this.config.UseClientApiHttpNoCert);
			}

			if (this.workerApi == null)
			{
				this.WorkerApiLog = [];
				return;
			}

			this.WorkerApiLog = (await this.workerApi.GetWorkerLogAsync(1024, this.ShowLogRefreshedMsg)).ToList();
		}

		public async Task CreateWorkerApiClient(string workerUrl, bool useNoCertHttp = false)
		{
			if (this.SuppressWorkerApiClient)
			{
				return;
			}

			try
			{
				HttpClient httpClient;
				if (useNoCertHttp)
				{
					var handler = new HttpClientHandler();
					handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true;
					httpClient = new HttpClient(handler)
					{
						BaseAddress = new Uri(workerUrl)
					};
				}
				else
				{
					httpClient = new HttpClient
					{
						BaseAddress = new Uri(workerUrl)
					};
				}
				this.workerApi = new WorkerApiClient(new RollingFileLogger(1024, false, null, "log_workerclient_"), httpClient, true, useNoCertHttp);

				var status = await this.workerApi.GetStatusAsync();

				await this.LogAsync($"Created WorkerApiClient for {workerUrl}. Worker reports {status.AvailableDevices.Count()} CUDA devices.", false);
			}
			catch (Exception ex)
			{
				this.notifications.Notify(new NotificationMessage
				{
					Summary = "Error",
					Detail = "Creating WorkerApiClient failed: " + ex.Message,
					Duration = 8000,
					Severity = NotificationSeverity.Error
				});

				await this.LogAsync("Error creating WorkerApiClient for " + workerUrl + ": " + ex.Message, false);
			}
		}

		public async Task RegisterWorkerAsync()
		{
			this.RegistrationMessage = string.Empty;
			this.RegistrationColor = "color:gray";
			if (string.IsNullOrWhiteSpace(this.NewWorkerUrl))
			{
				this.RegistrationMessage = "Please provide a worker URL.";
				this.RegistrationColor = "color:darkred";
				return;
			}

			try
			{
				var msg = await this.api.RegisterCudaWorkerUrlAsync(this.NewWorkerUrl);

				// WICHTIG: Worker-Refresh und Log NICHT synchron im UI-Thread ausführen!
				// Stattdessen: nur Worker-Liste aktualisieren, WorkerApiClient im Hintergrund initialisieren
				this.RegisteredWorkers = (await this.api.RefreshCudaWorkersAsync()).ToList();
				this.PreferredClientApiUrl = this.RegisteredWorkers.FirstOrDefault() ?? string.Empty;

				// WorkerApiClient im Hintergrund initialisieren, UI bleibt responsiv
				_ = Task.Run(async () =>
				{
					await this.CreateWorkerApiClient(this.PreferredClientApiUrl, this.config.UseClientApiHttpNoCert);
					if (this.workerApi != null)
					{
						await this.RefreshWorkerApiLog();
					}
				});

				// check presence
				var normalized = this.NewWorkerUrl.Trim().TrimEnd('/');
				bool present = this.RegisteredWorkers.Any(r => r.Trim().TrimEnd('/').Equals(normalized, StringComparison.OrdinalIgnoreCase));
				if (present)
				{
					this.RegistrationMessage = string.IsNullOrWhiteSpace(msg) ? "Registered" : msg;
					this.RegistrationColor = "color:green";
					await this.LogAsync("Registered new worker URL " + this.NewWorkerUrl + ".", true);
				}
				else
				{
					this.RegistrationMessage = string.IsNullOrWhiteSpace(msg) ? "Registration failed" : msg;
					this.RegistrationColor = "color:darkred";
					await this.LogAsync("Registration of worker URL " + this.NewWorkerUrl + " did not succeed (not present after registration).", true);
				}
			}
			catch (Exception ex)
			{
				this.RegistrationMessage = ex.Message;
				this.RegistrationColor = "color:darkred";
				await this.LogAsync("Error registering worker URL " + this.NewWorkerUrl + ": " + ex.Message, true);
			}
		}

		public async Task SetFftSize(decimal value)
		{
			const int MinFftSize = 32;
			const int MaxFftSize = 256 * 1024 * 1024; // 256k

			int intValue = (int) value;
			if (intValue < MinFftSize)
			{
				intValue = MinFftSize;
			}

			if (intValue > MaxFftSize)
			{
				intValue = MaxFftSize;
			}

			// Aktuellen ChunkSize in gültigen Bereich bringen
			int current = this.FftSize;
			if (current < MinFftSize)
			{
				current = MinFftSize;
			}

			if (current > MaxFftSize)
			{
				current = MaxFftSize;
			}

			// Sicherstellen, dass current eine Zweierpotenz ist (ansonsten auf nächstkleinere potenz setzen)
			if (!IsPowerOfTwo(current))
			{
				current = PrevPowerOfTwo(current);
			}

			// Benutzer hat inkrementiert -> eine Stufe (Faktor 2) nach oben
			if (intValue > current)
			{
				long next = (long) current * 2;
				if (next > MaxFftSize)
				{
					next = MaxFftSize;
				}

				this.FftSize = (int) next;

				await this.LogAsync($"Set FFT size to INC {this.FftSize}", true);
				return;
			}

			// Benutzer hat dekrementiert -> eine Stufe (Faktor 2) nach unten
			if (intValue < current)
			{
				int prev = current / 2;
				if (prev < MinFftSize)
				{
					prev = MinFftSize;
				}

				this.FftSize = prev;
				await this.LogAsync($"Set FFT size to DEC {this.FftSize}", true);
				return;
			}

			// unverändert: nichts tun
			await this.LogAsync($"Set FFT size to UNCH {this.FftSize}", true);
		}

		private static bool IsPowerOfTwo(int x) => x > 0 && (x & (x - 1)) == 0;

		private static int PrevPowerOfTwo(int x)
		{
			if (x < 1)
			{
				return 1;
			}

			int p = 1;
			while ((p << 1) <= x)
			{
				p <<= 1;
			}

			return p;
		}

		public async Task ExecuteTestFftWorkerApiAsync()
		{
			if (this.workerApi == null)
			{
				await this.LogAsync("ExecuteTestFftWorkerApiAsync called but workerApi is null", false);
				return;
			}

			bool inverted = false;
			try
			{
				// create sine wave batch * fftsize → input data
				var inputData = new List<float[]>
		{
			Enumerable.Range(0, this.FftSize).Select(i => (float)Math.Sin(2.0 * Math.PI * 5.0 * (double)i / (double)this.FftSize)).ToArray()
		};

				for (int b = 1; b < this.BatchSize; b++)
				{
					double freq = 5.0 + b * 3.0;
					var chunk = Enumerable.Range(0, this.FftSize).Select(i => (float) Math.Sin(2.0 * Math.PI * freq * (double) i / (double) this.FftSize)).ToArray();
					inputData.Add(chunk);
				}
				IEnumerable<object[]> dataChunksAsObjects = inputData
					.Select(ch => ch.Cast<object>().ToArray())
					.ToArray();

				var request = new CuFftRequest()
				{
					DataChunks = dataChunksAsObjects,
					DataForm = "f",
					DataType = "float",
					DataLength = (this.FftSize * this.BatchSize).ToString(),
					DataBase64Chunks = [],
					Size = this.FftSize,
					Batches = this.BatchSize,
					Inverse = inverted,
					DeviceName = string.IsNullOrWhiteSpace(this.ForceDeviceName) ? null : this.apiConfig?.PreferredDevice,
				};
				var result = await this.workerApi.RequestCuFftAsync(request);
				if (result == null || (result.ExecutionTimeMs == null && result.DataChunks == null))
				{
					string msg = "";
					if (result?.ErrorMessage != null)
					{
						msg = " Error: " + result.ErrorMessage;
					}
					await this.LogAsync("ExecuteTestFftWorkerApiAsync: result is null with ErrMsg: " + msg, false);
					this.ExecutionTimeMs = -1;
					return;
				}

				inverted = result.DataForm == "c";
				this.ExecutionTimeMs = result.ExecutionTimeMs ?? 0.0;

				this.PreviewText = FormatFftPreview(result.DataChunks?.ToList());

				this.notifications.Notify(new NotificationMessage
				{
					Summary = "Success",
					Detail = $"Test FFT executed on worker (inverse: {inverted}).",
					Duration = 4000,
					Severity = NotificationSeverity.Success
				});

				// If DoInverseAfterwards is set, do it now
				if (this.DoInverseAfterwards && inverted)
				{
					request = new CuFftRequest()
					{
						DataChunks = result.DataChunks ?? [],
						DataForm = "c",
						DataType = "float",
						DataLength = (this.FftSize * this.BatchSize).ToString(),
						DataBase64Chunks = [],
						Size = this.FftSize,
						Batches = this.BatchSize,
						Inverse = true,
						DeviceName = string.IsNullOrWhiteSpace(this.ForceDeviceName) ? null : this.apiConfig?.PreferredDevice,
					};

					var ifftResult = await this.workerApi.RequestCuFftAsync(request);
					if (ifftResult == null || (ifftResult.ExecutionTimeMs == null && ifftResult.DataChunks == null))
					{
						string msg = "";
						if (ifftResult?.ErrorMessage != null)
						{
							msg = " Error: " + ifftResult.ErrorMessage;
						}
						await this.LogAsync("ExecuteTestFftWorkerApiAsync: ifftResult is null with ErrMsg: " + msg, false);

						this.ExecutionTimeMs = -1;
						return;
					}

					this.ExecutionTimeMs = ifftResult.ExecutionTimeMs ?? this.ExecutionTimeMs;

					this.PreviewText = FormatFftPreview(ifftResult.DataChunks?.ToList());

					this.notifications.Notify(new NotificationMessage
					{
						Summary = "Success",
						Detail = $"Test I-FFT executed on worker.",
						Duration = 4000,
						Severity = NotificationSeverity.Success
					});
				}

				await this.LogAsync($"Executed test FFT on worker (inverse: {inverted}) successfully.", false);
			}
			catch (Exception ex)
			{
				Console.WriteLine(ex);
				this.PreviewText = "Error (test-request-cufft-worker): " + ex.Message;
				await this.LogAsync("Error executing test-worker FFT on worker: " + ex.Message, false);
			}
		}

		public async Task RequestFftTestAsync()
		{
			if (this.workerApi != null &! this.SuppressWorkerApiClient)
			{
				await this.LogAsync("RequestFftTestAsync: Redirecting to worker API", true);
				await this.ExecuteTestFftWorkerApiAsync();
				return;
			}
			try
			{
				// Use ACTUAL ApiClient Accessor pls
				var result = await this.api.TestRequestCuFftAsync(this.FftSize, this.BatchSize, this.DoInverseAfterwards, this.PreferredClientApiUrl, this.ForceDeviceName);
				if (result == null)
				{
					await this.LogAsync("RequestFftTestAsync: result is null", true);
					return;
				}

				this.ExecutionTimeMs = result.ExecutionTimeMs ?? 0.0;
				
				// If more than 48 elements, split and short middle entries with [...]
				this.PreviewText = FormatFftPreview(result.DataChunks?.ToList());
			}
			catch (Exception ex)
			{
				Console.WriteLine(ex);
				this.PreviewText = "Error (test-request-cufft): " + ex.Message;
				await this.LogAsync("Error executing request-test FFT via API: " + ex.Message, true);
			}
		}

		public async Task RunCufftTestAsync()
		{
			if (this.workerApi != null && !this.SuppressWorkerApiClient)
			{
				await this.LogAsync("RunCufftTestAsync: Redirecting to worker API", true);
				await this.ExecuteTestFftWorkerApiAsync();
				return;
			}

			this.ExecutionTimeMs = 0;
			this.PreviewText = string.Empty;

			this.BatchSize = Math.Clamp(this.BatchSize, 1, 1024);

			if (this.RequestTestMode)
			{
				await this.LogAsync("RunCufftTestAsync: Using RequestTestMode", true);
				await this.RequestFftTestAsync();
				return;
			}

			// generate synthetic test data
			var inputFloatChunks = new List<float[]>();
			for (int b = 0; b < this.BatchSize; b++)
			{
				var chunk = new float[this.FftSize];
				double freq1 = 5.0 + b;
				double freq2 = 20.0 + b * 0.3;
				double freq3 = 50.0 + b * 0.1;

				for (int i = 0; i < this.FftSize; i++)
				{
					double t = (double) i / (double) this.FftSize;
					chunk[i] = (float) (
						Math.Sin(2.0 * Math.PI * freq1 * t) +
						0.5 * Math.Sin(2.0 * Math.PI * freq2 * t) +
						0.25 * Math.Sin(2.0 * Math.PI * freq3 * t)
					);
				}

				inputFloatChunks.Add(chunk);
			}

			try
			{
				IEnumerable<object[]>? dataChunksAsObjects = null;
				IEnumerable<string>? dataChunksAsBase64 = null;

				if (this.SerializeAsBase64)
				{
					// serialize each float[] into a Base64 string (raw bytes)
					dataChunksAsBase64 = inputFloatChunks.Select(chunk =>
					{
						byte[] bytes = new byte[chunk.Length * sizeof(float)];
						Buffer.BlockCopy(chunk, 0, bytes, 0, bytes.Length);
						return Convert.ToBase64String(bytes);
					}).ToList();
				}
				else
				{
					// normal JSON-safe float[] → object[] conversion
					dataChunksAsObjects = inputFloatChunks
						.Select(ch => ch.Cast<object>().ToArray())
						.ToArray();
				}

				// call FFT API
				var fftResult = await this.api.RequestCuFfftAsync(
					dataChunksAsObjects,
					dataChunksAsBase64,
					inverse: this.DoInverseAfterwards,
					forceDeviceName: string.IsNullOrWhiteSpace(this.ForceDeviceName) ? null : this.ForceDeviceName,
					preferredClientApiUrl: string.IsNullOrWhiteSpace(this.PreferredClientApiUrl) ? null : this.PreferredClientApiUrl
				);

				if (fftResult == null)
				{
					await this.LogAsync("RunCufftTestAsync: fftResult is null", true);
					return;
				}

				this.ExecutionTimeMs = fftResult.ExecutionTimeMs ?? -1;
				this.PreviewText = FormatFftPreview(fftResult.DataChunks?.ToList());

				// optionally do inverse FFT
				if (this.DoInverseAfterwards && fftResult?.DataChunks != null)
				{
					var complexChunks = fftResult.DataChunks.Cast<object[]>().ToArray();

					var ifftResult = await this.api.RequestCuFfftAsync(
						complexChunks,
						inverse: this.DoInverseAfterwards,
						forceDeviceName: string.IsNullOrWhiteSpace(this.ForceDeviceName) ? null : this.ForceDeviceName,
						preferredClientApiUrl: string.IsNullOrWhiteSpace(this.PreferredClientApiUrl) ? null : this.RegisteredWorkers.FirstOrDefault()
					);

					if (ifftResult == null)
					{
						await this.LogAsync("RunCufftTestAsync: ifftResult is null", true);
						return;
					}

					this.ExecutionTimeMs = ifftResult.ExecutionTimeMs ?? -1;
					this.PreviewText = FormatFftPreview(ifftResult.DataChunks?.ToList());
				}

				await this.LogAsync("RunCufftTestAsync: FFT executed successfully", true);
			}
			catch (Exception ex)
			{
				this.PreviewText = "Error: " + ex.Message;
				await this.LogAsync("Error executing FFT run-test via API: " + ex.Message, true);
			}
		}



		private static string FormatComplexPreview(object? chunkObj, int maxPairs = 16)
		{
			if (chunkObj is null) return string.Empty;

			// Versuche als float[] zu casten
			if (chunkObj is float[] floatArr && floatArr.Length >= 2)
			{
				int pairCount = floatArr.Length / 2;
				if (pairCount <= maxPairs)
				{
					return string.Join(", ", Enumerable.Range(0, pairCount)
						.Select(i => $"[{floatArr[2 * i]:0.###}, {floatArr[2 * i + 1]:0.###}]"));
				}
				else
				{
					var first8 = Enumerable.Range(0, 8)
						.Select(i => $"[{floatArr[2 * i]:0.###}, {floatArr[2 * i + 1]:0.###}]");
					var last8 = Enumerable.Range(pairCount - 8, 8)
						.Select(i => $"[{floatArr[2 * i]:0.###}, {floatArr[2 * i + 1]:0.###}]");
					return string.Join(", ", first8) + ", [...], " + string.Join(", ", last8);
				}
			}
			// Falls chunkObj ein object[] ist, versuche float-Paare zu extrahieren
			if (chunkObj is object[] objArr && objArr.Length >= 2)
			{
				int pairCount = objArr.Length / 2;
				if (pairCount <= maxPairs)
				{
					return string.Join(", ", Enumerable.Range(0, pairCount)
						.Select(i => $"[{objArr[2 * i]}, {objArr[2 * i + 1]}]"));
				}
				else
				{
					var first8 = Enumerable.Range(0, 8)
						.Select(i => $"[{objArr[2 * i]}, {objArr[2 * i + 1]}]");
					var last8 = Enumerable.Range(pairCount - 8, 8)
						.Select(i => $"[{objArr[2 * i]}, {objArr[2 * i + 1]}]");
					return string.Join(", ", first8) + ", [...], " + string.Join(", ", last8);
				}
			}
			return chunkObj.ToString() ?? string.Empty;
		}

		private static string FormatFftPreview(IReadOnlyList<object[]>? chunks, int maxRows = 8, int maxCols = 8)
		{
			if (chunks == null || chunks.Count == 0)
				return "Keine FFT-Daten.";

			var sb = new System.Text.StringBuilder();
			int stride = chunks[0].Length;
			sb.AppendLine($"Batches: {chunks.Count}, Stride: {stride}");

			int rows = Math.Min(maxRows, chunks.Count);
			int cols = Math.Min(maxCols, stride);

			for (int i = 0; i < rows; i++)
			{
				var chunk = chunks[i];
				sb.Append($"Chunk {i + 1}: ");
				// Zeige die ersten und letzten 8 Werte, falls stride > 16
				if (stride > 16)
				{
					var firstVals = chunk.Take(8).Select(v => $"{v:0.###}");
					var lastVals = chunk.Skip(stride - 8).Select(v => $"{v:0.###}");
					sb.Append(string.Join(", ", firstVals));
					sb.Append(", [...], ");
					sb.Append(string.Join(", ", lastVals));
				}
				else
				{
					var vals = chunk.Take(cols).Select(v => $"{v:0.###}");
					sb.Append(string.Join(", ", vals));
				}
				sb.AppendLine();
			}
			if (chunks.Count > rows)
				sb.AppendLine($"... ({chunks.Count - rows} weitere Chunks)");

			return sb.ToString();
		}

	}
}

