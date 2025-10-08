using System.Text.Json.Serialization;

namespace OOCL.Image.Shared
{
	public class WebApiConfig
	{
		public string Environment { get; set; } = "UNDEFINED ENVIRONMENT";
		public string ApplicationName { get; set; } = "ASP.NET WebAPI using dotnet8";
		public bool? SwaggerEnabled { get; set; }
		public int? MaxUploadSizeMb { get; set; }
		public int? ImagesLimit { get; set; }
		public string? PreferredDevice { get; set; }
		public bool? LoadResources { get; set; }
		public bool? ServerSidedData { get; set; }
		public bool? UsePathBase { get; set; }
		public int? MaxLogLines { get; set; }
		public bool? CleanupPreviousLogFiles { get; set; }

		public string? CudaWorkerAddress { get; set; } = null;

		public WebApiConfig()
		{

		}

		[JsonConstructor]
		public WebApiConfig(string environment, string applicationName, bool? swaggerEnabled, int? maxUploadSizeMb, int? imagesLimit, string? preferredDevice, bool? loadResources, bool? serverSidedData, bool? usePathBase, int? maxLogLines, bool? cleanupPrevLogs)
		{
			this.Environment = environment;
			this.ApplicationName = applicationName;
			this.SwaggerEnabled = swaggerEnabled;
			this.MaxUploadSizeMb = maxUploadSizeMb;
			this.ImagesLimit = imagesLimit;
			this.PreferredDevice = preferredDevice;
			this.LoadResources = loadResources;
			this.ServerSidedData = serverSidedData;
			this.UsePathBase = usePathBase;
			this.MaxLogLines = maxLogLines;
			this.CleanupPreviousLogFiles = cleanupPrevLogs;
		}
	}
}
