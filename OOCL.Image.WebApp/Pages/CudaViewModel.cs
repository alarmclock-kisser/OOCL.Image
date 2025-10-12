using alarmclockkisser.KernelDtos;
using Microsoft.JSInterop;
using OOCL.Image.Client;
using OOCL.Image.Shared;
using Radzen;
using System.Text.Json;
using System.Text.Json.Serialization;

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

		public List<string> RegisteredWorkers { get; set; } = new();

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
		public double ExecutionTimeMs { get; private set; } = 0.0;
		public string PreviewText { get; private set; } = string.Empty;
		public bool SerializeAsBase64 { get; set; } = false;
		public bool RequestTestMode { get; set; } = true;
		public string? PreferredClientSwaggerLink => this.RegisteredWorkers != null && this.RegisteredWorkers.Count > 0
			? (this.PreferredClientApiUrl.TrimEnd('/') + ":32141/api/swagger")
			: null;

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
			await this.CreateWorkerApiClient(this.PreferredClientApiUrl);
		}

		public async Task RefreshRegisteredWorkersAsync()
		{
			this.RegisteredWorkers = (await this.api.RefreshCudaWorkersAsync()).ToList();
			if (string.IsNullOrWhiteSpace(this.PreferredClientApiUrl))
			{
				await this.CreateWorkerApiClient(this.PreferredClientApiUrl);
				this.PreferredClientApiUrl = this.RegisteredWorkers.FirstOrDefault() ?? string.Empty;
			}
		}

		public async Task CreateWorkerApiClient(string workerUrl)
		{
			try
			{
				var httpClient = new HttpClient
				{
					BaseAddress = new Uri(workerUrl)
				};
				this.workerApi = new WorkerApiClient(new RollingFileLogger(1024, false, null, "log_workerclient_"), httpClient, initializeApiConfig: true);

				var status = await this.workerApi.GetStatusAsync();
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
				await this.RefreshRegisteredWorkersAsync();

				// check presence
				var normalized = this.NewWorkerUrl.Trim().TrimEnd('/');
				bool present = this.RegisteredWorkers.Any(r => r.Trim().TrimEnd('/').Equals(normalized, StringComparison.OrdinalIgnoreCase));
				if (present)
				{
					this.RegistrationMessage = string.IsNullOrWhiteSpace(msg) ? "Registered" : msg;
					this.RegistrationColor = "color:green";
				}
				else
				{
					this.RegistrationMessage = string.IsNullOrWhiteSpace(msg) ? "Registration failed" : msg;
					this.RegistrationColor = "color:darkred";
				}
			}
			catch (Exception ex)
			{
				this.RegistrationMessage = ex.Message;
				this.RegistrationColor = "color:darkred";
			}
		}

		public void SetFftSize(decimal value)
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
				return;
			}

			// unverändert: nichts tun
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
				return;
			}

			bool inverted = false;
			try
			{
				// create sine wave batch * fftsize → input data
				var inputData = new List<float[]>()
				{
					Enumerable.Range(0, this.FftSize).Select(i => (float) Math.Sin(2.0 * Math.PI * 5.0 * (double) i / (double) this.FftSize)).ToArray()
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
				if (result == null)
				{
					return;
				}

				inverted = result.DataForm == "c";
				this.ExecutionTimeMs = result.ExecutionTimeMs ?? 0.0;
				this.PreviewText = JsonSerializer.Serialize(result, this.jsonOptions);
				if (this.PreviewText.Length > 48)
					this.PreviewText = this.PreviewText.Substring(0, 48) + "...";

				// If more than 48 elements, split and short middle entries with [...]
				this.PreviewText = result.DataChunks.FirstOrDefault()?.Length <= 48 ? string.Join(", ", result.DataChunks) : string.Join(", ", result.DataChunks.Select(c => c.Take(47) + ", ..."));

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
						DataChunks = result.DataChunks,
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
					if (ifftResult == null)
					{
						return;
					}

					this.ExecutionTimeMs = ifftResult.ExecutionTimeMs ?? this.ExecutionTimeMs;
					this.PreviewText = JsonSerializer.Serialize(ifftResult, this.jsonOptions);
					if (this.PreviewText.Length > 48)
						this.PreviewText = this.PreviewText.Substring(0, 48) + "...";
					this.notifications.Notify(new NotificationMessage
					{
						Summary = "Success",
						Detail = $"Test inverse FFT executed on worker.",
						Duration = 4000,
						Severity = NotificationSeverity.Success
					});
				}

			}
			catch (Exception ex)
			{
				Console.WriteLine(ex);
			}
		}

		public async Task RequestFftTestAsync()
		{
			if (this.workerApi != null)
			{
				await this.ExecuteTestFftWorkerApiAsync();
				return;
			}
			try
			{
				// Use ACTUAL ApiClient Accessor pls
				var result = await this.api.TestRequestCuFftAsync(this.FftSize, this.BatchSize, this.DoInverseAfterwards, this.PreferredClientApiUrl, this.ForceDeviceName);
				if (result == null)
				{
					return;
				}

				this.ExecutionTimeMs = result.ExecutionTimeMs ?? 0.0;
				this.PreviewText = JsonSerializer.Serialize(result, this.jsonOptions);
				
				// If more than 48 elements, split and short middle entries with [...]
				this.PreviewText = result.DataChunks.FirstOrDefault()?.Length <= 48 ? string.Join(", ", result.DataChunks) : string.Join(", ", result.DataChunks.Select(c => c.Take(47) + ", ..."));
			}
			catch (Exception ex)
			{
				Console.WriteLine(ex);
				this.PreviewText = "Error (test-request-cufft): " + ex.Message;
			}
		}

		public async Task RunCufftTestAsync()
		{
			if (this.workerApi != null)
			{
				await this.ExecuteTestFftWorkerApiAsync();
				return;
			}

			this.ExecutionTimeMs = 0;
			this.PreviewText = string.Empty;

			this.BatchSize = Math.Clamp(this.BatchSize, 1, 1024);

			if (this.RequestTestMode)
			{
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

				this.ExecutionTimeMs = fftResult?.ExecutionTimeMs ?? 0.0;
				this.PreviewText = JsonSerializer.Serialize(fftResult, this.jsonOptions);
				if (this.PreviewText.Length > 48)
					this.PreviewText = this.PreviewText.Substring(0, 48) + "...";

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

					this.ExecutionTimeMs = ifftResult?.ExecutionTimeMs ?? this.ExecutionTimeMs;
					this.PreviewText = JsonSerializer.Serialize(ifftResult, this.jsonOptions);
					if (this.PreviewText.Length > 48)
						this.PreviewText = this.PreviewText.Substring(0, 48) + "...";
				}
			}
			catch (Exception ex)
			{
				this.PreviewText = "Error: " + ex.Message;
			}
		}

	}
}
