using Microsoft.AspNetCore.Http.Features;
using Microsoft.OpenApi.Models;
using OOCL.Image.Core;
using OOCL.Image.OpenCl;

namespace OOCL.Image.Api
{
	public class Program
	{
		public static void Main(string[] args)
		{
			var builder = WebApplication.CreateBuilder(args);

			// Get config values
			bool swaggerEnabled = builder.Configuration.GetValue<bool>("SwaggerEnabled", true);
			int maxUploadSize = builder.Configuration.GetValue<int>("MaxUploadSizeMb", 128) * 1_000_000;
			int imagesLimit = builder.Configuration.GetValue<int>("ImagesLimit", 0);
			string preferredDevice = builder.Configuration.GetValue<string>("PreferredDevice") ?? "CPU";
			bool loadResources = builder.Configuration.GetValue<bool>("LoadResources", false);

			// Add services to the container.
			builder.Services.AddSingleton<ImageCollection>(sp => new ImageCollection(false,720, 480,  imagesLimit, loadResources));
			var openClService = new OpenClService();
			if (!string.IsNullOrWhiteSpace(preferredDevice))
			{
				openClService.Initialize(preferredDevice);
			}
			builder.Services.AddSingleton(openClService);

			// Swagger/OpenAPI (always register generator)
			builder.Services.AddEndpointsApiExplorer();
			builder.Services.AddSwaggerGen(c =>
			{
				c.SwaggerDoc("v1", new OpenApiInfo
				{
					Version = "v1",
					Title = "OOCL.Image API",
					Description = "API + WebApp using OpenCL Kernels for image generation etc.",
					TermsOfService = new Uri("https://localhost:7220/terms"),
					Contact = new OpenApiContact { Name = "github: alarmclock-kisser", Email = "marcel.king91299@gmail.com" }
				});
			});

			// Request Body Size Limits
			builder.WebHost.ConfigureKestrel(options =>
			{
				options.Limits.MaxRequestBodySize = maxUploadSize;
			});

			builder.Services.Configure<IISServerOptions>(options =>
			{
				options.MaxRequestBodySize = maxUploadSize;
			});

			builder.Services.Configure<FormOptions>(options =>
			{
				options.MultipartBodyLengthLimit = maxUploadSize;
			});

			// Logging
			builder.Logging.AddConsole();
			builder.Logging.AddDebug();
			builder.Logging.SetMinimumLevel(LogLevel.Debug);

			builder.Services.AddControllers();

			// CORS policy
			builder.Services.AddCors(options =>
			{
				options.AddPolicy("OOCLImageCors", policy =>
				{
					policy.WithOrigins("https://localhost:7220")
						  .AllowAnyHeader()
						  .AllowAnyMethod();
				});
			});

			var app = builder.Build();

			// Ensure Swagger JSON is available regardless of environment so UI can fetch it
			app.UseSwagger();

			// Development-only Middlewares
			if (app.Environment.IsDevelopment())
			{
				// Keep default developer behaviors
				app.UseDeveloperExceptionPage();
			}

			// Expose Swagger UI if enabled in configuration
			if (swaggerEnabled)
			{
				app.UseSwaggerUI(c =>
				{
					c.SwaggerEndpoint("/swagger/v1/swagger.json", "OOCL.Image API v1");
					// Serve the UI at /swagger
					c.RoutePrefix = "swagger";
				});
			}

			app.UseStaticFiles();
			app.UseHttpsRedirection();
			app.UseCors("OOCLImageCors");
			app.MapControllers();

			app.Run();
		}
	}
}
