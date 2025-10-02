using Microsoft.AspNetCore.Mvc;
using OOCL.Image.Core;
using OOCL.Image.OpenCl;
using OOCL.Image.Shared;
using System.Runtime.CompilerServices;

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
		public async Task<ActionResult<OpenClServiceInfo>> InitializeByIdAsync([FromQuery] int deviceId = 2)
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

		[HttpPost("execute-on-image")]
		[ProducesResponseType(typeof(ImageObjInfo), 200)]
		[ProducesResponseType(typeof(ProblemDetails), 400)]
		[ProducesResponseType(typeof(ProblemDetails), 404)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<ActionResult<ImageObjInfo>> ExecuteGenericImageKernelAsync([FromQuery] Guid imageId, [FromQuery] string kernelName, [FromQuery] Dictionary<string, string> arguments)
		{
			try
			{
				if (!this.openClService.Initialized)
				{
					return this.BadRequest(new ProblemDetails
					{
						Status = 400,
						Title = "OpenCL Not Initialized",
						Detail = "Please initialize the OpenCL service before executing kernels."
					});
				}

				var imageObj = this.imageCollection[imageId];
				if (imageObj == null)
				{
					return this.NotFound(new ProblemDetails()
					{
						Title = "ImageObj not found for ID: " + imageId.ToString(),
						Detail = "Error: NotFound()",
						Status = 404
					});
				}

				// Refine arguments
				arguments["width"] = imageObj.Width.ToString();
				arguments["height"] = imageObj.Height.ToString();

				ImageObj? result = await this.openClService.ExecuteEditImage(imageObj, kernelName, arguments);
				if (result == null)
				{
					return this.StatusCode(500, new ProblemDetails
					{
						Status = 500,
						Title = "Kernel Execution Failed",
						Detail = "The kernel execution did not return a valid ImageObj."
					});
				}

				bool added = this.imageCollection.Add(result);
				if (!added)
				{
					return this.StatusCode(500, new ProblemDetails
					{
						Status = 500,
						Title = "Image Collection Error",
						Detail = "Failed to add the created ImageObj to the ImageCollection."
					});
				}

				var resultInfo = new ImageObjInfo(result);
				return this.Ok(resultInfo);
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

		[HttpPost("execute-create-image")]
		[ProducesResponseType(typeof(ImageObjInfo), 200)]
		[ProducesResponseType(typeof(ProblemDetails), 400)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<ActionResult<ImageObjInfo>> ExecuteCreateImageKernelAsync([FromQuery] int width = 480, [FromQuery] int height = 360, [FromQuery] string kernelName = "mandelbrot00", [FromQuery] Dictionary<string, string>? args = null, [FromQuery] string baseColorHex = "#00000000")
		{
			try
			{
				if (!this.openClService.Initialized)
				{
					return this.BadRequest(new ProblemDetails
					{
						Status = 400,
						Title = "OpenCL Not Initialized",
						Detail = "Please initialize the OpenCL service before executing kernels."
					});
				}

				if (width <= 0 || height <= 0)
				{
					return this.BadRequest(new ProblemDetails
					{
						Status = 400,
						Title = "Invalid Image Dimensions",
						Detail = "Width and Height must be positive integers."
					});
				}

				// Übergib das Dictionary direkt an den OpenCl-Layer
				ImageObj? result = await this.openClService.ExecuteCreateImage(width, height, kernelName, baseColorHex, args);
				if (result == null)
				{
					return this.StatusCode(500, new ProblemDetails
					{
						Status = 500,
						Title = "Kernel Execution Failed",
						Detail = "The kernel execution did not return a valid ImageObj."
					});
				}

				bool added = this.imageCollection.Add(result);
				if (!added)
				{
					return this.StatusCode(500, new ProblemDetails
					{
						Status = 500,
						Title = "Image Collection Error",
						Detail = "Failed to add the created ImageObj to the ImageCollection."
					});
				}

				var resultInfo = new ImageObjInfo(result);
				if (resultInfo == null)
				{
					return this.StatusCode(500, new ProblemDetails
					{
						Status = 500,
						Title = "Internal Server Error",
						Detail = "Failed to create ImageObjInfo from the resulting ImageObj."
					});
				}

				return this.Ok(resultInfo);
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

		[HttpPost("execute-mandelbrot-test")]
		[ProducesResponseType(typeof(ImageObjInfo), 200)]
		[ProducesResponseType(typeof(ProblemDetails), 400)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<ActionResult<ImageObjInfo?>> ExecuteMandelbrotTestAsync([FromQuery] int width = 720, [FromQuery] int height = 480, [FromQuery] string kernelName = "mandelbrot00", [FromQuery] string baseColorHex = "#00000000")
		{
			if (string.IsNullOrEmpty(this.openClService.KernelExists(kernelName)))
			{
				return this.BadRequest(new ProblemDetails
				{
					Status = 400,
					Title = "Kernel Not Found",
					Detail = $"The specified kernel '{kernelName}' does not exist."
				});
			}

			try
			{
				if (!this.openClService.Initialized)
				{
					return this.BadRequest(new ProblemDetails
					{
						Status = 400,
						Title = "OpenCL Not Initialized",
						Detail = "Please initialize the OpenCL service before executing kernels."
					});
				}

				if (width <= 0 || height <= 0)
				{
					return this.BadRequest(new ProblemDetails
					{
						Status = 400,
						Title = "Invalid Image Dimensions",
						Detail = "Width and Height must be positive integers."
					});
				}

				// Dict for color args
				int[] rgb = ImageCollection.GetRgbFromHexColor(baseColorHex);
				Dictionary<string, string> args = new()
				{
					{ "baseR", rgb[0].ToString() },
					{ "baseG", rgb[1].ToString() },
					{ "baseB", rgb[2].ToString() }
				};

				// LOG CONSOLE
				Console.WriteLine("EXEC-Mandelbrot-Test: " + kernelName + " | Size: " + width + "x" + height + " | BaseColor: " + baseColorHex + " (R=" + rgb[0] + ",G=" + rgb[1] + ",B=" + rgb[2] + ")");

				ImageObj? result = await this.openClService.ExecuteCreateImage(width, height, kernelName, baseColorHex, args);
				if (result == null)
				{
					return this.StatusCode(500, new ProblemDetails
					{
						Status = 500,
						Title = "Kernel Execution Failed",
						Detail = "The kernel execution did not return a valid ImageObj."
					});
				}

				bool added = this.imageCollection.Add(result);
				if (!added)
				{
					return this.StatusCode(500, new ProblemDetails
					{
						Status = 500,
						Title = "Image Collection Error",
						Detail = "Failed to add the created ImageObj to the ImageCollection."
					});
				}

				var resultInfo = new ImageObjInfo(result);
				if (resultInfo == null)
				{
					return this.StatusCode(500, new ProblemDetails
					{
						Status = 500,
						Title = "Internal Server Error",
						Detail = "Failed to create ImageObjInfo from the resulting ImageObj."
					});
				}

				return this.Ok(resultInfo);
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





		[HttpPost("execute-edgeDetection00-default")]
		[ProducesResponseType(typeof(ImageObjInfo), 200)]
		[ProducesResponseType(typeof(ProblemDetails), 400)]
		[ProducesResponseType(typeof(ProblemDetails), 404)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<ActionResult<ImageObjInfo>> ExecuteEdgeDetectionDefaultAsync([FromQuery] Guid? imageId = null)
		{
			try
			{
				if (!this.openClService.Initialized)
				{
					return this.BadRequest(new ProblemDetails
					{
						Status = 400,
						Title = "OpenCL Not Initialized",
						Detail = "Please initialize the OpenCL service before executing kernels."
					});
				}

				if (imageId == null || imageId == Guid.Empty)
				{
					if (this.imageCollection.Images.Count <= 0)
					{
						// Try loading resources
						var loaded = await this.imageCollection.LoadResourcesAsync();
						if (loaded?.Count() == 0)
						{
							return this.BadRequest(new ProblemDetails
							{
								Status = 400,
								Title = "No Images Available",
								Detail = "No images are available in the collection. Please upload or load images before executing the edge detection."
							});
						}
					}

					// Use the last image in the collection if no ID is provided
					imageId = this.imageCollection.Images.Last().Id;
				}

				var imageObj = this.imageCollection[imageId.Value];
				if (imageObj == null)
				{
					return this.NotFound(new ProblemDetails()
					{
						Title = "ImageObj not found for ID: " + imageId.ToString(),
						Detail = "Error: NotFound()",
						Status = 404
					});
				}

				Dictionary<string, string> arguments = new()
				{
					{ "inputPixels", "0" },
					{ "outputPixels", "0" },
					{"width", imageObj.Width.ToString() },
					{"height", imageObj.Height.ToString() },
					{ "threshold", "0.25" },
					{ "thickness", "1" },
					{ "edgeR", "255" },
					{ "edgeG", "0" },
					{ "edgeB", "0" }
				};

				var resultObj = await this.openClService.ExecuteEditImage(imageObj, "edgeDetection00", arguments);
				if (resultObj == null)
				{
					return this.StatusCode(500, new ProblemDetails
					{
						Status = 500,
						Title = "Kernel Execution Failed",
						Detail = "The kernel execution did not return a valid ImageObj."
					});
				}

				if (!this.imageCollection.Add(resultObj))
				{
					return this.StatusCode(500, new ProblemDetails
					{
						Status = 500,
						Title = "Image Collection Error",
						Detail = "Failed to add the created ImageObj to the ImageCollection."
					});
				}

				ImageObjInfo resultInfo = new(this.imageCollection[resultObj.Id]);
				if (resultInfo.Id == Guid.Empty)
				{
					return this.StatusCode(500, new ProblemDetails
					{
						Status = 500,
						Title = "Internal Server Error",
						Detail = "Failed to create ImageObjInfo from the resulting ImageObj. (Adding to collection maybe failed.)"
					});
				}

				return this.Ok(resultInfo);
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




	}
}