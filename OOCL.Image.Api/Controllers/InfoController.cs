using Microsoft.AspNetCore.Mvc;
using OOCL.Image.Shared;

namespace OOCL.Image.Api.Controllers
{
	[ApiController]
	[Route("api/[controller]")]
	public class InfoController : ControllerBase
	{
		private readonly WebApiConfig webApiConfig;

		public InfoController(WebApiConfig webApiConfig)
		{
			this.webApiConfig = webApiConfig ?? throw new ArgumentNullException(nameof(webApiConfig));
		}

		[HttpGet("api-config")]
		public ActionResult<WebApiConfig> GetConfig()
		{
			return this.webApiConfig;
		}
	}
}
