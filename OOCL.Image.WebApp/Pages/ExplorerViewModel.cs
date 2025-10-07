using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using OOCL.Image.Client;
using OOCL.Image.Shared;
using Radzen;
using System.Globalization;

namespace OOCL.Image.WebApp.Pages
{
	public partial class ExplorerViewModel
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
		public bool IsRendering => this.isRendering;

		public int Width { get; set; } = 800;
		public int Height { get; set; } = 600;
		public double Zoom { get; set; } = 1.0;
		public double OffsetX { get; set; } = 0.0;
		public double OffsetY { get; set; } = 0.0;
		public int Iterations { get; set; } = 4;
		public string BaseColorHex { get; private set; } = "#000000FF";
		public int Red { get; private set; } = 0;
		public int Green { get; private set; } = 0;
		public int Blue { get; private set; } = 0;

		// Basiswerte (Original vom Picker)
		public int BaseRed { get; private set; } = 0;
		public int BaseGreen { get; private set; } = 0;
		public int BaseBlue { get; private set; } = 0;

		public string ColorHex { get; set; } = "#000000";  // #RRGGBB (für Picker)

		public bool HasColorGroup => this.SelectedKernel?.ColorInputArgNames != null && this.SelectedKernel.ColorInputArgNames.Length == 3;

		private bool isServerSideData = false;
		private bool isRendering = false;
		private DateTime lastRender = DateTime.MinValue;
		private readonly TimeSpan minRenderInterval = TimeSpan.FromMilliseconds(90);

		public Guid CurrentImageId { get; private set; }
		public ImageObjData? CurrentImageData { get; private set; }
		public ImageObjInfo? CurrentImageInfo => this.CurrentImageData != null ? this.ClientImageCollection.Where(d => d.Info.Id == this.CurrentImageData.Id).Select(d => d.Info).FirstOrDefault() : null;
		public Dictionary<Guid, string> ImageCache { get; } = [];
		public List<ImageObjDto> ClientImageCollection { get; private set; }
		public string RecordedStatus => this.RecordToClientCollection
			? $"{this.ClientImageCollection.Count} frame(s) " + $"[{(this.clientCacheSizeKb < 2048.0 ? this.clientCacheSizeKb : this.clientCacheSizeMb).ToString("F2")} " + $"{(this.clientCacheSizeKb > 2048.0 ? "MB" : "KB")}]"
			: "-";
		private double clientCacheSizeKb => this.CalculateClientImageCollectionSizeKb(2);
		private double clientCacheSizeMb => this.CalculateClientImageCollectionSizeKb() / 1024.0;

		public bool IsDragging { get; private set; }
		public bool RefreshEveryMove { get; set; } = true;
		private double dragStartX;
		private double dragStartY;
		private double dragOriginOffsetX;
		private double dragOriginOffsetY;

		public string StatusSummary => this.openClServiceInfo?.Initialized == true
			? $"{this.openClServiceInfo.DeviceName} [{this.openClServiceInfo.DeviceId}]"
			: "Device not initialized";

		public bool RecordToClientCollection { get; set; } = false;
		public int GifFrameRate { get; set; } = 10;
		public double GifScalingFactor { get; set; } = 1.0;
		public bool GifDoLoop { get; set; } = true;

		public void InjectPreloadedMeta(OpenClServiceInfo? info, List<OpenClKernelInfo>? kernels)
		{
			if (info != null)
			{
				this.openClServiceInfo = info;
			}
			if (kernels != null && kernels.Count > 0)
			{
				this.KernelInfos = kernels.Where(k => k.NeedsImage == false && k.MediaType == "Image").ToList();
				this.SelectedKernelName = this.Config?.DefaultKernel ??
					this.KernelInfos.FirstOrDefault(k => k.FunctionName.ToLower().Contains("mandelbrot"))?.FunctionName ??
					this.KernelInfos.FirstOrDefault()?.FunctionName ??
					string.Empty;
				this.SelectedKernel = this.KernelInfos.FirstOrDefault(k => k.FunctionName == this.SelectedKernelName);
			}
		}

		public async Task InitializeAsync()
		{
			if (this.KernelInfos.Count == 0)
			{
				try { this.KernelInfos = (await this.Api.GetOpenClKernelsAsync(true, "IMG")).ToList(); } catch { }
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

		private void SyncColorComponents()
		{
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
				return; // nur #RRGGBB
			}

			try
			{
				this.Red = int.Parse(hex.Substring(1, 2), NumberStyles.HexNumber);
				this.Green = int.Parse(hex.Substring(3, 2), NumberStyles.HexNumber);
				this.Blue = int.Parse(hex.Substring(5, 2), NumberStyles.HexNumber);
			}
			catch { }
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
				this.SyncColorComponents();

				var argNames = this.SelectedKernel.ArgumentNames?.ToArray() ?? [];
				var argTypes = this.SelectedKernel.ArgumentType?.ToArray() ?? [];
				var colorNames = this.SelectedKernel.ColorInputArgNames ?? [];
				string[] argVals = new string[argNames.Length];

				for (int i = 0; i < argNames.Length; i++)
				{
					var original = argNames[i];
					var n = original.ToLowerInvariant();
					var type = (i < argTypes.Length ? (argTypes[i] ?? string.Empty) : string.Empty).ToLowerInvariant();

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
							argVals[i] = AsColorComponent(this.Red); continue;
						}
						if (string.Equals(original, colorNames[1], StringComparison.OrdinalIgnoreCase))
						{
							argVals[i] = AsColorComponent(this.Green); continue;
						}
						if (string.Equals(original, colorNames[2], StringComparison.OrdinalIgnoreCase))
						{
							argVals[i] = AsColorComponent(this.Blue); continue;
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
					if (this.RecordToClientCollection)
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
						this.ClientImageCollection = [];
						this.CurrentImageData = dto.Data;
						if (this.CurrentImageData != null && !string.IsNullOrEmpty(this.CurrentImageData.Base64Data))
						{
							this.ImageCache[this.CurrentImageId] = this.CurrentImageData.Base64Data;
						}
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

			if (this.RefreshEveryMove)
			{
				await this.RenderAsync();
			}
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
			// Vor dem Clamp: eingehende Werte bereits auf Step runden
			this.Width = SnapDimensionNearest("width", this.Width);
			this.Height = SnapDimensionNearest("height", this.Height);

			this.Width = Math.Clamp(this.Width, 4, 16384);
			this.Height = Math.Clamp(this.Height, 4, 16384);
			await this.RenderAsync(true);
		}

		public static int SnapDimensionNearest(string name, int requested)
		{
			var stepsDecimal = HomeViewModel.GetDimensionSteps(name) ?? [];
			if (stepsDecimal.Length == 0)
				return requested;

			// In int konvertieren
			var steps = stepsDecimal.Select(d => (int) d).Distinct().OrderBy(v => v).ToArray();
			if (steps.Length == 0)
				return requested;

			int best = steps[0];
			int bestDiff = Math.Abs(best - requested);
			for (int i = 1; i < steps.Length; i++)
			{
				int diff = Math.Abs(steps[i] - requested);
				if (diff < bestDiff)
				{
					bestDiff = diff;
					best = steps[i];
				}
			}

			// Anpassung Richtung (wie im HomeViewModel-Ansatz)
			if (best < requested && best != steps[^1])
			{
				for (int i = 0; i < steps.Length - 1; i++)
				{
					if (steps[i] == best)
					{
						best = steps[i + 1];
						break;
					}
				}
			}
			else if (best > requested && best != steps[0])
			{
				for (int i = 1; i < steps.Length; i++)
				{
					if (steps[i] == best)
					{
						best = steps[i - 1];
						break;
					}
				}
			}
			return best;
		}

		public void SetSnappedWidth(int raw)
		{
			this.Width = SnapDimensionNearest("width", raw);
		}

		public void SetSnappedHeight(int raw)
		{
			this.Height = SnapDimensionNearest("height", raw);
		}

		public async Task ResetViewAsync()
		{
			this.Zoom = 1.0; this.OffsetX = 0; this.OffsetY = 0; this.Iterations = 2;
			await this.RenderAsync(true);
		}

		public void ApplyPickedColor(string? input)
		{
			if (string.IsNullOrWhiteSpace(input))
			{
				return;
			}

			int r, g, b, a;
			if (!TryParseColorFlexible(input, out r, out g, out b, out a))
			{
				return;
			}

			// Werte setzen
			this.BaseRed = r;
			this.BaseGreen = g;
			this.BaseBlue = b;

			this.Red = r;
			this.Green = g;
			this.Blue = b;

			// ColorHex (nur #RRGGBB für den Picker)
			this.ColorHex = $"#{r:X2}{g:X2}{b:X2}";
			// BaseColorHex im erwarteten Format #AARRGGBB (ARGB voll kompatibel zu bisheriger Nutzung)
			this.BaseColorHex = $"#{a:X2}{r:X2}{g:X2}{b:X2}";
		}

		public async Task OnRecordToClientCollectionChanged(bool value)
		{
			this.RecordToClientCollection = value;
			if (!value)
			{
				// await this.DownloadCreateGif(true);
				this.ClientImageCollection = [];
				this.ImageCache.Clear();
				this.CurrentImageData = null;
			}

			await this.RenderAsync(true);
		}

		private static bool TryParseColorFlexible(string raw, out int r, out int g, out int b, out int a)
		{
			a = 255;
			r = g = b = 0;
			if (string.IsNullOrWhiteSpace(raw))
			{
				return false;
			}

			// Erst Standard-RGB via HomeViewModel
			if (!HomeViewModel.TryParseColor(raw, out r, out g, out b))
			{
				return false;
			}

			// Alpha extrahieren wenn 8-stelliges Hex vorhanden ist (#AARRGGBB oder #RRGGBBAA)
			var s = raw.Trim();
			if (!s.StartsWith("#"))
			{
				s = "#" + s;
			}

			if (s.Length == 9)
			{
				var body = s.Substring(1); // 8 Zeichen
										   // Versuch 1: CSS #RRGGBBAA (Alpha am Ende)
				if (int.TryParse(body.Substring(6, 2), NumberStyles.HexNumber, null, out var aCss))
				{
					a = aCss;
				}
				// Versuch 2: ARGB #AARRGGBB (Alpha am Anfang) – falls plausibel
				if (int.TryParse(body.Substring(0, 2), NumberStyles.HexNumber, null, out var aArgb))
				{
					// Heuristik: wenn Alpha vorne ungleich Alpha hinten (oder hinten = FF), nimm vorn
					if (a == 255 || aArgb != a)
					{
						a = aArgb;
					}
				}
				a = Math.Clamp(a, 0, 255);
			}
			return true;
		}

		public async Task DownloadCreateGif(bool clearClientImages = false)
		{
			try
			{
				bool isServer = await this.Api.IsServersidedDataAsync();

				Guid[]? ids = null;
				ImageObjDto[]? dtos = null;

				if (isServer)
				{
					// Alle vorhandenen Bilder (oder optional nur eine Auswahl) in zeitlicher Reihenfolge
					ids = (await this.Api.GetImageListAsync())
						.OrderBy(i => i.CreatedAt)
						.Select(i => i.Id)
						.ToArray();
				}
				else
				{
					// Nur DTOs mit gültigen Bilddaten
					dtos = this.ClientImageCollection
						.Where(d => d?.Data != null && !string.IsNullOrEmpty(d.Data.Base64Data))
						.OrderBy(d => d.Info.CreatedAt)
						.ToArray();

					if (clearClientImages)
					{
						this.ClientImageCollection = [];
					}
				}

				if ((ids == null || ids.Length == 0) && (dtos == null || dtos.Length == 0))
				{
					this.Notifications.Notify(new NotificationMessage
					{
						Severity = NotificationSeverity.Warning,
						Summary = "No frames giveon for GIF-creation",
						Detail = "No images have been found for creating a gif animation.",
						Duration = 3000
					});
					return;
				}

				var file = await this.Api.DownloadAsGif(ids, dtos, this.GifFrameRate, this.GifScalingFactor, this.GifDoLoop);

				if (isServer)
				{
					if (file == null || file.Stream == null)
					{
						this.Notifications.Notify(new NotificationMessage
						{
							Severity = NotificationSeverity.Error,
							Summary = "GIF-creation failed.",
							Duration = 4000
						});
						return;
					}
					string fname = $"animation_{DateTime.UtcNow:yyyyMMdd_HHmmss}_srv.gif";
					await this.DownloadFileResponseAsync(file, fname, "image/gif");
				}
				else
				{
					// WICHTIG: Bei nicht serverseitigen Daten ist der HTTP-Response-Stream (FileResponse.Stream)
					// oft NICHT seekbar. Auf Length zuzugreifen wirft dann eine NotSupportedException.
					// Daher: Kein Zugriff auf file.Stream.Length mehr – wir puffern zuerst komplett.
					if (file == null || file.Stream == null)
					{
						this.Notifications.Notify(new NotificationMessage
						{
							Severity = NotificationSeverity.Error,
							Summary = "GIF-creation failed.",
							Detail = "No stream returned.",
							Duration = 4000
						});
						return;
					}

					using var ms = new MemoryStream();
					await file.Stream.CopyToAsync(ms);
					var bytes = ms.ToArray();

					if (bytes.Length == 0)
					{
						this.Notifications.Notify(new NotificationMessage
						{
							Severity = NotificationSeverity.Error,
							Summary = "GIF-creation failed.",
							Detail = "Empty GIF stream.",
							Duration = 4000
						});
						file.Dispose();
						return;
					}

					var base64 = Convert.ToBase64String(bytes);
					string mime = "image/gif";
					string filename = $"animation_{DateTime.UtcNow:yyyyMMdd_HHmmss}_client.gif";
					await this.JS.InvokeVoidAsync("downloadFileFromDataUri", filename, $"data:{mime};base64,{base64}");
					file.Dispose();
				}

				this.Notifications.Notify(new NotificationMessage
				{
					Severity = NotificationSeverity.Success,
					Summary = "GIF created",
					Detail = $"{(ids?.Length ?? dtos?.Length ?? 0)} Frames, Rate {this.GifFrameRate} fps, Scale {this.GifScalingFactor:F2}",
					Duration = 3500
				});
			}
			catch (Exception ex)
			{
				this.Notifications.Notify(new NotificationMessage
				{
					Severity = NotificationSeverity.Error,
					Summary = "GIF Error",
					Detail = ex.Message,
					Duration = 5000
				});
			}
		}

		public async Task DownloadFileResponseAsync(FileResponse file, string suggestedFileName, string fallbackMime = "application/octet-stream")
		{
			try
			{
				if (file.Stream == null)
				{
					return;
				}
				byte[] bytes;
				if (file.Stream.CanSeek)
				{
					file.Stream.Position = 0;
					using var ms = new MemoryStream();
					await file.Stream.CopyToAsync(ms);
					bytes = ms.ToArray();
				}
				else
				{
					// Nicht-seekbar -> direkt komplett lesen
					using var ms = new MemoryStream();
					await file.Stream.CopyToAsync(ms);
					bytes = ms.ToArray();
				}

				if (bytes.Length == 0)
				{
					return;
				}

				string mime = HomeViewModel.DetectMimeFromHeaders(file) ?? fallbackMime;
				string base64 = Convert.ToBase64String(bytes);
				string dataUri = $"data:{mime};base64,{base64}";
				await this.JS.InvokeVoidAsync("downloadFileFromDataUri", suggestedFileName, dataUri);
			}
			catch (Exception ex)
			{
				this.Notifications.Notify(new NotificationMessage
				{
					Severity = NotificationSeverity.Error,
					Summary = "Download fehlgeschlagen",
					Detail = ex.Message,
					Duration = 4000
				});
			}
			finally
			{
				file.Dispose();
			}
		}

		public double CalculateClientImageCollectionSizeKb(int decimalsRound = -1)
		{
			double total = 0.0;
			if (this.ClientImageCollection != null && this.ClientImageCollection.Count > 0)
			{
				foreach (var dto in this.ClientImageCollection)
				{
					if (dto?.Data != null && !string.IsNullOrEmpty(dto.Data.Base64Data))
					{
						int padding = dto.Data.Base64Data.EndsWith("==") ? 2 : dto.Data.Base64Data.EndsWith("=") ? 1 : 0;
						double bytes = (dto.Data.Base64Data.Length * 3 / 4.0) - padding;
						total += bytes / 1024.0;
					}
				}
			}

			if (decimalsRound >= 0)
			{
				total = Math.Round(total, decimalsRound);
			}

			return total;
		}

		// --- Mobile / Button Controls ---
		private readonly double panStepFraction = 0.06;          // relativer Schritt (6% der virtuellen Breite/Höhe)
		private readonly double zoomStepFactor = 1.15;          // Zoom-Verhältnis
		private readonly int iterationStep = 10;            // Iterations-Schritt

		public async Task PanAsync(int dxSign, int dySign)
		{
			// dxSign/dySign: -1,0,1
			double scale = 1.0 / this.Zoom;
			// Faktor 3.0 / 2.0 entspricht der bisherigen Mandelbrot-Skalierung im Drag
			this.OffsetX += (dxSign * this.panStepFraction) * 3.0 * scale;
			this.OffsetY += (dySign * this.panStepFraction) * 2.0 * scale;
			await this.RenderAsync(true);
		}

		public async Task AdjustZoomAsync(bool zoomIn)
		{
			this.Zoom *= zoomIn ? this.zoomStepFactor : (1.0 / this.zoomStepFactor);
			this.Zoom = Math.Clamp(this.Zoom, 0.0000001, 1_000_000);
			await this.RenderAsync(true);
		}

		public async Task AdjustIterationsAsync(int direction)
		{
			// direction: +1 oder -1
			int delta = direction * this.iterationStep;
			this.Iterations = Math.Max(1, this.Iterations + delta);
			await this.RenderAsync(true);
		}


	}

	public partial class ExplorerViewModel
	{
		// Liefert nur Kernel, die kein Eingangs-Image benötigen (Create-Kernels)
		public IEnumerable<string> AvailableCreateKernelNames =>
			this.KernelInfos == null
				? Enumerable.Empty<string>()
				: this.KernelInfos
					.Where(k => k != null && (k.NeedsImage == false)) // false oder (implizit) nicht gesetzt
					.Select(k => k.FunctionName)
					.Distinct()
					.OrderBy(n => n, StringComparer.OrdinalIgnoreCase);

		// Auswahl setzen + Color Triplet Erkennung
		public void SetSelectedKernel(string kernelName)
		{
			if (string.IsNullOrWhiteSpace(kernelName) || this.KernelInfos == null) return;
			var k = this.KernelInfos.FirstOrDefault(x =>
				string.Equals(x.FunctionName, kernelName, StringComparison.OrdinalIgnoreCase));
			if (k == null) return;

			this.SelectedKernel = k;
			this.SelectedKernelName = k.FunctionName;
			// Optional: sofortige Re-Render-Steuerung extern
		}
	}
}
