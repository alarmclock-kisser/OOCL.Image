using Microsoft.AspNetCore.Components.Forms;
using OOCL.Image.Shared;
using System.Net.Http.Headers;
using System.Text.Json;

namespace OOCL.Image.Client
{
	public class ApiClient
	{
		private readonly InternalClient internalClient;
		private readonly HttpClient httpClient;
		private readonly RollingFileLogger logger;
		private readonly string baseUrl;
		private JsonSerializerOptions jsonSerializerOptions = new() { PropertyNameCaseInsensitive = true };

		public string BaseUrl => this.baseUrl;

		public ApiClient(RollingFileLogger? logger, HttpClient httpClient)
		{
			this.logger = logger ?? new RollingFileLogger(1024, false, null, "log_client_");
			this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
			this.baseUrl = httpClient.BaseAddress?.ToString().TrimEnd('/') ?? throw new InvalidOperationException("HttpClient.BaseAddress is not set. Configure it in DI registration.");
			this.internalClient = new InternalClient(this.baseUrl, this.httpClient);
		}


		public async Task<WebApiConfig> GetApiConfigAsync()
		{
			// Logging
			await this.logger.LogAsync($"Called GetApiConfigAsync()", nameof(ApiClient));

			try
			{
				return await this.internalClient.ApiConfigAsync();
			}
			catch (Exception ex)
			{
				await this.logger.LogExceptionAsync(ex, nameof(ApiClient));
				return new WebApiConfig();
			}
		}

		public async Task<bool> IsServersidedDataAsync()
		{
			await this.logger.LogAsync($"Called IsServersidedDataAsync()", nameof(ApiClient));
			try
			{
				return await this.internalClient.ServerSidedDataAsync();
			}
			catch (Exception ex)
			{
				await this.logger.LogExceptionAsync(ex, nameof(ApiClient));
				return false;
			}
		}

		public async Task<IEnumerable<ImageObjInfo>> GetImageListAsync()
		{
			await this.logger.LogAsync($"Called GetImageListAsync()", nameof(ApiClient));
			try
			{
				return (await this.internalClient.ImagesAsync()).ToList();
			}
			catch (Exception ex)
			{
				await this.logger.LogExceptionAsync(ex, nameof(ApiClient));
				return [];
			}
		}

		public async Task<IEnumerable<AudioObjInfo>> GetAudioListAsync(bool includeData = false)
		{
			await this.logger.LogAsync($"Called GetAudioListAsync()", nameof(ApiClient));
			try
			{
				return (await this.internalClient.TracksAsync(includeData)).ToList();
			}
			catch (Exception ex)
			{
				await this.logger.LogExceptionAsync(ex, nameof(ApiClient));
				return [];
			}
		}

		public async Task<ImageObjDto> UploadImageAsync(FileParameter file)
		{
			await this.logger.LogAsync($"Called UploadImageAsync()", nameof(ApiClient));
			if (file == null || file.Data == null || string.IsNullOrWhiteSpace(file.FileName))
			{
				return new ImageObjDto();
			}

			if (await this.IsServersidedDataAsync())
			{
				try
				{
					return new ImageObjDto(await this.internalClient.LoadImageAsync(file), new ImageObjData());
				}
				catch (Exception ex)
				{
					await this.logger.LogExceptionAsync(ex, nameof(ApiClient));
					return new ImageObjDto();
				}
			}
			else
			{
				var data = await new StreamContent(file.Data).ReadAsByteArrayAsync();
				var ints = data.Select(b => (int)b).ToArray();
				var dto = await this.internalClient.CreateImageFromDataAsync(ints, file.FileName, file.ContentType, false);
				
				return dto ?? new ImageObjDto();
			}
		}

		public async Task<ImageObjInfo> UploadImageAsync(IBrowserFile browserFile)
		{
			await this.logger.LogAsync($"Called UploadImageAsync()", nameof(ApiClient));
			try
			{
				using var content = new MultipartFormDataContent();
				await using var stream = browserFile.OpenReadStream(long.MaxValue);
				var sc = new StreamContent(stream);
				sc.Headers.ContentType = new MediaTypeHeaderValue(browserFile.ContentType ?? "application/octet-stream");
				content.Add(sc, "file", browserFile.Name);
				var response = await this.httpClient.PostAsync("api/image/load-image", content);
				if (!response.IsSuccessStatusCode)
				{
					Console.WriteLine(await response.Content.ReadAsStringAsync());
					return new ImageObjInfo();
				}
				var json = await response.Content.ReadAsStringAsync();
				return JsonSerializer.Deserialize<ImageObjInfo>(json, this.jsonSerializerOptions) ?? new ImageObjInfo();
			}
			catch (Exception ex)
			{
				Console.WriteLine("Upload exception: " + ex);
				return new ImageObjInfo();
			}
		}

		public async Task<AudioObjDto> UploadAudioAsync(FileParameter file, bool includeData = false)
		{
			await this.logger.LogAsync($"Called UploadAudioAsync(FileParameter file: {(file != null)}, bool includeData: {includeData.ToString()})", nameof(ApiClient));
			if (file == null || file.Data == null || string.IsNullOrWhiteSpace(file.FileName))
			{
				return new AudioObjDto();
			}
			try
			{
				return await this.internalClient.LoadAudioAsync(includeData, file);
			}
			catch (Exception ex)
			{
				await this.logger.LogExceptionAsync(ex, nameof(ApiClient));
				return new AudioObjDto();
			}
		}

		public async Task<AudioObjDto> UploadAudioAsync(IBrowserFile browserFile, bool includeData = false)
		{
			await this.logger.LogAsync($"Called UploadAudioAsync(IBrowserFile browserFile: {(browserFile != null)}, bool includeData: {includeData.ToString()})", nameof(ApiClient));
			if (browserFile == null)
			{
				return new AudioObjDto();
			}
			try
			{
				using var content = new MultipartFormDataContent();
				await using var stream = browserFile.OpenReadStream(long.MaxValue);
				var sc = new StreamContent(stream);
				sc.Headers.ContentType = new MediaTypeHeaderValue(browserFile.ContentType ?? "application/octet-stream");
				content.Add(sc, "file", browserFile.Name);
				var response = await this.httpClient.PostAsync("api/audio/load-audio?includeData=" + (includeData ? "true" : "false"), content);
				if (!response.IsSuccessStatusCode)
				{
					Console.WriteLine(await response.Content.ReadAsStringAsync());
					return new AudioObjDto();
				}
				var json = await response.Content.ReadAsStringAsync();
				return JsonSerializer.Deserialize<AudioObjDto>(json, this.jsonSerializerOptions) ?? new AudioObjDto();
			}
			catch (Exception ex)
			{
				Console.WriteLine("Upload exception: " + ex);
				return new AudioObjDto();
			}
		}


		public async Task RemoveImageAsync(Guid id)
		{
			// Logging
			await this.logger.LogAsync($"Called RemoveImageAsync({id})", nameof(ApiClient));
			try
			{
				await this.internalClient.RemoveImageAsync(id);
			}
			catch (Exception ex)
			{
				await this.logger.LogExceptionAsync(ex, nameof(ApiClient));
			}
		}

		public async Task RemoveAudioAsync(Guid id)
		{
			// Logging
			await this.logger.LogAsync($"Called RemoveAudioAsync({id})", nameof(ApiClient));
			try
			{
				await this.internalClient.RemoveAudioAsync(id);
			}
			catch (Exception ex)
			{
				await this.logger.LogExceptionAsync(ex, nameof(ApiClient));
			}
		}

		public async Task ClearImagesAsync()
		{
			// Logging
			await this.logger.LogAsync($"Called ClearImagesAsync()", nameof(ApiClient));
			try
			{
				await this.internalClient.ClearAllImageAsync();
			}
			catch (Exception ex)
			{
				await this.logger.LogExceptionAsync(ex, nameof(ApiClient));
			}
		}

		public async Task ClearAudiosAsync()
		{
			// Logging
			await this.logger.LogAsync($"Called ClearAudiosAsync()", nameof(ApiClient));
			try
			{
				await this.internalClient.ClearAllAudioAsync();
			}
			catch (Exception ex)
			{
				await this.logger.LogExceptionAsync(ex, nameof(ApiClient));
			}
		}


		public async Task<ImageObjData> GetImageDataAsync(Guid id, string format = "png")
		{
			await this.logger.LogAsync($"Called GetImageDataAsync({id}, {format})", nameof(ApiClient));
			try
			{
				return await this.internalClient.DataImageAsync(id, format);
			}
			catch (Exception ex)
			{
				await this.logger.LogExceptionAsync(ex, nameof(ApiClient));
				return new ImageObjData();
			}
		}

		public async Task<AudioObjData> GetAudioDataAsync(Guid id)
		{
			await this.logger.LogAsync($"Called GetAudioDataAsync({id})", nameof(ApiClient));
			try
			{
				return await this.internalClient.DataAudioAsync(id);
			}
			catch (Exception ex)
			{
				await this.logger.LogExceptionAsync(ex, nameof(ApiClient));
				return new AudioObjData();
			}
		}

		public async Task<string> GetWaveformBase64Async(Guid? id = null, AudioObjDto? dto = null, int width = 800, int height = 200, long offset = 0, int samplesPerFixel = 128, string? graphColor = "#FFFFFF", string? backColor = "#000000", string format = "jpg")
		{
			try
			{
				if (dto == null)
				{
					dto = new();
				}

				var base64 = await this.internalClient.WaveformBase64Async(id, width, height, samplesPerFixel, offset.ToString(), graphColor, backColor, format, dto);

				return base64 ?? string.Empty;
			}
			catch (Exception ex)
			{
				await this.logger.LogExceptionAsync(ex, nameof(ApiClient));
				return string.Empty;
			}
		}




		public async Task<FileResponse?> DownloadImageAsync(Guid id, string format = "png")
		{
			await this.logger.LogAsync($"Called DownloadImageAsync({id}, {format})", nameof(ApiClient));
			try
			{
				return await this.internalClient.DownloadImageAsync(id, format);
			}
			catch (Exception ex)
			{
				await this.logger.LogExceptionAsync(ex, nameof(ApiClient));
				return null;
			}
		}

		public async Task<FileResponse?> DownloadAudioAsync(Guid id, string format = "wav", int bits = 24)
		{
			await this.logger.LogAsync($"Called DownloadAudioAsync({id})", nameof(ApiClient));
			try
			{
				return await this.internalClient.DownloadAudioAsync(id, format, bits);
			}
			catch (Exception ex)
			{
				await this.logger.LogExceptionAsync(ex, nameof(ApiClient));
				return null;
			}
		}

		public async Task<FileResponse?> DownloadAudioDataAsync(AudioObjDto? dto, string format = "wav", int bits = 24)
		{
			if (dto == null || dto.Data == null)
			{
				return null;
			}

			if (bits != 8 && bits != 16 && bits != 24 && bits != 32)
			{
				bits = 24;
			}

			await this.logger.LogAsync($"Called DownloadAudioDataAsync(dto: {(dto != null ? dto.Info.Id.ToString() : "null")}, {format}, {bits})", nameof(ApiClient));
			try
			{
				if (dto == null || dto.Info == null || dto.Data == null)
				{
					await this.logger.LogAsync("No valid dto provided for audio download.", nameof(ApiClient));
					return null;
				}

				return await this.internalClient.DownloadAudioDataAsync(format, bits, dto);
			}
			catch (Exception ex)
			{
				await this.logger.LogExceptionAsync(ex, nameof(ApiClient));
				return null;
			}
		}



		public async Task<FileResponse?> DownloadAsGif(Guid[]? ids = null, ImageObjDto[]? dtos = null, int frameRate = 10, double rescaleFactor = 1.0, bool doLoop = true)
		{
			await this.logger.LogAsync(
				$"Called DownloadAsGif(ids: {(ids != null ? ids.Length.ToString() : "null")}, dtos: {(dtos != null ? dtos.Length.ToString() : "null")}, frameRate: {frameRate}, rescaleFactor: {rescaleFactor}, doLoop: {doLoop})",
				nameof(ApiClient));

			try
			{
				if ((ids == null || ids.Length == 0) && (dtos == null || dtos.Length == 0))
				{
					await this.logger.LogAsync("No ids or dtos provided for GIF creation.", nameof(ApiClient));
					return null;
				}

				var request = new CreateGifRequest
				{
					Ids = ids != null ? ids.ToList() : [],
					Dtos = dtos != null ? dtos.ToList() : [],
					FrameRate = frameRate > 0 ? frameRate : 10,
					Rescale = rescaleFactor > 0.0 ? rescaleFactor : 1.0,
					DoLoop = doLoop
				};

				var response = await this.internalClient.CreateGifAsync(request);
				if (response == null || response.Stream == null)
				{
					await this.logger.LogAsync("GIF creation returned null response or null stream.", nameof(ApiClient));
					return null;
				}

				long sizeBytes = -1;
				try
				{
					if (response.Stream.CanSeek)
					{
						sizeBytes = response.Stream.Length;
						response.Stream.Position = 0;
					}
					else
					{
						// WICHTIG: Nicht konsumieren – nur protokollieren.
						await this.logger.LogAsync("Stream not seekable; skip size inspection to avoid consumption.", nameof(ApiClient));
					}
				}
				catch (Exception exSize)
				{
					await this.logger.LogAsync("Size inspection failed: " + exSize.Message, nameof(ApiClient));
				}

				await this.logger.LogAsync($"GIF response stream ready (bytes≈{(sizeBytes >= 0 ? sizeBytes : -1)})", nameof(ApiClient));
				return response;
			}
			catch (Exception ex)
			{
				await this.logger.LogExceptionAsync(ex, nameof(ApiClient));
				return null;
			}
		}



		public async Task<int> CleanupOldImages(int maxImages = 0)
		{
			await this.logger.LogAsync($"Called CleanupOldImages({maxImages})", nameof(ApiClient));
			try
			{
				return await this.internalClient.CleanupOnlyKeepLatestImageAsync(maxImages);
			}
			catch (Exception ex)
			{
				await this.logger.LogExceptionAsync(ex, nameof(ApiClient));
				return 0;
			}
		}

		public async Task<int> CleanupOldAudios(int maxAudios = 0)
		{
			await this.logger.LogAsync($"Called CleanupOldAudios({maxAudios})", nameof(ApiClient));
			try
			{
				return await this.internalClient.CleanupOnlyKeepLatestAudioAsync(maxAudios);
			}
			catch (Exception ex)
			{
				await this.logger.LogExceptionAsync(ex, nameof(ApiClient));
				return 0;
			}
		}



		public async Task<OpenClServiceInfo> GetOpenClServiceInfoAsync()
		{
			await this.logger.LogAsync($"Called GetOpenClServiceInfoAsync()", nameof(ApiClient));
			try
			{
				return await this.internalClient.Status2Async();
			}
			catch (Exception ex)
			{
				await this.logger.LogExceptionAsync(ex, nameof(ApiClient));
				return new OpenClServiceInfo();
			}
		}

		public async Task<OpenClServiceInfo> InitializeOpenClIndexAsync(int deviceId = -1)
		{
			await this.logger.LogAsync($"Called InitializeOpenClIndexAsync({deviceId})", nameof(ApiClient));
			try
			{
				return await this.internalClient.InitializeIdAsync(deviceId);
			}
			catch (Exception ex)
			{
				await this.logger.LogExceptionAsync(ex, nameof(ApiClient));
				return new OpenClServiceInfo();
			}
		}

		public async Task DisposeOpenClAsync()
		{
			await this.logger.LogAsync($"Called DisposeOpenClAsync()", nameof(ApiClient));
			try
			{
				await this.internalClient.ReleaseAsync();
			}
			catch (Exception ex)
			{
				await this.logger.LogExceptionAsync(ex, nameof(ApiClient));
			}
		}

		public async Task<IEnumerable<OpenClDeviceInfo>> GetOpenClDevicesAsync()
		{
			await this.logger.LogAsync($"Called GetOpenClDevicesAsync()", nameof(ApiClient));
			try
			{
				return await this.internalClient.DevicesAsync();
			}
			catch (Exception ex)
			{
				await this.logger.LogExceptionAsync(ex, nameof(ApiClient));
				return [];
			}
		}

		public async Task<IEnumerable<OpenClKernelInfo>> GetOpenClKernelsAsync(bool onlyCompiled = true, string? mediaType = null)
		{
			await this.logger.LogAsync($"Called GetOpenClKernelsAsync({onlyCompiled})", nameof(ApiClient));
			try
			{
				var kernels = await this.internalClient.KernelInfosAsync(onlyCompiled);
				if (!string.IsNullOrWhiteSpace(mediaType))
				{
					kernels = kernels.Where(k => string.Equals(k.MediaType, mediaType, StringComparison.OrdinalIgnoreCase)).ToList();
				}

				return kernels; 
			}
			catch (Exception ex)
			{
				await this.logger.LogExceptionAsync(ex, nameof(ApiClient));
				return [];
			}
		}

		public async Task<IEnumerable<OpenClMemoryInfo>> GetOpenClMemoryAsync()
		{
			await this.logger.LogAsync($"Called GetOpenClMemoryAsync()", nameof(ApiClient));
			try
			{
				// return this.internalClient.
				return [];
			}
			catch (Exception ex)
			{
				await this.logger.LogExceptionAsync(ex, nameof(ApiClient));
				return [];
			}
			finally
			{
				await Task.Yield();
			}
		}



		public async Task<ImageObjDto> ExecuteGenericImageKernel(Guid id, string kernelName, string[] argNames, string[] argValues, ImageObjDto? optionalImageObjDto = null)
		{
			await this.logger.LogAsync($"Called ExecuteGenericImageKernel({id}, {kernelName}, argNames: {(argNames != null ? argNames.Length.ToString() : "null")}, argValues: {(argValues != null ? argValues.Length.ToString() : "null")}, optionalImageObjDto: {(optionalImageObjDto != null ? "provided" : "null")})", nameof(ApiClient));
			try
			{
				ExecuteOnImageRequest request = new();
				request.ImageId = id;
				request.KernelName = kernelName;
				request.Arguments = [];
				for (int i = 0; i < argNames?.Length && i < argValues?.Length; i++)
				{
					if (!string.IsNullOrWhiteSpace(argNames[i]) && !string.IsNullOrWhiteSpace(argValues[i]))
					{
						request.Arguments[argNames[i]] = argValues[i];
					}
				}
				if (optionalImageObjDto != null && optionalImageObjDto.Info != null && optionalImageObjDto.Data != null)
				{
					await this.logger.LogAsync($"Including optional image with ID {optionalImageObjDto.Info.Id} in request.", nameof(ApiClient));
					request.OptionalImage = optionalImageObjDto;
				}

				return await this.internalClient.ExecuteOnImageAsync(request);
			}
			catch (Exception ex)
			{
				await this.logger.LogExceptionAsync(ex, nameof(ApiClient));
				return new ImageObjDto();
			}
		}

		public async Task<ImageObjDto> ExecuteCreateImageAsync(int width, int height, string kernelName, string baseColorHex, string[] argNames, string[] argValues)
		{
			await this.logger.LogAsync($"Called ExecuteCreateImageAsync({width}, {height}, {kernelName}, {baseColorHex}, argNames: {(argNames != null ? argNames.Length.ToString() : "null")}, argValues: {(argValues != null ? argValues.Length.ToString() : "null")})", nameof(ApiClient));
			try
			{
				CreateImageRequest request = new();

				request.Width = width;
				request.Height = height;
				request.KernelName = kernelName;
				request.Arguments = [];
				/*if (!string.IsNullOrWhiteSpace(baseColorHex) && Regex.IsMatch(baseColorHex, "^#([0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})$"))
				{
					string red, green, blue, alpha;
					red = Convert.ToInt32(baseColorHex.Substring(1, 2), 16).ToString();
					green = Convert.ToInt32(baseColorHex.Substring(3, 2), 16).ToString();
					blue = Convert.ToInt32(baseColorHex.Substring(5, 2), 16).ToString();
					alpha = baseColorHex.Length == 9 ? Convert.ToInt32(baseColorHex.Substring(7, 2), 16).ToString() : "255";

					// Try find index of match in argNames
				}*/
				for (int i = 0; i < argNames?.Length && i < argValues?.Length; i++)
				{
					if (!string.IsNullOrWhiteSpace(argNames[i]) && !string.IsNullOrWhiteSpace(argValues[i]))
					{
						request.Arguments[argNames[i]] = argValues[i];
					}
				}

				return await this.internalClient.ExecuteCreateImageAsync(request);
			}
			catch (Exception ex)
			{
				await this.logger.LogExceptionAsync(ex, nameof(ApiClient));
				return new ImageObjDto();
			}
		}


		public async Task<IEnumerable<string>> GetApiLogsAsync()
		{
			await this.logger.LogAsync($"Called GetApiLogsAsync()", nameof(ApiClient));
			try
			{
				return await this.internalClient.LogAsync();
			}
			catch (Exception ex)
			{
				await this.logger.LogExceptionAsync(ex, nameof(ApiClient));
				return [];
			}
		}

		public async Task<FileResponse?> DownloadApiLogAsync()
		{
			await this.logger.LogAsync($"Called DownloadApiLogAsync()", nameof(ApiClient));
			try
			{
				return await this.internalClient.DownloadLogAsync();
			}
			catch (Exception ex)
			{
				await this.logger.LogExceptionAsync(ex, nameof(ApiClient));
				return null;
			}
		}

		public async Task<IEnumerable<string>> GetWebAppLogs(int maxLines = 0)
		{
			await this.logger.LogAsync($"Called GetWebAppLogs({maxLines})", nameof(ApiClient));
			try
			{
				var lines = await this.logger.GetRecentLogsAsync(maxLines);
				return lines ?? [];
			}
			catch (Exception ex)
			{
				await this.logger.LogExceptionAsync(ex, nameof(ApiClient));
				return [];
			}
		}
	}
}
