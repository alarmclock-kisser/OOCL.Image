using OOCL.Image.Core;
using OOCL.Image.Shared;
using Microsoft.AspNetCore.Mvc;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;

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

		[HttpGet("list")]
		[ProducesResponseType(typeof(IEnumerable<ImageObjInfo>), 200)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<ActionResult<IEnumerable<ImageInfo>>> ImageListAsync()
		{
			try
			{
				var infos = await Task.Run(() => this.imageCollection.Images.Select(img => new Shared.ImageObjInfo(img)));

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
		[ProducesResponseType(typeof(ImageObjData), 200)]
		[ProducesResponseType(typeof(ProblemDetails), 400)]
		[ProducesResponseType(typeof(ProblemDetails), 404)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
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

				if (!ImageCollection.SupportedFormats.Contains(format.ToLower()))
				{
					format = "png";
				}

				var data = await Task.Run(() => new ImageObjData(obj,  format));
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
		public async Task<IActionResult> ImageCleanupOnlyKeepLatestAsync([FromQuery] int maxImages = 1)
		{
			try
			{
				int removed = await this.imageCollection.CleanupOldImages(maxImages);
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