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

		public OpenClServiceInfo openClServiceInfo { get; private set; } = new();
		public List<OpenClKernelInfo> KernelInfos { get; private set; } = [];
		public OpenClKernelInfo? SelectedKernel { get; private set; }
		public string SelectedKernelName { get; private set; } = string.Empty;

		public int Width { get; set; } = 800;
		public int Height { get; set; } = 600;
		public double Zoom { get; set; } = 1.0;
		public double OffsetX { get; set; } = 0.0;
		public double OffsetY { get; set; } = 0.0;
		public int Iterations { get; set; } = 4;
		public string BaseColorHex { get; private set; } = "#FF000000";
		public int Red { get; private set; } = 0;
		public int Green { get; private set; } = 0;
		public int Blue { get; private set; } = 0;

		public string ColorHex { get; set; } = "#000000";  // strictly #RRGGBB for picker

		public bool HasColorGroup => this.SelectedKernel?.ColorInputArgNames != null && this.SelectedKernel.ColorInputArgNames.Length == 3;

		private bool isServerSideData = false;
		private bool isRendering = false;
		private DateTime lastRender = DateTime.MinValue;
		private readonly TimeSpan minRenderInterval = TimeSpan.FromMilliseconds(90);

		public Guid CurrentImageId { get; private set; }
		public ImageObjData? CurrentImageData { get; private set; }
		public Dictionary<Guid, string> ImageCache { get; } = [];
		public List<ImageObjDto> ClientImageCollection { get; private set; }

		public bool IsDragging { get; private set; }
		private double dragStartX;
		private double dragStartY;
		private double dragOriginOffsetX;
		private double dragOriginOffsetY;

		public string StatusSummary => this.openClServiceInfo?.Initialized == true
			? $"{this.openClServiceInfo.DeviceName} [{this.openClServiceInfo.DeviceId}]"
			: "Device not initialized";

		public void InjectPreloadedMeta(OpenClServiceInfo? info, List<OpenClKernelInfo>? kernels)
		{
			if (info != null)
			{
				this.openClServiceInfo = info;
			}
			if (kernels != null && kernels.Count > 0)
			{
				this.KernelInfos = kernels;
				this.SelectedKernelName = this.Config?.DefaultKernel ??
					this.KernelInfos.FirstOrDefault(k => k.FunctionName.ToLower().Contains("mandelbrot"))?.FunctionName ??
					this.KernelInfos.FirstOrDefault()?.FunctionName ??
					string.Empty;
				this.SelectedKernel = this.KernelInfos.FirstOrDefault(k => k.FunctionName == this.SelectedKernelName);
			}
		}

		public async Task InitializeAsync()
		{
			// Wenn aus Layout schon vorhanden → nichts neu holen
			if (this.KernelInfos.Count == 0)
			{
				try
				{
					this.KernelInfos = (await this.Api.GetOpenClKernelsAsync()).ToList();
				}
				catch { }
			}

			if (this.openClServiceInfo?.Initialized != true)
			{
				try
				{
					this.openClServiceInfo = await this.Api.GetOpenClServiceInfoAsync();
					if (!this.openClServiceInfo.Initialized)
					{
						await this.Api.InitializeOpenClIndexAsync(0);
						this.openClServiceInfo = await this.Api.GetOpenClServiceInfoAsync();
					}
				}
				catch { }
			}

			if (string.IsNullOrEmpty(this.SelectedKernelName))
			{
				this.SelectedKernelName = this.Config?.DefaultKernel ??
					this.KernelInfos.FirstOrDefault(k => k.FunctionName.ToLower().Contains("mandelbrot"))?.FunctionName ??
					this.KernelInfos.FirstOrDefault()?.FunctionName ??
					string.Empty;
			}
			this.SelectedKernel = this.KernelInfos.FirstOrDefault(k => k.FunctionName == this.SelectedKernelName);

			if (string.IsNullOrEmpty(this.SelectedKernelName))
			{
				this.Notifications.Notify(new NotificationMessage { Severity = NotificationSeverity.Warning, Summary = "No kernel found", Duration = 3000 });
				return;
			}

			await this.RenderAsync();
		}

		public async Task EnsureDeviceInitializedAsync()
		{
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

		// ---------- NEU: Hilfsroutine zur Synchronisierung der Farb-Komponenten ----------
		private void SyncColorComponents()
		{
			// Erwartet ColorHex = #RRGGBB
			var hex = this.ColorHex;
			if (string.IsNullOrWhiteSpace(hex))
			{
				return;
			}
			if (!hex.StartsWith('#'))
			{
				hex = "#" + hex.Trim();
			}
			if (hex.Length != 7)
			{
				return;
			}
			try
			{
				int r = int.Parse(hex.Substring(1, 2), NumberStyles.HexNumber);
				int g = int.Parse(hex.Substring(3, 2), NumberStyles.HexNumber);
				int b = int.Parse(hex.Substring(5, 2), NumberStyles.HexNumber);
				this.Red = r;
				this.Green = g;
				this.Blue = b;
				this.BaseColorHex = "#FF" + hex.Substring(1); // ARGB mit voller Deckkraft
			}
			catch
			{
				// Ignorieren – ungültige Eingabe
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

				// Sicherstellen, dass Red/Green/Blue wirklich aus dem aktuellen ColorHex abgeleitet sind
				this.SyncColorComponents();

				var argNames = this.SelectedKernel.ArgumentNames?.ToArray() ?? [];
				var argTypes = this.SelectedKernel.ArgumentType?.ToArray() ?? [];              // <- NEU wieder aufgenommen
				var colorNames = this.SelectedKernel.ColorInputArgNames ?? [];
				string[] argVals = new string[argNames.Length];

				for (int i = 0; i < argNames.Length; i++)
				{
					var original = argNames[i];
					var n = original.ToLowerInvariant();
					var type = (i < argTypes.Length ? (argTypes[i] ?? string.Empty) : string.Empty).ToLowerInvariant();

					// Hilfsfunktion: Wandelt (0..255) ggf. in (0..1) um falls Float/Double erwartet
					string AsColorComponent(int comp)
					{
						if (type.Contains("single") || type.Contains("float") || type.Contains("double"))
						{
							double v = comp / 255.0;
							return v.ToString("G9", CultureInfo.InvariantCulture);
						}
						return comp.ToString(CultureInfo.InvariantCulture);
					}

					if (colorNames.Length == 3)
					{
						if (string.Equals(original, colorNames[0], StringComparison.OrdinalIgnoreCase))
						{
							argVals[i] = AsColorComponent(this.Red);
							continue;
						}
						if (string.Equals(original, colorNames[1], StringComparison.OrdinalIgnoreCase))
						{
							argVals[i] = AsColorComponent(this.Green);
							continue;
						}
						if (string.Equals(original, colorNames[2], StringComparison.OrdinalIgnoreCase))
						{
							argVals[i] = AsColorComponent(this.Blue);
							continue;
						}
					}

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
						argVals[i] = AsColorComponent(this.Red);
					}
					else if (n.EndsWith("g") || n.Contains("green"))
					{
						argVals[i] = AsColorComponent(this.Green);
					}
					else if (n.EndsWith("b") || n.Contains("blue"))
					{
						argVals[i] = AsColorComponent(this.Blue);
					}
					else
					{
						argVals[i] = "0";
					}
				}

				var dto = await this.Api.ExecuteCreateImageAsync(this.Width, this.Height, this.SelectedKernelName, this.BaseColorHex, argNames, argVals);
				if (dto == null || dto.Info == null || dto.Info.Id == Guid.Empty)
				{
					this.Notifications.Notify(new NotificationMessage { Severity = NotificationSeverity.Error, Summary = "Render failed", Duration = 2500 });
					return;
				}

				this.CurrentImageId = dto.Info.Id;

				if (!this.isServerSideData)
				{
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

		public async Task OnWheelAsync(WheelEventArgs e)
		{
			if (e == null)
			{
				return;
			}

			if (e.ShiftKey || e.CtrlKey)
			{
				int delta = e.DeltaY < 0 ? 10 : -10;
				this.Iterations = Math.Max(1, this.Iterations + delta);
			}
			else
			{
				double factor = e.DeltaY < 0 ? 1.1 : 0.9;
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
			double scale = 1.0 / this.Zoom;
			this.OffsetX = this.dragOriginOffsetX - (dx / this.Width) * 3.0 * scale;
			this.OffsetY = this.dragOriginOffsetY - (dy / this.Height) * 2.0 * scale;
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

		// Farbe anwenden (Picker liefert immer #RRGGBB) – lässt Red/Green/Blue jetzt vom nächsten Render() neu synchronisieren
		public void ApplyPickedColor(string? hex)
		{
			if (string.IsNullOrWhiteSpace(hex))
			{
				return;
			}

			if (!hex.StartsWith('#'))
			{
				hex = "#" + hex;
			}

			if (hex.Length != 7)
			{
				return;
			}

			this.ColorHex = hex;
			// BaseColorHex sofort setzen (ARGB)
			this.BaseColorHex = "#FF" + hex.Substring(1);
			// Red/Green/Blue werden in SyncColorComponents() vor dem Render sicher neu gesetzt.
		}
	}
}
