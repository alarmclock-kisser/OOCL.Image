using Microsoft.Extensions.DependencyInjection;
using Radzen;
using OOCL.Image.Client;
using OOCL.Image.WebApp.Components;
using OOCL.Image.Shared;

namespace OOCL.Image.WebApp
{
	public class Program
	{
		public static void Main(string[] args)
		{
			var builder = WebApplication.CreateBuilder(args);

			// Get assembly environment (appsettings used)
			string environment = builder.Environment.EnvironmentName;

			// Api Base URL aus Konfiguration (roh)
			var rawApiBaseUrl = builder.Configuration["ApiBaseUrl"];

			// Normalisieren (Controller haben 'api/[controller]')
			var normalizedBase = ApiBaseUrlUtility.Normalize(
				rawApiBaseUrl,
				builder.Environment.IsDevelopment(),
				msg => Console.WriteLine("[ApiBaseUrl] " + msg),
				controllerRoutesHaveApiPrefix: true
			);

			var defaultDark = builder.Configuration.GetValue<bool?>("DefaultDarkMode") ?? false;
			var preferredDevice = builder.Configuration.GetValue<string>("PreferredDevice") ?? "cpu";
			var maxImages = builder.Configuration.GetValue("ImagesLimit", 0);
			var appName = typeof(Program).Assembly.GetName().Name ?? "Blazor WebApp (.NET8)";
			var httpKestrel = builder.Configuration.GetValue<string?>("Kestrel:Endpoints:Http:Url");
			var httpsKestrel = builder.Configuration.GetValue<string?>("Kestrel:Endpoints:Https:Url");
			var defaults = builder.Configuration.GetSection("DefaultSelections");
			var defaultKernel = defaults.GetValue<string>("Kernel");
			var defaultFormat = defaults.GetValue<string>("Format");
			var defaultUnit = defaults.GetValue<string>("Unit");

			// Add config objs (singleton)
			builder.Services.AddSingleton(new ApiUrlConfig(normalizedBase));
			WebAppConfig config = new(environment, appName, defaultDark, preferredDevice, maxImages, rawApiBaseUrl, httpKestrel, httpsKestrel, defaultKernel, defaultFormat, defaultUnit);
			builder.Services.AddSingleton(config);

			builder.Services.AddRazorPages();
			builder.Services.AddServerSideBlazor();
			builder.Services.AddRadzenComponents();

			builder.Services.AddHttpClient<ApiClient>((sp, client) =>
			{
				var cfg = sp.GetRequiredService<ApiUrlConfig>();
				client.BaseAddress = new Uri(cfg.BaseUrl);
				client.Timeout = TimeSpan.FromSeconds(24);
			});

			builder.Services.AddSignalR(o =>
			{
				o.MaximumReceiveMessageSize = 1024 * 1024 * 128;
			});

			var app = builder.Build();

			if (!app.Environment.IsDevelopment())
			{
				app.UseExceptionHandler("/Error");
				app.UseHsts();
			}

			app.UseHttpsRedirection();
			app.UseStaticFiles();
			app.UseRouting();
			app.UseAntiforgery();

			app.MapBlazorHub();
			app.MapFallbackToPage("/_Host");
			app.MapRazorPages();

			app.Run();
		}
	}

	public class ApiUrlConfig
	{
		public string BaseUrl { get; set; }
		public ApiUrlConfig(string baseUrl) => this.BaseUrl = baseUrl;
	}
}
