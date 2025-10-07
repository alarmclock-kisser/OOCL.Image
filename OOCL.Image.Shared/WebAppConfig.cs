using System.Text.Json.Serialization;

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
		public int? TracksLimit { get; set; } = null;

		public string? KestelEndpointHttp { get; set; } = null;
		public string? KestelEndpointHttps { get; set; } = null;

		public string? DefaultKernel { get; set; } = null;
		public string? DefaultFormat { get; set; } = null;
		public string? DefaultUnit { get; set; } = null;

		public int? MaxLogLines { get; set; } = null;
		public bool? CleanupPreviousLogFiles { get; set; } = null;

		public WebAppConfig()
		{

		}

		[JsonConstructor]
		public WebAppConfig(string environment, string applicationName = "Blazor WebApp using dotnet8",
			bool? defaultDarkMode = null, string? preferredDevice = null, int? imagesLimit = null, int? tracksLimit = null, string? apiBaseUrl = null, string? kestelEndpointHttp = null, string? kestelEndpointHttps = null,
			string? defaultKernel = null, string? defaultFormat = null, string? defaultUnit = null, int? maxLogLines = null, bool? cleanupPrevLogs = null)
		{
			this.Environment = environment;
			this.ApplicationName = applicationName;
			this.DefaultDarkMode = defaultDarkMode;
			this.PreferredDevice = preferredDevice;
			this.ImagesLimit = imagesLimit;
			this.TracksLimit = tracksLimit;
			this.ApiBaseUrl = apiBaseUrl;
			this.KestelEndpointHttp = kestelEndpointHttp;
			this.KestelEndpointHttps = kestelEndpointHttps;
			this.DefaultKernel = defaultKernel;
			this.DefaultFormat = defaultFormat;
			this.DefaultUnit = defaultUnit;
			this.MaxLogLines = maxLogLines;
			this.CleanupPreviousLogFiles = cleanupPrevLogs;
		}
	}
}
