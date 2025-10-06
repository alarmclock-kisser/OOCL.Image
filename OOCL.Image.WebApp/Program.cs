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

			string environment = builder.Environment.EnvironmentName;
			var rawApiBaseUrl = builder.Configuration["ApiBaseUrl"];

			if (string.IsNullOrWhiteSpace(rawApiBaseUrl))
			{
				throw new InvalidOperationException("ApiBaseUrl fehlt.");
			}

			// WICHTIG: Controller behalten 'api/[controller]' -> trailing /api NICHT entfernen
			var normalizedBase = ApiBaseUrlUtility.Normalize(
				rawApiBaseUrl,
				builder.Environment.IsDevelopment(),
				msg => Console.WriteLine("[ApiBaseUrl] " + msg),
				controllerRoutesHaveApiPrefix: false
			);

			// normalizedBase endet immer mit Slash -> wir nutzen ohne doppelten Slash
			var effectiveBase = normalizedBase.TrimEnd('/'); // erwartet: https://api.oocl.work/api

			var defaultDark     = builder.Configuration.GetValue<bool?>("DefaultDarkMode") ?? false;
			var preferredDevice = builder.Configuration.GetValue<string>("PreferredDevice") ?? "cpu";
			var maxImages       = builder.Configuration.GetValue("ImagesLimit", 0);
			var appName         = typeof(Program).Assembly.GetName().Name ?? "Blazor WebApp (.NET8)";
			var httpsKestrel    = builder.Configuration.GetValue<string?>("Kestrel:Endpoints:Https:Url");
			var defaults        = builder.Configuration.GetSection("DefaultSelections");
			var defaultKernel   = defaults.GetValue<string>("Kernel");
			var defaultFormat   = defaults.GetValue<string>("Format");
			var defaultUnit     = defaults.GetValue<string>("Unit");
			var maxLogLines    = builder.Configuration.GetValue("MaxLogLines", 1024);
			var cleanupPreviousLogs = builder.Configuration.GetValue("CleanupPreviousLogs", false);

			builder.Services.AddSingleton(new ApiUrlConfig(effectiveBase));

			WebAppConfig config = new(
				environment,
				appName,
				defaultDark,
				preferredDevice,
				maxImages,
				effectiveBase,
				null,
				httpsKestrel,
				defaultKernel,
				defaultFormat,
				defaultUnit,
				maxLogLines,
				cleanupPreviousLogs
			);
			builder.Services.AddSingleton(config);

			builder.Services.AddSingleton(sp => new RollingFileLogger(maxLogLines, cleanupPreviousLogs, null, "log_" + environment + "_webapp_"));

			builder.Services.AddRazorPages();
			builder.Services.AddServerSideBlazor();
			builder.Services.AddRadzenComponents();

			builder.Services.AddHttpClient<ApiClient>((sp, client) =>
			{
				var cfg = sp.GetRequiredService<ApiUrlConfig>();
				// BaseAddress = https://api.oocl.work/api/
				client.BaseAddress = new Uri(cfg.BaseUrl.EndsWith("/") ? cfg.BaseUrl : (cfg.BaseUrl + "/"));
				client.Timeout = TimeSpan.FromSeconds(24);
			});

			builder.Services.AddSignalR(o => o.MaximumReceiveMessageSize = 1024 * 1024 * 128);

			builder.Services.AddServerSideBlazor(options =>
			{
				// Zeigt detaillierte Fehler in der Konsole/im Browser an
				options.DetailedErrors = true;
			});

			var app = builder.Build();

			app.Logger.LogInformation("WebApp Startup: ENV={Env}; RawBase={Raw}; Normalized={Norm}; EffectiveBase={Eff}",
				app.Environment.EnvironmentName,
				rawApiBaseUrl,
				normalizedBase,
				effectiveBase);

			// Selbsttest: erwartet funktionierende externe URL-Kette -> /api/api/OpenCl/status
			using (var scope = app.Services.CreateScope())
			{
				try
				{
					var apiClient = scope.ServiceProvider.GetRequiredService<ApiClient>();
					app.Logger.LogInformation("API Selftest: GET api/OpenCl/status (ergibt extern /api/api/OpenCl/status) ...");
					var status = apiClient.GetOpenClServiceInfoAsync().GetAwaiter().GetResult();
					app.Logger.LogInformation("API OK: Device={Device} Initialized={Init}", status.DeviceName, status.Initialized);
				}
				catch (Exception ex)
				{
					app.Logger.LogError(ex, "API Selftest FEHLGESCHLAGEN. Base={Base}", effectiveBase);
				}
			}

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
		public ApiUrlConfig(string baseUrl) => BaseUrl = baseUrl;
	}
}
