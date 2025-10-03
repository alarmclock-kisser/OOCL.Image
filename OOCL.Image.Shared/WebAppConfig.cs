using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace OOCL.Image.Shared
{
	public class WebAppConfig
	{
		public string Environment { get; set; } = "UNDEFINED ENVIRONMENT";
		public string ApplicationName { get; set; } = "Blazor WebApp using dotnet8";
		public string? ApiBaseUrl { get; set; } = null;
		public bool? DefaultDarkMode { get; set; } = null;
		public string? PreferredDevice { get; set; } = null;
		public int? ImagesLimit { get; set; } = null;

		public string? KestelEndpointHttp { get; set; } = null;
		public string? KestelEndpointHttps { get; set; } = null;

		public WebAppConfig()
		{

		}

		[JsonConstructor]
		public WebAppConfig(string environment, string applicationName = "Blazor WebApp using dotnet8", bool? defaultDarkMode = null, string? preferredDevice = null, int? imagesLimit = null, string? apiBaseUrl = null, string? kestelEndpointHttp = null, string? kestelEndpointHttps = null)
		{
			this.Environment = environment;
			this.ApplicationName = applicationName;
			this.DefaultDarkMode = defaultDarkMode;
			this.PreferredDevice = preferredDevice;
			this.ImagesLimit = imagesLimit;
			this.ApiBaseUrl = apiBaseUrl;
			this.KestelEndpointHttp = kestelEndpointHttp;
			this.KestelEndpointHttps = kestelEndpointHttps;
		}
	}
}
