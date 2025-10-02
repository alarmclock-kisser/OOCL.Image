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

		public string BaseUrl => this.baseUrl;


		public ApiClient(HttpClient httpClient)
		{
			this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
			this.baseUrl = httpClient.BaseAddress?.ToString().TrimEnd('/') ?? throw new InvalidOperationException("HttpClient.BaseAddress is not set. Configure it in DI registration.");
			this.internalClient = new InternalClient(this.baseUrl, this.httpClient);
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

		public async Task<ImageObjInfo> UploadImageAsync(FileParameter file)
		{
			try
			{
				return await this.internalClient.LoadAsync(file);
			}
			catch (Exception ex)
			{
				Console.WriteLine(ex);
				return new ImageObjInfo();
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
				return JsonSerializer.Deserialize<ImageObjInfo>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new ImageObjInfo();
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



		public async Task<ImageObjInfo> ExecuteGenericImageKernel(Guid id, string kernelName, string[] argNames, string[] argValues)
		{
			try
			{
				Dictionary<string, string> argsDict = [];
				for (int i = 0; i < argNames.Length && i < argValues.Length; i++)
				{
					argsDict[argNames[i]] = argValues[i];
				}
				return await this.internalClient.ExecuteOnImageAsync(id, kernelName, argsDict);
			}
			catch (Exception ex)
			{
				Console.WriteLine(ex);
				return new ImageObjInfo();
			}
		}

		public async Task<ImageObjInfo> ExecuteCreateImageAsync(int width, int height, string kernelName, string baseColorHex, string[] argNames, string[] argValues)
		{
			try
			{
				Dictionary<string, string> argsDict = [];
				for (int i = 0; i < argNames.Length && i < argValues.Length; i++)
				{
					argsDict[argNames[i]] = argValues[i];
				}

				return await this.internalClient.ExecuteCreateImageAsync(width, height, kernelName, argsDict, baseColorHex);
			}
			catch (Exception ex)
			{
				Console.WriteLine(ex);
				return new ImageObjInfo();
			}
		}


	}
}
