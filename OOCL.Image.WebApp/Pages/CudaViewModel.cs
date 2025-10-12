using Microsoft.JSInterop;
using OOCL.Image.Client;
using OOCL.Image.Shared;
using Radzen;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Globalization;

namespace OOCL.Image.WebApp.Pages
{
	public class CudaViewModel
	{
		private readonly ApiClient api;
		private readonly WebAppConfig config;
		private readonly NotificationService notifications;
		private readonly IJSRuntime js;
		private readonly DialogService dialogs;

		private WebApiConfig? apiConfig = null;

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

		private readonly JsonSerializerOptions jsonOptions = new() { PropertyNameCaseInsensitive = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

		public CudaViewModel(ApiClient api, WebAppConfig config, NotificationService notifications, IJSRuntime js, DialogService dialogs)
		{
			this.api = api ?? throw new ArgumentNullException(nameof(api));
			this.config = config ?? throw new ArgumentNullException(nameof(config));
			this.notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
			this.js = js ?? throw new ArgumentNullException(nameof(js));
			this.dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
		}

		public async Task InitializeAsync()
		{
			this.apiConfig = await api.GetApiConfigAsync();

			var workers = (await this.api.RefreshCudaWorkersAsync());
			this.RegisteredWorkers = workers.ToList();

			// default preferred client api url to first available
			this.PreferredClientApiUrl = this.RegisteredWorkers.FirstOrDefault() ?? string.Empty;
		}

		public async Task RefreshRegisteredWorkersAsync()
		{
			this.RegisteredWorkers = (await this.api.RefreshCudaWorkersAsync()).ToList();
			if (string.IsNullOrWhiteSpace(this.PreferredClientApiUrl))
			{
				this.PreferredClientApiUrl = this.RegisteredWorkers.FirstOrDefault() ?? string.Empty;
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

		private static int SnapToPowerOfTwo(int v)
		{
			if (v < 32) return 32;
			if (v > 65536) return 65536;
			// round to nearest power of two
			int p = 1;
			while (p < v) p <<= 1;
			// if overshoot, pick previous
			if (p > v) p >>= 1;
			return p;
		}

		public void SetFftSize(int v)
		{
			this.FftSize = SnapToPowerOfTwo(v);
		}

		public async Task RunCufftTestAsync()
		{
			this.ExecutionTimeMs = 0;
			this.PreviewText = string.Empty;

			int size = SnapToPowerOfTwo(this.FftSize);
			int batches = Math.Clamp(this.BatchSize, 1, 1024);

			// generate data
			var inputFloatChunks = new List<float[]>();
			for (int b = 0; b < batches; b++)
			{
				var chunk = new float[size];
				double freq1 = 5.0 + b;
				double freq2 = 20.0 + b * 0.3;
				double freq3 = 50.0 + b * 0.1;
				for (int i = 0; i < size; i++)
				{
					double t = (double)i / (double)size;
					chunk[i] = (float)(Math.Sin(2.0 * Math.PI * freq1 * t) + 0.5 * Math.Sin(2.0 * Math.PI * freq2 * t) + 0.25 * Math.Sin(2.0 * Math.PI * freq3 * t));
				}
				inputFloatChunks.Add(chunk);
			}

			// convert to object[][] where each row is an object[] of floats
			IEnumerable<object[]> dataChunksAsObjects = inputFloatChunks.Select(ch => ch.Cast<object>().ToArray()).ToArray();

			try
			{
				var fftResult = await this.api.RequestCuFfftAsync(dataChunksAsObjects, false, string.IsNullOrWhiteSpace(this.ForceDeviceName) ? null : this.ForceDeviceName, string.IsNullOrWhiteSpace(this.PreferredClientApiUrl) ? null : this.PreferredClientApiUrl);
				this.ExecutionTimeMs = fftResult?.ExecutionTimeMs ?? 0.0;
				this.PreviewText = JsonSerializer.Serialize(fftResult, this.jsonOptions);
				if (this.PreviewText.Length > 48) this.PreviewText = this.PreviewText.Substring(0, 48) + "...";

				if (this.DoInverseAfterwards && fftResult != null && fftResult.DataChunks != null)
				{
					// forward result.DataChunks back as inverse request
					IEnumerable<object[]> complexChunks = fftResult.DataChunks.Cast<object[]>().ToArray();
					var ifftResult = await this.api.RequestCuFfftAsync(complexChunks, true, string.IsNullOrWhiteSpace(this.ForceDeviceName) ? null : this.ForceDeviceName, string.IsNullOrWhiteSpace(this.PreferredClientApiUrl) ? null : this.PreferredClientApiUrl);
					this.ExecutionTimeMs = ifftResult?.ExecutionTimeMs ?? this.ExecutionTimeMs;
					this.PreviewText = JsonSerializer.Serialize(ifftResult, this.jsonOptions);
					if (this.PreviewText.Length > 48) this.PreviewText = this.PreviewText.Substring(0, 48) + "...";
				}
			}
			catch (Exception ex)
			{
				this.PreviewText = "Error: " + ex.Message;
			}
		}
	}
}
