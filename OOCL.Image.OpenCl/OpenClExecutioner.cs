using OpenTK.Compute.OpenCL;
using OpenTK.Mathematics;
using System.Diagnostics;
using System.Globalization;
using System.Numerics;

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


		// Audio Exec
		public IntPtr ExecuteFFT(IntPtr pointer, string version = "01", char form = 'f', int chunkSize = 16384, float overlap = 0.5f, bool free = true)
		{
			int overlapSize = (int) (overlap * chunkSize);

			string kernelsPath = Path.Combine(this.compiler.KernelPath, "Audio");
			string file = "";
			if (form == 'f')
			{
				file = Path.Combine(kernelsPath, $"fft{version}.cl");
			}
			else if (form == 'c')
			{
				file = Path.Combine(kernelsPath, $"ifft{version}.cl");
			}

			// Load kernel from file, else abort
			this.compiler.LoadKernel("", file);
			if (this.Kernel == null)
			{
				return pointer;
			}

			// Get input buffers
			OpenClMem? inputBuffers = this.register.GetBuffer(pointer);
			if (inputBuffers == null || inputBuffers.GetCount() <= 0)
			{
				return pointer;
			}

			// Get output buffers
			OpenClMem? outputBuffers = null;
			if (form == 'f')
			{
				outputBuffers = this.register.AllocateGroup<OpenTK.Mathematics.Vector2>(inputBuffers.GetCount(), (nint) inputBuffers.GetLengths().FirstOrDefault());
			}
			else if (form == 'c')
			{
				outputBuffers = this.register.AllocateGroup<float>(inputBuffers.GetCount(), (nint) inputBuffers.GetLengths().FirstOrDefault());
			}
			if (outputBuffers == null || outputBuffers.GetCount() <= 0 || outputBuffers.GetLengths().Any(l => l < 1))
			{
				return pointer;
			}


			// Set static args
			CLResultCode error = this.SetKernelArgSafe(2, (int) inputBuffers.GetLengths().FirstOrDefault());
			if (error != CLResultCode.Success)
			{
				this.lastError = error;
				return pointer;
			}
			error = this.SetKernelArgSafe(3, overlapSize);
			if (error != CLResultCode.Success)
			{
				this.lastError = error;
				return pointer;
			}

			// Calculate optimal work group size
			uint maxWorkGroupSize = this.GetMaxWorkGroupSize();
			uint globalWorkSize = 1;
			uint localWorkSize = 1;


			// Loop through input buffers
			int count = inputBuffers.GetCount();
			for (int i = 0; i < count; i++)
			{
				error = this.SetKernelArgSafe(0, inputBuffers[i]);
				if (error != CLResultCode.Success)
				{
					this.lastError = error;
					return pointer;
				}
				error = this.SetKernelArgSafe(1, outputBuffers[i]);
				if (error != CLResultCode.Success)
				{
					this.lastError = error;
					return pointer;
				}

				// Execute kernel
				error = CL.EnqueueNDRangeKernel(this.queue, this.Kernel.Value, 1, null, [(UIntPtr) globalWorkSize], [(UIntPtr) localWorkSize], 0, null, out CLEvent evt);
				if (error != CLResultCode.Success)
				{
					this.lastError = error;
					return pointer;
				}

				// Wait for completion
				error = CL.WaitForEvents(1, [evt]);
				if (error != CLResultCode.Success)
				{
					this.lastError = error;
				}

				// Release event
				CL.ReleaseEvent(evt);
			}

			if (outputBuffers != null && free)
			{
				this.register.FreeBuffer(pointer);
			}

			if (outputBuffers == null)
			{
				return pointer;
			}

			return outputBuffers[0].Handle;
		}

		public async Task<IntPtr> ExecuteFFTAsync(IntPtr pointer, string version = "01", char form = 'f', int chunkSize = 16384, float overlap = 0.5f, bool free = true, IProgress<int>? progress = null)
		{
			// Die gesamte blockierende Logik wird in einem Hintergrund-Task ausgeführt.
			return await Task.Run(() =>
			{
				int overlapSize = (int) (overlap * chunkSize);

				string kernelsPath = Path.Combine(this.compiler.KernelPath, "Audio");
				string file = "";
				if (form == 'f')
				{
					file = Path.Combine(kernelsPath, $"fft{version}.cl");
				}
				else if (form == 'c')
				{
					file = Path.Combine(kernelsPath, $"ifft{version}.cl");
				}

				// Load kernel from file, else abort
				this.compiler.LoadKernel("", file);
				if (this.Kernel == null)
				{
					return pointer;
				}

				// Get input buffers
				OpenClMem? inputBuffers = this.register.GetBuffer(pointer);
				if (inputBuffers == null || inputBuffers.GetCount() <= 0)
				{
					return pointer;
				}

				// Get output buffers
				OpenClMem? outputBuffers = null;
				if (form == 'f')
				{
					outputBuffers = this.register.AllocateGroup<OpenTK.Mathematics.Vector2>(inputBuffers.GetCount(), (nint) inputBuffers.GetLengths().FirstOrDefault());
				}
				else if (form == 'c')
				{
					outputBuffers = this.register.AllocateGroup<float>(inputBuffers.GetCount(), (nint) inputBuffers.GetLengths().FirstOrDefault());
				}
				if (outputBuffers == null || outputBuffers.GetCount() <= 0 || outputBuffers.GetLengths().Any(l => l < 1))
				{
					return pointer;
				}

				// Set static args
				CLResultCode error = this.SetKernelArgSafe(2, (int) inputBuffers.GetLengths().FirstOrDefault());
				if (error != CLResultCode.Success)
				{
					this.lastError = error;
					return pointer;
				}
				error = this.SetKernelArgSafe(3, overlapSize);
				if (error != CLResultCode.Success)
				{
					this.lastError = error;
					return pointer;
				}

				// Calculate optimal work group size
				uint maxWorkGroupSize = this.GetMaxWorkGroupSize();
				uint globalWorkSize = 1;
				uint localWorkSize = 1;

				// Loop through input buffers
				int count = inputBuffers.GetCount();
				for (int i = 0; i < count; i++)
				{
					error = this.SetKernelArgSafe(0, inputBuffers[i]);
					if (error != CLResultCode.Success)
					{
						this.lastError = error;
						return pointer;
					}
					error = this.SetKernelArgSafe(1, outputBuffers[i]);
					if (error != CLResultCode.Success)
					{
						this.lastError = error;
						return pointer;
					}

					// Execute kernel
					error = CL.EnqueueNDRangeKernel(this.queue, this.Kernel.Value, 1, null, [(UIntPtr) globalWorkSize], [(UIntPtr) localWorkSize], 0, null, out CLEvent evt);
					if (error != CLResultCode.Success)
					{
						this.lastError = error;
						return pointer;
					}

					// Wait for completion
					error = CL.WaitForEvents(1, [evt]);
					if (error != CLResultCode.Success)
					{
						this.lastError = error;
					}

					// Release event
					CL.ReleaseEvent(evt);

					// Update progress inkrementell
					progress?.Report(1);
				}

				if (outputBuffers != null && free)
				{
					this.register.FreeBuffer(pointer);
				}

				if (outputBuffers == null)
				{
					return pointer;
				}

				return outputBuffers[0].Handle;
			});
		}

		public IntPtr ExecuteAudioKernel(IntPtr objPointer, out double factor, long length = 0, string kernelName = "normalize", string version = "00", int chunkSize = 1024, float overlap = 0.5f, int samplerate = 44100, int bitdepth = 24, int channels = 2, Dictionary<string, string>? providedArguments = null)
		{
			factor = 1.000d; // Default factor

			// Get kernel path
			string kernelPath = this.compiler.KernelFiles.FirstOrDefault(f => f.ToLower().Contains((kernelName + version).ToLower())) ?? "";
			if (string.IsNullOrEmpty(kernelPath))
			{
				return IntPtr.Zero;
			}

			// Load kernel if not loaded
			if (this.Kernel == null || this.KernelFile != kernelPath)
			{
				this.compiler.LoadKernel("", kernelPath);
				if (this.Kernel == null || this.KernelFile == null || !this.KernelFile.Contains("\\Audio\\"))
				{
					return IntPtr.Zero;
				}
			}

			// Get input buffers
			OpenClMem? inputMem = this.register.GetBuffer(objPointer);
			if (inputMem == null || inputMem.GetCount() <= 0 || inputMem.GetLengths().Any(l => l < 1))
			{
				return IntPtr.Zero;
			}

			// Get variable arguments
			object[] variableArguments = this.compiler.GetArgumentDefaultValues();

			// Check if FFT is needed
			bool didFft = false;
			string factorArgMatch = providedArguments?.Keys.FirstOrDefault(k => k.ToLower().Contains("f")) ?? "factor";
			if (this.compiler.PointerInputType.Contains("Vector2") && inputMem.Type == typeof(float).Name)
			{
				if (providedArguments != null && providedArguments.ContainsKey("factor"))
				{
					if (float.TryParse(providedArguments[factorArgMatch], NumberStyles.Float, CultureInfo.InvariantCulture, out float fFactor))
					{
						factor = fFactor;
					}
					else if (double.TryParse(providedArguments[factorArgMatch], NumberStyles.Float, CultureInfo.InvariantCulture, out double dFactor))
					{
						factor = dFactor;
					}
					else
					{
						return IntPtr.Zero;
					}
				}
				else
				{
					factor = 1.000d;
				}

				IntPtr fftPointer = this.ExecuteFFT(objPointer, "01", 'f', chunkSize, overlap, true);
				if (fftPointer == IntPtr.Zero)
				{
					return IntPtr.Zero;
				}

				objPointer = fftPointer;
				didFft = true;

				// Load kernel if not loaded
				if (this.Kernel == null || this.KernelFile != kernelPath)
				{
					this.compiler.LoadKernel("", kernelPath);
					if (this.Kernel == null || this.KernelFile == null || !this.KernelFile.Contains("\\Audio\\"))
					{
						return IntPtr.Zero;
					}
				}
			}

			// Get input buffers
			inputMem = this.register.GetBuffer(objPointer);
			if (inputMem == null || inputMem.GetCount() <= 0 || inputMem.GetLengths().Any(l => l < 1))
			{
				return IntPtr.Zero;
			}

			// Get output buffers
			OpenClMem? outputMem = null;
			if (this.compiler.PointerInputType == typeof(float*).Name)
			{
				outputMem = this.register.AllocateGroup<float>(inputMem.GetCount(), (nint) inputMem.GetLengths().FirstOrDefault());
			}
			else if (this.compiler.PointerOutputType == typeof(OpenTK.Mathematics.Vector2*).Name)
			{
				outputMem = this.register.AllocateGroup<OpenTK.Mathematics.Vector2>(inputMem.GetCount(), (nint) inputMem.GetLengths().FirstOrDefault());
			}
			else
			{
				return IntPtr.Zero;
			}

			if (outputMem == null || outputMem.GetCount() == 0 || outputMem.GetLengths().Any(l => l < 1))
			{
				return IntPtr.Zero;
			}

			// Aggregate arguments
			Dictionary<string, string> args = new()
			{
				{ factorArgMatch, factor is double d ? d.ToString("F:15") : factor.ToString("F8") }
			};

			// Loop through input buffers
			int count = inputMem.GetCount();
			for (int i = 0; i < count; i++)
			{
				// Get buffers
				CLBuffer inputBuffer = inputMem[i];
				CLBuffer outputBuffer = outputMem[i];

				// Merge arguments
				object[] arguments = this.MergeAudioKernelArgumentsDynamic(kernelName + version, inputBuffer, outputBuffer, args);
				if (arguments == null || arguments.Length <= 0)
				{
					return IntPtr.Zero;
				}

				// Set kernel arguments
				CLResultCode error = CLResultCode.Success;
				for (uint j = 0; j < arguments.Length; j++)
				{
					error = this.SetKernelArgSafe(j, arguments[(int) j]);
					if (error != CLResultCode.Success)
					{
						this.lastError = error;
						return IntPtr.Zero;
					}
				}

				// Get work dimensions
				uint maxWorkGroupSize = this.GetMaxWorkGroupSize();
				uint globalWorkSize = (uint) inputMem.GetLengths()[i];
				uint localWorkSize = Math.Min(maxWorkGroupSize, globalWorkSize);
				if (localWorkSize == 0)
				{
					localWorkSize = 1; // Fallback to 1 if no valid local size
				}
				if (globalWorkSize < localWorkSize)
				{
					globalWorkSize = localWorkSize; // Ensure global size is at least local size
				}

				// Execute kernel
				error = CL.EnqueueNDRangeKernel(this.queue, this.Kernel.Value, 1, null, [(UIntPtr) globalWorkSize], [(UIntPtr) localWorkSize], 0, null, out CLEvent evt);
				if (error != CLResultCode.Success)
				{
					this.lastError = error;
					return IntPtr.Zero;
				}

				// Wait for completion
				error = CL.WaitForEvents(1, [evt]);
				if (error != CLResultCode.Success)
				{
					this.lastError = error;
				}

				// Release event
				CL.ReleaseEvent(evt);
			}

			// Free input buffer if necessary
			if (outputMem[0].Handle != IntPtr.Zero)
			{
				long freed = this.register.FreeBuffer(objPointer, true);
				if (freed > 0)
				{
					Console.WriteLine("Freed: " + freed + " MB");
				}
			}

			// Optionally execute IFFT if FFT was done
			IntPtr outputPointer = outputMem[0].Handle;
			if (didFft && outputMem.Type == typeof(OpenTK.Mathematics.Vector2).Name)
			{
				IntPtr ifftPointer = this.ExecuteFFT(outputMem[0].Handle, "01", 'c', chunkSize, overlap, true);
				if (ifftPointer == IntPtr.Zero)
				{
					return IntPtr.Zero;
				}

				outputPointer = ifftPointer; // Update output pointer to IFFT result
			}

			// Return output buffer handle if available, else return original pointer
			return outputPointer != IntPtr.Zero ? outputPointer : objPointer;
		}

		public async Task<(IntPtr Pointer, double Factor)> ExecuteAudioKernelAsync(IntPtr objPointer, long length = 0, string kernelName = "normalize", string version = "00", int chunkSize = 1024, float overlap = 0.5f, int samplerate = 44100, int bitdepth = 24, int channels = 2, Dictionary<string, string>? providedArguments = null, IProgress<int>? progress = null)
		{
			return await Task.Run(async () =>
			{
				double factor = 1.000d; // Initialisiere den Faktor

				// Lade Kernel-Pfad
				string kernelPath = this.compiler.KernelFiles.FirstOrDefault(f => f.ToLower().Contains((kernelName + version).ToLower())) ?? "";
				if (string.IsNullOrEmpty(kernelPath))
				{
					Console.WriteLine("EXEC-Error: kernelPath was null or empty (not found).");
					return (IntPtr.Zero, factor);
				}

				// Lade Kernel, falls nicht geladen
				if (this.Kernel == null)
				{
					this.compiler.LoadKernel("", kernelPath);
					if (this.Kernel == null || kernelPath == null || !kernelPath.ToLowerInvariant().Contains("\\audio\\"))
					{
						Console.WriteLine("EXEC-Error: kernelPath does not contain /Audio/ (subdir) (" + kernelPath + ")");
						return (IntPtr.Zero, factor);
					}
				}

				// Eingabepuffer abrufen
				OpenClMem? inputMem = this.register.GetBuffer(objPointer);
				if (inputMem == null || inputMem.GetCount() <= 0 || inputMem.GetLengths().Any(l => l < 1))
				{
					Console.WriteLine("EXEC-Error: inputMem was null or empty.");
					return (IntPtr.Zero, factor);
				}

				// Variable Argumente abrufen
				object[] variableArguments = this.compiler.GetArgumentDefaultValues();

				// Prüfen, ob FFT benötigt wird
				bool didFft = false;
				if (this.compiler.PointerInputType.Contains("Vector2") && inputMem.Type == typeof(float).Name)
				{
					if (providedArguments != null && providedArguments.ContainsKey("factor"))
					{
						string? factorArgMatch = providedArguments.Keys.FirstOrDefault(k => k.ToLower().Contains("f"));
						if (string.IsNullOrEmpty(factorArgMatch))
						{
							Console.WriteLine($"EXEC-Error: No factor args match found.");
							return (IntPtr.Zero, factor);
						}

						Console.WriteLine("EXEC-Info: Found factor argument: " + providedArguments[factorArgMatch]);

						if (float.TryParse(providedArguments[factorArgMatch].Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out float fFactor))
						{
							factor = fFactor;
						}
						else if (double.TryParse(providedArguments[factorArgMatch].Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double dFactor))
						{
							factor = dFactor;
						}
						else
						{
							Console.WriteLine($"EXEC-Error: Factor was neither float nor double (Type: {factor.GetType().Name}: {factor:F9})");
							return (IntPtr.Zero, factor);
						}
					}
					else
					{
						Console.WriteLine($"EXEC-Warning: No factor for kernel (no time-stretching method was requested)");
						factor = 1.000d;
					}

					// Führe die asynchrone FFT aus
					IntPtr fftPointer = await this.ExecuteFFTAsync(objPointer, "01", 'f', chunkSize, overlap, true, progress);
					if (fftPointer == IntPtr.Zero)
					{
						Console.WriteLine("EXEC-Error: FFT returned zero-pointer.");
						return (IntPtr.Zero, factor);
					}

					objPointer = fftPointer;
					didFft = true;

					// Kernel nach FFT erneut laden, falls nötig
					if (this.Kernel == null || this.KernelFile != kernelPath)
					{
						this.compiler.LoadKernel("", kernelPath);
						if (this.Kernel == null || this.KernelFile == null || !this.KernelFile.Contains("\\Audio\\"))
						{
							Console.WriteLine("EXEC-Error: KernelFile does not contain Audio (subdir) ((2))");
							return (IntPtr.Zero, factor);
						}
					}
				}

				// Eingabepuffer abrufen (möglicherweise neu)
				inputMem = this.register.GetBuffer(objPointer);
				if (inputMem == null || inputMem.GetCount() <= 0 || inputMem.GetLengths().Any(l => l < 1))
				{
					Console.WriteLine("EXEC-Error: inputMem was null or empty ((2)). DidFft: " + didFft.ToString());
					return (IntPtr.Zero, factor);
				}

				// Ausgabepuffer abrufen
				OpenClMem? outputMem = null;
				if (inputMem.elementType == typeof(float))
				{
					outputMem = this.register.AllocateGroup<float>(inputMem.GetCount(), (nint) inputMem.GetLengths().FirstOrDefault());
				}
				else if (inputMem.elementType == typeof(OpenTK.Mathematics.Vector2))
				{
					outputMem = this.register.AllocateGroup<OpenTK.Mathematics.Vector2>(inputMem.GetCount(), (nint) inputMem.GetLengths().FirstOrDefault());
				}
				else
				{
					Console.WriteLine("EXEC-Error: outputMem is null since inputMem Type was " + $"{inputMem.elementType.Name}" + $"");
					return (IntPtr.Zero, factor);
				}

				if (outputMem == null || outputMem.GetCount() == 0 || outputMem.GetLengths().Any(l => l < 1))
				{
					Console.WriteLine("EXEC-Error: outputMem was null or empty or had any chunk with a length of < 1.");
					return (IntPtr.Zero, factor);
				}

				if (providedArguments == null)
				{
					providedArguments = new Dictionary<string, string>();
				}

				if (chunkSize > 0)
				{
					providedArguments["chunkSize"] = chunkSize.ToString();
				}
				if (overlap >= 0f && overlap < 1f)
				{
					providedArguments["overlap"] = overlap.ToString("F4", CultureInfo.InvariantCulture);
					providedArguments["overlapSize"] = ((int) (overlap * chunkSize)).ToString();
				}

				// Schleife durch Eingabepuffer
				int count = inputMem.GetCount();
				for (int i = 0; i < count; i++)
				{
					// Puffer abrufen
					CLBuffer inputBuffer = inputMem[i];
					CLBuffer outputBuffer = outputMem[i];

					// Argumente zusammenführen
					object[] arguments = this.MergeAudioKernelArgumentsDynamic(kernelName + version, inputBuffer, outputBuffer, providedArguments);
					if (arguments == null || arguments.Length <= 0)
					{
						Console.WriteLine($"EXEC-Error: Aborting on processing audio chunk [{i} / {count}] since merged argumens were null or empty.");
						return (IntPtr.Zero, factor);
					}

					// Kernel-Argumente setzen
					CLResultCode error = CLResultCode.Success;
					for (uint j = 0; j < arguments.Length; j++)
					{
						error = this.SetKernelArgSafe(j, arguments[j]);
						if (error != CLResultCode.Success)
						{
							this.lastError = error;
							Console.WriteLine($"EXEC-CL-Error: " + this.lastError);
							return (IntPtr.Zero, factor);
						}
					}

					// Arbeitsdimensionen ermitteln
					uint maxWorkGroupSize = this.GetMaxWorkGroupSize();
					uint globalWorkSize = (uint) inputMem.GetLengths()[i];
					uint localWorkSize = Math.Min(maxWorkGroupSize, globalWorkSize);
					if (localWorkSize == 0)
					{
						localWorkSize = 1;
					}
					if (globalWorkSize < localWorkSize)
					{
						globalWorkSize = localWorkSize;
					}

					// Kernel ausführen
					error = CL.EnqueueNDRangeKernel(this.queue, this.Kernel.Value, 1, null, [(UIntPtr) globalWorkSize], [(UIntPtr) localWorkSize], 0, null, out CLEvent evt);
					if (error != CLResultCode.Success)
					{
						this.lastError = error;
						return (IntPtr.Zero, factor);
					}

					// Auf Fertigstellung warten
					error = CL.WaitForEvents(1, [evt]);
					if (error != CLResultCode.Success)
					{
						Console.WriteLine($"EXEC-CL-Error: " + this.lastError);
						this.lastError = error;
					}

					// Event freigeben
					error = CL.ReleaseEvent(evt);
					if (error != CLResultCode.Success)
					{
						Console.WriteLine($"EXEC-CL-Warning: " + this.lastError);
						this.lastError = error;
					}

					// Fortschritt melden
					progress?.Report(1);
				}

				// Eingabepuffer freigeben
				if (outputMem[0].Handle != IntPtr.Zero)
				{
					long freed = this.register.FreeBuffer(objPointer, true);
					if (freed > 0)
					{
						Console.WriteLine("Freed: " + freed + " MB");
					}
				}

				// Optional IFFT ausführen
				IntPtr outputPointer = outputMem[0].Handle;
				if (didFft && outputMem.Type == typeof(OpenTK.Mathematics.Vector2).Name)
				{
					// Führe die asynchrone IFFT aus
					IntPtr ifftPointer = await this.ExecuteFFTAsync(outputMem[0].Handle, "01", 'c', chunkSize, overlap, true, progress);
					if (ifftPointer == IntPtr.Zero)
					{
						Console.WriteLine("EXEC-Error: I-FFT returned zero-pointer.");
						return (IntPtr.Zero, factor);
					}

					outputPointer = ifftPointer;
				}

				// Rückgabe des Tupels
				return (outputPointer != IntPtr.Zero ? outputPointer : objPointer, factor);
			});
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
					if (argNameLower.Contains("in") && inputBuffer != null)
					{
						sortedArgs[i] = inputBuffer;
						continue;
					}
					else if (argNameLower.Contains("out") && outputBuffer != null)
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
						{
							sortedArgs[i] = 1;
						}
						else if (argType == typeof(float))
						{
							sortedArgs[i] = 1f;
						}
						else if (argType == typeof(double))
						{
							sortedArgs[i] = 1.0;
						}
						else
						{
							sortedArgs[i] = argType.IsValueType ? Activator.CreateInstance(argType) ?? 1 : 1;
						}
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

		public List<object> MergeArgumentsAudio(object[] variableArguments, CLBuffer inputBuffer, CLBuffer outputBuffer, long length, int chunkSize, float overlap, int samplerate, int bitdepth, int channels, Dictionary<string, object>? optionalArgs = null)
		{
			List<object> arguments = [];

			// Make overlap to size
			int overlapSize = (int) (overlap * chunkSize);

			// Get argument definition
			Dictionary<string, Type> definitions = this.compiler.Arguments;
			if (definitions == null || definitions.Count == 0)
			{
				return arguments;
			}

			// Merge args
			int found = 0;
			for (int i = 0; i < definitions.Count; i++)
			{
				string key = definitions.Keys.ElementAt(i);
				Type type = definitions[key];
				if (type.Name.Contains("*") && key.Contains("in"))
				{
					arguments.Add(inputBuffer);
					found++;
				}
				else if (type.Name.Contains("*") && key.Contains("out"))
				{
					arguments.Add(outputBuffer);
					found++;
				}
				else if ((type == typeof(long) || type == typeof(int)) && key.Contains("len"))
				{
					arguments.Add(chunkSize > 0 ? chunkSize : length);
					found++;
				}
				else if (type == typeof(int) && key.Contains("chunk"))
				{
					arguments.Add(chunkSize);
					found++;
				}
				else if (type == typeof(int) && key.Contains("overlap"))
				{
					arguments.Add(overlapSize);
					found++;
				}
				else if (type == typeof(int) && key == "samplerate")
				{
					arguments.Add(samplerate);
					found++;
				}
				else if (type == typeof(int) && key == "bit")
				{
					arguments.Add(bitdepth);
					found++;
				}
				else if (type == typeof(int) && key == "channel")
				{
					arguments.Add(channels);
					found++;
				}
				else
				{
					if (found < variableArguments.Length)
					{
						arguments.Add(variableArguments[found]);
						found++;
					}
					else
					{
						return arguments; // Return early if a required argument is missing
					}
				}
			}

			// Integrate / replace with optional arguments
			if (optionalArgs != null && optionalArgs.Count > 0)
			{
				foreach (var kvp in optionalArgs)
				{
					string key = kvp.Key.ToLowerInvariant();
					object value = kvp.Value;

					// Find matching argument by name
					int index = definitions.Keys.ToList().FindIndex(k => k.ToLower().Contains(key.ToLower()));
					if (index >= 0 && index < arguments.Count)
					{
						arguments[index] = value; // Replace existing argument
					}
					else
					{
						arguments.Add(value); // Add new optional argument
					}
				}
			}

			return arguments;
		}

		public object[] MergeAudioKernelArgumentsDynamic(string kernelName, CLBuffer? inputBuffer = null, CLBuffer? outputBuffer = null, Dictionary<string, string>? providedArguments = null)
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
					if (argNameLower.Contains("in") && inputBuffer != null)
					{
						sortedArgs[i] = inputBuffer;
						continue;
					}
					else if (argNameLower.Contains("out") && outputBuffer != null)
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
					value = value.Replace(',', '.');

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
					if (argNameLower.Contains("chan") && argType == typeof(int))
					{
						sortedArgs[i] = 1;
					}
					else if (argNameLower.Contains("rate") && argType == typeof(int))
					{
						sortedArgs[i] = 44100;
					}
					else if (argNameLower.Contains("bit") && argType == typeof(int))
					{
						sortedArgs[i] = 24;
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

		public object[] MergeGenericKernelArgumentsDynamic(CLKernel? kernel, CLBuffer? inputBuffer = null, CLBuffer? outputBuffer = null, Dictionary<string, string>? arguments = null)
		{
			if (kernel == null)
			{
				return [];
			}

			var requiredArgs = this.compiler.GetKernelArguments(kernel);
			if (requiredArgs == null || requiredArgs.Count == 0)
			{
				return [];
			}

			object[] sortedArgs = new object[requiredArgs.Count];

			for (int i = 0; i < requiredArgs.Count; i++)
			{
				string argName = requiredArgs.ElementAt(i).Key;
				Type argType = requiredArgs.ElementAt(i).Value;
				string argNameLower = argName.ToLowerInvariant();
				bool isPointer = argType.Name.EndsWith("*");

				// LOG
				Console.WriteLine("CL-EXEC | Merging Argument: " + argType.Name + " " + argName);
				if (isPointer)
				{
					if ((argNameLower.Contains("in") || argNameLower == "input") && inputBuffer != null)
					{
						sortedArgs[i] = inputBuffer.Value;
						Console.WriteLine("CL-EXEC | Using Input Buffer for Argument: " + argName);
						continue;
					}
					if ((argNameLower.Contains("out") || argNameLower == "output") && outputBuffer != null)
					{
						sortedArgs[i] = outputBuffer.Value;
						Console.WriteLine("CL-EXEC | Using Output Buffer for Argument: " + argName);
						continue;
					}
					sortedArgs[i] = new CLBuffer();
					Console.WriteLine("CL-EXEC | No Buffer Provided for Argument: " + argName + ", using empty CLBuffer");
					continue;
				}

				if (arguments != null && arguments.TryGetValue(argName, out string? raw))
				{
					sortedArgs[i] = raw == null ? 0 : ParseScalar(argType, raw);
					Console.WriteLine("CL-EXEC | Using Provided Value for Argument: " + argName + " = " + (sortedArgs[i]?.ToString() ?? "null"));
				}
				else
				{
					sortedArgs[i] = argType == typeof(int) ? 0 :
									argType == typeof(long) ? 0L :
									argType == typeof(float) ? 0f :
									argType == typeof(double) ? 0d :
									argType == typeof(byte) ? (byte) 0 :
									argType == typeof(uint) ? 0u :
									0;
					Console.WriteLine("CL-EXEC | No Value Provided for Argument: " + argName + ", using default = " + (sortedArgs[i]?.ToString() ?? "null") + " of type " + argType.Name);
				}
			}

			// Absicherung: keine null / unbekannten Typen an Kernel geben
			for (int i = 0; i < sortedArgs.Length; i++)
			{
				if (sortedArgs[i] == null)
				{
					Console.WriteLine("CL-EXEC | Warning: Argument " + i + " (" + requiredArgs.ElementAt(i).Key + ") is null, replacing with 0");
					sortedArgs[i] = 0;
				}
				else
				{
					bool supported = sortedArgs[i] is CLBuffer
						|| sortedArgs[i] is int
						|| sortedArgs[i] is long
						|| sortedArgs[i] is float
						|| sortedArgs[i] is double
						|| sortedArgs[i] is byte
						|| sortedArgs[i] is uint
						|| sortedArgs[i] is bool
						|| sortedArgs[i] is OpenTK.Mathematics.Vector2
						|| sortedArgs[i] is IntPtr;
					if (!supported)
					{
						Console.WriteLine("CL-EXEC | Warning: Argument " + i + " (" + requiredArgs.ElementAt(i).Key + ") has unsupported type " + sortedArgs[i].GetType().Name + ", replacing with 0");
						sortedArgs[i] = 0;
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

			return sortedArgs;
		}

		private static object ParseScalar(Type t, string raw)
		{
			if (t == typeof(int) && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i))
			{
				return i;
			}

			if (t == typeof(long) && long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out long l))
			{
				return l;
			}

			if (t == typeof(float) && float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float f))
			{
				return f;
			}

			if (t == typeof(double) && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
			{
				return d;
			}

			if (t == typeof(byte) && byte.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out byte b))
			{
				return b;
			}

			if (t == typeof(uint) && uint.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint u))
			{
				return u;
			}

			if (t == typeof(bool))
			{
				if (raw == "0")
				{
					return false;
				}

				if (raw == "1")
				{
					return true;
				}

				if (bool.TryParse(raw, out bool bo))
				{
					return bo;
				}

				return false;
			}
			return 0;
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
				case OpenTK.Mathematics.Vector2 v:
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





		// Generic Kernel Execution
		public async Task<TResult[]> ExecuteGenericKernelSingleAsync<TResult>(CLKernel? kernel, object[]? inputData, string? inputDataType = "Byte", long outputElementCount = 0, Dictionary<string, string>? arguments = null, int workDimensions = 1) where TResult : unmanaged
		{
			if (kernel == null || !kernel.HasValue || this.register == null || outputElementCount <= 0 || workDimensions < 1 || workDimensions > 3)
			{
				return [];
			}

			TResult[] result = [];

			OpenClMem? inputMem = null;
			if (inputData is { LongLength: > 0 } && !string.IsNullOrEmpty(inputDataType))
			{
				inputDataType = inputDataType.ToLowerInvariant().Trim();

				// WICHTIG: Cast<object>() kommt von boxed ValueTypes (ConvertStringToTypeAsync)
				inputMem = inputDataType switch
				{
					"byte" or "uint8" or "uchar" => this.register.PushData<byte>(inputData.Cast<byte>().ToArray()),
					"sbyte" or "int8" or "char" => this.register.PushData<sbyte>(inputData.Cast<sbyte>().ToArray()),
					"short" or "int16" => this.register.PushData<short>(inputData.Cast<short>().ToArray()),
					"ushort" or "uint16" => this.register.PushData<ushort>(inputData.Cast<ushort>().ToArray()),
					"int" or "int32" => this.register.PushData<int>(inputData.Cast<int>().ToArray()),
					"uint" or "uint32" => this.register.PushData<uint>(inputData.Cast<uint>().ToArray()),
					"long" or "int64" => this.register.PushData<long>(inputData.Cast<long>().ToArray()),
					"ulong" or "uint64" => this.register.PushData<ulong>(inputData.Cast<ulong>().ToArray()),
					"float" or "single" => this.register.PushData<float>(inputData.Cast<float>().ToArray()),
					"double" => this.register.PushData<double>(inputData.Cast<double>().ToArray()),
					"vector2" or "vec2" => this.register.PushData<OpenTK.Mathematics.Vector2>(inputData.Cast<OpenTK.Mathematics.Vector2>().ToArray()),
					"intptr" or "pointer" => this.register.PushData<IntPtr>(inputData.Cast<IntPtr>().ToArray()),
					_ => null,
				};

				Console.WriteLine($"CL-EXEC | Input Memory: {inputMem?.TotalLength} elements of type {inputMem?.Type ?? "null"}");
			}

			// FIX: outputElementCount AllocateSingle<T> macht schon intern elementSize * count
			OpenClMem? outputMem = this.register.AllocateSingle<TResult>((nint) outputElementCount);
			if (outputMem == null || outputMem.GetCount() == 0)
			{
				Console.WriteLine("CL-EXEC | Failed to allocate output memory.");
				return [];
			}

			Console.WriteLine($"CL-EXEC | Output Memory: {outputMem.TotalLength} elements of type {outputMem.Type}");

			// Argumente zusammenführen (Pointer richtig setzen)
			object[] mergedArgs = this.MergeGenericKernelArgumentsDynamic(kernel, inputMem?[0], outputMem[0], arguments);
			// Sicherheits-Check: Kernel-Argumentanzahl
			if (mergedArgs.Length == 0)
			{
				Console.WriteLine("CL-EXEC | No kernel arguments found.");
				return [];
			}

			// Set kernel arguments
			CLResultCode error;
			for (uint j = 0; j < mergedArgs.Length; j++)
			{
				error = this.SetKernelArgSafe(j, mergedArgs[(int) j]);
				if (error != CLResultCode.Success)
				{
					Console.WriteLine("CL-EXEC | Failed to set kernel argument at index " + j + ": " + error);
					this.lastError = error;
					return [];
				}

				Console.WriteLine("CL-EXEC | Set kernel argument at index " + j + ": " + (mergedArgs[(int) j] is CLBuffer buf ? (buf.Handle != IntPtr.Zero ? buf.Handle.ToString() : "null") : mergedArgs[(int) j]?.ToString() ?? "null"));
			}

			// Dimensions
			long elementsTotal = outputElementCount;

			// Work dimensions
			uint workDim = (uint) workDimensions;

			UIntPtr[] globalWorkSize = workDim switch
			{
				1 => [(UIntPtr) elementsTotal],
				2 => [(UIntPtr) Math.Ceiling(Math.Sqrt(elementsTotal)), (UIntPtr) Math.Ceiling(Math.Sqrt(elementsTotal))],
				3 =>
				[
					(UIntPtr) Math.Ceiling(Math.Pow(elementsTotal, 1.0 / 3.0)),
					(UIntPtr) Math.Ceiling(Math.Pow(elementsTotal, 1.0 / 3.0)),
					(UIntPtr) Math.Ceiling(Math.Pow(elementsTotal, 1.0 / 3.0))
				],
				_ => [(UIntPtr) elementsTotal]
			};

			Console.WriteLine("CL-EXEC | Elements Total: " + elementsTotal + ", WorkDim: " + workDim + ", GlobalWorkSize: " + string.Join(", ", globalWorkSize.Select(g => g.ToString())));

			await Task.Run(() =>
			{
				// Execute kernel
				CLResultCode error = CL.EnqueueNDRangeKernel(
					this.queue,
					kernel.Value,
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
					return;
				}

				Console.WriteLine("CL-EXEC | Kernel enqueued successfully.");

				// Wait for completion
				error = CL.WaitForEvents(1, [evt]);
				if (error != CLResultCode.Success)
				{
					Console.WriteLine("CL-EXEC | Failed to wait for kernel event: " + error);
					this.lastError = error;
				}

				Console.WriteLine("CL-EXEC | Kernel execution completed.");

				// Release event
				error = CL.ReleaseEvent(evt);
				if (error != CLResultCode.Success)
				{
					Console.WriteLine("CL-EXEC | Failed to release kernel event: " + error);
					this.lastError = error;
				}

				Console.WriteLine("CL-EXEC | Kernel event released.");

				// Read back
				if (outputMem != null && outputMem[0].Handle != IntPtr.Zero)
				{
					result = this.register.PullData<TResult>(outputMem[0]);
					// Sicherheitskürzung falls Kernel weniger/mehr geschrieben hat
					if (result.LongLength > outputElementCount)
					{
						Console.WriteLine("CL-EXEC | Warning: Kernel wrote more elements (" + result.LongLength + ") than expected (" + outputElementCount + "). Truncating result.");
						Array.Resize(ref result, (int) outputElementCount);
					}
				}

				Console.WriteLine("CL-EXEC | Data read back from device. Retrieved " + result.LongLength + " elements.");

				// Free input/output
				if (inputMem != null && inputMem[0].Handle != IntPtr.Zero)
				{
					Console.WriteLine("CL-EXEC | Freeing input memory.");
					this.register.FreeBuffer(inputMem[0].Handle, true);
				}
				if (outputMem != null && outputMem[0].Handle != IntPtr.Zero)
				{
					Console.WriteLine("CL-EXEC | Freeing output memory.");
					this.register.FreeBuffer(outputMem[0].Handle, true);
				}
			});

			Console.WriteLine("CL-EXEC | Kernel execution completed. Retrieved " + result.LongLength + " elements.");
			return result;
		}





		private static bool TryParseDoubleFlexible(string? raw, out double result)
		{
			result = 0d;
			if (string.IsNullOrWhiteSpace(raw)) return false;
			raw = raw.Trim();
			// 1) Invariant (Punkt)
			if (double.TryParse(raw, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out result))
				return true;
			// 2) Ersetze Komma durch Punkt
			var alt = raw.Replace(',', '.');
			if (double.TryParse(alt, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out result))
				return true;
			// 3) Aktuelle Kultur (falls caller lokal formatiert)
			if (double.TryParse(raw, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out result))
				return true;
			// 4) Entferne umschließende Quotes und nochmal versuchen
			var unq = raw.Trim('\'', '"');
			if (double.TryParse(unq, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out result))
				return true;
			return false;
		}
	}
}
