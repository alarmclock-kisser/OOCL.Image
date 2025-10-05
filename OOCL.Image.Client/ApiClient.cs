using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Mvc;
using OOCL.Image.Shared;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace OOCL.Image.Client
{
	public class ApiClient
	{
		private readonly InternalClient internalClient;
		private readonly HttpClient httpClient;
		private readonly string baseUrl;
		private JsonSerializerOptions jsonSerializerOptions = new() { PropertyNameCaseInsensitive = true };

		public string BaseUrl => this.baseUrl;


		public ApiClient(HttpClient httpClient)
		{
			this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
			this.baseUrl = httpClient.BaseAddress?.ToString().TrimEnd('/') ?? throw new InvalidOperationException("HttpClient.BaseAddress is not set. Configure it in DI registration.");
			this.internalClient = new InternalClient(this.baseUrl, this.httpClient);
		}


		public async Task<WebApiConfig> GetApiConfigAsync()
		{
			try
			{
				return await this.internalClient.ApiConfigAsync();
			}
			catch (Exception ex)
			{
				Console.WriteLine(ex);
				return new WebApiConfig();
			}
		}

		public async Task<bool> IsServersidedDataAsync()
		{
			try
			{
				return await this.internalClient.ServerSidedDataAsync();
			}
			catch (Exception ex)
			{
				Console.WriteLine(ex);
				return false;
			}
		}

		public async Task<IEnumerable<ImageObjInfo>> GetImageListAsync()
		{
			try
			{
				return (await this.internalClient.ListAsync()).ToList();
			}
			catch (Exception ex)
			{
				Console.WriteLine(ex);
				return [];
			}
		}

		public async Task<ImageObjDto> UploadImageAsync(FileParameter file)
		{
			if (file == null || file.Data == null || string.IsNullOrWhiteSpace(file.FileName))
			{
				return new ImageObjDto();
			}

			if (await this.IsServersidedDataAsync())
			{
				try
				{
					return new ImageObjDto(await this.internalClient.LoadAsync(file), new ImageObjData());
				}
				catch (Exception ex)
				{
					Console.WriteLine(ex);
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
			try
			{
				using var content = new MultipartFormDataContent();
				await using var stream = browserFile.OpenReadStream(long.MaxValue);
				var sc = new StreamContent(stream);
				sc.Headers.ContentType = new MediaTypeHeaderValue(browserFile.ContentType ?? "application/octet-stream");
				content.Add(sc, "file", browserFile.Name);
				var response = await this.httpClient.PostAsync("api/image/load", content);
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


		public async Task RemoveImageAsync(Guid id)
		{
			try
			{
				await this.internalClient.RemoveAsync(id);
			}
			catch (Exception ex)
			{
				Console.WriteLine(ex);
			}
		}

		public async Task ClearImagesAsync()
		{
			try
			{
				await this.internalClient.ClearAllAsync();
			}
			catch (Exception ex)
			{
				Console.WriteLine(ex);
			}
		}


		public async Task<ImageObjData> GetImageDataAsync(Guid id, string format = "png")
		{
			try
			{
				return await this.internalClient.DataAsync(id, format);
			}
			catch (Exception ex)
			{
				Console.WriteLine(ex);
				return new ImageObjData();
			}
		}




		public async Task<FileResponse?> DownloadImageAsync(Guid id, string format = "png")
		{
			try
			{
				return await this.internalClient.DownloadAsync(id, format);
			}
			catch (Exception ex)
			{
				Console.WriteLine(ex);
				return null;
			}
		}

		public async Task<FileResponse?> DownloadAsGif(Guid[]? ids = null, ImageObjDto[]? dtos = null, int frameRate = 10, double rescaleFactor = 1.0, bool doLoop = true)
		{
			try
			{
				if ((ids == null || ids.Length == 0) && (dtos == null || dtos.Length == 0))
				{
					return null;
				}
				
				return await this.internalClient.CreateGifAsync(ids ?? [], dtos ?? [], frameRate, rescaleFactor, doLoop);
			}
			catch (Exception ex)
			{
				Console.WriteLine(ex);
				return null;
			}
		}



		public async Task<int> CleanupOldImages(int maxImages = 0)
		{
			try
			{
				return await this.internalClient.CleanupOnlyKeepLatestAsync(maxImages);
			}
			catch (Exception ex)
			{
				Console.WriteLine(ex);
				return 0;
			}
		}



		public async Task<OpenClServiceInfo> GetOpenClServiceInfoAsync()
		{
			try
			{
				return await this.internalClient.StatusAsync();
			}
			catch (Exception ex)
			{
				Console.WriteLine(ex);
				return new OpenClServiceInfo();
			}
		}

		public async Task<OpenClServiceInfo> InitializeOpenClIndexAsync(int deviceId = -1)
		{
			try
			{
				return await this.internalClient.InitializeIdAsync(deviceId);
			}
			catch (Exception ex)
			{
				Console.WriteLine(ex);
				return new OpenClServiceInfo();
			}
		}

		public async Task DisposeOpenClAsync()
		{
			try
			{
				await this.internalClient.ReleaseAsync();
			}
			catch (Exception ex)
			{
				Console.WriteLine(ex);
			}
		}

		public async Task<IEnumerable<OpenClDeviceInfo>> GetOpenClDevicesAsync()
		{
			try
			{
				return await this.internalClient.DevicesAsync();
			}
			catch (Exception ex)
			{
				Console.WriteLine(ex);
				return [];
			}
		}

		public async Task<IEnumerable<OpenClKernelInfo>> GetOpenClKernelsAsync(bool onlyCompiled = true)
		{
			try
			{
				return await this.internalClient.KernelInfosAsync(onlyCompiled);
			}
			catch (Exception ex)
			{
				Console.WriteLine(ex);
				return [];
			}
		}

		public async Task<IEnumerable<OpenClMemoryInfo>> GetOpenClMemoryAsync()
		{
			try
			{
				// return this.internalClient.
				return [];
			}
			catch (Exception ex)
			{
				Console.WriteLine(ex);
				return [];
			}
			finally
			{
				await Task.Yield();
			}
		}



		public async Task<ImageObjDto> ExecuteGenericImageKernel(Guid id, string kernelName, string[] argNames, string[] argValues, ImageObjDto? optionalImageObjDto = null)
		{
			try
			{
				ExecuteOnImageRequest request = new();
				request.ImageId = id;
				request.KernelName = kernelName;
				request.Arguments = [];
				for (int i = 0; i < argNames.Length && i < argValues.Length; i++)
				{
					if (!string.IsNullOrWhiteSpace(argNames[i]) && !string.IsNullOrWhiteSpace(argValues[i]))
					{
						request.Arguments[argNames[i]] = argValues[i];
					}
				}
				if (optionalImageObjDto != null && optionalImageObjDto.Info != null && optionalImageObjDto.Data != null)
				{
					request.OptionalImage = optionalImageObjDto;
				}

				return await this.internalClient.ExecuteOnImageAsync(request);
			}
			catch (Exception ex)
			{
				Console.WriteLine(ex);
				return new ImageObjDto();
			}
		}

		public async Task<ImageObjDto> ExecuteCreateImageAsync(int width, int height, string kernelName, string baseColorHex, string[] argNames, string[] argValues)
		{
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
				for (int i = 0; i < argNames.Length && i < argValues.Length; i++)
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
				Console.WriteLine(ex);
				return new ImageObjDto();
			}
		}


	}
}
