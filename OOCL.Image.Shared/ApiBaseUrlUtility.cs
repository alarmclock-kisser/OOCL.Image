using System;
using System.Linq;

namespace OOCL.Image.Shared
{
    /// <summary>
    /// Normalisiert eine konfigurierte API Basis-URL so, dass sie zu Controllern mit Route-Pr‰fix "api/[controller]" passt
    /// und keine doppelten oder abschlieﬂenden '/api' Segmente mehr enth‰lt.
    /// Beispiel-Eingaben (alle liefern denselben Output):
    ///   https://localhost:7220
    ///   https://localhost:7220/
    ///   https://localhost:7220/api
    ///   https://localhost:7220/api/
    ///   https://localhost:7220/api/api
    ///   https://localhost:7220/api/api/
    /// Output: https://localhost:7220/
    /// </summary>
    public static class ApiBaseUrlUtility
    {
        public static string Normalize(string? raw,
                                       bool isDevelopment,
                                       Action<string>? log = null,
                                       bool controllerRoutesHaveApiPrefix = true)
        {
            void L(string m) => log?.Invoke(m);

            if (string.IsNullOrWhiteSpace(raw))
			{
				throw new InvalidOperationException("ApiBaseUrl leer oder nicht gesetzt.");
			}

			raw = raw.Trim();

            if (!raw.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !raw.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
			{
				throw new InvalidOperationException($"ApiBaseUrl ohne http/https Schema: '{raw}'");
			}

			if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri))
			{
				throw new InvalidOperationException($"ApiBaseUrl ist keine g¸ltige URI: '{raw}'");
			}

			// Segmente extrahieren (ohne f¸hrende '/')
			var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).ToList();

            // Doppel-/Mehrfach-'api' am Ende zusammenfassen
            for (int i = segments.Count - 2; i >= 0; i--)
            {
                if (segments[i].Equals("api", StringComparison.OrdinalIgnoreCase) &&
                    segments[i + 1].Equals("api", StringComparison.OrdinalIgnoreCase))
                {
                    segments.RemoveAt(i + 1);
                }
            }

            // Wenn Controller selbst bereits 'api/' in Route haben -> trailing 'api' ganz entfernen
            if (controllerRoutesHaveApiPrefix)
            {
                while (segments.Count > 0 &&
                       segments[^1].Equals("api", StringComparison.OrdinalIgnoreCase))
                {
                    segments.RemoveAt(segments.Count - 1);
                }
            }

            // (Optional) Environment-spezifische Anpassungen
            if (isDevelopment)
            {
                L("Environment=Development; normalisiert auf Basis-Host ohne /api");
            }

            var newPath = segments.Count == 0 ? "/" : "/" + string.Join('/', segments);

            var rebuilt = new UriBuilder(uri)
            {
                Path = newPath,
                Query = string.Empty,
                Fragment = string.Empty
            }.Uri.ToString();

            // Immer mit abschlieﬂendem Slash f¸r konsistente BaseAddress-Verwendung
            if (!rebuilt.EndsWith("/"))
			{
				rebuilt += "/";
			}

			L($"ApiBaseUrl Roh='{raw}' -> Normalisiert='{rebuilt}'");
            return rebuilt;
        }
    }
}