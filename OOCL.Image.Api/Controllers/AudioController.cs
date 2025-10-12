using Microsoft.AspNetCore.Mvc;
using OOCL.Image.Core;
using OOCL.Image.Shared;

namespace OOCL.Image.Api.Controllers
{
	[ApiController]
	[Route("api/[controller]")]
	public class AudioController : ControllerBase
	{
		private RollingFileLogger logger;
		private readonly AudioCollection audioCollection;

		public AudioController(RollingFileLogger logger, AudioCollection audioCollection)
		{
			this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
			this.audioCollection = audioCollection ?? throw new ArgumentNullException(nameof(audioCollection));
		}

		[HttpGet("tracks")]
		[ProducesResponseType(typeof(IEnumerable<AudioObjInfo>), 200)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<ActionResult<IEnumerable<AudioObjInfo>>> GetTracksAsync([FromQuery] bool includeData = false)
		{
			try
			{
				var infos = await Task.Run(() => this.audioCollection.Tracks.Select(t => new AudioObjDto(t, includeData)));

				return this.Ok(infos);
			}
			catch (Exception ex)
			{
				this.Response.StatusCode = 500;
				await this.logger.LogExceptionAsync(ex, nameof(AudioController));
				return this.StatusCode(500, new ProblemDetails
				{
					Title = "Error retrieving audio tracks",
					Detail = ex.Message,
					Status = 500
				});
			}
		}

		[HttpGet("load-resources-audio")]
		[ProducesResponseType(typeof(IEnumerable<AudioObjDto>), 200)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<ActionResult<IEnumerable<AudioObjDto>>> AudioLoadResourcesAsync([FromQuery] bool includeData = true)
		{
			try
			{
				var loaded = await this.audioCollection.LoadFromResources();
				var infos = loaded.Select(t => new AudioObjDto(t, includeData));
				await this.logger.LogAsync($"[200] api/audio/load-resources: Successfully loaded {loaded.Count()} audio resources from the resources directory.", nameof(AudioController));
				
				return this.Ok(infos);
			}
			catch (Exception ex)
			{
				this.Response.StatusCode = 500;
				await this.logger.LogExceptionAsync(ex, nameof(AudioController));
				return this.StatusCode(500, new ProblemDetails
				{
					Title = "Error loading audio resources",
					Detail = ex.Message,
					Status = 500
				});
			}
		}

		[HttpPost("load-audio")]
		[Consumes("multipart/form-data")]
		[ProducesResponseType(typeof(AudioObjDto), 200)]
		[ProducesResponseType(typeof(ProblemDetails), 400)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<ActionResult<AudioObjDto>> AudioLoadAsync(IFormFile file, [FromQuery] bool includeData = false)
		{
			if (file == null)
			{
				return this.StatusCode(400, new ProblemDetails
				{
					Title = "No file provided",
					Detail = "Please provide an audio file to load.",
					Status = 400
				});
			}

			try
			{
				var originalFileName = Path.GetFileName(file.FileName);
				var invalidChars = Path.GetInvalidFileNameChars();
				var safeFileName = string.Join("_", originalFileName.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));
				if (string.IsNullOrWhiteSpace(safeFileName))
				{
					await this.logger.LogAsync("[000] api/audio/load-audio: Uploaded file has no valid name. Defaulting to 'upload'.", nameof(AudioController));
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
					var audioObj = await this.audioCollection.ImportAsync(destPath);
					return new AudioObjDto(audioObj, includeData);
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
					await this.logger.LogAsync($"[000] api/audio/load-audio: Temporary file '{destPath}' could not be deleted: {ex.Message}", nameof(AudioController));
				}

				await this.logger.LogAsync($"[200] api/audio/load: Successfully loaded audio '{safeFileName}' with ID {info.Id} ({info.Data.SizeInMb.ToString("F2")} MB data).", nameof(AudioController));
				return this.Ok(info);
			}
			catch (Exception ex)
			{
				this.Response.StatusCode = 500;
				await this.logger.LogExceptionAsync(ex, nameof(AudioController));
				return this.StatusCode(500, new ProblemDetails
				{
					Title = "Error loading audio file",
					Detail = ex.Message,
					Status = 500
				});
			}
		}

		[HttpDelete("remove-audio")]
		[ProducesResponseType(200)]
		[ProducesResponseType(typeof(ProblemDetails), 404)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<IActionResult> AudioRemoveAsync([FromQuery] Guid id)
		{
			try
			{
				var obj = this.audioCollection[id];
				await Task.Run(() => this.audioCollection.RemoveAsync(obj));
				bool success = this.audioCollection[id] == null;
				if (!success)
				{
					await this.logger.LogAsync($"[404] api/audio/remove: No audio found with ID {id} to remove.", nameof(AudioController));
					return this.NotFound(new ProblemDetails
					{
						Title = "Audio not found",
						Detail = $"No audio found with ID {id}.",
						Status = 404
					});
				}

				await this.logger.LogAsync($"[200] api/audio/remove: Successfully removed audio with ID {id}.", nameof(AudioController));
				return this.Ok();
			}
			catch (Exception ex)
			{
				await this.logger.LogAsync($"[500] api/audio/remove: Exception occurred while removing audio with ID {id}: {ex.Message}", nameof(AudioController));
				return this.StatusCode(500, new ProblemDetails
				{
					Title = $"Error deleting audio with ID {id}",
					Detail = ex.Message,
					Status = 500
				});
			}
		}

		[HttpDelete("clearAll-audio")]
		[ProducesResponseType(200)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<IActionResult> AudioClearAllAsync()
		{
			try
			{
				await Task.Run(this.audioCollection.Clear);

				await this.logger.LogAsync("[200] api/audio/clearAll: Successfully cleared all audios from the collection.", nameof(AudioController));
				return this.Ok();
			}
			catch (Exception ex)
			{
				await this.logger.LogAsync($"[500] api/audio/clearAll: Exception occurred while clearing all audios: {ex.Message}", nameof(AudioController));
				return this.StatusCode(500, new ProblemDetails
				{
					Title = "Error clearing all audios",
					Detail = ex.Message,
					Status = 500
				});
			}
		}

		[HttpGet("data-audio")]
		[ProducesResponseType(typeof(AudioObjData), 200)]
		[ProducesResponseType(typeof(ProblemDetails), 404)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<ActionResult<AudioObjData>> GetAudioDataAsync([FromQuery] Guid id)
		{
			try
			{
				var obj = this.audioCollection[id];
				if (obj == null)
				{
					await this.logger.LogAsync($"[404] api/audio/data: No audio found with ID {id}.", nameof(AudioController));
					return this.NotFound(new ProblemDetails
					{
						Title = "Audio not found",
						Detail = $"No audio found with ID {id}.",
						Status = 404
					});
				}

				var data = await Task.Run(() => new AudioObjData(obj));
				return this.Ok(data);
			}
			catch (Exception ex)
			{
				this.Response.StatusCode = 500;
				await this.logger.LogExceptionAsync(ex, nameof(AudioController));
				return this.StatusCode(500, new ProblemDetails
				{
					Title = $"Error retrieving audio data for ID {id}",
					Detail = ex.Message,
					Status = 500
				});
			}
		}

		[HttpGet("download-audio")]
		[ProducesResponseType(typeof(FileContentResult), 200)]
		[ProducesResponseType(typeof(ProblemDetails), 400)]
		[ProducesResponseType(typeof(ProblemDetails), 404)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<IActionResult> AudioDownloadAsync([FromQuery] Guid id, [FromQuery] string format = "wav", [FromQuery] int bits = 24)
		{
			format = format.Trim().Trim('.').ToLowerInvariant();
			if (format == "mp3")
			{
				if (bits != 96 && bits != 128 && bits != 192 && bits != 256 && bits != 320)
				{
					bits = 192;
				}
			}
			else
			{
				format = "wav";
				if (bits != 16 && bits != 24 && bits != 32)
				{
					bits = 24;
				}
			}

			try
			{
				var obj = this.audioCollection[id];
				if (obj == null)
				{
					return this.NotFound(new ProblemDetails
					{
						Title = "Audio not found",
						Detail = $"No audio found with ID {id}.",
						Status = 404
					});
				}

				// Create temp out path
				var tempDir = Path.GetTempPath();
				var baseName = Path.GetFileNameWithoutExtension(obj.FilePath) ?? "audio";

				string? outFile = null;
				if (format.Contains("3"))
				{
					outFile = await AudioExporter.ExportMp3Async(obj, tempDir, bits);
				}
				else
				{
					outFile = await AudioExporter.ExportWavAsync(obj, tempDir, bits);
				}

				if (string.IsNullOrWhiteSpace(outFile) || !System.IO.File.Exists(outFile))
				{
					return this.StatusCode(500, new ProblemDetails
					{
						Title = "Error exporting audio",
						Detail = "The audio file could not be exported in the requested format.",
						Status = 500
					});
				}

				byte[] fileBytes = await System.IO.File.ReadAllBytesAsync(outFile);
				string fileName = Path.GetFileName(outFile) ?? $"{baseName}.{format}";
				string contentType = format.Contains("3") ? "audio/mpeg" : "audio/wav";
				return this.File(fileBytes, contentType, fileName);
			}
			catch (Exception ex)
			{
				return this.StatusCode(500, new ProblemDetails
				{
					Title = "Error downloading audio file",
					Detail = ex.Message,
					Status = 500
				});
			}
		}

		[HttpPost("download-audio-data")]
		[ProducesResponseType(typeof(FileContentResult), 200)]
		[ProducesResponseType(typeof(ProblemDetails), 400)]
		[ProducesResponseType(typeof(ProblemDetails), 404)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<IActionResult> AudioDownloadDataAsync([FromBody] AudioObjDto dto, [FromQuery] string format = "wav", [FromQuery] int bits = 24)
		{
			if (dto == null || dto.Data == null || dto.Data.Samples == null || dto.Data.Samples.Length == 0)
			{
				return this.StatusCode(400, new ProblemDetails
				{
					Title = "No audio data provided",
					Detail = "Please provide valid audio data in the request body.",
					Status = 400
				});
			}
			format = format.Trim().Trim('.').ToLowerInvariant();
			if (format == "mp3")
			{
				if (bits != 96 && bits != 128 && bits != 192 && bits != 256 && bits != 320)
				{
					bits = 192;
				}
			}
			else
			{
				format = "wav";
				if (bits != 16 && bits != 24 && bits != 32)
				{
					bits = 24;
				}
			}
			try
			{
				var tempDir = Path.GetTempPath();
				var baseName = string.IsNullOrWhiteSpace(dto.Info.Name) ? "audio" : dto.Info.Name.Replace("▶ ", "").Replace("|| ", "").Trim();
				if (string.IsNullOrWhiteSpace(baseName))
				{
					baseName = "audio";
				}
				var audioObj = await AudioCollection.CreateFromDataAsync(dto.Data.Samples, dto.Info.SampleRate, dto.Info.Channels, dto.Info.BitDepth);
				if (audioObj == null)
				{
					return this.StatusCode(500, new ProblemDetails
					{
						Title = "Error creating audio object",
						Detail = "The provided audio data could not be converted into an audio object.",
						Status = 500
					});
				}

				string? outFile = null;
				if (format.Contains("3"))
				{
					outFile = await AudioExporter.ExportMp3Async(audioObj, tempDir, bits);
				}
				else
				{
					outFile = await AudioExporter.ExportWavAsync(audioObj, tempDir, bits);
				}
				if (string.IsNullOrWhiteSpace(outFile) || !System.IO.File.Exists(outFile))
				{
					return this.StatusCode(500, new ProblemDetails
					{
						Title = "Error exporting audio",
						Detail = "The audio file could not be exported in the requested format.",
						Status = 500
					});
				}
				byte[] fileBytes = await System.IO.File.ReadAllBytesAsync(outFile);
				string fileName = Path.GetFileName(outFile) ?? $"{baseName}.{format}";
				string contentType = format.Contains("3") ? "audio/mpeg" : "audio/wav";
				return this.File(fileBytes, contentType, fileName);
			}
			catch (Exception ex)
			{
				return this.StatusCode(500, new ProblemDetails
				{
					Title = "Error downloading audio file",
					Detail = ex.Message,
					Status = 500
				});
			}
		}

		[HttpDelete("cleanup-only-keep-latest-audio")]
		[ProducesResponseType(typeof(int), 200)]
		[ProducesResponseType(typeof(ProblemDetails), 400)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<IActionResult> AudioCleanupOnlyKeepLatestAsync([FromQuery] int? maxTracks = 1)
		{
			if (maxTracks < 0)
			{
				return this.StatusCode(400, new ProblemDetails
				{
					Title = "Invalid maxTracks value",
					Detail = "The maxTracks parameter must be greater than or equal to zero.",
					Status = 400
				});
			}
			try
			{
				int removedCount = await Task.Run(() => this.audioCollection.EnforceTracksLimit(maxTracks));
				await this.logger.LogAsync($"[200] api/audio/cleanup-only-keep-latest: Successfully cleaned up audio collection, kept latest {maxTracks} tracks, removed {removedCount} tracks.", nameof(AudioController));
				return this.Ok(removedCount);
			}
			catch (Exception ex)
			{
				await this.logger.LogAsync($"[500] api/audio/cleanup-only-keep-latest: Exception occurred while cleaning up audio collection: {ex.Message}", nameof(AudioController));
				return this.StatusCode(500, new ProblemDetails
				{
					Title = "Error cleaning up audio collection",
					Detail = ex.Message,
					Status = 500
				});
			}
		}

		[HttpPost("waveform-base64")]
		[ProducesResponseType(typeof(string), 200)]
		[ProducesResponseType(typeof(ProblemDetails), 400)]
		[ProducesResponseType(typeof(ProblemDetails), 404)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<IActionResult> AudioGenerateWaveformBase64Async([FromQuery] Guid? id = null, [FromBody] AudioObjDto? dto = null, [FromQuery] int width = 800, [FromQuery] int height = 200, [FromQuery] int samplesPerPixel = 128, [FromQuery] string? offset = "0", [FromQuery] string graphColor = "#FFFFFF", [FromQuery] string backColor = "#000000", [FromQuery] string format = "jpg")
		{
			try
			{
				if (width <= 0 || height <= 0)
				{
					return this.StatusCode(400, new ProblemDetails
					{
						Title = "Invalid dimensions",
						Detail = "Width and height must be greater than zero.",
						Status = 400
					});
				}

				if (samplesPerPixel <= 0)
				{
					samplesPerPixel = 1;
				}

				format = format.Trim().Trim('.').ToLowerInvariant();
				if (!ImageCollection.SupportedFormats.Contains(format))
				{
					format = "jpg";
				}

				if (string.IsNullOrEmpty(offset))
				{
					offset = "0";
				}

				if (id == null || id == Guid.Empty)
				{
					if (dto == null || dto.Data == null || dto.Data.Samples == null || dto.Data.Samples.Length == 0)
					{
						return this.StatusCode(400, new ProblemDetails
						{
							Title = "No audio data provided",
							Detail = "Please provide a valid audio ID or audio data in the request body.",
							Status = 400
						});
					}
				}
				else
				{
					var obj = this.audioCollection[id.Value];
					if (obj == null)
					{
						return this.NotFound(new ProblemDetails
						{
							Title = "Audio not found",
							Detail = $"No audio found with ID {id}.",
							Status = 404
						});
					}

					dto = new AudioObjDto(obj, true);
				}

				if (dto == null || dto.Data == null || dto.Data.Samples == null || dto.Data.Samples.Length == 0)
				{
					return this.StatusCode(400, new ProblemDetails
					{
						Title = "No audio data available",
						Detail = "The provided audio ID or data is invalid or empty.",
						Status = 400
					});
				}
				
				string? b64 = await AudioCollection.GenerateWaveformFromBytesAsBase64Async(dto.Data.Samples, dto.Info.SampleRate, dto.Info.Channels, dto.Info.BitDepth, offset, width, height, samplesPerPixel, graphColor, backColor, format);
				if (string.IsNullOrEmpty(b64))
				{
					return this.StatusCode(500, new ProblemDetails
					{
						Title = "Error generating waveform image",
						Detail = "The waveform image could not be generated from the provided audio data.",
						Status = 500
					});
				}

				return this.Ok(b64);
			}
			catch(Exception ex)
			{
				return this.StatusCode(500, new ProblemDetails
				{
					Title = "Error generating waveform image",
					Detail = ex.Message,
					Status = 500
				});
			}
		}
			
			
		




	}
}
