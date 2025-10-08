using Microsoft.AspNetCore.Mvc;
using OOCL.Image.OpenCl;
using OOCL.Image.Shared;
using System.Diagnostics;
using System.Numerics;

namespace OOCL.Image.Api.Controllers
{
	[ApiController]
	[Route("api/[controller]")]
	public class GenericOpenClController : ControllerBase
	{
		private readonly OpenClService openClService;

		public GenericOpenClController(OpenClService openClService)
		{
			this.openClService = openClService;
		}


		// DTO request
		[HttpPost("request-execution")]
		[ProducesResponseType(typeof(KernelExecuteResult), 200)]
		[ProducesResponseType(typeof(ProblemDetails), 400)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<ActionResult<KernelExecuteResult>> RequestExecutionAsync([FromBody] KernelExecuteRequest request)
		{
			try
			{
				KernelExecuteResult result = new();

				if (string.IsNullOrWhiteSpace(request.KernelName) && string.IsNullOrWhiteSpace(request.KernelCode))
				{
					return this.BadRequest(new ProblemDetails { Title = "Invalid request.", Detail = "Kernel name or Kernel code are required.", Status = 400 });
				}

				// Try initialize OpenCL (if not done yet)
				if (request.DeviceIndex.HasValue)
				{
					this.openClService.Initialize(request.DeviceIndex.Value);
				}
				else if (!string.IsNullOrWhiteSpace(request.DeviceName))
				{
					this.openClService.Initialize(request.DeviceName);
				}
				else
				{
					this.openClService.Initialize();
				}

				if (string.IsNullOrWhiteSpace(request.OutputDataLength) || !int.TryParse(request.OutputDataLength, out int outputLength) || outputLength < 0)
				{
					request.OutputDataLength = "0";
				}

				if (!this.openClService.Initialized || this.openClService.Compiler == null)
				{
					return this.StatusCode(500, new ProblemDetails { Title = "OpenCL service is not initialized.", Detail = "Could not initialize OpenCL service with the specified device.", Status = 500 });
				}

				// If no KernelName given, try get it from code
				if (string.IsNullOrWhiteSpace(request.KernelName) && !string.IsNullOrWhiteSpace(request.KernelCode))
				{
					request.KernelName = this.openClService.Compiler.GetKernelName(request.KernelCode);
					if (string.IsNullOrWhiteSpace(request.KernelName))
					{
						return this.BadRequest(new ProblemDetails { Title = "Invalid request.", Detail = "Could not determine kernel name from provided kernel code.", Status = 400 });
					}
				}

				Stopwatch sw = Stopwatch.StartNew();
				object[]? execResult = await this.openClService.ExecuteGenericDataKernelAsync(request.KernelName, request.KernelCode, request.ArgumentTypes.ToArray(), request.ArgumentNames.ToArray(), request.ArgumentValues.ToArray(), request.WorkDimension, request.InputDataBase64, request.InputDataType, request.OutputDataType, request.OutputDataLength, request.DeviceIndex, request.DeviceName);
				sw.Stop();

				if (execResult == null || execResult.Length == 0)
				{
					result.KernelName = request.KernelName ?? "N/A";
					result.Success = false;
					result.OutputDataBase64 = null;
					result.OutputDataLength = "0";
					result.Message = "Kernel execution failed or returned no data.";
					result.OutputPointer = null;
					result.ExecutionTimeMs = sw.Elapsed.TotalMilliseconds;
					return this.Ok(result);
				}

				result.KernelName = request.KernelName ?? "N/A";
				result.Success = execResult != null;
				result.OutputDataBase64 = execResult != null && execResult.Length > 0 && execResult[0] is byte[] outputBytes ? Convert.ToBase64String(outputBytes) : null;
				result.OutputDataType = request.OutputDataType;
				result.OutputDataLength = execResult != null && execResult.Length > 0 && execResult[0] is byte[] outputBytes2 ? outputBytes2.Length.ToString() : "0";
				result.Message = result.Success ? "Kernel executed successfully." : "Kernel execution failed.";
				result.OutputPointer = execResult != null && execResult.Length > 0 ? execResult[0]?.GetHashCode().ToString() : null;

				return this.Ok(result);
			}
			catch (Exception ex)
			{
				return this.StatusCode(500, new ProblemDetails { Title = "An error occurred while processing the request.", Detail = ex.Message, Status = 500 });
			}
		}


		// Raw request
		[HttpPost("request-execution-raw")]
		[Consumes("text/plain", "application/json")]
		[ProducesResponseType(typeof(KernelExecuteResult), 200)]
		[ProducesResponseType(typeof(ProblemDetails), 400)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<ActionResult<KernelExecuteResult>> RequestExecutionRawAsync([FromBody] string kernelCode, [FromQuery] string? base64Input = null, [FromQuery] string? inputTypeStr = null, [FromQuery] string outputLengthStr = "0", [FromQuery] string outputTypeStr = "Byte", [FromQuery] Dictionary<string, string>? arguments = null, [FromQuery] string? openClDeviceName = "Core")
		{
			if (string.IsNullOrWhiteSpace(kernelCode))
			{
				return this.BadRequest(new ProblemDetails { Title = "Invalid request.", Detail = "Kernel code is required.", Status = 400 });
			}

			try
			{
				if (string.IsNullOrWhiteSpace(kernelCode))
				{
					return this.BadRequest(new ProblemDetails { Title = "Invalid request.", Detail = "Kernel name or Kernel code are required.", Status = 400 });
				}

				// Try initialize OpenCL (if not done yet)
				if (!string.IsNullOrWhiteSpace(openClDeviceName))
				{
					this.openClService.Initialize(openClDeviceName);
				}
				else
				{
					this.openClService.Initialize();
				}

				if (string.IsNullOrWhiteSpace(outputLengthStr) || !int.TryParse(outputLengthStr, out int outputLength) || outputLength < 0)
				{
					outputLengthStr = "0";
				}

				if (!this.openClService.Initialized || this.openClService.Compiler == null)
				{
					return this.StatusCode(500, new ProblemDetails { Title = "OpenCL service is not initialized.", Detail = "Could not initialize OpenCL service with the specified device.", Status = 500 });
				}

				// First try compile & get kernel name from code
				string? kernelName = this.openClService.Compiler.GetKernelName(kernelCode);

				KernelExecuteResult result = new();
				object[]? execResult = await this.openClService.ExecuteGenericDataKernelAsync("process_data", kernelCode, new string[] { "Byte*", "int" }, new string[] { "input", "length" }, new string[] { "0", outputLengthStr }, 1, base64Input, inputTypeStr, outputTypeStr, outputLengthStr, 2, openClDeviceName);
				if (execResult == null || execResult.Length == 0)
				{
					result.KernelName = kernelName ?? "N/A";
					result.Success = false;
					result.OutputDataBase64 = null;
					result.OutputDataLength = "0";
					result.Message = "Kernel execution failed or returned no data.";
					result.OutputPointer = null;
					return this.Ok(result);
				}
				result.KernelName = "process_data";
				result.Success = execResult != null;
				result.OutputDataBase64 = execResult != null && execResult.Length > 0 && execResult[0] is byte[] outputBytes ? Convert.ToBase64String(outputBytes) : null;
				result.OutputDataType = outputTypeStr;
				result.OutputDataLength = execResult != null && execResult.Length > 0 && execResult[0] is byte[] outputBytes2 ? outputBytes2.Length.ToString() : "0";
				result.Message = result.Success ? "Kernel executed successfully." : "Kernel execution failed.";
				result.OutputPointer = execResult != null && execResult.Length > 0 ? execResult[0]?.GetHashCode().ToString() : null;
				return this.Ok(result);
			}
			catch (Exception ex)
			{
				return this.StatusCode(500, new ProblemDetails { Title = "An error occurred while processing the request.", Detail = ex.Message, Status = 500 });
			}
		}


		// FFT
		[HttpPost("request-fft")]
		[ProducesResponseType(typeof(IEnumerable<Vector2>), 200)]
		[ProducesResponseType(typeof(ProblemDetails), 400)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<ActionResult<IEnumerable<Vector2>>> RequestExecuteFftAsync([FromBody] IEnumerable<float> data, [FromQuery] string? openClDevice = "Core")
		{
			int fftSize = data.Count();
			if (fftSize == 0 || (fftSize & (fftSize - 1)) != 0)
			{
				return this.BadRequest(new ProblemDetails { Title = "Invalid request.", Detail = "Data length must be a power of two and greater than zero.", Status = 400 });
			}

			try
			{
				if (!this.openClService.Initialized)
				{
					if (!string.IsNullOrWhiteSpace(openClDevice))
					{
						this.openClService.Initialize(openClDevice);
					}
					else
					{
						this.openClService.Initialize();
					}
				}
				
				var result = await this.openClService.ExecuteFftAsync(data.ToArray());
				if (result == null || result.Length == 0)
				{
					return this.StatusCode(500, new ProblemDetails { Title = "FFT execution failed.", Detail = "An unknown error occurred during FFT execution.", Status = 500 });
				}

				return this.Ok(result);
			}
			catch (Exception ex)
			{
				return this.StatusCode(500, new ProblemDetails { Title = "An error occurred while processing the request.", Detail = ex.Message, Status = 500 });
			}
		}

		[HttpPost("request-fft-bulk")]
		[ProducesResponseType(typeof(IEnumerable<Vector2[]>), 200)]
		[ProducesResponseType(typeof(ProblemDetails), 400)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<ActionResult<IEnumerable<Vector2[]>>> RequestExecuteFftBulkAsync([FromBody] IEnumerable<float[]> chunks, [FromQuery] string? openClDevice = "Core")
		{
			try
			{
				if (!this.openClService.Initialized)
				{
					if (!string.IsNullOrWhiteSpace(openClDevice))
					{
						this.openClService.Initialize(openClDevice);
					}
					else
					{
						this.openClService.Initialize();
					}
				}

				var result = await this.openClService.ExecuteFftBulkAsync(chunks.ToArray());
				if (result == null || result.Count() <= 0)
				{
					return this.StatusCode(500, new ProblemDetails { Title = "FFT execution failed.", Detail = "An unknown error occurred during FFT execution.", Status = 500 });
				}

				return this.Ok(result);
			}
			catch (Exception ex)
			{
				return this.StatusCode(500, new ProblemDetails { Title = "An error occurred while processing the request.", Detail = ex.Message, Status = 500 });
			}
		}

		// IFFT
		[HttpPost("request-ifft")]
		[ProducesResponseType(typeof(IEnumerable<float>), 200)]
		[ProducesResponseType(typeof(ProblemDetails), 400)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<ActionResult<IEnumerable<float>>> RequestExecuteIfftAsync([FromBody] IEnumerable<Vector2> data, [FromQuery] string? openClDevice = "Core")
		{
			int fftSize = data.Count();
			if (fftSize == 0 || (fftSize & (fftSize - 1)) != 0)
			{
				return this.BadRequest(new ProblemDetails { Title = "Invalid request.", Detail = "Data length must be a power of two and greater than zero.", Status = 400 });
			}

			try
			{
				if (!this.openClService.Initialized)
				{
					if (!string.IsNullOrWhiteSpace(openClDevice))
					{
						this.openClService.Initialize(openClDevice);
					}
					else
					{
						this.openClService.Initialize();
					}
				}

				var result = await this.openClService.ExecuteIfftAsync(data.ToArray());
				if (result == null || result.Length == 0)
				{
					return this.StatusCode(500, new ProblemDetails { Title = "FFT execution failed.", Detail = "An unknown error occurred during FFT execution.", Status = 500 });
				}

				return this.Ok(result);
			}
			catch (Exception ex)
			{
				return this.StatusCode(500, new ProblemDetails { Title = "An error occurred while processing the request.", Detail = ex.Message, Status = 500 });
			}
		}

		[HttpPost("request-ifft-bulk")]
		[ProducesResponseType(typeof(IEnumerable<float[]>), 200)]
		[ProducesResponseType(typeof(ProblemDetails), 400)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<ActionResult<IEnumerable<float[]>>> RequestExecuteIfftBulkAsync([FromBody] IEnumerable<Vector2[]> chunks, [FromQuery] string? openClDevice = "Core")
		{
			try
			{
				if (!this.openClService.Initialized)
				{
					if (!string.IsNullOrWhiteSpace(openClDevice))
					{
						this.openClService.Initialize(openClDevice);
					}
					else
					{
						this.openClService.Initialize();
					}
				}
				
				var result = await this.openClService.ExecuteIfftBulkAsync(chunks.ToArray());
				if (result == null || result.Count() <= 0)
				{
					return this.StatusCode(500, new ProblemDetails { Title = "iFFT execution failed.", Detail = "An unknown error occurred during iFFT execution.", Status = 500 });
				}

				return this.Ok(result);
			}
			catch (Exception ex)
			{
				return this.StatusCode(500, new ProblemDetails { Title = "An error occurred while processing the request.", Detail = ex.Message, Status = 500 });
			}
		}


		// Compile
		[HttpPost("compile-raw")]
		[ProducesResponseType(typeof(string), 200)]
		[ProducesResponseType(typeof(ProblemDetails), 400)]
		public async Task<ActionResult<string>> CompileRawAsync([FromBody] string code, [FromQuery] string? openClDeviceName = "Core")
		{
			if (string.IsNullOrWhiteSpace(code))
			{
				return this.BadRequest(new ProblemDetails { Title = "Invalid request", Detail = "Kernel code empty.", Status = 400 });
			}

			if (!this.openClService.Initialized)
			{
				if (!string.IsNullOrWhiteSpace(openClDeviceName))
				{
					this.openClService.Initialize(openClDeviceName);
				}
				else
				{
					this.openClService.Initialize();
				}
			}
			if (!this.openClService.Initialized || this.openClService.Compiler == null)
			{
				return this.StatusCode(500, new ProblemDetails { Title = "OpenCL not initialized", Status = 500 });
			}

			// Falls JSON-String gesendet wurde, sind Escapes schon entfernt.
			string result = await this.openClService.CompileKernelStringAsync(code);
			return this.Ok(result);
		}

		[HttpPost("compile-file")]
		[Consumes("multipart/form-data")]
		[ProducesResponseType(typeof(string), 200)]
		[ProducesResponseType(typeof(ProblemDetails), 400)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<ActionResult<string>> CompileFileAsync(IFormFile file, [FromQuery] string? openClDeviceName = "Core")
		{
			if (file == null || file.Length == 0)
			{
				return this.BadRequest(new ProblemDetails { Title = "Invalid request", Detail = "No file uploaded or file empty.", Status = 400 });
			}

			if (!this.openClService.Initialized)
			{
				if (!string.IsNullOrWhiteSpace(openClDeviceName))
				{
					this.openClService.Initialize(openClDeviceName);
				}
				else
				{
					this.openClService.Initialize();
				}
			}
			if (!this.openClService.Initialized || this.openClService.Compiler == null)
			{
				return this.StatusCode(500, new ProblemDetails { Title = "OpenCL not initialized", Status = 500 });
			}

			string code;
			using (var reader = new StreamReader(file.OpenReadStream()))
			{
				code = await reader.ReadToEndAsync();
			}
			if (string.IsNullOrWhiteSpace(code))
			{
				return this.BadRequest(new ProblemDetails { Title = "Invalid request", Detail = "Uploaded file is empty.", Status = 400 });
			}

			string compileResult = await this.openClService.CompileKernelStringAsync(code);
			if (!compileResult.Contains(' '))
			{
				compileResult = "Kernel compiled successfully: '" + compileResult + "'" + Environment.NewLine;

				var argDefinitions = this.openClService.Compiler.GetKernelArguments(null, code);
				if (argDefinitions != null && argDefinitions.Count > 0)
				{
					compileResult += "Arguments:" + Environment.NewLine;
					for (int i = 0; i < argDefinitions.Count; i++)
					{
						compileResult += $"  [{i}] {argDefinitions.ElementAt(i).Value.Name} {argDefinitions.ElementAt(i).Key}" + Environment.NewLine;
					}
				}
				else
				{
					compileResult += "No arguments found." + Environment.NewLine;
				}
			}
			
			return this.Ok(compileResult);
		}


		// TESTS
		[HttpGet("test-request-execution")]
		[ProducesResponseType(typeof(KernelExecuteResult), 200)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<ActionResult<KernelExecuteResult>> TestRequestExecutionAsync([FromQuery] int? arrayLength = null, [FromQuery] int? workDimension = null, [FromQuery] bool? resultAsString = null, [FromQuery] string? specificOpenClDevice = "Core")
		{
			try
			{
				if (!this.openClService.Initialized)
				{
					if (!string.IsNullOrWhiteSpace(specificOpenClDevice))
					{
						this.openClService.Initialize(specificOpenClDevice);
					}
					else
					{
						this.openClService.Initialize();
					}
				}

				string source = @"#pragma OPENCL EXTENSION cl_khr_fp64 : enable
					__kernel void to_int_round_or_cut(
						__global const double* input,
						__global int* output,
						const int cutoffMode,
						const int length)
					{
						int gid = get_global_id(0);
						if (gid >= length)
							return;
						double v = input[gid];
						int result;
						if (cutoffMode != 0)
						{
							result = (int)(v);
						}
						else
						{
							if (v >= 0.0)
								result = (int)floor(v + 0.5);
							else
								result = (int)ceil(v - 0.5);
						}
						output[gid] = result;
					}";

				int inputLength = arrayLength.HasValue && arrayLength.Value > 0 ? arrayLength.Value : 1024;
				double[] inputData = new double[inputLength];
				Random rand = new();
				for (int i = 0; i < inputLength; i++)
				{
					inputData[i] = rand.NextDouble() * 2000.0 - 1000.0;
				}

				// Input in Base64 (double -> 8 Bytes)
				string inputBase64 = Convert.ToBase64String(inputData.SelectMany(d => BitConverter.GetBytes(d)).ToArray());

				// WICHTIG: Pointer-Argumente (input, output) NICHT mitgeben – die werden intern behandelt.
				KernelExecuteRequest request = new()
				{
					KernelName = "to_int_round_or_cut",
					KernelCode = source,
					ArgumentNames = new List<string> { "input", "output", "cutoffMode", "length" },
					ArgumentTypes = new List<string> { "Double*", "Int32*", "int", "int" },
					ArgumentValues = new List<string> { "0", "0", "0", inputLength.ToString() },
					WorkDimension = workDimension.HasValue && workDimension.Value > 0 ? workDimension.Value : 1,
					InputDataBase64 = inputBase64,
					InputDataType = "double",
					OutputDataType = "int",
					OutputDataLength = inputLength.ToString(),
					DeviceIndex = 2,
					DeviceName = "Core"
				};

				KernelExecuteResult result = new();

				Stopwatch sw = Stopwatch.StartNew();
				object[]? execResult = await this.openClService.ExecuteGenericDataKernelAsync(
					request.KernelName,
					request.KernelCode,
					request.ArgumentTypes.ToArray(),
					request.ArgumentNames.ToArray(),
					request.ArgumentValues.ToArray(),
					request.WorkDimension,
					request.InputDataBase64,
					request.InputDataType,
					request.OutputDataType,
					request.OutputDataLength,
					request.DeviceIndex,
					request.DeviceName);

				sw.Stop();

				result.KernelName = request.KernelName ?? "to_int_round_or_cut";
				result.Success = execResult != null && execResult.Length > 0;
				// Da OutputDataType = int: Ergebnis ist int[] -> in Base64 umwandeln
				if (execResult != null && execResult.Length > 0 && execResult[0] is int[] intArr)
				{
					byte[] raw = new byte[intArr.Length * sizeof(int)];
					Buffer.BlockCopy(intArr, 0, raw, 0, raw.Length);
					result.OutputDataBase64 = Convert.ToBase64String(raw);
					result.OutputDataType = "int";
					result.OutputDataLength = intArr.Length.ToString();
					result.ExecutionTimeMs = sw.Elapsed.TotalMilliseconds;
				}
				else
				{
					result.OutputDataBase64 = null;
					result.OutputDataLength = "0";
				}
				result.Message = result.GetArrayPreview(32, true) ?? (result.Success ? "Kernel executed successfully." : "Kernel execution failed.");
				result.OutputPointer = null;

				return this.Ok(result);
			}
			catch (Exception ex)
			{
				return this.StatusCode(500, new ProblemDetails { Title = "An error occurred while processing the test request.", Detail = ex.Message, Status = 500 });
			}
		}

		[HttpGet("test-request-fft")]
		[ProducesResponseType(typeof(IEnumerable<Vector2>), 200)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<ActionResult<IEnumerable<Vector2>>> TestRequestFftAsync([FromQuery] int? arrayLength = null, [FromQuery] string? specificOpenClDevice = "Core")
		{
			try
			{
				if (!this.openClService.Initialized)
				{
					if (!string.IsNullOrWhiteSpace(specificOpenClDevice))
					{
						this.openClService.Initialize(specificOpenClDevice);
					}
					else
					{
						this.openClService.Initialize();
					}
				}

				int fftSize = arrayLength.HasValue && arrayLength.Value > 0 ? arrayLength.Value : 1024;
				if ((fftSize & (fftSize - 1)) != 0)
				{
					return this.BadRequest(new ProblemDetails { Title = "Invalid request.", Detail = "Data length must be a power of two and greater than zero.", Status = 400 });
				}

				double freq1 = 5.0;
				double freq2 = 20.0;
				double freq3 = 50.0;
				float[] inputData = new float[fftSize];
				for (int i = 0; i < fftSize; i++)
				{
					double t = (double)i / (double)fftSize;
					inputData[i] = (float)(Math.Sin(2.0 * Math.PI * freq1 * t) + 0.5 * Math.Sin(2.0 * Math.PI * freq2 * t) + 0.25 * Math.Sin(2.0 * Math.PI * freq3 * t));
				}

				var result = await this.openClService.ExecuteFftAsync(inputData);
				if (result == null || result.Length == 0)
				{
					return this.StatusCode(500, new ProblemDetails { Title = "FFT execution failed.", Detail = "An unknown error occurred during FFT execution.", Status = 500 });
				}
				
				return this.Ok(result);
			}
			catch (Exception ex)
			{
				return this.StatusCode(500, new ProblemDetails { Title = "An error occurred while processing the test FFT request.", Detail = ex.Message, Status = 500 });
			}
		}

		[HttpGet("test-request-ifft")]
		[ProducesResponseType(typeof(IEnumerable<float>), 200)]
		[ProducesResponseType(typeof(ProblemDetails), 500)]
		public async Task<ActionResult<IEnumerable<float>>> TestRequestIfftAsync([FromQuery] int? arrayLength = null, [FromQuery] string? specificOpenClDevice = "Core")
		{
			try
			{
				if (!this.openClService.Initialized)
				{
					if (!string.IsNullOrWhiteSpace(specificOpenClDevice))
					{
						this.openClService.Initialize(specificOpenClDevice);
					}
					else
					{
						this.openClService.Initialize();
					}
				}

				// Create sample data: three sine waves combined
				int fftSize = arrayLength.HasValue && arrayLength.Value > 0 ? arrayLength.Value : 1024;
				if ((fftSize & (fftSize - 1)) != 0)
				{
					return this.BadRequest(new ProblemDetails { Title = "Invalid request.", Detail = "Data length must be a power of two and greater than zero.", Status = 400 });
				}

				double freq1 = 5.0;
				double freq2 = 20.0;
				double freq3 = 50.0;
				float[] inputData = new float[fftSize];
				for (int i = 0; i < fftSize; i++)
				{
					double t = (double)i / (double)fftSize;
					inputData[i] = (float)(Math.Sin(2.0 * Math.PI * freq1 * t) + 0.5 * Math.Sin(2.0 * Math.PI * freq2 * t) + 0.25 * Math.Sin(2.0 * Math.PI * freq3 * t));
				}

				var fftResult = await this.openClService.ExecuteFftAsync(inputData);
				if (fftResult == null || fftResult.Length == 0)
				{
					return this.StatusCode(500, new ProblemDetails { Title = "FFT execution failed.", Detail = "An unknown error occurred during FFT execution.", Status = 500 });
				}

				var ifftResult = await this.openClService.ExecuteIfftAsync(fftResult);
				if (ifftResult == null || ifftResult.Length == 0)
				{
					return this.StatusCode(500, new ProblemDetails { Title = "iFFT execution failed.", Detail = "An unknown error occurred during iFFT execution.", Status = 500 });
				}

				return this.Ok(ifftResult);
			}
			catch (Exception ex)
			{
				return this.StatusCode(500, new ProblemDetails
				{
					Title = "An error occurred while processing the test iFFT request.",
					Detail = ex.Message,
					Status = 500
				});
			}
		}

	}
}
