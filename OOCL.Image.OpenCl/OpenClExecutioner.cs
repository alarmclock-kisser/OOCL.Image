using OpenTK.Compute.OpenCL;
using OpenTK.Mathematics;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace OOCL.Image.OpenCl
{
	public class OpenClExecutioner
	{
		// ----- Services ----- \\
		private OpenClRegister register;

		// ----- Fields ----- \\
		private CLContext context;
		private CLDevice device;
		private CLCommandQueue queue => this.register.Queue;
		private OpenClCompiler compiler;

		private CLKernel? Kernel => this.compiler.Kernel;
		private string KernelFile => this.compiler.KernelFile;

		// ----- Attributes ----- \\
		public CLResultCode lastError = CLResultCode.Success;




		// ----- Constructor ----- \\
		public OpenClExecutioner(CLContext context, CLDevice device, OpenClRegister register, OpenClCompiler compiler)
		{
			this.context = context;
			this.device = device;
			this.register = register;
			this.compiler = compiler;
		}

		// ----- Methods ----- \\
		public void Dispose()
		{
			// Free

		}


		// Exec
		public IntPtr ExecuteMandelbrotKernel(string kernel, int width, int height, object zoom, object xOffset, object yOffset, int iter, int red, int green, int blue)
		{
			// Load kernel
			string? kernelFile = this.compiler.KernelFiles.FirstOrDefault(f => f.ToLower().Contains(kernel.ToLower()));
			if (string.IsNullOrEmpty(kernelFile))
			{
				return IntPtr.Zero;
			}
			this.compiler.LoadKernel("", kernelFile);
			if (this.Kernel == null)
			{
				return IntPtr.Zero;
			}

			// Create output buffer
			IntPtr outputBufferSize = (IntPtr) (width * height * 4);
			var outputBuffer = this.register.AllocateSingle<byte>(outputBufferSize)?.GetBuffers().FirstOrDefault();
			if (outputBuffer == null)
			{
				return IntPtr.Zero;
			}

			// Set kernel arguments
			this.SetKernelArgSafe(0, outputBuffer);
			this.SetKernelArgSafe(1, width);
			this.SetKernelArgSafe(2, height);
			this.SetKernelArgSafe(3, zoom);
			this.SetKernelArgSafe(4, xOffset);
			this.SetKernelArgSafe(5, yOffset);
			this.SetKernelArgSafe(6, iter);
			this.SetKernelArgSafe(7, red);
			this.SetKernelArgSafe(8, green);
			this.SetKernelArgSafe(9, blue);

			// Dimensions
			int pixelsTotal = width * height * 4; // Anzahl der Pixel
			int workWidth = width > 0 ? width : pixelsTotal; // Falls kein width gegeben, 1D
			int workHeight = height > 0 ? height : 1;        // Falls kein height, 1D

			// Work dimensions
			uint workDim = (width > 0 && height > 0) ? 2u : 1u;
			UIntPtr[] globalWorkSize = workDim == 2
				? [(UIntPtr) workWidth, (UIntPtr) workHeight]
				: [(UIntPtr) pixelsTotal];

			// Execute kernel
			CLResultCode error = CL.EnqueueNDRangeKernel(
				this.queue,
				this.Kernel.Value,
				workDim,          // 1D oder 2D
				null,             // Kein Offset
				globalWorkSize,   // Work-Größe in Pixeln
				null,             // Lokale Work-Size (automatisch)
				0, null, out CLEvent evt
			);
			if (error != CLResultCode.Success)
			{
				this.lastError = error;
				return IntPtr.Zero;
			}

			// Wait for completion
			error = CL.WaitForEvents(1, [@evt]);
			if (error != CLResultCode.Success)
			{
				this.lastError = error;
				return IntPtr.Zero;
			}

			// Return output buffer pointer
			return outputBuffer.Value.Handle;
		}


		public IntPtr ExecuteGenericImageCreateKernel(string kernel = "mandelbrot00", int width = 720, int height = 480, Dictionary<string, string>? providedArguments = null)
		{
			// Load kernel
			string? kernelFile = this.compiler.KernelFiles.FirstOrDefault(f => f.ToLower().Contains(kernel.ToLower()));
			if (string.IsNullOrEmpty(kernelFile))
			{
				Console.WriteLine("CL-EXEC | ExecuteGenericImageCreateKernel #001: No kernel file found with name: '" + kernel + "'");
				return IntPtr.Zero;
			}
			this.compiler.LoadKernel("", kernelFile);
			if (this.Kernel == null)
			{
				Console.WriteLine("CL-EXEC | ExecuteGenericImageCreateKernel #002: Kernel is null after loading file: '" + kernelFile + "'");
				return IntPtr.Zero;
			}

			// Create output buffer
			IntPtr outputBufferSize = (IntPtr) (width * height * 4);
			var outputBuffer = this.register.AllocateSingle<byte>(outputBufferSize)?.GetBuffers().FirstOrDefault();
			if (outputBuffer == null)
			{
				Console.WriteLine("CL-EXEC | ExecuteGenericImageCreateKernel #003: Output buffer is null after allocation with 1D size: " + outputBufferSize);
				return IntPtr.Zero;
			}

			// Set kernel arguments dynamically:
			var args = this.compiler.Arguments;
			if (args == null || args.Count == 0)
			{
				Console.WriteLine("CL-EXEC | ExecuteGenericImageCreateKernel #004: No argument definitions found for kernel: '" + kernel + "' in file: '" + kernelFile + "'");
				return IntPtr.Zero;
			}

			// Check / set providedArguments width and height
			if (providedArguments == null)
			{
				providedArguments = [];
			}
			if (!providedArguments.ContainsKey("width"))
			{
				providedArguments["width"] = width.ToString();
			}
			if (!providedArguments.ContainsKey("height"))
			{
				providedArguments["height"] = height.ToString();
			}

			object[] sortedArgs = this.MergeImageKernelArgumentsDynamic(kernel, null, outputBuffer, providedArguments);
			for (uint i = 0; i < args.Count; i++)
			{
				var arg = sortedArgs[i];
				var err = this.SetKernelArgSafe(i, arg);
				if (err != CLResultCode.Success)
				{
					this.lastError = err;
					Console.WriteLine("CL-EXEC | ExecuteGenericImageCreateKernel #005: Error setting kernel argument at index " + i + " of Type: '" + arg.GetType().Name + "' for kernel: '" + kernel + "'. Error: " + err);
					return IntPtr.Zero;
				}
			}

			// Dimensions
			int pixelsTotal = width * height * 4; // Anzahl der Pixel
			int workWidth = width > 0 ? width : pixelsTotal; // Falls kein width gegeben, 1D
			int workHeight = height > 0 ? height : 1;        // Falls kein height, 1D

			// Work dimensions
			uint workDim = (width > 0 && height > 0) ? 2u : 1u;
			UIntPtr[] globalWorkSize = workDim == 2
				? [(UIntPtr) workWidth, (UIntPtr) workHeight]
				: [(UIntPtr) pixelsTotal];

			// Execute kernel
			CLResultCode error = CL.EnqueueNDRangeKernel(
				this.queue,
				this.Kernel.Value,
				workDim,          // 1D oder 2D
				null,             // Kein Offset
				globalWorkSize,   // Work-Größe in Pixeln
				null,             // Lokale Work-Size (automatisch)
				0, null, out CLEvent evt
			);
			if (error != CLResultCode.Success)
			{
				this.lastError = error;
				Console.WriteLine("CL-EXEC | ExecuteGenericImageCreateKernel #006: Error enqueuing NDRange kernel for kernel: '" + kernel + "'. Error: " + error);
				return IntPtr.Zero;
			}

			// Wait for completion
			error = CL.WaitForEvents(1, [@evt]);
			if (error != CLResultCode.Success)
			{
				this.lastError = error;
				Console.WriteLine("CL-EXEC | ExecuteGenericImageCreateKernel #007: Error waiting for events after executing kernel: '" + kernel + "'. Error: " + error);
				return IntPtr.Zero;
			}

			// Return output buffer pointer
			return outputBuffer.Value.Handle;
		}

		public IntPtr ExecuteGenericImageEditKernel(IntPtr inputPointer, string kernel = "edgeDetection00", int width = 720, int height = 480, Dictionary<string, string>? providedArguments = null)
		{
			// Load kernel
			string? kernelFile = this.compiler.KernelFiles.FirstOrDefault(f => f.ToLower().Contains(kernel.ToLower()));
			if (string.IsNullOrEmpty(kernelFile))
			{
				Console.WriteLine("CL-EXEC | ExecuteGenericImageEditKernel #001: No kernel file found with name: '" + kernel + "'");
				return IntPtr.Zero;
			}
			this.compiler.LoadKernel("", kernelFile);
			if (this.Kernel == null)
			{
				Console.WriteLine("CL-EXEC | ExecuteGenericImageEditKernel #0020: Kernel is null after loading file: '" + kernelFile + "'");
				return IntPtr.Zero;
			}

			// Get input buffer from pointer
			var inputBuffer = this.register.GetBuffer(inputPointer)?.GetBuffers().FirstOrDefault();
			if (inputBuffer == null)
			{
				Console.WriteLine("CL-EXEC | ExecuteGenericImageEditKernel #0031: Input buffer is null for pointer: " + inputPointer);
				return IntPtr.Zero;
			}

			// Create output buffer
			IntPtr outputBufferSize = (IntPtr) (width * height * 4);
			var outputBuffer = this.register.AllocateSingle<byte>(outputBufferSize)?.GetBuffers().FirstOrDefault();
			if (outputBuffer == null)
			{
				Console.WriteLine("CL-EXEC | ExecuteGenericImageEditKernel #0032: Output buffer is null after allocation with 1D size: " + outputBufferSize);
				return IntPtr.Zero;
			}

			// Set kernel arguments dynamically:
			var args = this.compiler.Arguments;
			if (args == null || args.Count == 0)
			{
				Console.WriteLine("CL-EXEC | ExecuteGenericImageEditKernel #004: No argument definitions found for kernel: '" + kernel + "' in file: '" + kernelFile + "'");
				return IntPtr.Zero;
			}

			object[] sortedArgs = this.MergeImageKernelArgumentsDynamic(kernel, inputBuffer, outputBuffer, providedArguments);
			for (uint i = 0; i < args.Count; i++)
			{
				var arg = sortedArgs[i];
				var err = this.SetKernelArgSafe(i, arg);
				if (err != CLResultCode.Success)
				{
					this.lastError = err;
					Console.WriteLine("CL-EXEC | ExecuteGenericImageEditKernel #005: Error setting kernel argument at index " + i + " of Type: '" + arg.GetType().Name + "' for kernel: '" + kernel + "'. Error: " + err);
					return IntPtr.Zero;
				}
			}

			// Dimensions
			int pixelsTotal = width * height * 4; // Anzahl der Pixel
			int workWidth = width > 0 ? width : pixelsTotal; // Falls kein width gegeben, 1D
			int workHeight = height > 0 ? height : 1;        // Falls kein height, 1D

			// Work dimensions
			uint workDim = (width > 0 && height > 0) ? 2u : 1u;
			UIntPtr[] globalWorkSize = workDim == 2
				? [(UIntPtr) workWidth, (UIntPtr) workHeight]
				: [(UIntPtr) pixelsTotal];

			// Execute kernel
			CLResultCode error = CL.EnqueueNDRangeKernel(
				this.queue,
				this.Kernel.Value,
				workDim,          // 1D oder 2D
				null,             // Kein Offset
				globalWorkSize,   // Work-Größe in Pixeln
				null,             // Lokale Work-Size (automatisch)
				0, null, out CLEvent evt
			);
			if (error != CLResultCode.Success)
			{
				this.lastError = error;
				Console.WriteLine("CL-EXEC | ExecuteGenericImageEditKernel #006: Error enqueuing NDRange kernel for kernel: '" + kernel + "'. Error: " + error);
				return IntPtr.Zero;
			}

			// Wait for completion
			error = CL.WaitForEvents(1, [@evt]);
			if (error != CLResultCode.Success)
			{
				this.lastError = error;
				Console.WriteLine("CL-EXEC | ExecuteGenericImageEditKernel #007: Error waiting for events after executing kernel: '" + kernel + "'. Error: " + error);
				return IntPtr.Zero;
			}

			// Return output buffer pointer
			return outputBuffer.Value.Handle;
		}



		// Helpers
		public object[] MergeImageKernelArgumentsDynamic(string kernelName, CLBuffer? inputBuffer = null, CLBuffer? outputBuffer = null, Dictionary<string, string>? providedArguments = null)
		{
			// LOG Provided Arguments
			string providedLog = "CL-EXEC | Provided Arguments: " + Environment.NewLine;
			if (providedArguments != null)
			{
				foreach (var kvp in providedArguments)
				{
					string typeName = kvp.Value?.GetType().Name ?? "null";
					string valueStr = kvp.Value?.ToString() ?? "null";
					providedLog += $"{typeName}:{kvp.Key}='{valueStr}', " + Environment.NewLine;
				}
			}
			else
			{
				providedLog += "None" + Environment.NewLine;
			}
			providedLog = providedLog.TrimEnd(',', ' ');
			Console.WriteLine(providedLog);

			// Get required arg definitions
			var requiredArgs = this.compiler.GetKernelArguments(kernelName);
			if (requiredArgs == null || requiredArgs.Count == 0)
			{
				return [];
			}

			object[] sortedArgs = new object[requiredArgs.Count];
			for (int i = 0; i < requiredArgs.Count; i++)
			{
				string argName = requiredArgs.ElementAt(i).Key;
				Type argType = requiredArgs.ElementAt(i).Value;
				string argNameLower = argName.ToLower();

				// Special case for CLBuffers (type endswith *)
				if (argType.Name.EndsWith("*"))
				{
					if (argNameLower.Contains("input") && inputBuffer != null)
					{
						sortedArgs[i] = inputBuffer;
						continue;
					}
					else if (argNameLower.Contains("output") && outputBuffer != null)
					{
						sortedArgs[i] = outputBuffer;
						continue;
					}
					else
					{
						// If no matching buffer provided, use null
						sortedArgs[i] = new CLBuffer();
						continue;
					}
				}

				// Check if provided
				if (providedArguments != null && providedArguments.TryGetValue(argName, out string? value))
				{
					try
					{
						if (value != null)
						{
							// Wenn value ein string ist und argType nicht string, dann parsen
							if (value is string s && argType != typeof(string))
							{
								if (argType == typeof(int) && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intVal))
								{
									sortedArgs[i] = intVal;
								}
								else if (argType == typeof(float) && float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var floatVal))
								{
									sortedArgs[i] = floatVal;
								}
								else if (argType == typeof(double) && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleVal))
								{
									sortedArgs[i] = doubleVal;
								}
								else if (argType == typeof(long) && long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var longVal))
								{
									sortedArgs[i] = longVal;
								}
								else if (argType == typeof(uint) && uint.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var uintVal))
								{
									sortedArgs[i] = uintVal;
								}
								else if (argType == typeof(byte) && byte.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var byteVal))
								{
									sortedArgs[i] = byteVal;
								}
								else
								{
									sortedArgs[i] = argType.IsValueType ? Activator.CreateInstance(argType) ?? 0 : 0;
								}
							}
							else
							{
								sortedArgs[i] = Convert.ChangeType(value, argType) ?? 0;
							}
						}
						else
						{
							sortedArgs[i] = argType.IsValueType ? Activator.CreateInstance(argType) ?? 0 : 0;
						}
					}
					catch
					{
						sortedArgs[i] = argType.IsValueType ? Activator.CreateInstance(argType) ?? 0 : 0;
					}
				}
				else
				{
					// Special defaults for known argument names
					if (argNameLower.Contains("coeff") && argType == typeof(int))
					{
						sortedArgs[i] = 1;
					}
					else if (argNameLower.Contains("zoom"))
					{
						if (argType == typeof(int))
							sortedArgs[i] = 1;
						else if (argType == typeof(float))
							sortedArgs[i] = 1f;
						else if (argType == typeof(double))
							sortedArgs[i] = 1.0;
						else
							sortedArgs[i] = argType.IsValueType ? Activator.CreateInstance(argType) ?? 1 : 1;
					}
					else
					{
						// Use default
						sortedArgs[i] = argType.IsValueType ? Activator.CreateInstance(argType) ?? 0 : 0;
					}
				}
			}

			// Print sorted args with Type:Name(value)

			string log = "CL-EXEC | Merged Kernel Arguments: " + Environment.NewLine;
			for (int i = 0; i < sortedArgs.Length; i++)
			{
				var arg = sortedArgs[i];
				string typeName = arg?.GetType().Name ?? "null";
				string valueStr = arg?.ToString() ?? "null";
				string argName = requiredArgs.ElementAt(i).Key;
				if (arg is CLBuffer buf)
				{
					valueStr = buf.Handle != IntPtr.Zero ? buf.Handle.ToString() : "null";
				}
				log += $"[{i}]{typeName}:{argName}='{valueStr}', " + Environment.NewLine;
			}

			log = log.TrimEnd(',', ' ');
			Console.WriteLine(log);
			return sortedArgs;
		}



		private CLResultCode SetKernelArgSafe(uint index, object value)
		{
			// Check kernel
			if (this.Kernel == null)
			{
				return CLResultCode.InvalidKernelDefinition;
			}

			switch (value)
			{
				case CLBuffer buffer:
					return CL.SetKernelArg(this.Kernel.Value, index, buffer);

				case int i:
					return CL.SetKernelArg(this.Kernel.Value, index, i);

				case long l:
					return CL.SetKernelArg(this.Kernel.Value, index, l);

				case float f:
					return CL.SetKernelArg(this.Kernel.Value, index, f);

				case double d:
					return CL.SetKernelArg(this.Kernel.Value, index, d);

				case byte b:
					return CL.SetKernelArg(this.Kernel.Value, index, b);

				case IntPtr ptr:
					return CL.SetKernelArg(this.Kernel.Value, index, ptr);

				// Spezialfall für lokalen Speicher (Größe als uint)
				case uint u:
					return CL.SetKernelArg(this.Kernel.Value, index, new IntPtr(u));

				// Fall für Vector2
				case Vector2 v:
					// Vector2 ist ein Struct, daher muss es als Array übergeben werden
					return CL.SetKernelArg(this.Kernel.Value, index, v);

				default:
					throw new ArgumentException($"Unsupported argument type: {value?.GetType().Name ?? "null"}");
			}
		}

		private uint GetMaxWorkGroupSize()
		{
			const uint FALLBACK_SIZE = 64;

			if (!this.Kernel.HasValue)
			{
				return FALLBACK_SIZE;
			}

			try
			{
				// 1. Zuerst die benötigte Puffergröße ermitteln
				CLResultCode result = CL.GetKernelWorkGroupInfo(
					this.Kernel.Value,
					this.device,
					KernelWorkGroupInfo.WorkGroupSize,
					UIntPtr.Zero,
					null,
					out nuint requiredSize);

				if (result != CLResultCode.Success || requiredSize == 0)
				{
					this.lastError = result;
					return FALLBACK_SIZE;
				}

				// 2. Puffer mit korrekter Größe erstellen
				byte[] paramValue = new byte[requiredSize];

				// 3. Tatsächliche Abfrage durchführen
				result = CL.GetKernelWorkGroupInfo(
					this.Kernel.Value,
					this.device,
					KernelWorkGroupInfo.WorkGroupSize,
					new UIntPtr(requiredSize),
					paramValue,
					out _);

				if (result != CLResultCode.Success)
				{
					this.lastError = result;
					return FALLBACK_SIZE;
				}

				// 4. Ergebnis konvertieren (abhängig von der Plattform)
				uint maxSize;
				if (requiredSize == sizeof(uint))
				{
					maxSize = BitConverter.ToUInt32(paramValue, 0);
				}
				else if (requiredSize == sizeof(ulong))
				{
					maxSize = (uint) BitConverter.ToUInt64(paramValue, 0);
				}
				else
				{
					return FALLBACK_SIZE;
				}

				// 5. Gültigen Wert sicherstellen
				if (maxSize == 0)
				{
					return FALLBACK_SIZE;
				}

				return maxSize;
			}
			catch (Exception ex)
			{
				Console.WriteLine(ex);
				return FALLBACK_SIZE;
			}
		}

		
	}
}
