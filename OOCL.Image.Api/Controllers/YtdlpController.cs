using Microsoft.AspNetCore.Mvc;
using OOCL.Image.Core;

namespace OOCL.Image.Api.Controllers
{
	[ApiController]
	[Route("api/[controller]")]
	public class YtdlpController : ControllerBase
	{
		private YtdlpService ytdlpService;

		public YtdlpController(YtdlpService ytdlpService)
		{
			this.ytdlpService = ytdlpService;
		}

		[HttpPost("update-executable")]
		[ProducesResponseType(typeof(string), 200)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<ActionResult<string>> UpdateExecutableAsync()
		{
			try
			{
				var response = await this.ytdlpService.UpdateExecutableAsync();
				
				return this.Ok(response ?? "yt-dlp is already up to date.");
			}
			catch (Exception ex)
			{
				return this.StatusCode(500, new ProblemDetails
				{
					Title = "Error updating yt-dlp executable",
					Detail = ex.Message,
					Status = 500
				});
			}
		}

		[HttpPost("download-audio-server")]
		[ProducesResponseType(typeof(string), 200)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<ActionResult<string>> DownloadAudioServerAsync([FromQuery] string url, [FromQuery] string format = "mp3", [FromQuery] int bits = 256)
		{
			try
			{
				var response = await this.ytdlpService.DownloadAudioAsync(url, format, bits);
				
				return this.Ok(response ?? "Audio download failed.");
			}
			catch (Exception ex)
			{
				return this.StatusCode(500, new ProblemDetails
				{
					Title = "Error downloading audio",
					Detail = ex.Message,
					Status = 500
				});
			}
		}

		[HttpPost("download-audio-client")]
		[ProducesResponseType(typeof(FileContentResult), 200)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<ActionResult> DownloadAudioClientAsync([FromQuery] string url, [FromQuery] string format = "mp3", [FromQuery] int bits = 256)
		{
			try
			{
				var tempOutputPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
				this.ytdlpService.OutputPath = tempOutputPath;
				var response = await this.ytdlpService.DownloadAudioAsync(url, format, bits);
				if (response == null)
				{
					return this.StatusCode(500, new ProblemDetails
					{
						Title = "Audio download failed",
						Detail = "The audio download did not complete successfully.",
						Status = 500
					});
				}
				var files = Directory.GetFiles(tempOutputPath);
				if (files.Length == 0)
				{
					return this.StatusCode(500, new ProblemDetails
					{
						Title = "No audio file found",
						Detail = "No audio file was found in the output directory after download.",
						Status = 500
					});
				}
				var filePath = files[0];
				var fileBytes = await System.IO.File.ReadAllBytesAsync(filePath);
				var contentType = format.ToLower() switch
				{
					"mp3" => "audio/mpeg",
					"wav" => "audio/wav",
					"flac" => "audio/flac",
					_ => "application/octet-stream"
				};
				System.IO.Directory.Delete(tempOutputPath, true);
				return this.File(fileBytes, contentType, Path.GetFileName(filePath));
			}
			catch (Exception ex)
			{
				return this.StatusCode(500, new ProblemDetails
				{
					Title = "Error downloading audio",
					Detail = ex.Message,
					Status = 500
				});
			}
		}
	}
}
