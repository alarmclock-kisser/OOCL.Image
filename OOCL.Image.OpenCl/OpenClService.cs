using OOCL.Image.Core;
using OpenTK.Compute.OpenCL;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

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
	}
}
