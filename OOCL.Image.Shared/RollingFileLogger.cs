using System.Text;

namespace OOCL.Image.Shared
{
	public class RollingFileLogger
	{
		public string ResourcesPath { get; set; } = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "OOCL.Image.Core", "Resources"));
		public string LogDirName { get; set; } = "Logs";
		public string LogPath { get; set; } = string.Empty;

		public string LogFilePrefix { get; set; } = "log_";
		public string LogFileExtension { get; set; } = ".txt";

		public long MaxLogLines { get; set; } = 65536;

		private Dictionary<long, string> logCache = [];


		private readonly object lockObj = new();

		public RollingFileLogger(int maxLogLines = 4096, bool cleanupPrevLogFiles = false, string? logDir = null, string? logFileNamePrefix = null)
		{
			maxLogLines = Math.Max(0, maxLogLines);
			this.MaxLogLines = maxLogLines;

			// Verify and create resources directory
			if (!string.IsNullOrWhiteSpace(logDir))
			{
				this.ResourcesPath = logDir;
			}
			if (!Directory.Exists(this.ResourcesPath))
			{
				// take temp path
				this.ResourcesPath = Path.GetTempPath();
			}

			// Set log file name
			if (!string.IsNullOrWhiteSpace(logFileNamePrefix))
			{
				this.LogFilePrefix = logFileNamePrefix;
			}

			string logDirPath = this.GetLogDirectory();
			string logFileName = $"{this.LogFilePrefix}{DateTime.Now:yyyyMMdd_HHmmss}{this.LogFileExtension}";
			this.LogPath = Path.Combine(logDirPath, logFileName);

			if (cleanupPrevLogFiles)
			{
				// Delete previous log files
				var oldLogFiles = Directory.GetFiles(logDirPath, $"{this.LogFilePrefix}*{this.LogFileExtension}");
				foreach (var oldFile in oldLogFiles)
				{
					try
					{
						File.Delete(oldFile);
					}
					catch
					{
						// Ignore exceptions during cleanup
					}
				}
			}

			// Create initial log file
			if (!File.Exists(this.LogPath))
			{
				using var fs = File.Create(this.LogPath);
				
				// Create and write intro
				string intro = $"Log started at {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}\n";
				byte[] info = new UTF8Encoding(true).GetBytes(intro);
				fs.Write(info, 0, info.Length);
			}
		}

		public string GetLogDirectory()
		{
			// Ensure the resources path exists
			if (!Directory.Exists(this.ResourcesPath))
			{
				this.ResourcesPath = AppDomain.CurrentDomain.BaseDirectory;
				if (!Directory.Exists(this.ResourcesPath))
				{
					Directory.CreateDirectory(this.ResourcesPath);
				}
			}

			string logDir = Path.Combine(this.ResourcesPath, this.LogDirName);
			if (!Directory.Exists(logDir))
			{
				logDir = Path.GetTempPath();
			}

			return logDir;
		}




		public async Task<IEnumerable<string>> GetRecentLogsAsync(int numberOfLines)
		{
			// If cache is empty, load from file
			if (this.logCache.Count == 0 && File.Exists(this.LogPath))
			{
				var lines = await File.ReadAllLinesAsync(this.LogPath);
				long tick = DateTime.Now.Ticks - lines.Length;
				foreach (var line in lines)
				{
					this.logCache[tick++] = line;
				}
			}

			if (numberOfLines <= 0)
			{
				return this.logCache.Values;
			}

			// Return the most recent lines from cache
			return this.logCache.Values.TakeLast(numberOfLines);
		}

		public async Task<string?> LogAsync(string? message = null, string? sender = null)
		{
			if (string.IsNullOrEmpty(sender))
			{
				sender = "UI";
			}

			string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");

			string logEntry = $"[{timestamp}] [{sender}] {message}";

			this.logCache[DateTime.Now.Ticks] = logEntry;

			if (this.MaxLogLines <= 0)
			{
				return null;
			}

			// Get lines
			var lines = await File.ReadAllLinesAsync(this.LogPath);

			// Check if exceeds max lines
			if (lines.LongLength >= this.MaxLogLines)
			{
				// Remove first lines
				lines = lines.Skip(lines.Length - (int)this.MaxLogLines + 1).ToArray();
			}

			// Append new log entry
			var updatedLines = lines.Append(logEntry);

			await File.WriteAllLinesAsync(this.LogPath, updatedLines);

			return logEntry;
		}

		public async Task<string?> LogExceptionAsync(Exception ex, string? sender = null)
		{
			string message = ex.Message;
			var inner = ex.InnerException;
			while (inner != null)
			{
				message += $" | Inner: {inner.Message}";
				inner = inner.InnerException;
			}

			return await this.LogAsync(message, sender ?? ex.Source);
		}


	}
}
