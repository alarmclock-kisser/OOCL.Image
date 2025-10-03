using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;
using OOCL.Image.Client;
using OOCL.Image.Shared;
using Radzen;
using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace OOCL.Image.WebApp.Pages
{
    public class HomeViewModel
    {
        private readonly ApiClient Api;
        private readonly WebAppConfig Config;
        private readonly NotificationService Notifications;
        private readonly IJSRuntime JS;
        private readonly DialogService DialogService;

        public HomeViewModel(ApiClient api, WebAppConfig config, NotificationService notifications, IJSRuntime js, DialogService dialog)
        {
            this.Api = api ?? throw new ArgumentNullException(nameof(api));
            this.Config = config ?? throw new ArgumentNullException(nameof(config));
            this.Notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
            this.JS = js ?? throw new ArgumentNullException(nameof(js));
            this.DialogService = dialog;

            this.devices = [];
            this.images = [];
            this.kernelInfos = [];
            this.kernelNames = [];
            this.kernelArgViewModels = [];
            this.formats = ["png", "jpg", "bmp"];
        }

		// --- If no server sided data provided, use internal dto list of images ---
        public List<ImageObjDto> ClientImageCollection { get; set; } = [];

        // --- WebAppConfig text ---
        private JsonSerializerOptions jsonSerializerOptions = new() { WriteIndented = true };
        public string WebAppConfigText { 
            get => JsonSerializer.Serialize(this.Config, this.jsonSerializerOptions);
            set
            {
                try
                {
                    var cfg = JsonSerializer.Deserialize<WebAppConfig>(value, this.jsonSerializerOptions);
                    if (cfg != null)
                    {
                        this.Config.ApplicationName = cfg.ApplicationName;
                        this.Config.DefaultDarkMode = cfg.DefaultDarkMode;
                        this.Config.PreferredDevice = cfg.PreferredDevice;
                        this.Config.ImagesLimit = cfg.ImagesLimit;
                        this.Config.ApiBaseUrl = cfg.ApiBaseUrl;
                        this.Config.KestelEndpointHttp = cfg.KestelEndpointHttp;
                        this.Config.KestelEndpointHttps = cfg.KestelEndpointHttps;
                    }
                }
                catch { }
			}
		}
        public WebApiConfig ApiConfig { get; set; } = new();
        public string WebApiConfigText
        {
            get => JsonSerializer.Serialize(this.ApiConfig, this.jsonSerializerOptions);
            set
            {
                try
                {
                    var cfg = JsonSerializer.Deserialize<WebApiConfig>(value, this.jsonSerializerOptions);
                    if (cfg != null)
                    {
                        this.ApiConfig = cfg;
                    }
                }
                catch { }
            }
        }

		// --- Public state used by the UI (kept with same names as before) ---
		public List<OpenClDeviceInfo> devices { get; set; }
        public int selectedDeviceIndex { get; set; }
        public List<ImageObjInfo> images { get; set; }
        public Guid selectedImageId { get; set; }
        public ImageObjData? imageData { get; set; }
        public string selectedFormat { get; set; } = "bmp";
        public int quality { get; set; } = 90;
        public double scalingFactor { get; set; } = 1.0;
        public OpenClServiceInfo openClServiceInfo { get; set; } = new OpenClServiceInfo();
        public List<string> formats { get; set; }

        public List<OpenClKernelInfo> kernelInfos { get; set; }
        public List<string> kernelNames { get; set; }
        public string selectedKernelName { get; set; } = string.Empty;
        public OpenClKernelInfo? selectedKernelInfo { get; set; }
        public List<KernelArgViewModel> kernelArgViewModels { get; set; }

        // redraw after value change
        public bool redrawAfterValueChange { get; set; } = false;
        public string kernelInfoText { get; set; } = string.Empty;
        public bool useExistingImage { get; set; } = true;
        public string colorHex { get; set; } = "#000000";
        public string colorHexForNewImage { get; set; } = "#000000";

        // Growing image data cache
        public Dictionary<Guid, string> ImageCache { get; } = [];

        // Max images numeric and magnitude display
        public int MaxImagesToKeepNumeric { get; set; }
        public List<string> Magnitudes { get; } = ["Byte", "kB", "KB", "mB", "MB", "gB", "GB"];
        public string SelectedMagnitude { get; set; } = "MB";
        public double MagnitudeFactor { get; set; } = 1024.0 * 1024.0;

		public bool CanExecute => !string.IsNullOrEmpty(this.selectedKernelName) && (this.useExistingImage ? this.selectedImageId != Guid.Empty : true);

        // Gibt an, ob das Setzen auf 0 (unbegrenzt) lokal erlaubt ist — nur true, wenn Server-Config <= 0
        public bool AllowZeroMaxImagesSetting => (this.Config?.ImagesLimit ?? 0) <= 0;

        public bool RandomizeRgb { get; set; } = false;

		private Dictionary<string, decimal> defaultArgValues = new()
		{
	            { "width", 720.0m },
	            { "height", 480.0m },
				{ "zoom", 1.0m },
				{ "iter", 8.0m },
				{ "coef", 8.0m },
				{ "thresh", 0.3m },
				{ "thick", 1.0m },
                { "amount", 1.5m },
                { "scale", 1.0m },
                { "pass", 1.0m },
			};

		// --- Initialization / loading ---
		public async Task InitializeAsync()
        {
			// Update api config (webapp config is alread injected)
			this.ApiConfig = await this.Api.GetApiConfigAsync();

			await this.LoadDevices();
            await this.LoadImages();
            await this.LoadOpenClStatus();
            await this.LoadKernels();
            // initialize max images numeric from config
            this.MaxImagesToKeepNumeric = this.Config?.ImagesLimit ?? 0;
            // ensure magnitude factor
            if (string.IsNullOrEmpty(this.SelectedMagnitude))
			{
				this.SelectedMagnitude = "kB";
			}

			this.UpdateMagnitudeFactor();

            // Not working.
            /*this.selectedKernelName = this.Config?.DefaultKernel ?? string.Empty;
            this.SelectedMagnitude = this.Config?.DefaultUnit ?? "KB";
            this.selectedFormat = this.Config?.DefaultFormat ?? "png";*/

            await this.FirstApplyDefaultSelections();
		}

        public async Task FirstApplyDefaultSelections()
        {
			// From config set default kernel, unit, format
            if (!string.IsNullOrEmpty(this.Config?.PreferredDevice) && !this.openClServiceInfo.Initialized)
            {
                var index = this.devices.FindIndex(d => d.DeviceName.ToLowerInvariant().Contains(this.Config.PreferredDevice, StringComparison.OrdinalIgnoreCase));
                if (index >= 0)
                {
                    this.selectedDeviceIndex = index;
                    await this.InitializeDevice();
                }
			}

            if (!string.IsNullOrEmpty(this.Config?.DefaultKernel) && this.kernelNames.Contains(this.Config.DefaultKernel))
            {
                this.selectedKernelName = this.Config.DefaultKernel;
                this.OnKernelChanged(this.selectedKernelName);
            }
            if (!string.IsNullOrEmpty(this.Config?.DefaultUnit) && this.Magnitudes.Contains(this.Config.DefaultUnit))
            {
                this.SelectedMagnitude = this.Config.DefaultUnit;
                this.UpdateMagnitudeFactor();
            }
            if (!string.IsNullOrEmpty(this.Config?.DefaultFormat) && this.formats.Contains(this.Config.DefaultFormat))
            {
                this.selectedFormat = this.Config.DefaultFormat;
			}

            // Ensure image data is loaded
            await this.UpdateImageData();

			// Notify user
            this.Notifications.Notify(new NotificationMessage { Severity = NotificationSeverity.Info, Summary = "Default selections applied", Duration = 3000 });
		}

		public void UpdateMagnitudeFactor()
		{
			var m = (this.SelectedMagnitude ?? string.Empty).Trim();
			switch (m)
			{
				case "Byte":
					this.MagnitudeFactor = 1.0; break;
				case "kB":
					this.MagnitudeFactor = 1000.0; break;
				case "KB":
					this.MagnitudeFactor = 1024.0; break;
				case "mB":
					this.MagnitudeFactor = 1000.0 * 1000.0; break;
				case "MB":
					this.MagnitudeFactor = 1024.0 * 1024.0; break;
				case "gB":
					this.MagnitudeFactor = 1000.0 * 1000.0 * 1000.0; break;
				case "GB":
					this.MagnitudeFactor = 1024.0 * 1024.0 * 1024.0; break;
				default:
					// default to 1000 (kB)
					this.MagnitudeFactor = 1000.0; break;
			}
		}

		public async Task LoadDevices() => this.devices = (await this.Api.GetOpenClDevicesAsync()).ToList();

		public async Task LoadImages()
		{
			// Die Basis-Klasse für die Liste, die angezeigt wird (vermutlich ImageObjInfo)
			// Dies muss auf der Client-Seite die Quelldaten von der API (true) ODER vom Client-Store (false) sein.
			List<ImageObjInfo> imageInfoList = new();

			if (await this.Api.IsServersidedDataAsync())
			{
				try
				{
					int effectiveLimit = 0;
					var cfgLimit = this.Config?.ImagesLimit ?? 0;

					if (this.MaxImagesToKeepNumeric > 0)
					{
						effectiveLimit = this.MaxImagesToKeepNumeric;
					}

					if (cfgLimit > 0)
					{
						if (effectiveLimit <= 0)
						{
							effectiveLimit = cfgLimit;
						}
						else
						{
							effectiveLimit = Math.Min(effectiveLimit, cfgLimit);
						}
					}

					if (effectiveLimit > 0)
					{
						await this.Api.CleanupOldImages(effectiveLimit);
					}
				}
				catch {  }

				imageInfoList = (await this.Api.GetImageListAsync()).ToList();

			}
			else
			{
				imageInfoList = this.ClientImageCollection.Select(dto => dto.Info).ToList();
			}

			this.images = imageInfoList.OrderBy(i => i.CreatedAt).ToList();

			if (this.images.Count > 0)
			{
				this.selectedImageId = this.images.Last().Id;
				this.lastKernelProcessingTimeMs = this.images.Last().LastProcessingTimeMs;
			}
			else
			{
				this.selectedImageId = Guid.Empty;
				this.lastKernelProcessingTimeMs = 0;
			}

			if (this.useExistingImage)
			{
				var info = this.images.FirstOrDefault(i => i.Id == this.selectedImageId);
				this.UpdateWidthHeightFromImage(info);
			}
		}

		public async Task LoadOpenClStatus()
        {
			this.openClServiceInfo = await this.Api.GetOpenClServiceInfoAsync();
        }

        public async Task LoadKernels()
        {
			try
            {
				this.kernelInfos = (await this.Api.GetOpenClKernelsAsync()).ToList();
				this.kernelNames = this.kernelInfos.Select(k => k.FunctionName).ToList();
				if (this.kernelNames.Count > 0)
				{
					this.selectedKernelName = this.kernelNames[0];
					this.OnKernelChanged(this.selectedKernelName);
				}
			}
            catch { }
            finally
            {
                await Task.Yield();
            }
		}

        public async Task OnDeviceChanged(object value)
        {
			this.selectedDeviceIndex = (int)value;
            await this.InitializeDevice();
            await this.LoadOpenClStatus();
        }

        public async Task InitializeDevice()
        {
            await this.Api.InitializeOpenClIndexAsync(this.selectedDeviceIndex);
            await this.LoadOpenClStatus();
        }

        public async Task ReloadDevices()
        {
            await this.LoadDevices();
            await this.LoadOpenClStatus();
        }

        public async Task ReleaseDevice()
        {
            await this.Api.DisposeOpenClAsync();
            await this.LoadOpenClStatus();
        }

        public async Task<ImageObjData?> GetImageDataAsync(Guid id, string format)
        {
            if (id == Guid.Empty)
            {
                return null;
			}

            if (await this.Api.IsServersidedDataAsync())
            {
               return await this.Api.GetImageDataAsync(id, format);
            }
            else
            {
                var dto = this.ClientImageCollection.FirstOrDefault(d => d.Info.Id == id);
                if (dto == null)
                    {
                    return null;
				}

                return dto.Data;
            }
        }


		public async Task DownloadImage(Guid id, string format)
        {
            if (id == Guid.Empty)
			{
				return;
			}
            if (await this.Api.IsServersidedDataAsync())
            {
                // Server sided data
                var file = await this.Api.DownloadImageAsync(id, format);
            }
            else
            {
				// Client sided data
				var dto = this.ClientImageCollection.FirstOrDefault(d => d.Info.Id == id);

				if (dto == null || dto.Data == null || string.IsNullOrEmpty(dto.Data.Base64Data))
				{
					return;
				}

				var base64Data = dto.Data.Base64Data;
				var mimeType = dto.Data.MimeType;
				var fileName = $"image_{id.ToString().Substring(0, 8)}.{format}";

				// **KORREKTUR: Erstellen des vollständigen Data-URI-Strings**
				// Struktur: data:[MIME-Type];base64,[Base64-Daten]
				string dataUri = $"data:{mimeType};base64,{base64Data}";

				// Übergeben Sie den Data-URI-String an die JS-Funktion
				// Wir vereinfachen die JS-Übergabe, indem wir nur den URI und den Dateinamen senden
				await this.JS.InvokeVoidAsync("downloadFileFromDataUri", fileName, dataUri);
			}
        }

        public async Task RemoveImage(Guid id)
        {
            if (id == Guid.Empty)
			{
				return;
			}

            if (await this.Api.IsServersidedDataAsync())
            {
                await this.Api.RemoveImageAsync(id);
            }
            else
            {
                var dto = this.ClientImageCollection.FirstOrDefault(d => d.Info.Id == id);
                if (dto != null)
                {
                    var list = this.ClientImageCollection.ToList();
                    list.Remove(dto);
                    this.ClientImageCollection = list;
                }
			}

			await this.LoadImages();
        }

        public async Task ClearImages()
        {
            if (await this.Api.IsServersidedDataAsync())
            {
                await this.Api.ClearImagesAsync();
            }
            else
            {
                this.ClientImageCollection = [];
			}

            this.ImageCache.Clear();
			await this.LoadImages();
        }

        public async Task OnInputFileChange(InputFileChangeEventArgs e)
        {
            var file = e.File;
            if (file == null)
            {
				this.Notifications.Notify(new NotificationMessage { Severity = NotificationSeverity.Warning, Summary = "No file selected", Duration = 2000 });
                return;
            }
            try
            {
                using var stream = file.OpenReadStream(20 * 1024 * 1024);
                using var ms = new MemoryStream();
                await stream.CopyToAsync(ms);
                var bytes = ms.ToArray();
                var fileParameter = new FileParameter(new MemoryStream(bytes), file.Name, file.ContentType);
                
                if (await this.Api.IsServersidedDataAsync())
                {
                    var info = await this.Api.UploadImageAsync(fileParameter);
                    if (info == null || info.Id == Guid.Empty)
					{
                        this.Notifications.Notify(new NotificationMessage { Severity = NotificationSeverity.Error, Summary = "Upload failed", Duration = 4000 });
                        return;
					}

                    // select uploaded image
                    this.selectedImageId = info.Id;
                }
                else
                {
                    ImageObjDto dto = await ImageObjDto.FromBytesAsync(bytes, file.Name, file.ContentType);

					// Client sided upload
					var list = this.ClientImageCollection.ToList();
                    list.Add(dto);
                    this.ClientImageCollection = list;
                    
                    // select uploaded image
                    this.selectedImageId = dto.Info.Id;
				}

				await this.LoadImages();
            	this.Notifications.Notify(new NotificationMessage { Severity = NotificationSeverity.Success, Summary = "Image uploaded successfully", Duration = 2000 });
            }
            catch (Exception ex)
            {
				this.Notifications.Notify(new NotificationMessage { Severity = NotificationSeverity.Error, Summary = $"Upload failed: {ex.Message}", Duration = 4000 });
            }
        }

        public async Task OnImageChanged(object value)
        {
			this.selectedImageId = (Guid)value;
            await this.UpdateImageData();
        }

		public async Task UpdateImageData()
		{
			if (this.selectedImageId == Guid.Empty)
			{
				this.imageData = null;
				return;
			}
			if (this.ImageCache.TryGetValue(this.selectedImageId, out var cached))
			{
				this.imageData = new ImageObjData { Id = this.selectedImageId, Base64Data = cached, MimeType = "image/" + this.selectedFormat.Trim('.').ToLower() };
				return;
			}
			this.imageData = await this.Api.IsServersidedDataAsync() ? await this.Api.GetImageDataAsync(this.selectedImageId, this.selectedFormat) : await this.GetImageDataAsync(this.selectedImageId, this.selectedFormat);
			if (this.imageData != null && !string.IsNullOrEmpty(this.imageData.Base64Data))
			{
				this.ImageCache[this.selectedImageId] = this.imageData.Base64Data;
			}
		}

		public void OnKernelChanged(object value)
        {
			this.selectedKernelName = value?.ToString() ?? string.Empty;
			this.selectedKernelInfo = this.kernelInfos.FirstOrDefault(k => k.FunctionName == this.selectedKernelName);
			this.kernelArgViewModels.Clear();
			this.kernelInfoText = string.Empty;
			this.colorHex = "#000000";
            if (this.selectedKernelInfo?.ArgumentType != null && this.selectedKernelInfo.ArgumentType.Any())
            {
                var argTypes = this.selectedKernelInfo.ArgumentType.ToArray();
                var argNames = this.selectedKernelInfo.ArgumentNames?.ToArray();
                for (int i = 0; i < argTypes.Length; i++)
                {
                    var t = argTypes[i];                      // z.B. "Int32", "Single", "Double", "Byte"
                    var name = argNames != null && i < argNames.Length ? argNames[i] : $"arg{i}";
                    bool isPointer = t.Contains("*");
                    bool isColor = this.selectedKernelInfo.ColorInputArgNames != null && this.selectedKernelInfo.ColorInputArgNames.Contains(name);
                    decimal defaultValue;

                    if (isColor)
                    {
                        defaultValue = this.RandomizeRgb ? new Random().Next(0, 255) : 0m;
                    }
                    else
                    {
                        defaultValue = this.GetDefaultArgDecimal(name);
                    }

                    // Auf Typ anpassen (Runden / Clamp für Integer, Byte etc.)
                    defaultValue = this.AdjustValueForType(defaultValue, t);

					this.kernelArgViewModels.Add(new KernelArgViewModel
                    {
                        Index = i,
                        Name = name,
                        Type = t,
                        Value = defaultValue,
                        Step = this.GetStep(t),
                        Min = this.GetMin(t),
                        Max = this.GetMax(t),
                        IsPointer = isPointer,
                        IsColor = isColor,
                        IsIntegerType = IsIntegerTypeName(t),
                        NormalizedClrType = NormalizeTypeName(t)
                    });
                }
				this.BuildKernelInfoText();

                // Optionally toggle OnImage Checkbox if selectedKernelInfo.NeedsImage
                this.useExistingImage = this.selectedKernelInfo.NeedsImage;
				if (!this.useExistingImage)
                {
                    foreach (var arg in this.kernelArgViewModels)
                    {
                        var lname2 = arg.Name.ToLower();
                        if (this.IsWidthOrHeight(arg))
                        {
                            if (lname2.Contains("width") && arg.Value == 0)
							{
								arg.Value = 720;
							}

							if (lname2.Contains("height") && arg.Value == 0)
							{
								arg.Value = 480;
							}
						}
                    }
                }
            } 

            if (this.useExistingImage)
            {
                var info = this.images.FirstOrDefault(i => i.Id == this.selectedImageId);
				this.UpdateWidthHeightFromImage(info);
            }
		}

        // Liefert passenden Decimal Default (roh) aus Dictionary oder 0
        private decimal GetDefaultArgDecimal(string argName)
        {
            var lname = argName.ToLowerInvariant();
            foreach (var kv in this.defaultArgValues)
            {
                if (lname.Contains(kv.Key.ToLowerInvariant()))
                {
                    return kv.Value;
                }
            }
            return 0m;
        }

        // Wandelt decimal in "typkonformen" decimal (Runden/Clamp) um
        private decimal AdjustValueForType(decimal value, string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName))
			{
				return value;
			}

			var t = typeName.Trim().ToLowerInvariant();

            bool isFloat = t.Contains("single") || t.Contains("float");
            bool isDouble = t.Contains("double");
            bool isByte = t.Contains("byte") && !t.Contains("sbyte");
            bool isSByte = t.Contains("sbyte");
            bool isUnsigned = t.StartsWith("u") || t.Contains("uint") || t.Contains("ulong") || t.Contains("ushort");
            bool isInteger = IsIntegerTypeName(typeName) && !isFloat && !isDouble;

            if (isByte)
            {
                if (value < 0)
				{
					value = 0;
				}

				if (value > 255)
				{
					value = 255;
				}

				value = Math.Round(value, 0, MidpointRounding.AwayFromZero);
                return value;
            }

            if (isSByte)
            {
                if (value < sbyte.MinValue)
				{
					value = sbyte.MinValue;
				}

				if (value > sbyte.MaxValue)
				{
					value = sbyte.MaxValue;
				}

				value = Math.Round(value, 0, MidpointRounding.AwayFromZero);
                return value;
            }

            if (isInteger)
            {
                // Präzise auf ganzzahligen Bereich runden
                value = Math.Round(value, 0, MidpointRounding.AwayFromZero);

                if (t.Contains("int64") || t.Contains("long"))
                {
                    if (value < long.MinValue)
					{
						value = long.MinValue;
					}

					if (value > long.MaxValue)
					{
						value = long.MaxValue;
					}
				}
                else if (t.Contains("uint64") || (t.Contains("ulong")))
                {
                    if (value < 0)
					{
						value = 0;
					}

					if (value > ulong.MaxValue)
					{
						value = ulong.MaxValue;
					}
				}
                else if (t.Contains("int16") || t.Contains("short"))
                {
                    if (value < short.MinValue)
					{
						value = short.MinValue;
					}

					if (value > short.MaxValue)
					{
						value = short.MaxValue;
					}
				}
                else if (t.Contains("uint16") || t.Contains("ushort"))
                {
                    if (value < 0)
					{
						value = 0;
					}

					if (value > ushort.MaxValue)
					{
						value = ushort.MaxValue;
					}
				}
                else if (t.Contains("uint32"))
                {
                    if (value < 0)
					{
						value = 0;
					}

					if (value > uint.MaxValue)
					{
						value = uint.MaxValue;
					}
				}
                else // int32 / int
                {
                    if (value < int.MinValue)
					{
						value = int.MinValue;
					}

					if (value > int.MaxValue)
					{
						value = int.MaxValue;
					}
				}
                return value;
            }

            if (isFloat)
            {
				const decimal floatMinValue = -3.40282347E+28m;
				const decimal floatMaxValue = 3.40282347E+28m;
				if (value < floatMinValue)
				{
					value = floatMinValue;
				}

				if (value > floatMaxValue)
				{
					value = floatMaxValue;
				}

				// 6 decimalsi
				value = Math.Round(value, 6, MidpointRounding.AwayFromZero);
				return value;
			}

            if (isDouble)
            {
				const decimal doubleMinValue = -7.9228162514264337593543950335E+28m;
				const decimal doubleMaxValue = 7.9228162514264337593543950335E+28m;
				if (value < doubleMinValue)
				{
					value = doubleMinValue;
				}

				if (value > doubleMaxValue)
				{
					value = doubleMaxValue;
				}

				value = Math.Round(value, 10, MidpointRounding.AwayFromZero);
				return value;
			}

            return value;
        }

        private static bool IsIntegerTypeName(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName))
			{
				return false;
			}

			var t = typeName.ToLowerInvariant();
            if (t.Contains("single") || t.Contains("float") || t.Contains("double"))
			{
				return false;
			}

			return t.Contains("int") || t.Contains("long") || t.Contains("short") || t.Contains("byte");
        }

        private static string NormalizeTypeName(string typeName)
        {
            // Für UI oder Logging vereinheitlichen
            return typeName?.Trim() ?? string.Empty;
        }

        // UI-Hook: Kann vom Numeric Input aufgerufen werden nach Änderung
        public void OnArgValueChanged(KernelArgViewModel vm)
        {
            if (vm == null)
			{
				return;
			}

			vm.Value = this.AdjustValueForType(vm.Value, vm.Type);
        }

        // Vor Ausführung alles normalisieren
        public void NormalizeAllArgValues()
        {
            foreach (var vm in this.kernelArgViewModels)
            {
                vm.Value = this.AdjustValueForType(vm.Value, vm.Type);
            }
        }

        public bool IsColorGroupRepresentative(KernelArgViewModel arg)
        {
            if (this.selectedKernelInfo?.ColorInputArgNames == null || this.selectedKernelInfo.ColorInputArgNames.Length != 3)
			{
				return false;
			}

			return this.selectedKernelInfo.ColorInputArgNames[0] == arg.Name;
        }

        public void OnColorChanged()
        {
            if (this.selectedKernelInfo?.ColorInputArgNames == null || this.selectedKernelInfo.ColorInputArgNames.Length != 3)
			{
				return;
			}

			var hex = this.colorHex.StartsWith('#') ? this.colorHex[1..] : this.colorHex;
            if (hex.Length == 6)
            {
                int r = int.Parse(hex[..2], NumberStyles.HexNumber);
                int g = int.Parse(hex.Substring(2, 2), NumberStyles.HexNumber);
                int b = int.Parse(hex.Substring(4, 2), NumberStyles.HexNumber);
                for (int i = 0; i < 3; i++)
                {
                    var name = this.selectedKernelInfo.ColorInputArgNames[i];
                    var vm = this.kernelArgViewModels.FirstOrDefault(x => x.Name == name);
                    if (vm != null)
					{
						vm.Value = i == 0 ? r : i == 1 ? g : b;
					}
				}
            }
        }

        public async Task<ImageObjDto?> ExecuteGenericImageKernel(Guid id, string kernelName, string[] argNames, string[] argVals)
        {
            try
            {
				this.NormalizeAllArgValues();

                bool serverSideData = await this.Api.IsServersidedDataAsync();

				var optionalImageDto = serverSideData ? null : this.ClientImageCollection.FirstOrDefault(d => d.Info.Id == id);
                if (optionalImageDto != null)
                {
					// Set guid to empty to avoid confusion
                    id = Guid.Empty;
				}

				return await this.Api.ExecuteGenericImageKernel(id, kernelName, argNames, argVals, optionalImageDto);
            }
            catch { return null; }
        }

        public async Task<ImageObjDto?> ExecuteCreateImageAsync(int width, int height, string kernelName, string baseColorHex, string[] argNames, string[] argVals)
        {
            try
            {
				this.NormalizeAllArgValues();
                return await this.Api.ExecuteCreateImageAsync(width, height, kernelName, baseColorHex, argNames, argVals);
            }
            catch { return null; }
        }

        public void BuildKernelInfoText()
        {
            if (this.selectedKernelInfo == null) { this.kernelInfoText = string.Empty; return; }
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Function: {this.selectedKernelInfo.FunctionName}");
            try { sb.AppendLine($"File: {this.selectedKernelInfo.Filepath}"); } catch { }
            try { sb.AppendLine($"ArgumentsCount: {this.selectedKernelInfo.ArgumentsCount}"); } catch { }
            try { sb.AppendLine($"InputPointer: {this.selectedKernelInfo.InputPointerName}"); } catch { }
            try { sb.AppendLine($"OutputPointer: {this.selectedKernelInfo.OutputPointerName}"); } catch { }
            sb.AppendLine("Arguments:");
            for (int i = 0; i < this.selectedKernelInfo.ArgumentNames.Count(); i++)
            {
                var name = this.selectedKernelInfo.ArgumentNames.ElementAt(i);
                var type = this.selectedKernelInfo.ArgumentType.ElementAt(i);
                var flags = "";
                if (this.selectedKernelInfo.ColorInputArgNames != null && this.selectedKernelInfo.ColorInputArgNames.Contains(name))
				{
					flags += "[color] ";
				}

				if (type.Contains("*"))
				{
					flags += "[ptr] ";
				}

				sb.AppendLine($"  #{i} {type} {name} {flags}");
            }
            if (this.selectedKernelInfo.ColorInputArgNames != null && this.selectedKernelInfo.ColorInputArgNames.Length == 3)
            {
                sb.AppendLine($"Color Group: {string.Join(", ", this.selectedKernelInfo.ColorInputArgNames)}");
            }
            sb.AppendLine($"Needs Image: {(this.selectedKernelInfo.NeedsImage ? "Yes" : "No")}");
			this.kernelInfoText = sb.ToString();
        }

        public decimal GetStep(string type)
        {
            var t = type?.ToLower() ?? string.Empty;
            if (t.Contains("int"))
			{
				return 1m;
			}

			if (t.Contains("single") || t.Contains("float"))
			{
				return 0.05m; // changed to 0.1 for floats
			}

			if (t.Contains("double"))
			{
				return 0.01m; // changed to 0.005 for double
			}

			if (t.Contains("byte"))
			{
				return 1m;
			}

			return 1m;
        }

        public decimal GetMin(string type)
        {
            var t = type?.ToLower() ?? string.Empty;
            if (t.Contains("int"))
			{
				return int.MinValue;
			}

			if (t.Contains("single") || t.Contains("float") || t.Contains("double"))
			{
				return -1000000m;
			}

			if (t.Contains("byte"))
			{
				return 0m;
			}

			return int.MinValue;
        }

        public decimal GetMax(string type)
        {
            var t = type?.ToLower() ?? string.Empty;
            if (t.Contains("int"))
			{
				return int.MaxValue;
			}

			if (t.Contains("single") || t.Contains("float") || t.Contains("double"))
			{
				return 1000000m;
			}

			if (t.Contains("byte"))
			{
				return 255m;
			}

			return int.MaxValue;
        }

        // Erweitert: unterstützt nun "Int32", "Single", "Double", "Byte", "UInt32", etc.
        public object CastArg(decimal value, string type)
        {
            if (string.IsNullOrWhiteSpace(type))
			{
				return (int)Math.Round(value, 0, MidpointRounding.AwayFromZero);
			}

			var t = type.Trim();

            // Erst normalisieren
            value = this.AdjustValueForType(value, t);

            var tl = t.ToLowerInvariant();
            if (tl.Contains("single") || tl == "float")
			{
				return (float)value;
			}

			if (tl.Contains("double"))
			{
				return (double)value;
			}

			if (tl.Contains("byte") && !tl.Contains("sbyte"))
			{
				return (byte)Math.Round(value, 0, MidpointRounding.AwayFromZero);
			}

			if (tl.Contains("sbyte"))
			{
				return (sbyte)Math.Round(value, 0, MidpointRounding.AwayFromZero);
			}

			if (tl.Contains("uint64") || tl.Contains("ulong"))
			{
				return (ulong)Math.Max(0, Math.Round(value, 0, MidpointRounding.AwayFromZero));
			}

			if (tl.Contains("int64") || tl.Contains("long"))
			{
				return (long)Math.Round(value, 0, MidpointRounding.AwayFromZero);
			}

			if (tl.Contains("uint32"))
			{
				return (uint)Math.Max(0, Math.Round(value, 0, MidpointRounding.AwayFromZero));
			}

			if (tl.Contains("uint16") || tl.Contains("ushort"))
			{
				return (ushort)Math.Max(0, Math.Round(value, 0, MidpointRounding.AwayFromZero));
			}

			if (tl.Contains("int16") || tl.Contains("short"))
			{
				return (short)Math.Round(value, 0, MidpointRounding.AwayFromZero);
			}

			// Default Int32
			return (int)Math.Round(value, 0, MidpointRounding.AwayFromZero);
        }

        public bool IsWidthOrHeight(KernelArgViewModel arg)
        {
            var lname = arg.Name.ToLower();
            var t = arg.Type?.ToLower() ?? string.Empty;
            return (lname.Contains("width") || lname.Contains("height")) && (t.Contains("int") || t.Contains("decimal"));
        }

        public decimal[] GetDimensionSteps(string name)
        {
            var lname = name.ToLower();
            if (lname.Contains("width"))
			{
				return WidthSteps;
			}

			if (lname.Contains("height"))
			{
				return HeightSteps;
			}

			return [];
        }

        private static readonly decimal[] WidthSteps = [64, 144, 240, 320, 360, 480, 512, 720, 960, 1024, 1080, 1280, 1440, 1680, 1920, 2048, 2160, 2560,3840, 4096, 8192, 16384];
        private static readonly decimal[] HeightSteps = [64, 144, 240, 320, 360, 480, 512, 720,960, 1024, 1080, 1200, 1440, 1600, 1920, 2048, 4096, 8192, 16384];
		public double lastKernelProcessingTimeMs;

		// Small viewmodel for arguments
		public sealed class KernelArgViewModel
        {
            public int Index { get; set; }
            public string Name { get; set; } = string.Empty;
            public string Type { get; set; } = string.Empty;
            public decimal Value { get; set; }
            public decimal Step { get; set; }
            public decimal Min { get; set; }
            public decimal Max { get; set; }
            public bool IsPointer { get; set; }
            public bool IsColor { get; set; }
            // Zusatzinfos für UI-Steuerung
            public bool IsIntegerType { get; set; }
            public string NormalizedClrType { get; set; } = string.Empty;
            public string StepString => this.Step.ToString(CultureInfo.InvariantCulture);
        }

        // Color helpers
        public static bool TryParseColor(string? input, out int r, out int g, out int b)
        {
            r = g = b = 0;
            if (string.IsNullOrWhiteSpace(input))
			{
				return false;
			}

			var s = input.Trim();
            try
            {
                if (s.StartsWith("#"))
                {
                    var h = s.Substring(1);
                    if (h.Length == 8)
					{
						h = h.Substring(2);
					}

					if (h.Length >= 6)
                    {
                        r = int.Parse(h.Substring(0, 2), NumberStyles.HexNumber);
                        g = int.Parse(h.Substring(2, 2), NumberStyles.HexNumber);
                        b = int.Parse(h.Substring(4, 2), NumberStyles.HexNumber);
                        return true;
                    }
                    return false;
                }
                else if (s.StartsWith("rgb", StringComparison.OrdinalIgnoreCase))
                {
                    var start = s.IndexOf('(');
                    var end = s.IndexOf(')');
                    if (start >= 0 && end > start)
                    {
                        var inner = s.Substring(start + 1, end - start - 1);
                        var parts = inner.Split(',').Select(p => p.Trim()).ToArray();
                        if (parts.Length >= 3)
                        {
                            bool parsedR = int.TryParse(parts[0].Split('%')[0], out r);
                            bool parsedG = int.TryParse(parts[1].Split('%')[0], out g);
                            bool parsedB = int.TryParse(parts[2].Split('%')[0], out b);
                            if (parsedR && parsedG && parsedB)
							{
								return true;
							}

							if (float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var fr) &&
                                float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var fg) &&
                                float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var fb))
                            {
                                r = (int)fr; g = (int)fg; b = (int)fb; return true;
                            }
                        }
                    }
                    return false;
                }
                else
                {
                    var h = s;
                    if (h.Length == 8)
					{
						h = h.Substring(2);
					}

					if (h.Length >= 6 && h.All(c => Uri.IsHexDigit(c)))
                    {
                        r = int.Parse(h.Substring(0, 2), NumberStyles.HexNumber);
                        g = int.Parse(h.Substring(2, 2), NumberStyles.HexNumber);
                        b = int.Parse(h.Substring(4, 2), NumberStyles.HexNumber);
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        public string GetRgbLabel(string hex)
        {
            string[] colorArgNames = new string[] { "R", "G", "B" };
            if (this.selectedKernelInfo?.ColorInputArgNames != null && this.selectedKernelInfo.ColorInputArgNames.Length >= 3)
            {
                colorArgNames = this.selectedKernelInfo.ColorInputArgNames.ToArray();
            }

            if (TryParseColor(hex, out var r, out var g, out var b))
            {
                return $"{colorArgNames[0]}:{r}, {colorArgNames[1]}:{g}, {colorArgNames[2]}:{b}";
            }
            return $"{colorArgNames[0]}:0, {colorArgNames[1]}:0, {colorArgNames[2]}:0";
        }

        public void OnBaseColorChanged(string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                this.colorHexForNewImage = value!;
            }

            // Prüfe, ob ein RGB-Tupel erkannt wurde
            if (this.selectedKernelInfo?.ColorInputArgNames != null && this.selectedKernelInfo.ColorInputArgNames.Length == 3)
            {
                if (TryParseColor(this.colorHexForNewImage, out var r, out var g, out var b))
                {
                    for (int i = 0; i < 3; i++)
                    {
                        var argName = this.selectedKernelInfo.ColorInputArgNames[i];
                        var vm = this.kernelArgViewModels.FirstOrDefault(x => x.Name == argName);
                        if (vm != null)
                        {
                            vm.Value = i == 0 ? r : i == 1 ? g : b;
                        }
                    }
                }
            }
        }
        // helper to get max allowed for numeric
        public int GetMaxImagesConfig() => this.Config?.ImagesLimit ?? 0;

        // helper to compute formatted size for UI usage
        public string FormatSize(ImageObjInfo img)
        {
            double bytes = img.FrameSizeMb * 1024.0 * 1024.0;
            double scaled = bytes / Math.Max(1.0, this.MagnitudeFactor);
            return scaled.ToString("F2", CultureInfo.InvariantCulture);
        }

        public string GetImageDisplayHtml(ImageObjInfo img)
        {
            if (img == null)
			{
				return string.Empty;
			}

			var id = img.Id.ToString();
            var name = string.IsNullOrWhiteSpace(img.FilePath) ? string.Empty : img.FilePath;
            // compute scaled size using MagnitudeFactor
            double bytes = img.FrameSizeMb * 1024.0 * 1024.0;
            double scaled = bytes / Math.Max(1.0, this.MagnitudeFactor);
            var sizeStr = scaled.ToString("F2", CultureInfo.InvariantCulture);
            var unit = this.SelectedMagnitude ?? "kB";
            // Build HTML with small font, 1-3 lines separated visually
            var sb = new System.Text.StringBuilder();
            sb.Append("<div style='font-size:0.8em;line-height:1.05em;'>");
            sb.AppendFormat("<div>{0}</div>", System.Net.WebUtility.HtmlEncode(id));
            sb.AppendFormat("<div style='color:var(--fg);'>'{0}'</div>", System.Net.WebUtility.HtmlEncode(name));
            sb.AppendFormat("<div>[{0},{1}] : {2} {3}</div>", img.Size.Width, img.Size.Height, sizeStr, unit);
            sb.Append("</div>");
            return sb.ToString();
        }

        public async Task SetMaxImagesAsync(int value)
        {
            try
            {
                // Normalisieren: negative Werte wie 0 behandeln (unbegrenzt)
                if (value < 0)
				{
					value = 0;
				}

				bool isServer = await this.Api.IsServersidedDataAsync();

                if (!isServer)
                {
                    // Client‑seitige Verwaltung
                    this.MaxImagesToKeepNumeric = value;

                    // Nur begrenzen wenn value > 0
                    if (value > 0)
                    {
                        this.ClientImageCollection = this.ClientImageCollection
                            .OrderBy(i => i.Info.CreatedAt)
                            .Take(value)
                            .ToList();
                    }
                    // Bilderliste neu aufbauen
                    await this.LoadImages();

                    // Auswahl korrigieren
                    if (this.images.Count > 0)
                    {
                        this.selectedImageId = this.images.Last().Id;
                        await this.UpdateImageData();
                    }
                    else
                    {
                        this.selectedImageId = Guid.Empty;
                        this.imageData = null;
                    }
                    return; // Wichtig: Server-Teil überspringen
                }

                // Server-seitige Begrenzung
                var cfg = this.Config?.ImagesLimit ?? 0;

                if (value == 0 && cfg > 0)
                {
                    this.Notifications.Notify(new NotificationMessage
                    {
                        Severity = NotificationSeverity.Warning,
                        Summary = $"Unbegrenzt (0) ist nicht erlaubt. Server-Config erzwingt höchstens {cfg}.",
                        Duration = 4000
                    });
                    value = cfg;
                }

                int effective = value;
                if (cfg > 0)
                {
                    if (effective <= 0)
					{
						effective = cfg;
					}
					else
					{
						effective = Math.Min(effective, cfg);
					}
				}

                if (effective > 0)
                {
                    await this.Api.CleanupOldImages(effective);
                }

                this.MaxImagesToKeepNumeric = effective;

                // Nach Cleanup Liste neu laden
                await this.LoadImages();

                if (this.images.Count > 0)
                {
                    this.selectedImageId = this.images.Last().Id;
                    await this.UpdateImageData();
                }
                else
                {
                    this.selectedImageId = Guid.Empty;
                    this.imageData = null;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("SetMaxImagesAsync error: " + ex.Message);
            }
        }

		public void RandomizeColor()
		{
			if (this.selectedKernelInfo?.ColorInputArgNames != null && this.selectedKernelInfo.ColorInputArgNames.Length == 3)
			{
				var rnd = new Random();
				int r = rnd.Next(0, 256);
				int g = rnd.Next(0, 256);
				int b = rnd.Next(0, 256);
				this.colorHexForNewImage = $"#{r:X2}{g:X2}{b:X2}";
				this.OnBaseColorChanged(this.colorHexForNewImage);
			}
		}

        // NEU: Methode zum Anpassen der Width/Height-Argumente an selektiertes Bild oder 0
        public void UpdateWidthHeightFromImage(ImageObjInfo? info)
        {
            if (this.kernelArgViewModels == null || this.kernelArgViewModels.Count == 0)
                return;

            int w = info?.Size?.Width ?? 0;
            int h = info?.Size?.Height ?? 0;

            foreach (var a in this.kernelArgViewModels)
            {
                var lname = a.Name.ToLowerInvariant();
                if (lname.Contains("width"))
                    a.Value = w;
                else if (lname.Contains("height"))
                    a.Value = h;
            }
        }

        // NEU: Aufruf wenn Modus (useExistingImage) geändert wird
        public void OnUseExistingModeChanged(ImageObjInfo? currentImageInfo)
        {
            if (this.useExistingImage)
            {
				this.UpdateWidthHeightFromImage(currentImageInfo);
            }
            else
            {
                // Beim Umschalten auf "neues Bild" bisherige Default-Werte beibehalten oder falls 0 -> Standard
                foreach (var a in this.kernelArgViewModels)
                {
                    if (this.IsWidthOrHeight(a) && a.Value <= 0)
                    {
                        var lname = a.Name.ToLower();
                        if (lname.Contains("width")) a.Value = 720;
                        if (lname.Contains("height")) a.Value = 480;
                    }
                }
            }
        }

        // NEU: Snap für Dimensionen (Auflösungsschritte)
        public decimal SnapDimensionValue(string name, decimal previous, decimal requested)
        {
            var lname = name.ToLowerInvariant();
            decimal[] steps = lname.Contains("width")
                ? WidthSteps
                : HeightSteps;

            // Wenn Pfeil-Inkrement → requested unterscheidet sich typischerweise um +1 / -1 oder kleinen Wert
            bool increased = requested > previous;

            // Falls vorher noch 0 (z.B. beim Start), auf ersten sinnvollen Wert springen
            if (previous <= 0)
                return steps.First();
			// Suche nächsthöheren / nächstniedrigeren Wert
            if (increased)
            {
                foreach (var s in steps)
                {
                    if (s > previous)
                        return s;
                }
                return steps.Last(); // Maximalwert
            }
            else
            {
                for (int i = steps.Length - 1; i >= 0; i--)
                {
                    if (steps[i] < previous)
                        return steps[i];
                }
                return steps.First(); // Minimalwert
			}
        }

		// Füge diese Methode zu deinem @code-Block oder ViewModel hinzu
		public MarkupString FormatGuidForDisplay(Guid id)
		{
			string[] segments = id.ToString("D").Split('-');

			var sb = new System.Text.StringBuilder();

			string currentLine = "";
			const int MaxLineLength = 10;

			for (int i = 0; i < segments.Length; i++)
			{
				string segment = segments[i];
				if (currentLine.Length > 0 && (currentLine.Length + 1 + segment.Length) > MaxLineLength)
				{
					sb.Append("<br>");
					currentLine = "";
				}

				if (currentLine.Length > 0)
				{
					currentLine += "-"; 
				}
				currentLine += segment;

				if (i > 0)
				{
					sb.Append('-');
				}
				sb.Append(segment);
			}

			var finalSb = new System.Text.StringBuilder();
			string currentLineSegments = "";

			for (int i = 0; i < segments.Length; i++)
			{
				string segment = segments[i];
				string nextSegmentSeparator = (i < segments.Length - 1) ? "-" : "";
				int newLength = currentLineSegments.Length + segment.Length;
				if (currentLineSegments.Length > 0)
				{
					newLength++;
				}

				if (newLength > MaxLineLength && currentLineSegments.Length > 0)
				{
					finalSb.Append("<br>");
					finalSb.Append(segment);
					currentLineSegments = segment;
				}
				else if (currentLineSegments.Length > 0)
				{
					finalSb.Append('-');
					finalSb.Append(segment);
					currentLineSegments += ("-" + segment);
				}
				else
				{
					finalSb.Append(segment);
					currentLineSegments = segment;
				} 
				if (i == 3 && i != segments.Length - 1)
				{
					finalSb.Append("<br>-");
					currentLineSegments = "";
				}
			}

			return (MarkupString) finalSb.ToString();
		}

	}
}


