using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace OOCL.Image.Shared
{
	public class WebApiConfig
	{
		public string ApplicationName { get; set; } = "ASP.NET WebAPI using dotnet8";
		public bool? SwaggerEnabled { get; set; }
		public int? MaxUploadSizeMb { get; set; }
		public int? ImagesLimit { get; set; }
		public string? PreferredDevice { get; set; }
		public bool? LoadResources { get; set; }
		public bool? ServerSidedData { get; set; }
		public bool? UsePathBase { get; set; }

		public WebApiConfig()
		{

		}

		[JsonConstructor]
		public WebApiConfig(string applicationName, bool? swaggerEnabled, int? maxUploadSizeMb, int? imagesLimit, string? preferredDevice, bool? loadResources, bool? serverSidedData, bool? usePathBase)
		{
			this.ApplicationName = applicationName;
			this.SwaggerEnabled = swaggerEnabled;
			this.MaxUploadSizeMb = maxUploadSizeMb;
			this.ImagesLimit = imagesLimit;
			this.PreferredDevice = preferredDevice;
			this.LoadResources = loadResources;
			this.ServerSidedData = serverSidedData;
			this.UsePathBase = usePathBase;
		}
	}
}
