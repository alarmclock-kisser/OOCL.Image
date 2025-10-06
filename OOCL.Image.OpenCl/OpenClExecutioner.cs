using OpenTK.Compute.OpenCL;
using OpenTK.Mathematics;
using System.Globalization;

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
				outputBuffers = this.register.AllocateGroup<Vector2>(inputBuffers.GetCount(), (nint) inputBuffers.GetLengths().FirstOrDefault());
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
					outputBuffers = this.register.AllocateGroup<Vector2>(inputBuffers.GetCount(), (nint) inputBuffers.GetLengths().FirstOrDefault());
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
			else if (this.compiler.PointerOutputType == typeof(Vector2*).Name)
			{
				outputMem = this.register.AllocateGroup<Vector2>(inputMem.GetCount(), (nint) inputMem.GetLengths().FirstOrDefault());
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
				object[] arguments = this.MergeAudioKernelArgumenstDynamic(kernelName + version, inputBuffer, outputBuffer, args);
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
					return objPointer;
				}

				// Release event
				error = CL.ReleaseEvent(evt);
				if (error != CLResultCode.Success)
				{
					this.lastError = error;
				}
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
			if (didFft && outputMem.Type == typeof(Vector2).Name)
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
					return (IntPtr.Zero, factor);
				}

				// Lade Kernel, falls nicht geladen
				if (this.Kernel == null || this.KernelFile != kernelPath)
				{
					this.compiler.LoadKernel("", kernelPath);
					if (this.Kernel == null || this.KernelFile == null || !this.KernelFile.Contains("\\Audio\\"))
					{
						return (IntPtr.Zero, factor);
					}
				}

				// Eingabepuffer abrufen
				OpenClMem? inputMem = this.register.GetBuffer(objPointer);
				if (inputMem == null || inputMem.GetCount() <= 0 || inputMem.GetLengths().Any(l => l < 1))
				{
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
						string factorArgMatch = providedArguments.Keys.FirstOrDefault(k => k.ToLower().Contains("f")) ?? "factor";
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
							return (IntPtr.Zero, factor);
						}
					}
					else
					{
						factor = 1.000d;
					}

					// Führe die asynchrone FFT aus
					IntPtr fftPointer = await this.ExecuteFFTAsync(objPointer, "01", 'f', chunkSize, overlap, true, progress);
					if (fftPointer == IntPtr.Zero)
					{
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
							return (IntPtr.Zero, factor);
						}
					}
				}

				// Eingabepuffer abrufen (möglicherweise neu)
				inputMem = this.register.GetBuffer(objPointer);
				if (inputMem == null || inputMem.GetCount() <= 0 || inputMem.GetLengths().Any(l => l < 1))
				{
					return (IntPtr.Zero, factor);
				}

				// Ausgabepuffer abrufen
				OpenClMem? outputMem = null;
				if (this.compiler.PointerInputType == typeof(float*).Name)
				{
					outputMem = this.register.AllocateGroup<float>(inputMem.GetCount(), (nint) inputMem.GetLengths().FirstOrDefault());
				}
				else if (this.compiler.PointerOutputType == typeof(Vector2*).Name)
				{
					outputMem = this.register.AllocateGroup<Vector2>(inputMem.GetCount(), (nint) inputMem.GetLengths().FirstOrDefault());
				}
				else
				{
					return (IntPtr.Zero, factor);
				}

				if (outputMem == null || outputMem.GetCount() == 0 || outputMem.GetLengths().Any(l => l < 1))
				{
					return (IntPtr.Zero, factor);
				}

				// Schleife durch Eingabepuffer
				int count = inputMem.GetCount();
				for (int i = 0; i < count; i++)
				{
					// Puffer abrufen
					CLBuffer inputBuffer = inputMem[i];
					CLBuffer outputBuffer = outputMem[i];

					// Argumente zusammenführen
					object[] arguments = this.MergeAudioKernelArgumenstDynamic(kernelName + version, inputBuffer, outputBuffer, providedArguments);
					if (arguments == null || arguments.Length <= 0)
					{
						return (IntPtr.Zero, factor);
					}

					// Kernel-Argumente setzen
					CLResultCode error = CLResultCode.Success;
					for (uint j = 0; j < arguments.Length; j++)
					{
						error = this.SetKernelArgSafe(j, arguments[(int) j]);
						if (error != CLResultCode.Success)
						{
							this.lastError = error;
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
						this.lastError = error;
						return (objPointer, factor);
					}

					// Event freigeben
					error = CL.ReleaseEvent(evt);
					if (error != CLResultCode.Success)
					{
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
				if (didFft && outputMem.Type == typeof(Vector2).Name)
				{
					// Führe die asynchrone IFFT aus
					IntPtr ifftPointer = await this.ExecuteFFTAsync(outputMem[0].Handle, "01", 'c', chunkSize, overlap, true, progress);
					if (ifftPointer == IntPtr.Zero)
					{
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

		public object[] MergeAudioKernelArgumenstDynamic(string kernelName, CLBuffer? inputBuffer = null, CLBuffer? outputBuffer = null, Dictionary<string, string>? providedArguments = null)
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
