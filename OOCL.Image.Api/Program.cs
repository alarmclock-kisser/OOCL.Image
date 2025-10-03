using Microsoft.AspNetCore.Http.Features;
using Microsoft.OpenApi.Models;
using OOCL.Image.Core;
using OOCL.Image.OpenCl;
using OOCL.Image.Shared;
using System.Text.Json.Serialization;

namespace OOCL.Image.Api
{
	public class Program
	{
		public static void Main(string[] args)
		{
			var builder = WebApplication.CreateBuilder(args);

			bool swaggerEnabled = builder.Configuration.GetValue("SwaggerEnabled", true);
			int maxUploadSize = builder.Configuration.GetValue<int>("MaxUploadSizeMb", 128) * 1_000_000;
			int imagesLimit = builder.Configuration.GetValue<int>("ImagesLimit");
			string preferredDevice = builder.Configuration.GetValue<string>("PreferredDevice") ?? "CPU";
			bool loadResources = builder.Configuration.GetValue("LoadResources", false);
			bool serverSidedData = builder.Configuration.GetValue<bool>("ServerSidedData", false);
			bool usePathBase = builder.Configuration.GetValue("UsePathBase", false);

			// Get assembly name for Swagger UI
			string appName = typeof(Program).Assembly.GetName().Name ?? "ASP.NET WebAPI (.NET8)";

			// Add WebApiConfig (singleton)
			WebApiConfig config = new(appName, swaggerEnabled, maxUploadSize / 1_000_000, imagesLimit, preferredDevice, loadResources, serverSidedData, usePathBase);
			builder.Services.AddSingleton(config);

			if (serverSidedData)
			{
				builder.Services.AddSingleton(sp => new ImageCollection(false, 720, 480, imagesLimit, loadResources, serverSidedData));
			}
			else
			{
				builder.Services.AddScoped(sp => new ImageCollection(false, 720, 480, imagesLimit, loadResources, serverSidedData));
			}

			builder.Services.AddSingleton(sp => new OpenClService(preferredDevice));

			builder.Services.AddEndpointsApiExplorer();
			builder.Services.AddSwaggerGen(c =>
			{
				c.SwaggerDoc("v1", new OpenApiInfo
				{
					Version = "v1",
					Title = "OOCL.Image API",
					Description = "API + WebApp using OpenCL Kernels for image generation etc.",
					TermsOfService = new Uri("https://example.com/terms"),
					Contact = new OpenApiContact { Name = "github: alarmclock-kisser", Email = "marcel.king91299@gmail.com" }
				});
			});

			builder.WebHost.ConfigureKestrel((context, options) =>
			{
				// Limits
				options.Limits.MaxRequestBodySize = maxUploadSize;

				// Endpoints aus Konfiguration (Kestrel:Endpoints ...)
				options.Configure(context.Configuration.GetSection("Kestrel"));
			});

			builder.Services.Configure<IISServerOptions>(options =>
			{
				options.MaxRequestBodySize = maxUploadSize;
			});

			builder.Services.Configure<FormOptions>(options =>
			{
				options.MultipartBodyLengthLimit = maxUploadSize;
			});

			builder.Logging.AddConsole();
			builder.Logging.AddDebug();
			builder.Logging.SetMinimumLevel(LogLevel.Debug);

			builder.Services.AddControllers();

			builder.Services.AddCors(options =>
			{
				options.AddPolicy("OOCLImageCors", policy =>
				{
					policy.WithOrigins("https://api.oocl.work", "https://localhost:7220", "http://localhost:5019", "http://api.oocl.work")
						  .AllowAnyHeader()
						  .AllowAnyMethod();
				});
			});

			var app = builder.Build();

			// Optional PathBase
			if (usePathBase)
			{
				app.UsePathBase("/api");
			}

			app.UseSwagger(c =>
			{
				c.RouteTemplate = "swagger/{documentName}/swagger.json";
			});

			if (swaggerEnabled)
			{
				app.UseSwaggerUI(c =>
				{
					// prefix für den JSON-Endpunkt wenn PathBase aktiv
					var prefix = usePathBase ? "/api" : string.Empty;
					c.SwaggerEndpoint($"{prefix}/swagger/v1/swagger.json", "OOCL.Image API v1");
					// UI jetzt unter /swagger (bzw. /api/swagger wenn PathBase aktiv), statt Root
					c.RoutePrefix = "swagger";
				});
			}

			if (app.Environment.IsDevelopment())
			{
				app.UseDeveloperExceptionPage();
			}

			// Warnung: wwwroot existiert nicht -> entweder Ordner anlegen oder Zeile entfernen falls nicht benötigt
			// app.UseStaticFiles();

			app.UseHttpsRedirection();
			app.UseCors("OOCLImageCors");
			app.MapControllers();

			app.Run();
		}
	}
}
