using OOCL.Image.Core;
using OOCL.Image.Shared;
using Microsoft.AspNetCore.Mvc;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace OOCL.Image.Api.Controllers
{
	[ApiController]
	[Route("api/[controller]")]
	public class ImageController : ControllerBase
	{
		private readonly ImageCollection imageCollection;

		public ImageController(ImageCollection imageCollection)
		{
			this.imageCollection = imageCollection;
		}

		[HttpGet("server-sided-data")]
		[ProducesResponseType(typeof(bool), 200)]
		public ActionResult<bool> IsServerSidedData()
		{
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
					return this.BadRequest(new ProblemDetails
					{
						Title = "Server sided mode disabled",
						Detail = "Serialization by ID is only available when server-sided data storage is enabled.",
						Status = 400
					});
				}

				if (id == Guid.Empty)
				{
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
					return this.NotFound(new ProblemDetails
					{
						Title = "Image not found",
						Detail = $"No image with ID {id} exists.",
						Status = 404
					});
				}

				if (obj.Img == null)
				{
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
					format = "png";
				}

				scale = float.IsFinite(scale) ? scale : 1.0f;
				if (scale <= 0f)
				{
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
				return this.BadRequest(new ProblemDetails
				{
					Title = "No data provided",
					Detail = "You must provide image data as int array.",
					Status = 400
				});
			}
			if (string.IsNullOrEmpty(file))
			{
				file = "upload.png";
			}
			if (string.IsNullOrEmpty(contentType))
			{
				return this.BadRequest(new ProblemDetails
				{
					Title = "No content type provided",
					Detail = "You must provide a valid content type (e.g. image/png).",
					Status = 400
				});
			}

			try
			{
				var dto = await ImageObjDto.FromBytesAsync(bytesAsInts.Select(b => (byte)b).ToArray(), file, contentType);
				if (dto.Id == Guid.Empty || dto.Info.Id == Guid.Empty || string.IsNullOrEmpty(dto.Data.Base64Data))
				{
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

				return this.Ok(infos);
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

				return this.Ok(info);
			}
			catch (Exception ex)
			{
				return this.StatusCode(500, new ProblemDetails
				{
					Title = $"Error loading image from file '{file.FileName}'",
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

				return this.Ok(info);
			}
			catch (Exception ex)
			{
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
					return this.NotFound(new ProblemDetails
					{
						Title = "Image not found",
						Detail = $"No image found with ID {id}.",
						Status = 404
					});
				}

				return this.Ok();
			}
			catch (Exception ex)
			{
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
				return this.Ok();
			}
			catch (Exception ex)
			{
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
					return this.StatusCode(500, new ProblemDetails
					{
						Title = "Image data is null",
						Detail = $"Image object found, but image data (Img) is null for ID {id}.",
						Status = 500
					});
				}

				if (!ImageCollection.SupportedFormats.Contains(format.ToLower()))
				{
					format = "png";
				}

				var data = await Task.Run(() => new ImageObjData(obj, format));
				if (string.IsNullOrEmpty(data.Base64Data))
				{
					return this.StatusCode(500, new ProblemDetails
					{
						Title = "Error encoding image data",
						Detail = "Failed to encode image data to Base64.",
						Status = 500
					});
				}

				return this.Ok(data);
			}
			catch (Exception ex)
			{
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
					format = "png";
				}

				var image = obj.Img;
				if (image == null)
				{
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
				return this.File(ms.ToArray(), contentType, fileName);
			}
			catch (Exception ex)
			{
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
				return this.Ok(removed);
			}
			catch (Exception ex)
			{
				return this.StatusCode(500, new ProblemDetails
				{
					Title = "Error during cleanup",
					Detail = ex.Message,
					Status = 500
				});
			}
		}

	}
}