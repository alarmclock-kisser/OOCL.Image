using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using OOCL.Image.Client;
using OOCL.Image.Shared;
using Radzen;
using System.Globalization;

namespace OOCL.Image.WebApp.Pages
{
	public class ExplorerViewModel
	{
		private readonly ApiClient Api;
		private readonly WebAppConfig Config;
		private readonly NotificationService Notifications;
		private readonly IJSRuntime JS;
		private readonly DialogService DialogService;

		public ExplorerViewModel(ApiClient api, WebAppConfig config, NotificationService notifications, IJSRuntime js, DialogService dialogService)
		{
			this.Api = api ?? throw new ArgumentNullException(nameof(api));
			this.Config = config ?? throw new ArgumentNullException(nameof(config));
			this.Notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
			this.JS = js ?? throw new ArgumentNullException(nameof(js));
			this.DialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
			this.ClientImageCollection = [];
		}

		// --- OpenCL / Kernel Meta ---
		public OpenClServiceInfo openClServiceInfo { get; private set; } = new();
		public List<OpenClKernelInfo> KernelInfos { get; private set; } = [];
		public OpenClKernelInfo? SelectedKernel { get; private set; }
		public string SelectedKernelName { get; private set; } = string.Empty;

		// --- Fractal Parameters ---
		public int Width { get; set; } = 800;
		public int Height { get; set; } = 600;
		public double Zoom { get; set; } = 1.0;              // Scale factor
		public double OffsetX { get; set; } = 0.0;            // Pan offset X (complex plane shift)
		public double OffsetY { get; set; } = 0.0;            // Pan offset Y
		public int Iterations { get; set; } = 250;            // Max iterations
		public string BaseColorHex { get; set; } = "#000000"; // Provided base color (unused for mandelbrot, reserved)
		public int Red { get; set; } = 255;                   // Optional color args
		public int Green { get; set; } = 255;
		public int Blue { get; set; } = 255;

		// Execution state
		private bool isServerSideData = true;
		private bool isRendering = false;
		private DateTime lastRender = DateTime.MinValue;
		private readonly TimeSpan minRenderInterval = TimeSpan.FromMilliseconds(90); // simple throttle

		// Image data & cache
		public Guid CurrentImageId { get; private set; }
		public ImageObjData? CurrentImageData { get; private set; }
		public Dictionary<Guid, string> ImageCache { get; } = [];
		public List<ImageObjDto> ClientImageCollection { get; private set; }

		// Drag handling
		public bool IsDragging { get; private set; }
		private double dragStartX;
		private double dragStartY;
		private double dragOriginOffsetX;
		private double dragOriginOffsetY;

		// Public helpers for UI
		public string StatusSummary => this.openClServiceInfo?.Initialized == true
			? $"{this.openClServiceInfo.DeviceName} [{this.openClServiceInfo.DeviceId}]"
			: "Device not initialized";

		public async Task InitializeAsync()
		{
			this.isServerSideData = await this.Api.IsServersidedDataAsync();
			this.openClServiceInfo = await this.Api.GetOpenClServiceInfoAsync();

			// Load kernels and select mandelbrot-like kernel
			try
			{
				this.KernelInfos = (await this.Api.GetOpenClKernelsAsync()).ToList();
				this.SelectedKernelName = !string.IsNullOrWhiteSpace(this.Config?.DefaultKernel)
					? this.Config.DefaultKernel!
					: this.KernelInfos.FirstOrDefault(k => k.FunctionName.ToLower().Contains("mandelbrot"))?.FunctionName
					  ?? this.KernelInfos.FirstOrDefault()?.FunctionName
					  ?? string.Empty;
				this.SelectedKernel = this.KernelInfos.FirstOrDefault(k => k.FunctionName == this.SelectedKernelName);
			}
			catch { }

			if (string.IsNullOrEmpty(this.SelectedKernelName))
			{
				this.Notifications.Notify(new NotificationMessage { Severity = NotificationSeverity.Warning, Summary = "No kernel found", Duration = 3000 });
				return;
			}

			await this.RenderAsync();
		}

		public async Task EnsureDeviceInitializedAsync()
		{
			// If not initialized try default device index 0
			if (!this.openClServiceInfo.Initialized)
			{
				try
				{
					await this.Api.InitializeOpenClIndexAsync(0);
					this.openClServiceInfo = await this.Api.GetOpenClServiceInfoAsync();
				}
				catch { }
			}
		}

		public async Task RenderAsync(bool force = false)
		{
			if (this.isRendering)
			{
				return;
			}
			if (!force && DateTime.UtcNow - this.lastRender < this.minRenderInterval)
			{
				return;
			}
			if (this.SelectedKernel == null)
			{
				return;
			}

			this.isRendering = true;
			try
			{
				await this.EnsureDeviceInitializedAsync();

				var argNames = this.SelectedKernel.ArgumentNames?.ToArray() ?? [];
				var argTypes = this.SelectedKernel.ArgumentType?.ToArray() ?? [];
				string[] argVals = new string[argNames.Length];

				for (int i = 0; i < argNames.Length; i++)
				{
					var n = argNames[i].ToLowerInvariant();
					if (n.Contains("width"))
					{
						argVals[i] = this.Width.ToString(CultureInfo.InvariantCulture);
					}
					else if (n.Contains("height"))
					{
						argVals[i] = this.Height.ToString(CultureInfo.InvariantCulture);
					}
					else if (n.Contains("zoom"))
					{
						argVals[i] = this.Zoom.ToString(CultureInfo.InvariantCulture);
					}
					else if (n.Contains("xoffset") || n.Contains("x_off") || (n.Contains("x") && n.Contains("offset")))
					{
						argVals[i] = this.OffsetX.ToString(CultureInfo.InvariantCulture);
					}
					else if (n.Contains("yoffset") || n.Contains("y_off") || (n.Contains("y") && n.Contains("offset")))
					{
						argVals[i] = this.OffsetY.ToString(CultureInfo.InvariantCulture);
					}
					else if (n.Contains("iter"))
					{
						argVals[i] = this.Iterations.ToString(CultureInfo.InvariantCulture);
					}
					else if (n.EndsWith("r") || n.Contains("red"))
					{
						argVals[i] = this.Red.ToString();
					}
					else if (n.EndsWith("g") || n.Contains("green"))
					{
						argVals[i] = this.Green.ToString();
					}
					else if (n.EndsWith("b") || n.Contains("blue"))
					{
						argVals[i] = this.Blue.ToString();
					}
					else
					{
						argVals[i] = "0"; // default
					}
				}

				// Always create a fresh image (fractal regenerate)
				var dto = await this.Api.ExecuteCreateImageAsync(this.Width, this.Height, this.SelectedKernelName, this.BaseColorHex, argNames, argVals);
				if (dto == null || dto.Info == null || dto.Info.Id == Guid.Empty)
				{
					this.Notifications.Notify(new NotificationMessage { Severity = NotificationSeverity.Error, Summary = "Render failed", Duration = 2500 });
					return;
				}

				this.CurrentImageId = dto.Info.Id;

				if (!this.isServerSideData)
				{
					// Keep client collection (unbounded for now)
					var list = this.ClientImageCollection.ToList();
					list.Add(dto);
					this.ClientImageCollection = list;
					this.CurrentImageData = dto.Data;
					if (this.CurrentImageData != null && !string.IsNullOrEmpty(this.CurrentImageData.Base64Data))
					{
						this.ImageCache[this.CurrentImageId] = this.CurrentImageData.Base64Data;
					}
				}
				else
				{
					this.CurrentImageData = await this.Api.GetImageDataAsync(this.CurrentImageId, "png");
					if (this.CurrentImageData != null && !string.IsNullOrEmpty(this.CurrentImageData.Base64Data))
					{
						this.ImageCache[this.CurrentImageId] = this.CurrentImageData.Base64Data;
					}
				}
				this.lastRender = DateTime.UtcNow;
			}
			finally
			{
				this.isRendering = false;
			}
		}

		// --- Interaction API (called from component) ---
		public async Task OnWheelAsync(WheelEventArgs e)
		{
			if (e == null)
			{
				return;
			}
			// Ctrl + wheel -> iterations adjust, else zoom
			if (e.CtrlKey)
			{
				int delta = e.DeltaY < 0 ? 10 : -10;
				this.Iterations = Math.Max(1, this.Iterations + delta);
			}
			else
			{
				double factor = e.DeltaY < 0 ? 0.9 : 1.1; // zoom in/out
				this.Zoom *= factor;
				this.Zoom = Math.Clamp(this.Zoom, 0.0000001, 1_000_000);
			}
			await this.RenderAsync();
		}

		public void OnMouseDown(double clientX, double clientY)
		{
			this.IsDragging = true;
			this.dragStartX = clientX;
			this.dragStartY = clientY;
			this.dragOriginOffsetX = this.OffsetX;
			this.dragOriginOffsetY = this.OffsetY;
		}

		public async Task OnMouseMoveAsync(double clientX, double clientY)
		{
			if (!this.IsDragging)
			{
				return;
			}

			double dx = clientX - this.dragStartX;
			double dy = clientY - this.dragStartY;
			// Scale translation relative to current zoom & resolution
			double scale = 1.0 / this.Zoom; // coarse factor
			this.OffsetX = this.dragOriginOffsetX - (dx / this.Width) * 3.0 * scale; // 3.0 is view span heuristic
			this.OffsetY = this.dragOriginOffsetY + (dy / this.Height) * 2.0 * scale; // 2.0 vertical aspect
			await this.RenderAsync();
		}

		public async Task OnMouseUpAsync()
		{
			if (!this.IsDragging)
			{
				return;
			}

			this.IsDragging = false;
			await this.RenderAsync();
		}

		public async Task OnResolutionChangedAsync()
		{
			this.Width = Math.Clamp(this.Width, 4, 16384);
			this.Height = Math.Clamp(this.Height, 4, 16384);
			await this.RenderAsync(true);
		}

		public async Task ResetViewAsync()
		{
			this.Zoom = 1.0; this.OffsetX = 0; this.OffsetY = 0; this.Iterations = 2;
			await this.RenderAsync(true);
		}
	}
}
