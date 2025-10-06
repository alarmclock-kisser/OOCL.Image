using Microsoft.AspNetCore.Mvc;
using OOCL.Image.Core;
using OOCL.Image.Shared;

namespace OOCL.Image.Api.Controllers
{
	[ApiController]
	[Route("api/[controller]")]
	public class InfoController : ControllerBase
	{
		private readonly WebApiConfig webApiConfig;
		private readonly RollingFileLogger logger;

		public InfoController(RollingFileLogger logger, WebApiConfig webApiConfig)
		{
			this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
			this.webApiConfig = webApiConfig ?? throw new ArgumentNullException(nameof(webApiConfig));
		}

		[HttpGet("api-config")]
		public ActionResult<WebApiConfig> GetConfig()
		{
			return this.webApiConfig;
		}

		[HttpGet("download-log")]
		[ProducesResponseType(typeof(FileContentResult), 200)]
		[ProducesResponseType(typeof(ProblemDetails), 400)]
		[ProducesResponseType(typeof(ProblemDetails), 404)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<IActionResult> DownloadLogFileAsync()
		{
			try
			{
				if (string.IsNullOrWhiteSpace(this.logger.LogPath) || !System.IO.File.Exists(this.logger.LogPath))
				{
					return this.NotFound(new ProblemDetails { Title = "Log file not found", Detail = "The log file does not exist on the server." });
				}

				byte[] fileBytes = await System.IO.File.ReadAllBytesAsync(this.logger.LogPath);
				string fileName = Path.GetFileName(this.logger.LogPath) ?? "log.txt";

				return this.File(fileBytes, "text/plain", fileName);
			}
			catch (Exception ex)
			{
				this.Response.StatusCode = 500;
				return this.Problem(title: "Error downloading log file", detail: ex.Message);
			}
		}

		[HttpGet("log")]
		[ProducesResponseType(typeof(IEnumerable<string>), 200)]
		[ProducesResponseType(typeof(ProblemDetails), 204)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<ActionResult<IEnumerable<string>>> GetLogAsync()
		{
			try
			{
				var lines = await this.logger.GetRecentLogsAsync(this.webApiConfig.MaxLogLines ?? 0);
				if (lines == null || !lines.Any())
				{
					return this.NoContent();
				}

				return this.Ok(lines);
			}
			catch (Exception ex)
			{
				return this.StatusCode(500, new ProblemDetails
				{
					Title = "Error retrieving logs",
					Detail = ex.Message,
					Status = 500
				});
			}
		}
	}
}
