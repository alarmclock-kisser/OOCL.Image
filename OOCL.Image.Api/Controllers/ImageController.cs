using OOCL.Image.Core;
using OOCL.Image.Shared;
using Microsoft.AspNetCore.Mvc;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Threading.Tasks;

namespace OOCL.Image.Api.Controllers
{
	[ApiController]
	[Route("api/[controller]")]
	public class ImageController : ControllerBase
	{
		private readonly RollingFileLogger logger;
		private readonly ImageCollection imageCollection;

		public ImageController(RollingFileLogger logger, ImageCollection imageCollection)
		{
			this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
			this.imageCollection = imageCollection;
		}

		[HttpGet("server-sided-data")]
		[ProducesResponseType(typeof(bool), 200)]
		public async Task<ActionResult<bool>> IsServerSidedData()
		{
			await this.logger.LogAsync("[200] api/image/server-sided-data" + (this.imageCollection.ServerSidedData ? " (enabled)" : " (disabled)", nameof(ImageController)));
			return this.Ok(this.imageCollection.ServerSidedData);
		}

		[HttpGet("pop-new-image")]
		[ProducesResponseType(typeof(ImageObjDto), 200)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<ActionResult<ImageObjDto>> PopNewImageAsync([FromQuery] int width = 512, [FromQuery] int height = 512, [FromQuery] string hexColor = "#00000000", [FromQuery] bool tryAdd = false, [FromQuery] string? optionalSerializationFormat = null)
		{
			width = Math.Clamp(width, 1, 32768);
			height = Math.Clamp(height, 1, 32768);
			optionalSerializationFormat ??= "png";
			optionalSerializationFormat = optionalSerializationFormat.ToLowerInvariant().Trim('.');
			if (!ImageCollection.SupportedFormats.Contains(optionalSerializationFormat))
			{
				await this.logger.LogAsync($"[000] api/image/pop-new-image: Unsupported serialization format '{optionalSerializationFormat}' requested. Defaulting to 'png'.", nameof(ImageController));
				optionalSerializationFormat = "png";
			}

			try
			{
				ImageObj obj = await Task.Run(() => new ImageObj(width, height, hexColor));
				if (this.imageCollection.ServerSidedData && tryAdd)
				{
					bool added = await Task.Run(() => this.imageCollection.Add(obj));
					if (!added)
					{
						obj.Dispose();
						await this.logger.LogAsync("[500] api/image/pop-new-image: Failed to add new image to server-side collection.", nameof(ImageController));
						return this.StatusCode(500, new ProblemDetails
						{
							Title = "Error creating new image",
							Detail = "Failed to add the new image to the collection.",
							Status = 500
						});
					}
				}

				var infoDto = new ImageObjInfo(obj);
				var dataDto = new ImageObjData(obj, optionalSerializationFormat);
				ImageObjDto dto = new(infoDto, dataDto);

				return this.Ok(dto);
			}
			catch (Exception ex)
			{
				return this.StatusCode(500, new ProblemDetails
				{
					Title = "Error creating new image",
					Detail = ex.Message,
					Status = 500
				});
			}
		}

		[HttpPost("serialize-image")]
		[ProducesResponseType(typeof(ImageObjDto), 200)]
		[ProducesResponseType(typeof(ProblemDetails), 400)]
		[ProducesResponseType(typeof(ProblemDetails), 404)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<ActionResult<ImageObjDto>> SerializeImageAsync([FromQuery] Guid id, [FromQuery] string format = "png", [FromQuery] float scale = 1.0f)
		{
			try
			{
				if (!this.imageCollection.ServerSidedData)
				{
					await this.logger.LogAsync("[400] api/image/serialize-image: Attempt to serialize image by ID when server-sided mode is disabled.", nameof(ImageController));
					return this.BadRequest(new ProblemDetails
					{
						Title = "Server sided mode disabled",
						Detail = "Serialization by ID is only available when server-sided data storage is enabled.",
						Status = 400
					});
				}

				if (id == Guid.Empty)
				{
					await this.logger.LogAsync("[400] api/image/serialize-image: Invalid or empty image ID supplied.", nameof(ImageController));
					return this.BadRequest(new ProblemDetails
					{
						Title = "Invalid id",
						Detail = "You must supply a valid image Guid.",
						Status = 400
					});
				}

				var obj = await Task.Run(() => this.imageCollection[id]);
				if (obj == null)
				{
					await this.logger.LogAsync($"[404] api/image/serialize-image: No image found with ID {id}.", nameof(ImageController));
					return this.NotFound(new ProblemDetails
					{
						Title = "Image not found",
						Detail = $"No image with ID {id} exists.",
						Status = 404
					});
				}

				if (obj.Img == null)
				{
					await this.logger.LogAsync($"[500] api/image/serialize-image: Image data is null for image with ID {id}.", nameof(ImageController));
					return this.StatusCode(500, new ProblemDetails
					{
						Title = "Image data missing",
						Detail = "The image exists but underlying pixel data is null.",
						Status = 500
					});
				}

				format = (format ?? "png").Trim('.').ToLowerInvariant();
				if (!ImageCollection.SupportedFormats.Contains(format))
				{
					await this.logger.LogAsync($"[000] api/image/serialize-image: Unsupported serialization format '{format}' requested. Defaulting to 'png'.", nameof(ImageController));
					format = "png";
				}

				scale = float.IsFinite(scale) ? scale : 1.0f;
				if (scale <= 0f)
				{
					await this.logger.LogAsync($"[000] api/image/serialize-image: Invalid scale factor '{scale}' requested. Defaulting to 1.0f.", nameof(ImageController));
					scale = 1.0f;
				}

				// Optional skalieren + serialisieren (nutzt vorhandenen ImageObjData-Konstruktor; falls du später deine optimierte SerializeBase64Async verwendest, hier austauschen)
				ImageObjData dataDto = new();
				if (Math.Abs(scale - 1.0f) > 0.0001f)
				{
					int newWidth = Math.Clamp((int)(obj.Width * scale), 1, 32768);
					int newHeight = Math.Clamp((int)(obj.Height * scale), 1, 32768);
					dataDto = new(await obj.ResizeAsync(newWidth, newHeight, true, true), format);
				}
				else
				{
					dataDto = new ImageObjData(obj, format);
				}

				if (string.IsNullOrEmpty(dataDto.Base64Data))
				{
					await this.logger.LogAsync($"[500] api/image/serialize-image: Serialization to Base64 failed for image with ID {id}.", nameof(ImageController));
					return this.StatusCode(500, new ProblemDetails
					{
						Title = "Serialization failed",
						Detail = "Base64 string empty after encoding.",
						Status = 500
					});
				}

				var infoDto = new ImageObjInfo(obj);
				var dto = new ImageObjDto(infoDto, dataDto);
				return this.Ok(dto);
			}
			catch (Exception ex)
			{
				await this.logger.LogAsync($"[500] api/image/serialize-image: Exception occurred while serializing image with ID {id}: {ex.Message}", nameof(ImageController));
				return this.StatusCode(500, new ProblemDetails
				{
					Title = $"Error serializing image {id}",
					Detail = ex.Message,
					Status = 500
				});
			}
		}

		[HttpGet("create-image-from-data")]
		[ProducesResponseType(typeof(ImageObjDto), 200)]
		[ProducesResponseType(typeof(ProblemDetails), 400)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<ActionResult<ImageObjDto>> CreateImageFromFileAsync([FromQuery] IEnumerable<int> bytesAsInts, [FromQuery] string file, [FromQuery] string contentType = "image/png", [FromQuery] bool tryAdd = false)
		{
			if (bytesAsInts.Count() <= 0)
			{
				await this.logger.LogAsync("[400] api/image/create-image-from-data: No image data provided in request.", nameof(ImageController));
				return this.BadRequest(new ProblemDetails
				{
					Title = "No data provided",
					Detail = "You must provide image data as int array.",
					Status = 400
				});
			}
			if (string.IsNullOrEmpty(file))
			{
				await this.logger.LogAsync("[000] api/image/create-image-from-data: No filename provided in request. Defaulting to 'upload.png'.", nameof(ImageController));
				file = "upload.png";
			}
			if (string.IsNullOrEmpty(contentType))
			{
				await this.logger.LogAsync("[000] api/image/create-image-from-data: No content type provided in request. Defaulting to 'image/png'.", nameof(ImageController));
				contentType = "image/png";
			}

			try
			{
				var dto = await ImageObjDto.FromBytesAsync(bytesAsInts.Select(b => (byte)b).ToArray(), file, contentType);
				if (dto.Id == Guid.Empty || dto.Info.Id == Guid.Empty || string.IsNullOrEmpty(dto.Data.Base64Data))
				{
					await this.logger.LogAsync("[500] api/image/create-image-from-data: Created ImageObjDto is invalid (empty ID or Base64 data).", nameof(ImageController));
					return this.StatusCode(500, new ProblemDetails
					{
						Title = "Error creating image from data",
						Detail = "The resulting image object is invalid (empty ID or Base64 data).",
						Status = 500
					});
				}
				byte[] bytes = await Task.Run(() => bytesAsInts.Select(b => (byte)b).ToArray());
				var obj = await Task.Run(() => ImageCollection.CreateFromData(bytes, file));
				if (obj == null || obj.Id == Guid.Empty)
				{
					await this.logger.LogAsync("[500] api/image/create-image-from-data: Failed to create valid ImageObj from provided data.", nameof(ImageController));
					return this.StatusCode(500, new ProblemDetails
					{
						Title = "Error creating image from data",
						Detail = "Failed to create a valid ImageObj from the provided data.",
						Status = 500
					});
				}

				if (this.imageCollection.ServerSidedData && tryAdd)
				{
					bool added= await Task.Run(() => this.imageCollection.Add(obj));
					if (!added)
					{
						obj.Dispose();
						await this.logger.LogAsync("[500] api/image/create-image-from-data: Failed to add new image to server-side collection.", nameof(ImageController));
						return this.StatusCode(500, new ProblemDetails
						{
							Title = "Error adding image to collection",
							Detail = "Failed to add the new image to the server-side collection.",
							Status = 500
						});
					}
				}

				return this.Ok(dto);
			}
			catch (Exception ex)
			{
				await this.logger.LogAsync($"[500] api/image/create-image-from-data: Exception occurred while creating image from data: {ex.Message}", nameof(ImageController));
				return this.StatusCode(500, new ProblemDetails
				{
					Title = "Error creating image from data",
					Detail = ex.Message,
					Status = 500
				});
			}
		}


		[HttpGet("list")]
		[ProducesResponseType(typeof(IEnumerable<ImageObjInfo>), 200)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<ActionResult<IEnumerable<ImageInfo>>> ImageListAsync()
		{
			try
			{
				var infos = await Task.Run(() => this.imageCollection.Images.Select(img => new ImageObjInfo(img)));

				await this.logger.LogAsync($"[200] api/image/list: Returning {infos.Count()} images in the list.", nameof(ImageController));
				return this.Ok(infos);
			}
			catch (Exception ex)
			{
				await this.logger.LogAsync($"[500] api/image/list: Exception occurred while listing images: {ex.Message}", nameof(ImageController));
				return this.StatusCode(500, new ProblemDetails
				{
					Status = 500,
					Title = "Internal Server Error",
					Detail = ex.Message
				});
			}
		}

		[HttpPost("load")]
		[Consumes("multipart/form-data")]
		[ProducesResponseType(typeof(ImageObjInfo), 200)]
		[ProducesResponseType(typeof(ProblemDetails), 400)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<ActionResult<ImageObjInfo>> ImageLoadAsync(IFormFile file)
		{
			try
			{
				if (file == null || file.Length == 0)
				{
					await this.logger.LogAsync("[400] api/image/load: No file uploaded or file is empty.", nameof(ImageController));
					return this.BadRequest(new ProblemDetails
					{
						Title = "Invalid file",
						Detail = "No file was uploaded or the file is empty.",
						Status = 400
					});
				}

				var originalFileName = Path.GetFileName(file.FileName);
				var invalidChars = Path.GetInvalidFileNameChars();
				var safeFileName = string.Join("_", originalFileName.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));
				if (string.IsNullOrWhiteSpace(safeFileName))
				{
					await this.logger.LogAsync("[000] api/image/load: Uploaded file has no valid name. Defaulting to 'upload'.", nameof(ImageController));
					safeFileName = "upload";
				}

				var extension = Path.GetExtension(safeFileName);
				if (string.IsNullOrEmpty(extension))
				{
					var origExt = Path.GetExtension(originalFileName);
					if (!string.IsNullOrEmpty(origExt))
					{
						safeFileName += origExt;
					}
				}

				var tempDir = Path.GetTempPath();
				var destPath = Path.Combine(tempDir, safeFileName);

				if (System.IO.File.Exists(destPath))
				{
					var baseName = Path.GetFileNameWithoutExtension(safeFileName);
					var ext = Path.GetExtension(safeFileName);
					destPath = Path.Combine(tempDir, $"{baseName}_{Guid.NewGuid():N}{ext}");
				}

				using (var stream = System.IO.File.Create(destPath))
				{
					await file.CopyToAsync(stream);
				}

				var info = await Task.Run(async () =>
				{
					var imgObj = await this.imageCollection.LoadImage(destPath);
					return new ImageObjInfo(imgObj);
				});

				// Optionally delete the temp file after loading
				try
				{
					if (System.IO.File.Exists(destPath))
					{
						System.IO.File.Delete(destPath);
					}
				}
				catch (Exception ex)
				{
					await this.logger.LogAsync($"[000] api/image/load: Temporary file '{destPath}' could not be deleted: {ex.Message}", nameof(ImageController));
				}

				await this.logger.LogAsync($"[200] api/image/load: Successfully loaded image '{safeFileName}' with ID {info.Id} ({info.Size.Width}x{info.Size.Height}).", nameof(ImageController));
				return this.Ok(info);
			}
			catch (Exception ex)
			{
				await this.logger.LogAsync($"[500] api/image/load: Exception occurred while loading image from file '{file?.FileName}': {ex.Message}", nameof(ImageController));
				return this.StatusCode(500, new ProblemDetails
				{
					Title = $"Error loading image from file '{(file?.FileName ?? "N/A")}'",
					Detail = ex.Message,
					Status = 500
				});
			}
		}

		[HttpPost("create")]
		[ProducesResponseType(typeof(ImageObjInfo), 200)]
		[ProducesResponseType(typeof(ProblemDetails), 400)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<ActionResult<ImageObjInfo>> ImageCreateAsync([FromQuery] int width = 1920, [FromQuery] int height = 1080, [FromQuery] string hexColor = "#00000000")
		{
			try
			{
				if (width <= 0 || height <= 0 || width > 32768 || height > 32768)
				{
					await this.logger.LogAsync($"[400] api/image/create: Invalid dimensions requested: {width}x{height}. Must be between 1 and 32768.", nameof(ImageController));
					return this.BadRequest(new ProblemDetails
					{
						Title = "Invalid dimensions",
						Detail = "Width and Height must be positive integers between 1 - 32768",
						Status = 400
					});
				}

				var sharpColor = ImageCollection.GetSharpColor(hexColor);
				var sharpSize = ImageCollection.GetSharpSize(height, width);

				var info = await Task.Run(async () =>
				{
					var imgObj = await this.imageCollection.PopEmpty(sharpSize, true);
					return new ImageObjInfo(imgObj);
				});

				await this.logger.LogAsync($"[200] api/image/create: Created new image with ID {info.Id} and size {width}x{height} (color={hexColor}).", nameof(ImageController));
				return this.Ok(info);
			}
			catch (Exception ex)
			{
				await this.logger.LogAsync($"[500] api/image/create: Exception occurred while creating new image with size {width}x{height} (color={hexColor}): {ex.Message}", nameof(ImageController));
				return this.StatusCode(500, new ProblemDetails
				{
					Title = $"Error creating new image with size {width}x{height} (color={hexColor}",
					Detail = ex.Message,
					Status = 500
				});
			}
		}

		[HttpDelete("remove")]
		[ProducesResponseType(200)]
		[ProducesResponseType(typeof(ProblemDetails), 404)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<IActionResult> ImageRemoveAsync([FromQuery] Guid id)
		{
			try
			{
				var success = await Task.Run(() => this.imageCollection.Remove(id));
				if (!success)
				{
					await this.logger.LogAsync($"[404] api/image/remove: No image found with ID {id} to remove.", nameof(ImageController));
					return this.NotFound(new ProblemDetails
					{
						Title = "Image not found",
						Detail = $"No image found with ID {id}.",
						Status = 404
					});
				}

				await this.logger.LogAsync($"[200] api/image/remove: Successfully removed image with ID {id}.", nameof(ImageController));
				return this.Ok();
			}
			catch (Exception ex)
			{
				await this.logger.LogAsync($"[500] api/image/remove: Exception occurred while removing image with ID {id}: {ex.Message}", nameof(ImageController));
				return this.StatusCode(500, new ProblemDetails
				{
					Title = $"Error deleting image with ID {id}",
					Detail = ex.Message,
					Status = 500
				});
			}
		}

		[HttpDelete("clearAll")]
		[ProducesResponseType(200)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<IActionResult> ImageClearAllAsync()
		{
			try
			{
				await Task.Run(this.imageCollection.Clear);

				await this.logger.LogAsync("[200] api/image/clearAll: Successfully cleared all images from the collection.", nameof(ImageController));
				return this.Ok();
			}
			catch (Exception ex)
			{
				await this.logger.LogAsync($"[500] api/image/clearAll: Exception occurred while clearing all images: {ex.Message}", nameof(ImageController));
				return this.StatusCode(500, new ProblemDetails
				{
					Title = "Error clearing all images",
					Detail = ex.Message,
					Status = 500
				});
			}
		}

		[HttpGet("data")]
		public async Task<ActionResult<ImageObjData>> ImageDataAsync([FromQuery] Guid id, [FromQuery] string format = "png")
		{
			try
			{
				var obj = await Task.Run(() => this.imageCollection[id]);
				if (obj == null)
				{
					await this.logger.LogAsync($"[404] api/image/data: No image found with ID {id}.", nameof(ImageController));
					return this.NotFound(new ProblemDetails
					{
						Title = "Image not found",
						Detail = $"No image found with ID {id}.",
						Status = 404
					});
				}

				if (obj.Img == null)
				{
					// Logge den Fehler
					await this.logger.LogAsync($"[500] api/image/data: Image data is null for image with ID {id}.", nameof(ImageController));
					return this.StatusCode(500, new ProblemDetails
					{
						Title = "Image data is null",
						Detail = $"Image object found, but image data (Img) is null for ID {id}.",
						Status = 500
					});
				}

				if (!ImageCollection.SupportedFormats.Contains(format.ToLower()))
				{
					await this.logger.LogAsync($"[000] api/image/data: Unsupported format '{format}' requested. Defaulting to 'png'.", nameof(ImageController));
					format = "png";
				}

				var data = await Task.Run(() => new ImageObjData(obj, format));
				if (string.IsNullOrEmpty(data.Base64Data))
				{
					await this.logger.LogAsync($"[500] api/image/data: Failed to encode image data to Base64 for image with ID {id}.", nameof(ImageController));
					return this.StatusCode(500, new ProblemDetails
					{
						Title = "Error encoding image data",
						Detail = "Failed to encode image data to Base64.",
						Status = 500
					});
				}

				await this.logger.LogAsync($"[200] api/image/data: Successfully retrieved image data for ID {id} (as '{format}').", nameof(ImageController));
				return this.Ok(data);
			}
			catch (Exception ex)
			{
				await this.logger.LogAsync($"[500] api/image/data: Exception occurred while retrieving image data for ID {id} (as '{format}'): {ex.Message}", nameof(ImageController));
				return this.StatusCode(500, new ProblemDetails
				{
					Title = $"Error retrieving image data for ID {id} (as '{format})'",
					Detail = ex.Message,
					Status = 500
				});
			}
		}

		[HttpGet("download")]
		[ProducesResponseType(typeof(FileContentResult), 200)]
		[ProducesResponseType(typeof(ProblemDetails), 400)]
		[ProducesResponseType(typeof(ProblemDetails), 404)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<IActionResult> ImageDownloadAsync([FromQuery] Guid id, [FromQuery] string format = "png")
		{
			try
			{
				var obj = await Task.Run(() => this.imageCollection[id]);
				if (obj == null)
				{
					await this.logger.LogAsync($"[404] api/image/download: No image found with ID {id}.", nameof(ImageController));
					return this.NotFound(new ProblemDetails
					{
						Title = "Image not found",
						Detail = $"No image found with ID {id}.",
						Status = 404
					});
				}

				format = format.ToLowerInvariant();
				if (!ImageCollection.SupportedFormats.Contains(format.ToLower()))
				{
					await this.logger.LogAsync($"[000] api/image/download: Unsupported format '{format}' requested. Defaulting to 'png'.", nameof(ImageController));
					format = "png";
				}

				var image = obj.Img;
				if (image == null)
				{
					await this.logger.LogAsync($"[500] api/image/download: Image data is null for image with ID {id}.", nameof(ImageController));
					return this.StatusCode(500, new ProblemDetails
					{
						Title = "Error retrieving image",
						Detail = "The image data is null.",
						Status = 500
					});
				}

				using var ms = new MemoryStream();
				string contentType;
				string ext;
				switch (format)
				{
					case "jpg":
					case "jpeg":
						await image.SaveAsJpegAsync(ms, new JpegEncoder());
						contentType = "image/jpeg"; ext = "jpg"; break;
					case "bmp":
						await image.SaveAsBmpAsync(ms, new BmpEncoder());
						contentType = "image/bmp"; ext = "bmp"; break;
					case "gif":
						await image.SaveAsGifAsync(ms);
						contentType = "image/gif"; ext = "gif"; break;
					case "tga":
						await image.SaveAsTgaAsync(ms);
						contentType = "image/tga"; ext = "tga"; break;
					case "tif":
					case "tiff":
						await image.SaveAsTiffAsync(ms);
						contentType = "image/tiff"; ext = "tiff"; break;
					default:
						await image.SaveAsPngAsync(ms, new PngEncoder());
						contentType = "image/png"; ext = "png"; break;
				}

				var fileName = $"{obj.Id}_exported.{ext}";

				await this.logger.LogAsync($"[200] api/image/download: Successfully prepared image data for download for ID {id} (as '{format}').", nameof(ImageController));
				return this.File(ms.ToArray(), contentType, fileName);
			}
			catch (Exception ex)
			{
				await this.logger.LogAsync($"[500] api/image/download: Exception occurred while downloading image data for ID {id} (as '{format}'): {ex.Message}", nameof(ImageController));
				return this.StatusCode(500, new ProblemDetails
				{
					Title = $"Error downloading image data for ID {id} (as ' {format})'",
					Detail = ex.Message,
					Status = 500
				});
			}
		}

		[HttpDelete("cleanup-only-keep-latest")]
		[ProducesResponseType(typeof(int), 200)]
		[ProducesResponseType(typeof(ProblemDetails), 400)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<IActionResult> ImageCleanupOnlyKeepLatestAsync([FromQuery] int maxImages = -1)
		{
			try
			{
				int removed = 0;

				if (maxImages <= 0)
				{
					removed = await this.imageCollection.ApplyImagesLimitAsync();
				}

				removed = await this.imageCollection.CleanupOldImages(maxImages);

				await this.logger.LogAsync($"[200] api/image/cleanup-only-keep-latest: Cleanup completed. Removed {removed} images to enforce max of {maxImages} images.", nameof(ImageController));
				return this.Ok(removed);
			}
			catch (Exception ex)
			{
				await this.logger.LogAsync($"[500] api/image/cleanup-only-keep-latest: Exception occurred during cleanup: {ex.Message}", nameof(ImageController));
				return this.StatusCode(500, new ProblemDetails
				{
					Title = "Error during cleanup",
					Detail = ex.Message,
					Status = 500
				});
			}
		}

		[HttpGet("create-gif")]
		[ProducesResponseType(typeof(FileContentResult), 200)]
		[ProducesResponseType(typeof(ProblemDetails), 400)]
		[ProducesResponseType(typeof(ProblemDetails), 404)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<IActionResult> CreateGifAsync([FromQuery] IEnumerable<Guid>? ids = null, [FromQuery] IEnumerable<ImageObjDto>? dtos = null, [FromQuery] int frameRate = 10, [FromQuery] double rescale = 1.0, [FromQuery] bool doLoop = false)
		{
			List<ImageObj> objs = [];
			string fileName = $"animation_{DateTime.Now:yyyyMMdd_HHmmss}.gif";

			try
			{
				// If Guids are provided, load those images from the collection
				if (ids != null && ids.Count() > 0)
				{
					await this.logger.LogAsync($"[000] api/image/create-gif: Creating GIF from {ids.Count()} image IDs with frame rate {frameRate}, rescale {rescale}, loop {(doLoop ? "enabled" : "disabled")}.", nameof(ImageController));

					await Task.Run(async () =>
					{
						foreach (var id in ids)
						{
							var img = this.imageCollection[id];
							if (img != null)
							{
								if (Math.Abs(rescale - 1.0) > 0.01f)
								{
									await img.ResizeAsync((int) (img.Width * rescale), (int) (img.Height * rescale), true, false);
								}
								objs.Add(img);
							}
						}
					});
				}
				else if (dtos != null && dtos.Count() > 0)
				{
					await this.logger.LogAsync($"[000] api/image/create-gif: Creating GIF from {dtos.Count()} image DTOs with frame rate {frameRate}, rescale {rescale}, loop {(doLoop ? "enabled" : "disabled")}.", nameof(ImageController));

					List<string> base64s = dtos.Select(d => d.Data.Base64Data).Where(b64 => !string.IsNullOrEmpty(b64)).ToList();
					foreach (var b64 in base64s)
					{
						var img = await ImageCollection.CreateFromBase64(b64, (float)rescale);
						if (img != null)
						{
							objs.Add(img);
						}
					}
				}

				if ((dtos == null || dtos.Count() <= 0) && (ids == null || ids.Count() <= 0))
				{
					await this.logger.LogAsync($"[000] api/image/create-gif: No IDs or DTOs provided, creating GIF from all {this.imageCollection.Images.Count} images in the collection with frame rate {frameRate}, rescale {rescale}, loop {(doLoop ? "enabled" : "disabled")}.", nameof(ImageController));
					objs = this.imageCollection.Images.ToList();
				}

				if (objs.Count <= 0)
				{
					await this.logger.LogAsync("[400] api/image/create-gif: No valid images provided to create GIF.", nameof(ImageController));
					return this.BadRequest(new ProblemDetails
					{
						Title = "No images provided",
						Detail = "You must provide at least one valid image ID or DTO to create a GIF.",
						Status = 400
					});
				}

				fileName = objs.FirstOrDefault()?.Name ?? fileName;
				Image<Rgba32>[] arr = objs.Select(o => o.Img).OfType<Image<Rgba32>>().ToArray();

				string? gifPath = await ImageCollection.CreateGifAsync(arr, null, fileName, frameRate, doLoop);
				if (string.IsNullOrEmpty(gifPath) || !System.IO.File.Exists(gifPath))
				{
					await this.logger.LogAsync("[500] api/image/create-gif: GIF file was not created successfully.", nameof(ImageController));
					return this.StatusCode(500, new ProblemDetails
					{
						Title = "Error creating GIF",
						Detail = "GIF file was not created successfully.",
						Status = 500
					});
				}

				var gifBytes = await System.IO.File.ReadAllBytesAsync(gifPath);

				await this.logger.LogAsync($"[200] api/image/create-gif: Successfully created GIF '{fileName}' with {arr.Length} frames at {frameRate} FPS.", nameof(ImageController));
				return this.File(gifBytes, "image/gif", Path.GetFileName(gifPath));
			}
			catch (Exception ex)
			{
				await this.logger.LogAsync($"[500] api/image/create-gif: Exception occurred while creating GIF: {ex.Message}", nameof(ImageController));
				return this.StatusCode(500, new ProblemDetails
				{
					Title = "Error creating GIF",
					Detail = ex.Message,
					Status = 500
				});
			}
		}

	}
}