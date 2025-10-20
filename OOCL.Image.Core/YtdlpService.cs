using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OOCL.Image.Core
{
	public class YtdlpService
	{
		public string ExecutablePath => Directory.EnumerateFiles(AppContext.BaseDirectory, "yt-dlp.exe").FirstOrDefault() ?? string.Empty;

		public string OutputPath { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic));

		public DateTime LastUpdated { get; private set; } = DateTime.MinValue;

		public YtdlpService()
		{
			
		}



		public async Task<string?> UpdateExecutableAsync()
		{
			Console.WriteLine("Updating yt-dlp executable...");
			// Das Standard-Update-Kommando ist '-U' oder '--update'
			string? result = await this.ExecuteYtdlpCommandAsync("-U");

			if (result != null)
			{
				Console.WriteLine("yt-dlp updated successfully.");
				this.LastUpdated = DateTime.Now;
			}
			return result;
		}

		public async Task<string?> DownloadAudioAsync(string url, string format = "mp3", int bits = 256)
		{
			if (string.IsNullOrEmpty(url))
			{
				throw new ArgumentNullException(nameof(url), "Download URL cannot be null or empty.");
			}

			if (!Directory.Exists(this.OutputPath))
			{
				Directory.CreateDirectory(this.OutputPath);
			}

			// Definiere die yt-dlp Argumente:
			// 1. --extract-audio: Nur Audio extrahieren
			// 2. --audio-format {format}: Das gewünschte Format (mp3, wav, etc.)
			// 3. --audio-quality {bits}k: Die Bitrate (z.B. 256k)
			// 4. -o "{path}/%(title)s.%(ext)s": Der Ausgabe-Pfad und das Dateiformat
			// 5. {url}: Die Ziel-URL
			string arguments = new StringBuilder()
				.Append("--extract-audio ")
				.Append($"--audio-format {format} ")
				.Append($"--audio-quality {bits}k ")
				.Append($"-o \"{Path.Combine(this.OutputPath, "%(title)s.%(ext)s")}\" ")
				.Append($"\"{url}\"")
				.ToString();

			Console.WriteLine($"Starting download for: {url}");
			string? result = await this.ExecuteYtdlpCommandAsync(arguments);

			if (result != null)
			{
				Console.WriteLine($"Download finished successfully to: {this.OutputPath}");
			}
			return result;
		}

		private async Task<string?> ExecuteYtdlpCommandAsync(string arguments)
		{
			if (!File.Exists(this.ExecutablePath))
			{
				throw new FileNotFoundException("yt-dlp executable not found.", this.ExecutablePath);
			}

			var process = new Process
			{
				StartInfo = new ProcessStartInfo
				{
					FileName = this.ExecutablePath,
					Arguments = arguments,
					RedirectStandardOutput = true,
					RedirectStandardError = true,
					UseShellExecute = false,
					CreateNoWindow = true,
					StandardOutputEncoding = Encoding.UTF8,
					StandardErrorEncoding = Encoding.UTF8
				}
			};

			var output = new StringBuilder();
			var error = new StringBuilder();

			process.OutputDataReceived += (sender, e) => {
				if (!string.IsNullOrEmpty(e.Data)) output.AppendLine(e.Data);
			};
			process.ErrorDataReceived += (sender, e) => {
				if (!string.IsNullOrEmpty(e.Data)) error.AppendLine(e.Data);
			};

			process.Start();
			process.BeginOutputReadLine();
			process.BeginErrorReadLine();

			// Warte asynchron auf den Abschluss des Prozesses
			await process.WaitForExitAsync();

			if (process.ExitCode == 0)
			{
				return output.ToString();
			}
			else
			{
				// Logge den Fehler und gib null zurück
				Console.WriteLine($"yt-dlp Error (Exit Code {process.ExitCode}): {error.ToString()}");
				return null;
			}
		}
	}
}
