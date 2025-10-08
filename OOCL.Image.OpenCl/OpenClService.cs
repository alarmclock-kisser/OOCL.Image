using OOCL.Image.Core;
using OpenTK.Compute.OpenCL;
using System.Diagnostics;
using System.Numerics;
using System.Text;

namespace OOCL.Image.OpenCl
{
	public class OpenClService
	{
		// ----- Services ----- \\
		public OpenClRegister? Register { get; private set; }
		public OpenClCompiler? Compiler { get; private set; }
		public OpenClExecutioner? Executioner { get; private set; }

		// ----- Fields ----- \\
		private Dictionary<CLDevice, CLPlatform> devicesPlatforms = [];

		public int Index { get; private set; } = -1;
		private CLContext? context = null;
		private CLPlatform? platform = null;
		private CLDevice? device = null;

		// ----- Attributes ----- \\
		public int DeviceCount => this.devicesPlatforms.Count;
		public bool Initialized => this.context != null && this.Index >= 0 && this.Register != null && this.Compiler != null && this.Executioner != null;

		public CLResultCode lastError = CLResultCode.Success;

		// ----- Constructors ----- \\
		public OpenClService(int deviceIndex = -1)
		{
			this.GetDevicesPlatforms();

			if (deviceIndex > 0 && deviceIndex < this.DeviceCount)
			{
				this.Initialize(deviceIndex);
			}
		}

		public OpenClService(string? deviceName = null)
		{
			this.GetDevicesPlatforms();

			if (!string.IsNullOrEmpty(deviceName))
			{
				this.Initialize(deviceName);
			}	
		}

		// ----- Methods ----- \\
		public void Dispose()
		{
			this.Register?.Dispose();
			this.Register = null;
			this.Compiler?.Dispose();
			this.Compiler = null;
			this.Executioner?.Dispose();
			this.Executioner = null;

			if (this.context != null)
			{
				this.lastError = CL.ReleaseContext(this.context.Value);
			}
			this.context = null;
			this.device = null;
			this.platform = null;
			this.Index = -1;
		}

		private void GetDevicesPlatforms()
		{
			CLPlatform[] platforms = [];
			CL.GetPlatformIds(out platforms);

			for (int i = 0; i < platforms.Length; i++)
			{
				CLDevice[] devices = [];
				CL.GetDeviceIds(platforms[i], DeviceType.All, out devices);
				foreach (var device in devices)
				{
					if (!this.devicesPlatforms.ContainsKey(device))
					{
						this.devicesPlatforms.Add(device, platforms[i]);
					}
				}
			}
		}

		public string? GetDeviceInfo(int deviceId = -1, DeviceInfo info = DeviceInfo.Name)
		{
			// Verify device
			CLDevice? device = null;
			if (deviceId < 0)
			{
				device = this.device;
			}
			else if (deviceId >= 0 && deviceId < this.devicesPlatforms.Count)
			{
				device = this.devicesPlatforms.Keys.ElementAt(deviceId);
			}
			if (device == null)
			{
				return null;
			}

			this.lastError = CL.GetDeviceInfo(device.Value, info, out byte[] infoCode);
			if (this.lastError != CLResultCode.Success || infoCode == null || infoCode.LongLength == 0)
			{
				return null;
			}

			return Encoding.UTF8.GetString(infoCode).Trim('\0');
		}

		public string? GetPlatformInfo(int platformId = -1, PlatformInfo info = PlatformInfo.Name)
		{
			// Verify platform
			CLPlatform? platform = null;
			if (platformId < 0)
			{
				platform = this.platform;
			}
			else if (platformId >= 0 && platformId < this.devicesPlatforms.Count)
			{
				platform = this.devicesPlatforms.Values.ElementAt(platformId);
			}
			if (platform == null)
			{
				return null;
			}

			this.lastError = CL.GetPlatformInfo(platform.Value, info, out byte[] infoCode);
			if (this.lastError != CLResultCode.Success || infoCode == null || infoCode.LongLength == 0)
			{
				return null;
			}

			return Encoding.UTF8.GetString(infoCode).Trim('\0');
		}

		public IEnumerable<string> GetDeviceEntries()
		{
			int count = this.DeviceCount;
			List<string> entries = [];
			for (int i = 0; i < count; i++)
			{
				string device = this.GetDeviceInfo(i) ?? "N/A";
				string platform = this.GetPlatformInfo(i) ?? "N/A";

				entries.Add($"[{i}] {device} ({platform})");
			}

			return entries;
		}

		public void Initialize(int index = 0)
		{
			this.Dispose();

			this.GetDevicesPlatforms();

			if (index < 0 || index >= this.devicesPlatforms.Count)
			{
				return;
			}

			this.Index = index;
			this.device = this.devicesPlatforms.Keys.ElementAt(index);
			this.platform = this.devicesPlatforms.Values.ElementAt(index);

			this.context = CL.CreateContext(0, [this.device.Value], 0, IntPtr.Zero, out CLResultCode error);
			if (error != CLResultCode.Success || this.context == null)
			{
				this.lastError = error;
				return;
			}

			this.Register = new OpenClRegister(this.context.Value, this.device.Value);
			this.Compiler = new OpenClCompiler(this.context.Value, this.device.Value, this.Register);
			this.Executioner = new OpenClExecutioner(this.context.Value, this.device.Value, this.Register, this.Compiler);

			this.Index = index;
		}

		public void Initialize(string deviceName)
		{
			var deviceNames = this.devicesPlatforms.Keys
				.Select(d => this.GetDeviceInfo(this.devicesPlatforms.Keys.ToList().IndexOf(d), DeviceInfo.Name))
				.ToList();

			var foundDeviceName = deviceNames.FirstOrDefault(name =>
				!string.IsNullOrEmpty(name) && name.ToLower().Contains(deviceName.ToLower()));

			int index = -1;
			if (foundDeviceName != null)
			{
				index = deviceNames.IndexOf(foundDeviceName);
			}

			this.Initialize(index);
		}


		// ----- ACCESSORS ----- \\
		public async Task<ImageObj> MoveImage(ImageObj obj)
		{
			if (this.Register == null || this.Executioner == null)
			{
				return obj;
			}

			if (obj.Pointer == IntPtr.Zero)
			{
				var bytes = await obj.GetBytes();
				var mem = this.Register.PushData(bytes.ToArray());
				if (mem == null)
				{
					return obj;
				}

				obj.Pointer = mem.indexHandle;
			}
			else
			{
				var bytes = this.Register.PullData<byte>((nint) obj.Pointer);
				if (bytes == null || bytes.Length == 0)
				{
					return obj;
				}

				await obj.SetImageAsync(bytes);
			}

			return obj;
		}

		public async Task<ImageObj?> ExecuteCreateImage(int width, int height, string kernelName = "mandelbrot00", string colorHex = "#00000000", Dictionary<string, string>? arguments = null)
		{
			if (this.Executioner == null || this.Compiler == null || this.Register == null)
			{
				Console.WriteLine("CL-SER | ExecuteCreateImage #001: OpenCL-Services not initialized.");
				return null;
			}

			// Check if kernel exists
			if (string.IsNullOrEmpty(this.KernelExists(kernelName)))
			{
				Console.WriteLine($"CL-SER | ExecuteCreateImage #002: Kernel '{kernelName}' does not exist.");
				return null;
			}

			Stopwatch sw = Stopwatch.StartNew();

			// Exec call
			IntPtr result = await Task.Run(() =>
			{
				return this.Executioner.ExecuteGenericImageCreateKernel(kernelName, width, height, arguments);
			});

			// Check result pointer
			if (result == IntPtr.Zero)
			{
				Console.WriteLine($"CL-SERV | ExecuteCreateImage #003: Kernel '{kernelName}' execution failed or returned IntPtr.Zero.");
				return null;
			}

			// Pull data + verify warn if length mismatch
			byte[] bytes = this.Register.PullData<byte>(result);
			if (bytes == null || bytes.Length == 0)
			{
				Console.WriteLine($"CL-SERV | ExecuteCreateImage #004: Pulling data from kernel '{kernelName}' returned no or empty data.");
				return null;
			}
			long expectedLength = width * height * 4;
			if (bytes.Length != expectedLength)
			{
				Console.WriteLine($"CL-SERV | ExecuteCreateImage #005: Warning: Pulled data length ({bytes.Length}) does not match expected length ({expectedLength}).");
			}

			// Create new image with data
			string imageName = $"OOCL_{kernelName}_{Guid.NewGuid():N}";
			ImageObj imgObj = new(bytes, width, height, imageName);
			sw.Stop();
			imgObj.ElapsedProcessingTime = sw.Elapsed.TotalMilliseconds;

			// Free buffer
			this.Register.FreeBuffer(result);

			return imgObj;
		}

		public async Task<ImageObj?> ExecuteEditImage(ImageObj obj, string kernelName = "edgeDetection00", Dictionary<string, string>? arguments = null)
		{
			if (this.Executioner == null || this.Compiler == null || this.Register == null)
			{
				Console.WriteLine("CL-SER | ExecuteEditImage #001: OpenCL-Services not initialized.");
				return null;
			}

			int width = obj.Width;
			int height = obj.Height;

			// Check if kernel exists
			if (string.IsNullOrEmpty(this.KernelExists(kernelName)))
			{
				Console.WriteLine($"CL-SER | ExecuteEditImage #002: Kernel '{kernelName}' does not exist.");
				return null;
			}

			Stopwatch sw = Stopwatch.StartNew();

			// Push image if not already done
			IntPtr inputPointer = (nint) obj.Pointer;
			if (inputPointer == IntPtr.Zero)
			{
				var data = await obj.GetBytes(true);
				var mem = this.Register.PushData(data.ToArray());
				if (mem == null)
				{
					Console.WriteLine($"CL-SER | ExecuteEditImage #003: Pushing image data to OpenCL register failed.");
					return null;
				}
				inputPointer = mem.indexHandle; // Nicht obj.Pointer setzen!
			}

			// Kernel-Exec wie gehabt
			IntPtr result = await Task.Run(() =>
			{
				return this.Executioner.ExecuteGenericImageEditKernel((nint)inputPointer, kernelName, width, height, arguments);
			});

			// Neues ImageObj erzeugen
			byte[] bytes = this.Register.PullData<byte>(result);
			string imageName = $"OOCL_{kernelName}_{Guid.NewGuid():N}";
			ImageObj imgObj = new(bytes, width, height, imageName);
			sw.Stop();
			imgObj.ElapsedProcessingTime = sw.Elapsed.TotalMilliseconds;

			// Free buffer
			this.Register.FreeBuffer(result);

			return imgObj;
		}



		// Audio accessors
		public async Task<AudioObj> MoveAudio(AudioObj obj, int chunkSize = 16384, float overlap = 0.5f, bool keep = false)
		{
			if (this.Register == null)
			{
				Console.WriteLine("Memory Register is not initialized.");
				return obj;
			}

			Stopwatch sw = Stopwatch.StartNew();

			try
			{
				List<float[]> chunks = [];

				// -> Device
				if (obj.OnHost)
				{
					chunks = (await obj.GetChunks(chunkSize, overlap, keep)).ToList();
					if (chunks.Count <= 0)
					{
						Console.WriteLine("Failed to get audio chunks from AudioObj.");
						return obj;
					}

					var mem = this.Register.PushChunks<float>(chunks);
					if (mem == null)
					{
						Console.WriteLine("Failed to push audio chunks to OpenCL memory.");
						return obj;
					}

					obj["push"] = sw.Elapsed.TotalMilliseconds;

					long memIndexHandle = mem[0].Handle;
					if (memIndexHandle == 0)
					{
						Console.WriteLine("Failed to parse memory index handle.");
						return obj;
					}

					obj.Pointer = (nint) memIndexHandle;
				}
				else if (obj.OnDevice)
				{
					if (obj.Form == "c")
					{
						// obj.ComplexChunks = this.Register.PullChunks<Complex>(obj.Pointer);
					}
					else if (obj.Form == "f")
					{
						chunks = this.Register.PullChunks<float>(obj.Pointer);
						obj["pull"] = sw.Elapsed.TotalMilliseconds;

						await obj.AggregateStretchedChunks(chunks);
					}
				}
				else
				{
					Console.WriteLine("Error: AudioObj is neither on Host nor on Device.");
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine(ex);
				return obj;
			}
			finally
			{
				sw.Stop();
				await Task.Yield();
			}

			return obj;
		}

		public async Task<AudioObj> ExecuteAudioKernel(AudioObj obj, string kernelName = "normalize", string version = "00", int chunkSize = 0, float overlap = 0.0f, Dictionary<string, string>? providedArguments = null, bool log = false, IProgress<int>? progress = null)
		{
			// Check executioner
			if (this.Executioner == null)
			{
				obj.ErrorMessage = "Kernel executioner not initialized.";
				Console.WriteLine("Kernel executioner not initialized (Cannot execute audio kernel)");
				return obj;
			}

			// Optionally move audio to device
			bool moved = false;
			if (obj.OnHost)
			{
				await this.MoveAudio(obj, chunkSize, overlap);
				moved = true;
			}
			if (!obj.OnDevice)
			{
				obj.ErrorMessage = "AudioObj is not on device after moving.";
				return obj;
			}

			// Take time
			Stopwatch sw = Stopwatch.StartNew();

			// Execute kernel on device
			double factor = 1.0d;

			// Aggregate arguments
			Dictionary<string, string> optionalArgs = new()
			{
				{ "rate", obj.SampleRate.ToString() },
				{ "bit", obj.BitDepth.ToString() },
				{ "chan", obj.Channels.ToString() },
				{ "len", obj.Length.ToString()   }
			};

			if (progress == null)
			{
				obj.Pointer = this.Executioner.ExecuteAudioKernel(
					(nint) obj.Pointer,
					out factor, // Hier wird der 'out' Parameter für die synchrone Methode verwendet
					obj.Length,
					kernelName,
					version,
					chunkSize,
					overlap,
					obj.SampleRate,
					obj.BitDepth,
					obj.Channels,
					providedArguments
				);
			}
			else
			{
				// Die asynchrone Methode gibt ein Tupel zurück, das wir direkt entpacken.
				// Das 'out' Keyword wird hier entfernt.
				(obj.Pointer, factor) = await this.Executioner.ExecuteAudioKernelAsync(
					(nint) obj.Pointer,
					obj.Length,
					kernelName,
					version,
					chunkSize,
					overlap,
					obj.SampleRate,
					obj.BitDepth,
					obj.Channels,
					providedArguments,
					progress
				);
			}

			sw.Stop();
			obj["stretch"] = sw.Elapsed.TotalMilliseconds;

			if (obj.Pointer == IntPtr.Zero)
			{
				obj.ErrorMessage = "Failed to execute audio kernel: " + kernelName;
				Console.WriteLine("Failed to execute audio kernel: " + kernelName + " Pointer=" + obj.Pointer.ToString("X16"));
				return obj;
			}

			// Reload kernel
			this.Compiler?.LoadKernel(kernelName + version, "");

			// Log factor & set new bpm
			if (factor != 1.00f)
			{
				// IMPORTANT: Set obj Factor
				obj.StretchFactor = factor;
				await obj.UpdateBpm((float) (obj.Bpm / factor));
				// Console.WriteLine("Factor for audio kernel: " + factor + " Pointer=" + obj.Pointer.ToString("X16") + " BPM: " + obj.Bpm);
			}

			// Move back optionally
			if (moved && obj.OnDevice && obj.Form.StartsWith("f"))
			{
				await this.MoveAudio(obj, chunkSize, overlap);
				if (obj.OnDevice)
				{
					obj.ErrorMessage = "Failed to move AudioObj back to host. (Pointer is not zero after pulling)";
					return obj;
				}
			}

			return obj;
		}

		public async Task<AudioObj> PerformFFT(AudioObj obj, string version = "01", int chunkSize = 0, float overlap = 0.0f, bool keep = false)
		{
			// Optionally move audio to device
			bool moved = false;
			if (obj.OnHost)
			{
				await this.MoveAudio(obj, chunkSize, overlap, keep);
				moved = true;
			}
			if (!obj.OnDevice)
			{
				return obj;
			}

			Stopwatch sw = Stopwatch.StartNew();

			// Perform FFT on device
			obj.Pointer = this.Executioner?.ExecuteFFT((nint) obj.Pointer, version, obj.Form.FirstOrDefault(), chunkSize, overlap, true) ?? obj.Pointer;
			obj["fft"] = sw.Elapsed.TotalMilliseconds;

			if (obj.Pointer == IntPtr.Zero)
			{
				Console.WriteLine("Failed to perform FFT", "Pointer=" + obj.Pointer.ToString("X16"), 1);
			}
			else
			{
				obj.Form = obj.Form.StartsWith("f") ? "c" : "f";
			}

			if (moved)
			{
				// await this.MoveAudio(obj);
			}

			sw.Stop();

			return obj;
		}

		public async Task<AudioObj> TimeStretch(AudioObj obj, string kernelName = "timestretch_double", string version = "03", double factor = 1.000d, int chunkSize = 16384, float overlap = 0.5f, IProgress<int>? progress = null)
		{
			if (this.Executioner == null)
			{
				Console.WriteLine("Kernel executioner is not initialized.");
				return obj;
			}

			kernelName = kernelName + version;

			try
			{
				// Optionally move obj to device
				bool moved = false;
				if (obj.OnHost)
				{
					IntPtr pointer = (await this.MoveAudio(obj, chunkSize, overlap, false)).Pointer;
					if (pointer == IntPtr.Zero)
					{
						obj.ErrorMessage = "Failed to move AudioObj to device.";
						return obj;
					}
					moved = true;
				}

				// Get optional args
				Dictionary<string, string> optionalArgs;
				if (kernelName.ToLower().Contains("double"))
				{
					// Double kernel
					optionalArgs = new()
						{
							{ "factor", ((double) factor).ToString("F15")  },
						};
				}
				else
				{
					optionalArgs = new()
						{
							{ "factor", ((float) factor).ToString("F6")  },
						};
				}

				// Execute time stretch kernel
				var ptr = (await this.ExecuteAudioKernel(obj, kernelName, "", chunkSize, overlap, optionalArgs, true, progress)).Pointer;
				if (ptr == IntPtr.Zero)
				{
					obj.ErrorMessage = obj.ErrorMessage + " (Failed to execute time-stretch kernel: " + kernelName + ")";
					return obj;
				}

				// Optionally move obj back to host
				if (moved && obj.OnDevice)
				{
					IntPtr resultPointer = (await this.MoveAudio(obj, chunkSize, overlap)).Pointer;
					if (resultPointer != IntPtr.Zero)
					{
						obj.ErrorMessage = "Failed to move AudioObj back to host. (Pointer is not zero after pulling)";
						return obj;
					}
				}
			}
			catch (Exception ex)
			{
				obj.ErrorMessage = ex.Message;
				Console.WriteLine(ex);
			}
			finally
			{
				await Task.Yield();
			}

			return obj;
		}



		// API Accessors
		public string? KernelExists(string kernelName, bool fuzzyMatch = false)
		{
			if (this.Compiler == null)
			{
				return null;
			}

			if (fuzzyMatch)
			{
				return this.Compiler.KernelNames.FirstOrDefault(n => n.ToLower().Contains(kernelName.ToLower()));
			}
			else
			{
				return this.Compiler.KernelNames.FirstOrDefault(n => n.ToLower() == kernelName.ToLower());
			}
		}

		public string GetDeviceType(int index)
		{
			return this.GetDeviceInfo(index, DeviceInfo.Type) ?? "N/A";
		}

		public async Task<string> CompileKernelStringAsync(string kernelString)
		{
			if (this.Compiler == null)
			{
				return "Compiler not initialized.";
			}
			

			string kernelNameOrBuildLog = await this.Compiler.TryCopileKernelFromStringAsync(kernelString);

			return kernelNameOrBuildLog;
		}


		public async Task<object[]?> ExecuteGenericDataKernelAsync(string? kernelName, string? kernelCode, string[] argTypes, string[] argNames, string[] argValues, int workDimensions, string? inputDataBase64, string? inputDataType, string outputDataType, string outputDataLength, int? openClDeviceIndex, string? openClDeviceName)
		{
			if (!this.Initialized)
			{
				if (openClDeviceIndex != null && openClDeviceIndex >= 0 && openClDeviceIndex < this.DeviceCount)
				{
					this.Initialize(openClDeviceIndex.Value);
				}
				else if (!string.IsNullOrEmpty(openClDeviceName))
				{
					this.Initialize(openClDeviceName);
				}
			}

			if (this.Executioner == null || this.Compiler == null || this.Register == null)
			{
				Console.WriteLine("CL-SER | ExecuteGenericDataKernel #000: OpenCL-Services not initialized.");
				return [];
			}

			CLKernel? kernel = null;
			if (kernelName == null)
			{
				if (string.IsNullOrEmpty(kernelCode))
				{
					Console.WriteLine("CL-SER | ExecuteGenericDataKernel #001: No kernel name or code provided.");
					return [];
				}
				kernel = await this.Compiler.CompileKernelFromStringAsync(kernelCode, true);
				if (kernel == null)
				{
					Console.WriteLine("CL-SER | ExecuteGenericDataKernel #002: Kernel compilation failed.");
					return [];
				}
			}
			else
			{
				if (string.IsNullOrEmpty(this.KernelExists(kernelName)))
				{
					if (!string.IsNullOrEmpty(kernelCode))
					{
						kernel = await this.Compiler.CompileKernelFromStringAsync(kernelCode, true);
						if (kernel == null)
						{
							Console.WriteLine($"CL-SER | ExecuteGenericDataKernel #003: Kernel '{kernelName}' compilation failed.");
							return [];
						}
					}
					else
					{
						Console.WriteLine($"CL-SER | ExecuteGenericDataKernel #004: Kernel '{kernelName}' does not exist.");
						return [];
					}
				}
			}

			// Input-Daten dekodieren (falls vorhanden)
			object[]? typedData = null;
			if (!string.IsNullOrWhiteSpace(inputDataBase64) && !string.IsNullOrWhiteSpace(inputDataType))
			{
				switch (inputDataType.ToLowerInvariant())
				{
					case "double":
						typedData = (await ConvertStringToTypeAsync<double>(inputDataBase64)).Cast<object>().ToArray();
						break;
					case "float":
					case "single":
						typedData = (await ConvertStringToTypeAsync<float>(inputDataBase64)).Cast<object>().ToArray();
						break;
					case "int":
					case "int32":
						typedData = (await ConvertStringToTypeAsync<int>(inputDataBase64)).Cast<object>().ToArray();
						break;
					case "byte":
					case "uint8":
						typedData = (await ConvertStringToTypeAsync<byte>(inputDataBase64)).Cast<object>().ToArray();
						break;
					default:
						Console.WriteLine("CL-SER | Unsupported inputDataType: " + inputDataType);
						break;
				}
			}

			// Pointer-Argumente herausfiltern (werden automatisch gesetzt)
			Dictionary<string, string> scalarArgs = [];
			for (int i = 0; i < Math.Min(argTypes.Length, Math.Min(argNames.Length, argValues.Length)); i++)
			{
				string t = argTypes[i].Trim().ToLowerInvariant();
				if (t.Contains("*"))
				{
					continue; // input / output pointer NICHT setzen
				}
				scalarArgs[argNames[i]] = argValues[i];
			}

			long outputLength = long.TryParse(outputDataLength, out long len) ? len : 0;
			if (outputLength <= 0)
			{
				Console.WriteLine("CL-SER | ExecuteGenericDataKernel #005: Invalid output length.");
				return [];
			}

			// Kernel ausführen je nach Output-Typ
			switch (outputDataType.ToLowerInvariant())
			{
				case "int":
				case "int32":
					{
						var data = await this.Executioner.ExecuteGenericKernelSingleAsync<int>(kernel, typedData, inputDataType, outputLength, scalarArgs, workDimensions);
						return new object[] { data };
					}
				case "double":
					{
						var data = await this.Executioner.ExecuteGenericKernelSingleAsync<double>(kernel, typedData, inputDataType, outputLength, scalarArgs, workDimensions);
						return new object[] { data };
					}
				case "float":
				case "single":
					{
						var data = await this.Executioner.ExecuteGenericKernelSingleAsync<float>(kernel, typedData, inputDataType, outputLength, scalarArgs, workDimensions);
						return new object[] { data };
					}
				case "byte":
				case "bytes":
				case "uint8":
					{
						var data = await this.Executioner.ExecuteGenericKernelSingleAsync<byte>(kernel, typedData, inputDataType, outputLength, scalarArgs, workDimensions);
						return new object[] { data };
					}
				default:
					Console.WriteLine("CL-SER | ExecuteGenericDataKernel #006: Unsupported outputDataType: " + outputDataType);
					return [];
			}
		}

		public async Task<Vector2[]?> ExecuteFftAsync(float[] floats)
		{
			Vector2[]? result = null;

			if (!this.Initialized || this.Executioner == null || this.Compiler == null || this.Register == null)
			{
				Console.WriteLine("CL-SER | ExecuteFftAsync #000: OpenCL-Services not initialized.");
				return result;
			}

			Stopwatch sw = Stopwatch.StartNew();

			// Push data
			var mem = this.Register.PushData(floats);
			if (mem == null)
			{
				Console.WriteLine("CL-SER | ExecuteFftAsync #001: Pushing data to OpenCL register failed.");
				return result;
			}

			// Exec FFT
			var pointer = await this.Executioner.ExecuteFFTAsync((nint) mem.indexHandle, "01", 'f', floats.Length, 0.0f);
			if (pointer == IntPtr.Zero)
			{
				Console.WriteLine("CL-SER | ExecuteFftAsync #002: Executing FFT kernel failed.");
				return result;
			}

			// Pull data
			result = this.Register.PullData<Vector2>(pointer);
			if (result == null || result.Length == 0)
			{
				Console.WriteLine("CL-SER | ExecuteFftAsync #003: Pulling data from OpenCL register failed.");
				return result;
			}

			// Free buffers
			this.Register.FreeBuffer((nint) mem.indexHandle);
			this.Register.FreeBuffer(pointer);
			sw.Stop();

			Console.WriteLine($"CL-SER | ExecuteFftAsync completed in {sw.Elapsed.TotalMilliseconds:F2} ms. Result length: {result.Length}");

			return result;
		}

		public async Task<IEnumerable<Vector2[]>?> ExecuteFftBulkAsync(IEnumerable<float[]> floatChunks, float overlap = 0.0f)
		{
			if (!this.Initialized || this.Executioner == null || this.Compiler == null || this.Register == null)
			{
				Console.WriteLine("CL-SER | ExecuteFftBulkAsync #000: OpenCL-Services not initialized.");
				return null;
			}

			List<Vector2[]> results = [];
			Stopwatch swTotal = Stopwatch.StartNew();

			int chunkSize = floatChunks.FirstOrDefault()?.Length ?? 0;

			// Push chunks
			var mem = this.Register.PushChunks<float>(floatChunks.ToList());
			if (mem == null || mem.GetCount() <= 0)
			{
				Console.WriteLine("CL-SER | ExecuteFftBulkAsync #001: Pushing chunks to OpenCL register failed.");
				return null;
			}

			// Execute FFT for each chunk
			IntPtr pointer = await this.Executioner.ExecuteFFTAsync(mem.indexHandle, "01", 'f', chunkSize, overlap);
			if (pointer == IntPtr.Zero)
			{
				Console.WriteLine("CL-SER | ExecuteFftBulkAsync #002: Executing FFT kernel failed.");
				return null;
			}

			// Pull chunks
			results = this.Register.PullChunks<Vector2>(pointer).ToList();
			if (results == null || results.Count == 0)
			{
				Console.WriteLine("CL-SER | ExecuteFftBulkAsync #002: Executing FFT kernel failed.");
				return null;
			}

			// Free buffers
			this.Register.FreeBuffer((nint) mem.indexHandle);
			
			swTotal.Stop();
			Console.WriteLine($"CL-SER | ExecuteFftBulkAsync completed in {swTotal.Elapsed.TotalMilliseconds:F2} ms. Result chunks: {results.Count}, Chunk size: {chunkSize}");

			return results;
		}

		public async Task<float[]?> ExecuteIfftAsync(Vector2[] complexes)
		{
			float[]? result = null;
			if (!this.Initialized || this.Executioner == null || this.Compiler == null || this.Register == null)
			{
				Console.WriteLine("CL-SER | ExecuteIfftAsync #000: OpenCL-Services not initialized.");
				return result;
			}

			Stopwatch sw = Stopwatch.StartNew();
			
			// Push data
			var mem = this.Register.PushData(complexes);
			if (mem == null)
			{
				Console.WriteLine("CL-SER | ExecuteIfftAsync #001: Pushing data to OpenCL register failed.");
				return result;
			}
			
			// Exec IFFT
			var pointer = await this.Executioner.ExecuteFFTAsync((nint) mem.indexHandle, "01", 'c', complexes.Length, 0.0f);
			if (pointer == IntPtr.Zero)
			{
				Console.WriteLine("CL-SER | ExecuteIfftAsync #002: Executing IFFT kernel failed.");
				return result;
			}
			
			// Pull data
			result = this.Register.PullData<float>(pointer);
			if (result == null || result.Length == 0)
			{
				Console.WriteLine("CL-SER | ExecuteIfftAsync #003: Pulling data from OpenCL register failed.");
				return result;
			}
			
			// Free buffers
			this.Register.FreeBuffer((nint) mem.indexHandle);
			this.Register.FreeBuffer(pointer);
			
			sw.Stop();
			
			Console.WriteLine($"CL-SER | ExecuteIfftAsync completed in {sw.Elapsed.TotalMilliseconds:F2} ms. Result length: {result.Length}");
			
			return result;
		}

		public async Task<IEnumerable<float[]>?> ExecuteIfftBulkAsync(IEnumerable<Vector2[]> complexChunks, float overlap = 0.0f)
		{
			if (!this.Initialized || this.Executioner == null || this.Compiler == null || this.Register == null)
			{
				Console.WriteLine("CL-SER | ExecuteIfftBulkAsync #000: OpenCL-Services not initialized.");
				return null;
			}
			
			List<float[]> results = [];
			Stopwatch swTotal = Stopwatch.StartNew();
			int chunkSize = complexChunks.FirstOrDefault()?.Length ?? 0;
			
			// Push chunks
			var mem = this.Register.PushChunks<Vector2>(complexChunks.ToList());
			if (mem == null || mem.GetCount() <= 0)
			{
				Console.WriteLine("CL-SER | ExecuteIfftBulkAsync #001: Pushing chunks to OpenCL register failed.");
				return null;
			}
			
			// Execute IFFT for each chunk
			IntPtr pointer = await this.Executioner.ExecuteFFTAsync(mem.indexHandle, "01", 'c', chunkSize, overlap);
			if (pointer == IntPtr.Zero)
			{
				Console.WriteLine("CL-SER | ExecuteIfftBulkAsync #002: Executing IFFT kernel failed.");
				return null;
			}
			
			// Pull chunks
			results = this.Register.PullChunks<float>(pointer).ToList();
			if (results == null || results.Count == 0)
			{
				Console.WriteLine("CL-SER | ExecuteIfftBulkAsync #003: Pulling chunks from OpenCL register failed.");
				return null;
			}
			
			// Free buffers
			this.Register.FreeBuffer((nint) mem.indexHandle);
			
			swTotal.Stop();
			Console.WriteLine($"CL-SER | ExecuteIfftBulkAsync completed in {swTotal.Elapsed.TotalMilliseconds:F2} ms. Result chunks: {results.Count}, Chunk size: {chunkSize}");
			
			return results;
		}



		public static async Task<T[]> ConvertStringToTypeAsync<T>(string? base64Data, int parallelThresholdChars = 16_000_000, int? maxDegreeOfParallelism = null, bool ignoreRemainderBytes = true) where T : unmanaged
		{
			if (string.IsNullOrWhiteSpace(base64Data))
			{
				return [];
			}

			try
			{
				// 1) Base64 Decode (synchron, CPU-bound) – optional auslagern
				// Bei extrem großen Strings im Hintergrund-Thread decodieren
				byte[] raw = base64Data.Length > parallelThresholdChars
					? await Task.Run(() => Convert.FromBase64String(base64Data))
					: Convert.FromBase64String(base64Data);

				if (typeof(T) == typeof(byte))
				{
					return (T[]) (object) raw; // Direkt zurück (zero-copy)
				}

				int typeSize = System.Runtime.InteropServices.Marshal.SizeOf<T>();
				if (raw.Length < typeSize)
				{
					return [];
				}

				int elementCount = raw.Length / typeSize;
				int usableBytes = elementCount * typeSize;

				if (!ignoreRemainderBytes && usableBytes != raw.Length)
				{
					throw new InvalidOperationException($"Rohdatenlänge ({raw.Length}) nicht durch Elementgröße ({typeSize}) teilbar.");
				}

				// Klein? -> Singlethread + Array.Copy + Buffer.BlockCopy
				if (base64Data.Length < parallelThresholdChars)
				{
					byte[] usableRaw = new byte[usableBytes];
					Array.Copy(raw, 0, usableRaw, 0, usableBytes);
					T[] result = new T[elementCount];
					Buffer.BlockCopy(usableRaw, 0, result, 0, usableBytes);
					return result;
				}

				// Groß: Parallel kopieren (ohne unsafe)
				T[] resultLarge = new T[elementCount];

				// Partitionierung
				int logical = maxDegreeOfParallelism ?? Environment.ProcessorCount;
				int partitionSizeElements = Math.Max(elementCount / (logical * 4), 1024); // Mindestens 1024 Elemente pro Partition
				int partitionSizeBytes = partitionSizeElements * typeSize;

				var ranges = new List<(int byteStart, int byteLen)>();
				for (int offset = 0; offset < usableBytes; offset += partitionSizeBytes)
				{
					int len = Math.Min(partitionSizeBytes, usableBytes - offset);
					ranges.Add((offset, len));
				}

				var po = new ParallelOptions
				{
					MaxDegreeOfParallelism = logical
				};

				Parallel.ForEach(ranges, po, range =>
				{
					int elements = range.byteLen / typeSize;
					int destIndex = range.byteStart / typeSize;
					Buffer.BlockCopy(raw, range.byteStart, resultLarge, destIndex * typeSize, elements * typeSize);
				});

				return resultLarge;
			}
			catch (FormatException)
			{
				return [];
			}
			catch (Exception ex)
			{
				Console.WriteLine("ConvertStringToTypeAsync error: " + ex.Message);
				return [];
			}
		}

		public static Task<object[]> ConvertStringToTypeAsync(string? base64Data, string typeName)
		{
			return typeName.ToLower() switch
			{
				"byte" or "bytes" => ConvertStringToTypeAsync<byte>(base64Data).ContinueWith(t => t.Result.Cast<object>().ToArray()),
				"sbyte" => ConvertStringToTypeAsync<sbyte>(base64Data).ContinueWith(t => t.Result.Cast<object>().ToArray()),
				"short" or "int16" => ConvertStringToTypeAsync<short>(base64Data).ContinueWith(t => t.Result.Cast<object>().ToArray()),
				"ushort" or "uint16" => ConvertStringToTypeAsync<ushort>(base64Data).ContinueWith(t => t.Result.Cast<object>().ToArray()),
				"int" or "int32" => ConvertStringToTypeAsync<int>(base64Data).ContinueWith(t => t.Result.Cast<object>().ToArray()),
				"uint" or "uint32" => ConvertStringToTypeAsync<uint>(base64Data).ContinueWith(t => t.Result.Cast<object>().ToArray()),
				"long" or "int64" => ConvertStringToTypeAsync<long>(base64Data).ContinueWith(t => t.Result.Cast<object>().ToArray()),
				"ulong" or "uint64" => ConvertStringToTypeAsync<ulong>(base64Data).ContinueWith(t => t.Result.Cast<object>().ToArray()),
				"float" or "single" => ConvertStringToTypeAsync<float>(base64Data).ContinueWith(t => t.Result.Cast<object>().ToArray()),
				"double" => ConvertStringToTypeAsync<double>(base64Data).ContinueWith(t => t.Result.Cast<object>().ToArray()),
				_ => Task.FromResult<object[]>([])
			};
		}

	}
}
