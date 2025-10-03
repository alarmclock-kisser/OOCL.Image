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
        private readonly OpenClService openClService;
        private readonly ImageCollection imageCollection;

        public OpenClController(OpenClService openClService, ImageCollection imageCollection)
        {
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
					return this.StatusCode(500, new ProblemDetails
					{
						Status = 500,
						Title = "Internal Server Error",
						Detail = "Failed to retrieve OpenCL service information."
					});
				}

				return this.Ok(info);
			}
			catch (Exception ex)
			{
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

				return this.Ok(devices);
			}
			catch (Exception ex)
			{
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
					return this.StatusCode(500, new ProblemDetails
					{
						Status = 500,
						Title = "Internal Server Error",
						Detail = "Failed to initialize OpenCL service."
					});
				}

				return this.Ok(info);
			}
			catch (Exception ex)
			{
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
					return this.StatusCode(500, new ProblemDetails
					{
						Status = 500,
						Title = "Internal Server Error",
						Detail = "Failed to initialize OpenCL service."
					});
				}

				return this.Ok(info);
			}
			catch (Exception ex)
			{
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
				return this.Ok(info);
			}
			catch (Exception ex)
			{
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
					return this.StatusCode(500, new ProblemDetails
					{
						Status = 500,
						Title = "Internal Server Error",
						Detail = "Failed to retrieve kernel information."
					});
				}

				if (onlyCompiled)
				{
					kernelInfos = kernelInfos.Where(i => i.CompiledSuccessfully);
				}

				return this.Ok(kernelInfos);
			}
			catch (Exception ex)
			{
				return this.StatusCode(500, new ProblemDetails
				{
					Status = 500,
					Title = "Internal Server Error",
					Detail = ex.Message
				});
			}
		}

		[HttpPost("execute-on-image")]
		[ProducesResponseType(typeof(ImageObjInfo), 200)]
		[ProducesResponseType(typeof(ProblemDetails), 400)]
		[ProducesResponseType(typeof(ProblemDetails), 404)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<ActionResult<ImageObjInfo>> ExecuteOnImageAsync([FromBody] ExecuteOnImageRequest request)
		{
			if (!this.openClService.Initialized)
			{
				return this.BadRequest(new ProblemDetails { Status = 400, Title = "OpenCL Not Initialized" });
			}

			if (request.ImageId == Guid.Empty)
			{
				return this.BadRequest(new ProblemDetails { Status = 400, Title = "ImageId required" });
			}

			var img = this.imageCollection[request.ImageId];
			if (img == null)
			{
				if (request.OptionalImage == null)
				{
					return this.NotFound(new ProblemDetails { Status = 404, Title = $"Image {request.ImageId} not found" });
				}

				// Optionales Nachladen aus Base64
				if (string.IsNullOrWhiteSpace(request.OptionalImage.Data?.Base64Data))
				{
					return this.BadRequest(new ProblemDetails { Status = 400, Title = "OptionalImage missing Base64Data" });
				}

				var created = await ImageCollection.CreateFromBase64(request.OptionalImage.Data.Base64Data, request.Rescale, request.OptionalImage.Info?.Name);
				if (created == null)
				{
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
				return this.StatusCode(500, new ProblemDetails { Status = 500, Title = "Kernel execution failed" });
			}

			if (!this.imageCollection.Add(result))
			{
				return this.StatusCode(500, new ProblemDetails { Status = 500, Title = "Add result to collection failed" });
			}

			return this.Ok(new ImageObjInfo(result));
		}

		[HttpPost("execute-create-image")]
		[ProducesResponseType(typeof(ImageObjInfo), 200)]
		[ProducesResponseType(typeof(ProblemDetails), 400)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<ActionResult<ImageObjInfo>> ExecuteCreateImageAsync([FromBody] CreateImageRequest request)
		{
			if (!this.openClService.Initialized)
			{
				return this.BadRequest(new ProblemDetails { Status = 400, Title = "OpenCL Not Initialized" });
			}

			if (request.Width <= 0 || request.Height <= 0)
			{
				return this.BadRequest(new ProblemDetails { Status = 400, Title = "Invalid dimensions" });
			}

			var args = request.Arguments ?? new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
			args["width"] = request.Width.ToString();
			args["height"] = request.Height.ToString();

			var result = await this.openClService.ExecuteCreateImage(request.Width, request.Height, request.KernelName, request.BaseColorHex, args);
			if (result == null)
			{
				return this.StatusCode(500, new ProblemDetails { Status = 500, Title = "Kernel execution failed" });
			}

			if (!this.imageCollection.Add(result))
			{
				return this.StatusCode(500, new ProblemDetails { Status = 500, Title = "Add result to collection failed" });
			}

			return this.Ok(new ImageObjInfo(result));
		}
	}
}