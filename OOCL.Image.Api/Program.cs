using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.OpenApi.Models;
using OOCL.Image.Core;
using OOCL.Image.OpenCl;
using OOCL.Image.Shared;

namespace OOCL.Image.Api
{
	public class Program
	{
		public static void Main(string[] args)
		{
			var builder = WebApplication.CreateBuilder(args);

			string environment      = builder.Environment.EnvironmentName;
			bool   swaggerEnabled   = builder.Configuration.GetValue("SwaggerEnabled", true);
			int    maxUploadSize    = builder.Configuration.GetValue<int>("MaxUploadSizeMb", 128) * 1_000_000;
			int    imagesLimit      = builder.Configuration.GetValue<int>("ImagesLimit");
			string preferredDevice  = builder.Configuration.GetValue<string>("PreferredDevice") ?? "CPU";
			bool   loadResources    = builder.Configuration.GetValue("LoadResources", false);
			bool   serverSidedData  = builder.Configuration.GetValue<bool>("ServerSidedData", false);
			bool   usePathBase      = builder.Configuration.GetValue("UsePathBase", false); // jetzt false

			if (!serverSidedData)
				loadResources = false;

			string appName = typeof(Program).Assembly.GetName().Name ?? "ASP.NET WebAPI (.NET8)";

			WebApiConfig config = new(environment, appName, swaggerEnabled, maxUploadSize / 1_000_000,
				imagesLimit, preferredDevice, loadResources, serverSidedData, usePathBase);
			builder.Services.AddSingleton(config);

			if (serverSidedData)
				builder.Services.AddSingleton(sp => new ImageCollection(false, 720, 480, imagesLimit, loadResources, serverSidedData));
			else
				builder.Services.AddScoped(sp => new ImageCollection(false, 720, 480, imagesLimit, loadResources, serverSidedData));

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
				options.Limits.MaxRequestBodySize = maxUploadSize;
				options.Configure(context.Configuration.GetSection("Kestrel"));
			});

			builder.Services.Configure<IISServerOptions>(o => o.MaxRequestBodySize = maxUploadSize);
			builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = maxUploadSize);

			builder.Logging.AddConsole().AddDebug();
			builder.Logging.SetMinimumLevel(LogLevel.Debug);

			builder.Services.AddControllers();
			builder.Services.AddCors(options =>
			{
				options.AddPolicy("OOCLImageCors", policy =>
				{
					policy.WithOrigins(
						"https://api.oocl.work",
						"http://api.oocl.work",
						"https://localhost:7240",
						"http://localhost:5019"
					)
					.AllowAnyHeader()
					.AllowAnyMethod();
				});
			});

			var app = builder.Build();

			app.UseForwardedHeaders(new ForwardedHeadersOptions
			{
				ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
			});

			// usePathBase ist jetzt false -> kein Prefix mehr
			if (usePathBase)
				app.UsePathBase("/api");

			app.UseHttpsRedirection();

			app.UseSwagger(c => c.RouteTemplate = "swagger/{documentName}/swagger.json");

			app.UseSwaggerUI(c =>
			{
				// Explizit relative Referenz (./) schützt vor Root-Fehlinterpretation auf manchen Clients/Caches
				c.SwaggerEndpoint("./v1/swagger.json", "OOCL.Image API v1");
				c.RoutePrefix = "swagger";
				c.DisplayRequestDuration();
			});
			
			// Optional: Fallback-Redirect falls Clients noch /swagger/v1/... ohne /api nutzen (IIS Rewrite besser, hier API-Ebene):
			app.MapGet("/swagger/v1/swagger.json", ctx =>
			{
				ctx.Response.Headers.Location = "/api/swagger/v1/swagger.json";
				ctx.Response.StatusCode = StatusCodes.Status302Found;
				return Task.CompletedTask;
			}).ExcludeFromDescription();

			if (app.Environment.IsDevelopment())
				app.UseDeveloperExceptionPage();

			app.UseCors("OOCLImageCors");
			app.MapControllers();

			app.Logger.LogInformation("Startup: ENV={Env}; UsePathBase={UsePathBase}; ApiBaseUrl={ApiBaseUrl}",
				app.Environment.EnvironmentName,
				usePathBase,
				builder.Configuration["ApiBaseUrl"]);

			app.Run();
		}
	}
}