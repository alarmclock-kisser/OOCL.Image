using Microsoft.AspNetCore.Mvc;
using OOCL.Image.Core;
using OOCL.Image.OpenCl;
using OOCL.Image.Shared;
using System.Diagnostics;

namespace OOCL.Image.Api.Controllers
{
	[ApiController]
	[Route("api/[controller]")]
	public class OpenClController : ControllerBase
	{
		private readonly RollingFileLogger logger;
		private readonly OpenClService openClService;
		private readonly ImageCollection imageCollection;
		private readonly AudioCollection audioCollection;

		public OpenClController(RollingFileLogger logger, OpenClService openClService, ImageCollection imageCollection, AudioCollection audioCollection)
		{
			this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
			this.openClService = openClService;
			this.imageCollection = imageCollection;
			this.audioCollection = audioCollection;
		}

		[HttpGet("status")]
		[ProducesResponseType(typeof(OpenClServiceInfo), 200)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<ActionResult<OpenClServiceInfo>> GetStatusAsync()
		{
			try
			{
				var info = await Task.Run(() => new OpenClServiceInfo(this.openClService));
				if (info == null)
				{
					await this.logger.LogAsync("OpenClServiceInfo is null", nameof(OpenClController));
					return this.StatusCode(500, new ProblemDetails
					{
						Status = 500,
						Title = "Internal Server Error",
						Detail = "Failed to retrieve OpenCL service information."
					});
				}

				await this.logger.LogAsync("[200] api/opencl/status: " + (info.Initialized ? "Initialized!" : "OpenCL is currently NOT initialized."), nameof(OpenClController));
				return this.Ok(info);
			}
			catch (Exception ex)
			{
				await this.logger.LogExceptionAsync(ex, nameof(OpenClController));
				return this.StatusCode(500, new ProblemDetails
				{
					Status = 500,
					Title = "Internal Server Error",
					Detail = ex.Message
				});
			}
		}

		[HttpGet("devices")]
		[ProducesResponseType(typeof(IEnumerable<OpenClDeviceInfo>), 200)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<ActionResult<IEnumerable<OpenClDeviceInfo>>> GetDevicesAsync()
		{
			try
			{
				List<OpenClDeviceInfo> devices = [];
				for (int i = 0; i < this.openClService.DeviceCount; i++)
				{
					devices.Add(await Task.Run(() => new OpenClDeviceInfo(this.openClService, i)));
				}

				await this.logger.LogAsync($"[200] api/opencl/devices: Found {devices.Count} devices", nameof(OpenClController));
				return this.Ok(devices);
			}
			catch (Exception ex)
			{
				await this.logger.LogExceptionAsync(ex, nameof(OpenClController));
				return this.StatusCode(500, new ProblemDetails
				{
					Status = 500,
					Title = "Internal Server Error",
					Detail = ex.Message
				});
			}
		}

		[HttpPost("initialize-id")]
		[ProducesResponseType(typeof(OpenClServiceInfo), 200)]
		[ProducesResponseType(typeof(ProblemDetails), 400)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<ActionResult<OpenClServiceInfo>> InitializeByIdAsync([FromBody] int deviceId = 2)
		{
			try
			{
				if (deviceId < 0 || deviceId >= this.openClService.DeviceCount)
				{
					await this.logger.LogAsync($"[400] api/opencl/initialize-id: Invalid Device ID {deviceId}", nameof(OpenClController));
					return this.BadRequest(new ProblemDetails
					{
						Status = 400,
						Title = "Invalid Device ID",
						Detail = $"Device ID must be between 0 and {this.openClService.DeviceCount - 1}."
					});
				}

				await Task.Run(() => this.openClService.Initialize(deviceId));
				var info = new OpenClServiceInfo(this.openClService);
				if (!info.Initialized)
				{
					await this.logger.LogAsync($"[500] api/opencl/initialize-id: Failed to initialize OpenCL with Device ID {deviceId}", nameof(OpenClController));
					return this.StatusCode(500, new ProblemDetails
					{
						Status = 500,
						Title = "Internal Server Error",
						Detail = "Failed to initialize OpenCL service."
					});
				}

				await this.logger.LogAsync($"[200] api/opencl/initialize-id: Successfully initialized OpenCL with Device ID {deviceId}", nameof(OpenClController));
				return this.Ok(info);
			}
			catch (Exception ex)
			{
				await this.logger.LogExceptionAsync(ex, nameof(OpenClController));
				return this.StatusCode(500, new ProblemDetails
				{
					Status = 500,
					Title = "Internal Server Error",
					Detail = ex.Message
				});
			}
		}

		[HttpPost("initialize-name")]
		[ProducesResponseType(typeof(OpenClServiceInfo), 200)]
		[ProducesResponseType(typeof(ProblemDetails), 400)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<ActionResult<OpenClServiceInfo>> InitializeByNameAsync([FromQuery] string deviceName = "Core")
		{
			try
			{
				if (string.IsNullOrWhiteSpace(deviceName))
				{
					await this.logger.LogAsync("[400] api/opencl/initialize-name: Invalid Device Name (null or empty)", nameof(OpenClController));
					return this.BadRequest(new ProblemDetails
					{
						Status = 400,
						Title = "Invalid Device Name",
						Detail = "Device name cannot be null or empty."
					});
				}

				await Task.Run(() => this.openClService.Initialize(deviceName));
				var info = new OpenClServiceInfo(this.openClService);
				if (!info.Initialized)
				{
					await this.logger.LogAsync($"[500] api/opencl/initialize-name: Failed to initialize OpenCL with Device Name '{deviceName}'", nameof(OpenClController));
					return this.StatusCode(500, new ProblemDetails
					{
						Status = 500,
						Title = "Internal Server Error",
						Detail = "Failed to initialize OpenCL service."
					});
				}

				await this.logger.LogAsync($"[200] api/opencl/initialize-name: Successfully initialized OpenCL with Device Name '{deviceName}'", nameof(OpenClController));
				return this.Ok(info);
			}
			catch (Exception ex)
			{
				await this.logger.LogExceptionAsync(ex, nameof(OpenClController));
				return this.StatusCode(500, new ProblemDetails
				{
					Status = 500,
					Title = "Internal Server Error",
					Detail = ex.Message
				});
			}
		}

		[HttpDelete("release")]
		[ProducesResponseType(typeof(OpenClServiceInfo), 200)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<ActionResult<OpenClServiceInfo>> ReleaseAsync()
		{
			try
			{
				await Task.Run(this.openClService.Dispose);
				var info = new OpenClServiceInfo(this.openClService);
				await this.logger.LogAsync("[200] api/opencl/release: Successfully released OpenCL resources", nameof(OpenClController));
				return this.Ok(info);
			}
			catch (Exception ex)
			{
				await this.logger.LogExceptionAsync(ex, nameof(OpenClController));
				return this.StatusCode(500, new ProblemDetails
				{
					Status = 500,
					Title = "Internal Server Error",
					Detail = ex.Message
				});
			}
		}

		[HttpGet("kernel-infos")]
		[ProducesResponseType(typeof(IEnumerable<OpenClKernelInfo>), 200)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<ActionResult<IEnumerable<OpenClKernelInfo>>> GetKernelInfosAsync([FromQuery] bool onlyCompiled = true)
		{
			try
			{
				if (!this.openClService.Initialized)
				{
					await this.logger.LogAsync("[400] api/opencl/kernel-infos: OpenCL Not Initialized", nameof(OpenClController));
					return this.BadRequest(new ProblemDetails
					{
						Status = 400,
						Title = "OpenCL Not Initialized",
						Detail = "Please initialize the OpenCL service before retrieving kernel infos."
					});
				}
				var kernelInfos = await Task.Run(() => this.openClService.Compiler?.KernelFiles.Select(kf => new OpenClKernelInfo(this.openClService.Compiler, this.openClService.Compiler.KernelFiles.ToList().IndexOf(kf))));
				if (kernelInfos == null)
				{
					await this.logger.LogAsync("[500] api/opencl/kernel-infos: Failed to retrieve kernel information", nameof(OpenClController));
					return this.StatusCode(500, new ProblemDetails
					{
						Status = 500,
						Title = "Internal Server Error",
						Detail = "Failed to retrieve kernel information."
					});
				}

				if (onlyCompiled)
				{
					kernelInfos = kernelInfos.Where(ki => (Boolean) (ki.CompiledSuccessfully ?? false));
				}

				await this.logger.LogAsync($"[200] api/opencl/kernel-infos: Retrieved {kernelInfos.Count()} kernel infos (onlyCompiled={onlyCompiled})", nameof(OpenClController));
				return this.Ok(kernelInfos);
			}
			catch (Exception ex)
			{
				await this.logger.LogExceptionAsync(ex, nameof(OpenClController));
				return this.StatusCode(500, new ProblemDetails
				{
					Status = 500,
					Title = "Internal Server Error",
					Detail = ex.Message
				});
			}
		}

		[HttpPost("execute-on-image")]
		[ProducesResponseType(typeof(ImageObjDto), 200)]
		[ProducesResponseType(typeof(ProblemDetails), 400)]
		[ProducesResponseType(typeof(ProblemDetails), 404)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<ActionResult<ImageObjDto>> ExecuteOnImageAsync([FromBody] ExecuteOnImageRequest request)
		{
			if (!this.openClService.Initialized)
			{
				await this.logger.LogAsync("[400] api/opencl/execute-on-image: OpenCL Not Initialized", nameof(OpenClController));
				return this.BadRequest(new ProblemDetails { Status = 400, Title = "OpenCL Not Initialized" });
			}

			if (request.ImageId == Guid.Empty && request.OptionalImage == null)
			{
				await this.logger.LogAsync("[400] api/opencl/execute-on-image: Either ImageId or OptionalImage required", nameof(OpenClController));
				return this.BadRequest(new ProblemDetails { Status = 400, Title = "Either ImageId or OptionalImage required" });
			}

			var img = this.imageCollection[request.ImageId];
			if (img == null)
			{
				if (request.OptionalImage == null)
				{
					await this.logger.LogAsync($"[404] api/opencl/execute-on-image: Image {request.ImageId} not found", nameof(OpenClController));
					return this.NotFound(new ProblemDetails { Status = 404, Title = $"Image {request.ImageId} not found" });
				}

				// Optionales Nachladen aus Base64
				if (string.IsNullOrWhiteSpace(request.OptionalImage.Data?.Base64Data))
				{
					await this.logger.LogAsync("[400] api/opencl/execute-on-image: OptionalImage missing Base64Data", nameof(OpenClController));
					return this.BadRequest(new ProblemDetails { Status = 400, Title = "OptionalImage missing Base64Data" });
				}

				var created = await ImageCollection.CreateFromBase64(request.OptionalImage.Data.Base64Data, request.Rescale, request.OptionalImage.Info?.Name);
				if (created == null)
				{
					await this.logger.LogAsync("[500] api/opencl/execute-on-image: Failed to create temp image from OptionalImage", nameof(OpenClController));
					return this.StatusCode(500, new ProblemDetails { Status = 500, Title = "Failed to create temp image" });
				}

				img = created;
			}

			var args = request.Arguments ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			args["width"] = img.Width.ToString();
			args["height"] = img.Height.ToString();

			var result = await this.openClService.ExecuteEditImage(img, request.KernelName, args);
			if (result == null)
			{
				await this.logger.LogAsync("[500] api/opencl/execute-on-image: OpenCL returned null image", nameof(OpenClController));
				return this.StatusCode(500, new ProblemDetails { Status = 500, Title = "Kernel execution failed" });
			}

			if (this.imageCollection.ServerSidedData)
			{
				if (!this.imageCollection.Add(result))
				{
					await this.logger.LogAsync("[500] api/opencl/execute-on-image: Failed to add result to collection, still returning result DTO", nameof(OpenClController));
					return this.StatusCode(500, new ProblemDetails { Status = 500, Title = "Add result to collection failed" });
				}
			}

			await this.logger.LogAsync($"[200] api/opencl/execute-on-image: Successfully executed kernel '{request.KernelName}' on image {img.Id}, new image {result.Id}", nameof(OpenClController));
			return this.Ok(new ImageObjDto(result));
		}

		[HttpPost("execute-create-image")]
		[ProducesResponseType(typeof(ImageObjDto), 200)]
		[ProducesResponseType(typeof(ProblemDetails), 400)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<ActionResult<ImageObjDto>> ExecuteCreateImageAsync([FromBody] CreateImageRequest request)
		{
			if (!this.openClService.Initialized)
			{
				await this.logger.LogAsync("[400] api/opencl/execute-create-image: OpenCL Not Initialized", nameof(OpenClController));
				return this.BadRequest(new ProblemDetails { Status = 400, Title = "OpenCL Not Initialized" });
			}

			if (request.Width <= 0 || request.Height <= 0)
			{
				await this.logger.LogAsync($"[400] api/opencl/execute-create-image: Invalid dimensions {request.Width}x{request.Height}", nameof(OpenClController));
				return this.BadRequest(new ProblemDetails { Status = 400, Title = "Invalid dimensions" });
			}

			var args = request.Arguments ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			args["width"] = request.Width.ToString();
			args["height"] = request.Height.ToString();

			var result = await this.openClService.ExecuteCreateImage(request.Width, request.Height, request.KernelName, request.BaseColorHex, args);
			if (result == null)
			{
				await this.logger.LogAsync("[500] api/opencl/execute-create-image: OpenCL returned null image", nameof(OpenClController));
				return this.StatusCode(500, new ProblemDetails { Status = 500, Title = "Kernel execution failed" });
			}

			if (this.imageCollection.ServerSidedData)
			{
				if (!this.imageCollection.Add(result))
				{
					await this.logger.LogAsync("[500] api/opencl/execute-create-image: Failed to add result to collection, still returning result DTO", nameof(OpenClController));
					return this.Ok(new ImageObjDto(result));
				}
			}

			return this.Ok(new ImageObjDto(result));
		}

		[HttpPost("execute-audio-timestretch")]
		[DisableRequestSizeLimit]
		[ProducesResponseType(typeof(AudioObjDto), 200)]
		[ProducesResponseType(typeof(ProblemDetails), 400)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<ActionResult<AudioObjDto>> ExecuteAudioTimestretchAsync([FromBody] AudioTimestretchRequest request)
		{
			if (!this.openClService.Initialized)
			{
				await this.logger.LogAsync("[400] api/opencl/execute-audio-timestretch: OpenCL Not Initialized", nameof(OpenClController));
				return this.BadRequest(new ProblemDetails { Status = 400, Title = "OpenCL Not Initialized" });
			}
			if (request.AudioId == Guid.Empty && request.OptionalAudio == null)
			{
				await this.logger.LogAsync("[400] api/opencl/execute-audio-timestretch: Either AudioId or OptionalAudio required", nameof(OpenClController));
				return this.BadRequest(new ProblemDetails { Status = 400, Title = "Either AudioId or OptionalAudio required" });
			}

			float initialBpm = 100.0f;
			try
			{
				AudioObj? audio = null;
				if (request.OptionalAudio != null)
				{
					initialBpm = request.InitialBpm;
					await this.logger.LogAsync("Creating temp audio from OptionalAudio..." + $" ({request.OptionalAudio.Data.Samples.LongLength} bytes, sr: {request.OptionalAudio.Info.SampleRate}, ch: {request.OptionalAudio.Info.Channels}, bits: {request.OptionalAudio.Info.BitDepth})", nameof(OpenClController));
					audio = await AudioCollection.CreateFromDataAsync(request.OptionalAudio.Data.Samples, request.OptionalAudio.Info.SampleRate, request.OptionalAudio.Info.Channels, request.OptionalAudio.Info.BitDepth);
					await this.logger.LogAsync("Created temp audio from OptionalAudio: " + (audio != null ? "Yes" : "No"), nameof(OpenClController));
				}
				else if (request.AudioId != Guid.Empty)
				{
					audio = this.audioCollection[request.AudioId];
					await this.logger.LogAsync($"Found audio {request.AudioId} in collection: " + (audio != null ? "Yes" : "No"), nameof(OpenClController));
					initialBpm = audio?.Bpm ?? 100.0f;
				}

				if (audio == null)
				{
					await this.logger.LogAsync("[500] api/opencl/execute-audio-timestretch: Failed to create temp audio from OptionalAudio", nameof(OpenClController));
					return this.StatusCode(500, new ProblemDetails { Status = 500, Title = "Failed to create temp audio" });
				}

				await audio.UpdateBpm(initialBpm);

				audio = await this.openClService.TimeStretch(audio, request.KernelName, "", request.SpeedFactor, request.ChunkSize, request.Overlap);
				if (audio == null)
				{
					await this.logger.LogAsync("[500] api/opencl/execute-audio-timestretch: OpenCL returned null audio", nameof(OpenClController));
					return this.StatusCode(500, new ProblemDetails { Status = 500, Title = "Kernel execution failed" });
				}

				float newFactor = (float) (initialBpm / request.SpeedFactor);
				await audio.UpdateBpm(newFactor);

				var dto = new AudioObjDto(audio, request.OptionalAudio != null);
				dto.Info.Name = (request.OptionalAudio?.Info.Name ?? dto.Info.Name) + "_stretched_" + request.SpeedFactor.ToString("F5");

				await this.logger.LogAsync($"Stretched audio successfully. {dto.Data.Length} bytes will be transferred. (Actual: {audio.Length} f32, {audio.Bpm:F3} BPM now)", nameof(OpenClController));
				return this.Ok(dto);
			}
			catch (Exception ex)
			{
				await this.logger.LogExceptionAsync(ex, nameof(OpenClController));
				return this.StatusCode(500, new ProblemDetails { Status = 500, Title = "Failed to create temp audio", Detail = ex.Message });
			}
		}

		[HttpPost("execute-on-audio")]
		[ProducesResponseType(typeof(AudioObjDto), 200)]
		[ProducesResponseType(typeof(ProblemDetails), 400)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<ActionResult<AudioObjDto>> ExecuteOnAudioAsync([FromBody] ExecuteOnAudioRequest request)
		{
			if (!this.openClService.Initialized)
			{
				await this.logger.LogAsync("[400] api/opencl/execute-on-audio: OpenCL Not Initialized", nameof(OpenClController));
				return this.BadRequest(new ProblemDetails { Status = 400, Title = "OpenCL Not Initialized" });
			}
			if (request.AudioId == Guid.Empty && request.OptionalAudio == null)
			{
				await this.logger.LogAsync("[400] api/opencl/execute-on-audio: Either AudioId or OptionalAudio required", nameof(OpenClController));
				return this.BadRequest(new ProblemDetails { Status = 400, Title = "Either AudioId or OptionalAudio required" });
			}

			try
			{
				AudioObj? audio = this.audioCollection[request.AudioId];
				if (audio == null && request.OptionalAudio != null)
				{
					audio = await AudioCollection.CreateFromDataAsync(request.OptionalAudio.Data.Samples, request.OptionalAudio.Info.SampleRate, request.OptionalAudio.Info.Channels, request.OptionalAudio.Info.BitDepth);
				}
				if (audio == null)
				{
					await this.logger.LogAsync("[500] api/opencl/execute-on-audio: Failed to create temp audio from OptionalAudio", nameof(OpenClController));
					return this.StatusCode(500, new ProblemDetails { Status = 500, Title = "Failed to create temp audio" });
				}

				var args = request.Arguments ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
				args["rate"] = audio.SampleRate.ToString();
				args["chan"] = audio.Channels.ToString();
				args["bit"] = audio.BitDepth.ToString();
				args["length"] = audio.Data.LongLength.ToString();

				var result = await this.openClService.ExecuteAudioKernel(audio, request.KernelName, "", request.ChunkSize, request.Overlap, args);
				if (result == null)
				{
					await this.logger.LogAsync("[500] api/opencl/execute-on-audio: OpenCL returned null audio", nameof(OpenClController));
					return this.StatusCode(500, new ProblemDetails { Status = 500, Title = "Kernel execution failed" });
				}

				var dto = new AudioObjDto(result);

				return this.Ok(dto);
			}
			catch (Exception ex)
			{
				await this.logger.LogExceptionAsync(ex, nameof(OpenClController));
				return this.StatusCode(500, new ProblemDetails { Status = 500, Title = "Failed to create temp audio", Detail = ex.Message });
			}
		}


		[HttpPost("timestretch-audio-file-download")]
		[Consumes("multipart/form-data")]
		[ProducesResponseType(typeof(FileContentResult), 200)]
		[ProducesResponseType(typeof(ProblemDetails), 400)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<IActionResult> TimestretchAudioFileAsync(IFormFile audioFile, [FromQuery] string kernelName = "timestretch_double03", [FromQuery] double speedFactor = 0.6667, [FromQuery] int chunkSize = 8192, [FromQuery] float overlap = 0.5f, [FromQuery] string format = "wav", [FromQuery] int bits = 24, [FromQuery] string? forceOpenClDevice = "Core")
		{
			if (!string.IsNullOrEmpty(forceOpenClDevice))
			{
				await Task.Run(() => this.openClService.Initialize(forceOpenClDevice));
			}

			if (!this.openClService.Initialized)
			{
				await this.logger.LogAsync("[400] api/opencl/timestretch-audio-file: OpenCL Not Initialized", nameof(OpenClController));
				return this.BadRequest(new ProblemDetails { Status = 400, Title = "OpenCL Not Initialized" });
			}
			if (audioFile == null || audioFile.Length == 0)
			{
				await this.logger.LogAsync("[400] api/opencl/timestretch-audio-file: No audio file uploaded", nameof(OpenClController));
				return this.BadRequest(new ProblemDetails { Status = 400, Title = "No audio file uploaded" });
			}

			overlap = Math.Clamp(overlap, 0.0f, 0.9f);
			speedFactor = Math.Clamp(speedFactor, 0.1, 10.0);
			if (string.IsNullOrWhiteSpace(kernelName))
			{
				kernelName = "timestretch_double03";
			}

			format = format.Trim('.').ToLowerInvariant();
			if (format != "wav" && format != "mp3")
			{
				format = "wav";
			}

			// Make ChunkSize a power of two
			int power = (int) Math.Round(Math.Log2(chunkSize));
			chunkSize = (int) Math.Pow(2, power);
			chunkSize = Math.Clamp(chunkSize, 128, 65536);

			string tempFilePath = string.Empty;
			string tempOutDir = string.Empty;
			string? outFile = null;
			string originalFileName = Path.GetFileName(audioFile.FileName) ?? audioFile.Name ?? "upload";

			try
			{
				// Temp upload file
				tempFilePath = Path.GetTempFileName();
				using (var stream = System.IO.File.Create(tempFilePath))
				{
					await audioFile.CopyToAsync(stream);
				}

				var audio = await this.audioCollection.ImportAsync(tempFilePath, originalFileName);
				if (audio == null || audio.Data.LongLength <= 0)
				{
					await this.logger.LogAsync("[400] api/opencl/timestretch-audio-file: Failed to import uploaded audio file", nameof(OpenClController));
					return this.BadRequest(new ProblemDetails { Status = 400, Title = "Failed to import uploaded audio file" });
				}

				var result = await this.openClService.TimeStretch(audio, kernelName, "", speedFactor, chunkSize, overlap);
				if (result == null || result.Data.LongLength <= 0)
				{
					await this.logger.LogAsync("[500] api/opencl/timestretch-audio-file: OpenCL returned null or empty audio. " + $"Data length is {result?.Data.Length}", nameof(OpenClController));
					return this.StatusCode(500, new ProblemDetails { Status = 500, Title = "Kernel execution failed", Detail = result?.ErrorMessage ?? "No result object returned." });
				}

				if (format == "mp3")
				{
					if (bits != 96 && bits != 128 && bits != 192 && bits != 256 && bits != 320)
					{
						bits = 192;
					}
				}
				else
				{
					format = "wav";
					if (bits != 16 && bits != 24 && bits != 32)
					{
						bits = 24;
					}
				}

				var baseName = string.IsNullOrWhiteSpace(result.Name) ? "audio" : result.Name.Replace("▶ ", "").Replace("|| ", "").Trim();
				if (string.IsNullOrWhiteSpace(baseName))
				{
					baseName = "audio";
				}

				// WICHTIG: AudioExporter erwartet ein Verzeichnis als outPath. Erzeuge ein temporäres Verzeichnis statt eine Datei.
				tempOutDir = Path.Combine(Path.GetTempPath(), "oocl_audio_" + Guid.NewGuid().ToString("N"));
				Directory.CreateDirectory(tempOutDir);

				if (format.Contains("3"))
				{
					outFile = await AudioExporter.ExportMp3Async(result, tempOutDir, bits);
				}
				else
				{
					outFile = await AudioExporter.ExportWavAsync(result, tempOutDir, bits);
				}

				if (string.IsNullOrWhiteSpace(outFile) || !System.IO.File.Exists(outFile))
				{
					await this.logger.LogAsync("[500] api/opencl/timestretch-audio-file: Export failed, outFile path invalid: " + (outFile ?? "null"), nameof(OpenClController));
					return this.StatusCode(500, new ProblemDetails
					{
						Title = "Error exporting audio as " + format,
						Detail = "The audio file could not be exported in the requested format. ('" + outFile + "')",
						Status = 500
					});
				}

				byte[] fileBytes = await System.IO.File.ReadAllBytesAsync(outFile);
				string fileName = Path.GetFileName(outFile) ?? $"{baseName} [{result.Bpm:F2}].{format}";
				string contentType = format.Contains("3") ? "audio/mpeg" : "audio/wav";
				return this.File(fileBytes, contentType, fileName);
			}
			catch (Exception ex)
			{
				await this.logger.LogExceptionAsync(ex, nameof(OpenClController));
				return this.StatusCode(500, new ProblemDetails { Status = 500, Title = "Failed to process uploaded audio", Detail = ex.Message });
			}
			finally
			{
				// Aufräumen temporärer Dateien/Verzeichnisse (best effort)
				try
				{
					if (!string.IsNullOrEmpty(tempFilePath) && System.IO.File.Exists(tempFilePath))
					{
						System.IO.File.Delete(tempFilePath);
					}
				}
				catch { }

				try
				{
					if (!string.IsNullOrEmpty(outFile) && System.IO.File.Exists(outFile))
					{
						System.IO.File.Delete(outFile);
					}

					if (!string.IsNullOrEmpty(tempOutDir) && Directory.Exists(tempOutDir))
					{
						// Versuche, das Verzeichnis zu löschen (nur wenn leer)
						try { Directory.Delete(tempOutDir, true); } catch { /* ignore */ }
					}
				}
				catch { }
			}
		}


		[HttpGet("test-execute-audio-timestretch-default")]
		[ProducesResponseType(typeof(IEnumerable<AudioObjInfo>), 200)]
		[ProducesResponseType(typeof(ProblemDetails), 400)]
		[ProducesResponseType(typeof(ProblemDetails), 404)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<ActionResult<IEnumerable<AudioObjInfo>>> TestExecuteAudioTimestretchResourcesDefaultAsync([FromQuery] int? forceInitializeOpenClDeviceIndex = 2, [FromQuery] bool? onlyTestFirstResourceFile = true, [FromQuery] double? overwriteStretchFactor = null)
		{
			try
			{
				if (!this.openClService.Initialized)
				{
					if (forceInitializeOpenClDeviceIndex.HasValue)
					{
						await Task.Run(() => this.openClService.Initialize(forceInitializeOpenClDeviceIndex.Value));
						await this.logger.LogAsync($"[200] api/opencl/test-execute-audio-timestretch-default: Forced OpenCL initialization with device index {forceInitializeOpenClDeviceIndex.Value}", nameof(OpenClController));
						if (!this.openClService.Initialized)
						{
							await this.logger.LogAsync("[400] api/opencl/test-execute-audio-timestretch-default: OpenCL Not Initialized after forced initialization", nameof(OpenClController));
							return this.BadRequest(new ProblemDetails { Status = 400, Title = "OpenCL Not Initialized after forced initialization" });
						}
					}
				}

				if (!this.openClService.Initialized)
				{
					await this.logger.LogAsync("[400] api/opencl/test-execute-audio-timestretch-default: OpenCL Not Initialized", nameof(OpenClController));
					return this.BadRequest(new ProblemDetails { Status = 400, Title = "OpenCL Not Initialized" });
				}

				// Build params for timestretch call in OpenClService
				string kernelName = "timestretch_double03";
				double speedFactor = (overwriteStretchFactor.HasValue && overwriteStretchFactor.Value > 0 ? overwriteStretchFactor.Value : 0.80);
				int chunkSize = 8192;
				float overlap = 0.5f;

				var loaded = await this.audioCollection.LoadFromResources();
				if (loaded == null || !loaded.Any())
				{
					await this.logger.LogAsync("[404] api/opencl/test-execute-audio-timestretch-default: No audio files found in resources", nameof(OpenClController));
					return this.NotFound(new ProblemDetails { Status = 404, Title = "No audio files found in resources" });
				}

				loaded = (onlyTestFirstResourceFile == true ? loaded.Take(1).ToList() : loaded.ToList()) ?? [];
				var results = new List<AudioObj>();
				foreach (var obj in loaded)
				{
					await this.logger.LogAsync($"Processing audio {obj.Id} ({obj.Name}, {obj.Length.ToString()} samples, {obj.SampleRate} Hz, {obj.Channels} channels, {obj.BitDepth} bit)", nameof(OpenClController));
					results.Add(await this.openClService.TimeStretch(obj, kernelName, "", speedFactor, chunkSize, overlap) ?? obj);
				}

				var infos = await Task.Run(() => results.Select(r => new AudioObjInfo(r)));

				return this.Ok(infos);
			}
			catch (Exception ex)
			{
				await this.logger.LogExceptionAsync(ex, nameof(OpenClController));
				return this.StatusCode(500, new ProblemDetails
				{
					Status = 500,
					Title = "Internal Server Error",
					Detail = ex.Message
				});
			}
		}

		[HttpGet("run-benchmark")]
		[ProducesResponseType(typeof(KernelBenchmarkResult), 200)]
		[ProducesResponseType(typeof(ProblemDetails), 400)]
		[ProducesResponseType(typeof(ProblemDetails), 404)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<ActionResult<KernelBenchmarkResult>> RunKernelBenchmarkAsync([FromQuery] string? kernelName = "benchmark00", [FromQuery] int iterations = 4096, [FromQuery] int opsPerIter = 4096, [FromQuery] string? deviceName = null)
		{
			try
			{
				if (!this.openClService.Initialized)
				{
					if (!string.IsNullOrWhiteSpace(deviceName))
					{
						await Task.Run(() => this.openClService.Initialize(deviceName));
						await this.logger.LogAsync($"[200] api/opencl/run-benchmark: Forced OpenCL initialization with device name '{deviceName}'", nameof(OpenClController));
						if (!this.openClService.Initialized)
						{
							await this.logger.LogAsync("[400] api/opencl/run-benchmark: OpenCL Not Initialized after forced initialization", nameof(OpenClController));
							return this.BadRequest(new ProblemDetails { Status = 400, Title = "OpenCL Not Initialized after forced initialization" });
						}
					}
				}
				if (!this.openClService.Initialized)
				{
					await this.logger.LogAsync("[400] api/opencl/run-benchmark: OpenCL Not Initialized", nameof(OpenClController));
					return this.BadRequest(new ProblemDetails { Status = 400, Title = "OpenCL Not Initialized" });
				}
				if (string.IsNullOrWhiteSpace(kernelName))
				{
					kernelName = "benchmark00";
				}
				
				string? kernelFile = this.openClService.Compiler?.KernelFiles.FirstOrDefault(kf => kf.Contains(kernelName, StringComparison.OrdinalIgnoreCase));
				if (string.IsNullOrWhiteSpace(kernelFile))
				{
					await this.logger.LogAsync($"[404] api/opencl/run-benchmark: Kernel '{kernelName}' not found", nameof(OpenClController));
					return this.NotFound(new ProblemDetails { Status = 404, Title = $"Kernel '{kernelName}' not found" });
				}

				Stopwatch sw = Stopwatch.StartNew();

				double? result = await this.openClService.RunBenchmark(kernelName, iterations, opsPerIter);
				if (!result.HasValue || result.Value <= 0)
				{
					await this.logger.LogAsync("[500] api/opencl/run-benchmark: Benchmark execution failed", nameof(OpenClController));
					return this.StatusCode(500, new ProblemDetails { Status = 500, Title = "Benchmark execution failed" });
				}

				sw.Stop();

				var dto = new KernelBenchmarkResult
				{
					KernelName = kernelName,
					DeviceName = this.openClService.GetDeviceEntries().ElementAt(this.openClService.Index),
					Score = result.Value,
					IterationsArg = iterations,
					OperationsPerIterationArg = opsPerIter,
					RawScore = null,
					Unit = "GFLOP/s",
					ExecutionTimeMs = sw.Elapsed.TotalMilliseconds,
					ErrorMessage = null
				};

				await this.logger.LogAsync($"[200] api/opencl/run-benchmark: Benchmark completed for kernel '{kernelName}' on device '{dto.DeviceName}' with score {dto.Score} {dto.Unit}", nameof(OpenClController));
				return this.Ok(dto);
			}
			catch (Exception ex)
			{
				await this.logger.LogExceptionAsync(ex, nameof(OpenClController));
				return this.StatusCode(500, new ProblemDetails
				{
					Status = 500,
					Title = "Internal Server Error",
					Detail = ex.Message
				});
			}
		}
	}
}