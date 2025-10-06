using Microsoft.AspNetCore.Mvc;
using OOCL.Image.Core;
using OOCL.Image.OpenCl;
using OOCL.Image.Shared;

namespace OOCL.Image.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class OpenClController : ControllerBase
    {
		private readonly RollingFileLogger logger;
		private readonly OpenClService openClService;
        private readonly ImageCollection imageCollection;

        public OpenClController(RollingFileLogger logger, OpenClService openClService, ImageCollection imageCollection)
        {
			this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
			this.openClService = openClService;
            this.imageCollection = imageCollection;
        }

		[HttpGet("status")]
		[ProducesResponseType(typeof(OpenClServiceInfo), 200)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<ActionResult<OpenClServiceInfo>> GetStatusAsync()
		{
			try
			{
				var info = await Task.Run(() => new OpenClServiceInfo(this.openClService));
				if (info == null)
				{
					await this.logger.LogAsync("OpenClServiceInfo is null", nameof(OpenClController));
					return this.StatusCode(500, new ProblemDetails
					{
						Status = 500,
						Title = "Internal Server Error",
						Detail = "Failed to retrieve OpenCL service information."
					});
				}

				await this.logger.LogAsync("[200] api/opencl/status: " + (info.Initialized ? "Initialized!" : "OpenCL is currently NOT initialized."), nameof(OpenClController));
				return this.Ok(info);
			}
			catch (Exception ex)
			{
				await this.logger.LogExceptionAsync(ex, nameof(OpenClController));
				return this.StatusCode(500, new ProblemDetails
				{
					Status = 500,
					Title = "Internal Server Error",
					Detail = ex.Message
				});
			}
		}

		[HttpGet("devices")]
		[ProducesResponseType(typeof(IEnumerable<OpenClDeviceInfo>), 200)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<ActionResult<IEnumerable<OpenClDeviceInfo>>> GetDevicesAsync()
		{
			try
			{
				List<OpenClDeviceInfo> devices = [];
				for (int i = 0; i < this.openClService.DeviceCount; i++)
				{
					devices.Add(await Task.Run(() => new OpenClDeviceInfo(this.openClService, i)));
				}

				await this.logger.LogAsync($"[200] api/opencl/devices: Found {devices.Count} devices", nameof(OpenClController));
				return this.Ok(devices);
			}
			catch (Exception ex)
			{
				await this.logger.LogExceptionAsync(ex, nameof(OpenClController));
				return this.StatusCode(500, new ProblemDetails
				{
					Status = 500,
					Title = "Internal Server Error",
					Detail = ex.Message
				});
			}
		}

		[HttpPost("initialize-id")]
		[ProducesResponseType(typeof(OpenClServiceInfo), 200)]
		[ProducesResponseType(typeof(ProblemDetails), 400)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<ActionResult<OpenClServiceInfo>> InitializeByIdAsync([FromBody] int deviceId = 2)
		{
			try
			{
				if (deviceId < 0 || deviceId >= this.openClService.DeviceCount)
				{
					await this.logger.LogAsync($"[400] api/opencl/initialize-id: Invalid Device ID {deviceId}", nameof(OpenClController));
					return this.BadRequest(new ProblemDetails
					{
						Status = 400,
						Title = "Invalid Device ID",
						Detail = $"Device ID must be between 0 and {this.openClService.DeviceCount - 1}."
					});
				}

				await Task.Run(() => this.openClService.Initialize(deviceId));
				var info = new OpenClServiceInfo(this.openClService);
				if (!info.Initialized)
				{
					await this.logger.LogAsync($"[500] api/opencl/initialize-id: Failed to initialize OpenCL with Device ID {deviceId}", nameof(OpenClController));
					return this.StatusCode(500, new ProblemDetails
					{
						Status = 500,
						Title = "Internal Server Error",
						Detail = "Failed to initialize OpenCL service."
					});
				}

				await this.logger.LogAsync($"[200] api/opencl/initialize-id: Successfully initialized OpenCL with Device ID {deviceId}", nameof(OpenClController));
				return this.Ok(info);
			}
			catch (Exception ex)
			{
				await this.logger.LogExceptionAsync(ex, nameof(OpenClController));
				return this.StatusCode(500, new ProblemDetails
				{
					Status = 500,
					Title = "Internal Server Error",
					Detail = ex.Message
				});
			}
		}

		[HttpPost("initialize-name")]
		[ProducesResponseType(typeof(OpenClServiceInfo), 200)]
		[ProducesResponseType(typeof(ProblemDetails), 400)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<ActionResult<OpenClServiceInfo>> InitializeByNameAsync([FromQuery] string deviceName = "Core")
		{
			try
			{
				if (string.IsNullOrWhiteSpace(deviceName))
				{
					await this.logger.LogAsync("[400] api/opencl/initialize-name: Invalid Device Name (null or empty)", nameof(OpenClController));
					return this.BadRequest(new ProblemDetails
					{
						Status = 400,
						Title = "Invalid Device Name",
						Detail = "Device name cannot be null or empty."
					});
				}

				await Task.Run(() => this.openClService.Initialize(deviceName));
				var info = new OpenClServiceInfo(this.openClService);
				if (!info.Initialized)
				{
					await this.logger.LogAsync($"[500] api/opencl/initialize-name: Failed to initialize OpenCL with Device Name '{deviceName}'", nameof(OpenClController));
					return this.StatusCode(500, new ProblemDetails
					{
						Status = 500,
						Title = "Internal Server Error",
						Detail = "Failed to initialize OpenCL service."
					});
				}

				await this.logger.LogAsync($"[200] api/opencl/initialize-name: Successfully initialized OpenCL with Device Name '{deviceName}'", nameof(OpenClController));
				return this.Ok(info);
			}
			catch (Exception ex)
			{
				await this.logger.LogExceptionAsync(ex, nameof(OpenClController));
				return this.StatusCode(500, new ProblemDetails
				{
					Status = 500,
					Title = "Internal Server Error",
					Detail = ex.Message
				});
			}
		}

		[HttpDelete("release")]
		[ProducesResponseType(typeof(OpenClServiceInfo), 200)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<ActionResult<OpenClServiceInfo>> ReleaseAsync()
		{
			try
			{
				await Task.Run(this.openClService.Dispose);
				var info = new OpenClServiceInfo(this.openClService);
				await this.logger.LogAsync("[200] api/opencl/release: Successfully released OpenCL resources", nameof(OpenClController));
				return this.Ok(info);
			}
			catch (Exception ex)
			{
				await this.logger.LogExceptionAsync(ex, nameof(OpenClController));
				return this.StatusCode(500, new ProblemDetails
				{
					Status = 500,
					Title = "Internal Server Error",
					Detail = ex.Message
				});
			}
		}

		[HttpGet("kernel-infos")]
		[ProducesResponseType(typeof(IEnumerable<OpenClKernelInfo>), 200)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<ActionResult<IEnumerable<OpenClKernelInfo>>> GetKernelInfosAsync([FromQuery] bool onlyCompiled = true)
		{
			try
			{
				if (!this.openClService.Initialized)
				{
					await this.logger.LogAsync("[400] api/opencl/kernel-infos: OpenCL Not Initialized", nameof(OpenClController));
					return this.BadRequest(new ProblemDetails
					{
						Status = 400,
						Title = "OpenCL Not Initialized",
						Detail = "Please initialize the OpenCL service before retrieving kernel infos."
					});
				}
				var kernelInfos = await Task.Run(() => this.openClService.Compiler?.KernelFiles.Select(kf => new OpenClKernelInfo(this.openClService.Compiler, this.openClService.Compiler.KernelFiles.ToList().IndexOf(kf))));
				if (kernelInfos == null)
				{
					await this.logger.LogAsync("[500] api/opencl/kernel-infos: Failed to retrieve kernel information", nameof(OpenClController));
					return this.StatusCode(500, new ProblemDetails
					{
						Status = 500,
						Title = "Internal Server Error",
						Detail = "Failed to retrieve kernel information."
					});
				}

				if (onlyCompiled)
				{
					kernelInfos = kernelInfos.Where(ki => (Boolean) (ki.CompiledSuccessfully ?? false));
				}

				await this.logger.LogAsync($"[200] api/opencl/kernel-infos: Retrieved {kernelInfos.Count()} kernel infos (onlyCompiled={onlyCompiled})", nameof(OpenClController));
				return this.Ok(kernelInfos);
			}
			catch (Exception ex)
			{
				await this.logger.LogExceptionAsync(ex, nameof(OpenClController));
				return this.StatusCode(500, new ProblemDetails
				{
					Status = 500,
					Title = "Internal Server Error",
					Detail = ex.Message
				});
			}
		}

		[HttpPost("execute-on-image")]
		[ProducesResponseType(typeof(ImageObjDto), 200)]
		[ProducesResponseType(typeof(ProblemDetails), 400)]
		[ProducesResponseType(typeof(ProblemDetails), 404)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<ActionResult<ImageObjDto>> ExecuteOnImageAsync([FromBody] ExecuteOnImageRequest request)
		{
			if (!this.openClService.Initialized)
			{
				await this.logger.LogAsync("[400] api/opencl/execute-on-image: OpenCL Not Initialized", nameof(OpenClController));
				return this.BadRequest(new ProblemDetails { Status = 400, Title = "OpenCL Not Initialized" });
			}

			if (request.ImageId == Guid.Empty && request.OptionalImage == null)
			{
				await this.logger.LogAsync("[400] api/opencl/execute-on-image: Either ImageId or OptionalImage required", nameof(OpenClController));
				return this.BadRequest(new ProblemDetails { Status = 400, Title = "Either ImageId or OptionalImage required" });
			}

			var img = this.imageCollection[request.ImageId];
			if (img == null)
			{
				if (request.OptionalImage == null)
				{
					await this.logger.LogAsync($"[404] api/opencl/execute-on-image: Image {request.ImageId} not found", nameof(OpenClController));
					return this.NotFound(new ProblemDetails { Status = 404, Title = $"Image {request.ImageId} not found" });
				}

				// Optionales Nachladen aus Base64
				if (string.IsNullOrWhiteSpace(request.OptionalImage.Data?.Base64Data))
				{
					await this.logger.LogAsync("[400] api/opencl/execute-on-image: OptionalImage missing Base64Data", nameof(OpenClController));
					return this.BadRequest(new ProblemDetails { Status = 400, Title = "OptionalImage missing Base64Data" });
				}

				var created = await ImageCollection.CreateFromBase64(request.OptionalImage.Data.Base64Data, request.Rescale, request.OptionalImage.Info?.Name);
				if (created == null)
				{
					await this.logger.LogAsync("[500] api/opencl/execute-on-image: Failed to create temp image from OptionalImage", nameof(OpenClController));
					return this.StatusCode(500, new ProblemDetails { Status = 500, Title = "Failed to create temp image" });
				}

				img = created;
			}

			var args = request.Arguments ?? new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
			args["width"] = img.Width.ToString();
			args["height"] = img.Height.ToString();

			var result = await this.openClService.ExecuteEditImage(img, request.KernelName, args);
			if (result == null)
			{
				await this.logger.LogAsync("[500] api/opencl/execute-on-image: OpenCL returned null image", nameof(OpenClController));
				return this.StatusCode(500, new ProblemDetails { Status = 500, Title = "Kernel execution failed" });
			}

			if (this.imageCollection.ServerSidedData)
			{
				if (!this.imageCollection.Add(result))
				{
					await this.logger.LogAsync("[500] api/opencl/execute-on-image: Failed to add result to collection, still returning result DTO", nameof(OpenClController));
					return this.StatusCode(500, new ProblemDetails { Status = 500, Title = "Add result to collection failed" });
				}
			}

			await this.logger.LogAsync($"[200] api/opencl/execute-on-image: Successfully executed kernel '{request.KernelName}' on image {img.Id}, new image {result.Id}", nameof(OpenClController));
			return this.Ok(new ImageObjDto(result));
		}

		[HttpPost("execute-create-image")]
		[ProducesResponseType(typeof(ImageObjDto), 200)]
		[ProducesResponseType(typeof(ProblemDetails), 400)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<ActionResult<ImageObjDto>> ExecuteCreateImageAsync([FromBody] CreateImageRequest request)
		{
			if (!this.openClService.Initialized)
			{
				await this.logger.LogAsync("[400] api/opencl/execute-create-image: OpenCL Not Initialized", nameof(OpenClController));
				return this.BadRequest(new ProblemDetails { Status = 400, Title = "OpenCL Not Initialized" });
			}

			if (request.Width <= 0 || request.Height <= 0)
			{
				await this.logger.LogAsync($"[400] api/opencl/execute-create-image: Invalid dimensions {request.Width}x{request.Height}", nameof(OpenClController));
				return this.BadRequest(new ProblemDetails { Status = 400, Title = "Invalid dimensions" });
			}

			var args = request.Arguments ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			args["width"] = request.Width.ToString();
			args["height"] = request.Height.ToString();

			var result = await this.openClService.ExecuteCreateImage(request.Width, request.Height, request.KernelName, request.BaseColorHex, args);
			if (result == null)
			{
				await this.logger.LogAsync("[500] api/opencl/execute-create-image: OpenCL returned null image", nameof(OpenClController));
				return this.StatusCode(500, new ProblemDetails { Status = 500, Title = "Kernel execution failed" });
			}

			if (this.imageCollection.ServerSidedData)
			{
				if (!this.imageCollection.Add(result))
				{
					await this.logger.LogAsync("[500] api/opencl/execute-create-image: Failed to add result to collection, still returning result DTO", nameof(OpenClController));
					return this.Ok(new ImageObjDto(result));
				}
			}

			return this.Ok(new ImageObjDto(result));
		}
	}
}