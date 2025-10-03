using OOCL.Image.OpenCl;
using System.Text.Json.Serialization;

namespace OOCL.Image.Shared
{
	public class OpenClKernelInfo
	{
		public string Filepath = string.Empty;
		public int ArgumentsCount { get; set; } = 0;
		public IEnumerable<string> ArgumentNames { get; set; } = [];
		public IEnumerable<string> ArgumentType { get; set; } = [];
		public string InputPointerName { get; set; } = string.Empty;
		public string OutputPointerName { get; set; } = "void*";

		public string InputPointerType { get; set; } = string.Empty;
		public string OutputPointerType { get; set; } = "void*";

		public string MediaType { get; set; } = "DIV";
		public string FunctionName { get; set; } = string.Empty;

		public string[] ColorInputArgNames { get; set; } = [];
		public int PointersCount { get; set; } = 0;
		public bool NeedsImage { get; set; } = false;

		public bool? CompiledSuccessfully { get; set; } = null;



		public OpenClKernelInfo()
		{
			// Empty default ctor
		}

		[JsonConstructor]
		public OpenClKernelInfo(OpenClCompiler? obj, int index, bool tryCompile = true)
		{
			if (obj == null)
			{
				return;
			}

			var compiler = obj as OpenClCompiler;
			if (compiler == null)
			{
				Console.WriteLine("Compiler object is not of type OpenClKernelCompiler.");
				return;
			}

			if (index < 0 || index >= compiler.KernelFiles.Count())
			{
				Console.WriteLine($"Kernel-index is out of range (max: {(compiler.KernelFiles.Count() - 1)}, was {index})");
				return;
			}

			this.Filepath = compiler.KernelFiles.ElementAt(index);
			this.FunctionName = compiler.GetKernelName(this.Filepath) ?? string.Empty;

			var argsDict = compiler.GetKernelArguments(null, this.Filepath);
			if (argsDict != null)
			{
				this.ArgumentsCount = argsDict.Count;
				this.ArgumentNames = argsDict.Keys;
				this.ArgumentType = argsDict.Values.Select(v => v.Name ?? "unknown");

				this.InputPointerName = this.DetermineInputPointerName();
				this.OutputPointerName = this.DetermineOutputPointerName();
				this.InputPointerType = this.DetermineInputPointerType();
				this.OutputPointerType = this.DetermineOutputPointerType();
				this.MediaType = this.DetermineMediaType();
				this.ColorInputArgNames = this.GetColorInputArgNames();

				if (tryCompile)
				{
					this.CompiledSuccessfully = compiler.TryCompileKernel(this.Filepath) ?? false;
				}

				this.PointersCount = this.ArgumentType.Count(t => t.EndsWith("*"));
				this.NeedsImage = this.ArgumentNames.Any(name => name.ToLowerInvariant().Contains("image") || name.ToLowerInvariant().Contains("img") || name.ToLowerInvariant().Contains("input"));
				this.PointersCount = this.ArgumentType.Count(t => t.EndsWith("*"));
			}
		}


		private string[] GetColorInputArgNames()
		{
			// Find argNames in fields that contain end or start with r, g,b (tolower comparison)
			List<string> colorArgCandidates = this.ArgumentNames
				.Where(name => !string.IsNullOrWhiteSpace(name))
				.Where(name =>
				{
					var lower = name.ToLowerInvariant();
					return lower.Contains("color") || lower.Contains("colour") || lower.EndsWith("r") || lower.EndsWith("g") || lower.EndsWith("b") || lower.StartsWith("base");
				})
				.ToList();

			// Only if one for r, g, b is found, return those 3
			var rArg = colorArgCandidates.FirstOrDefault(name => name.ToLowerInvariant().EndsWith("r"));
			var gArg = colorArgCandidates.FirstOrDefault(name => name.ToLowerInvariant().EndsWith("g"));
			var bArg = colorArgCandidates.FirstOrDefault(name => name.ToLowerInvariant().EndsWith("b"));
			if (rArg != null && gArg != null && bArg != null)
			{
				return [rArg, gArg, bArg];
			}

			return [];
		}

		private string DetermineInputPointerName()
		{
			// Look for first argument that is a pointer (ends with *)
			var pointerArg = this.ArgumentType.FirstOrDefault(t => t.EndsWith("*"));
			if (pointerArg != null)
			{
				return pointerArg;
			}

			return "";
		}

		private string DetermineOutputPointerName()
		{
			// Look for argument names that contain "output" or "out" (case insensitive)
			var outputArg = this.ArgumentNames.FirstOrDefault(name => name.ToLowerInvariant().Contains("output") || name.ToLowerInvariant().Contains("out") || name.ToLowerInvariant().Contains("*"));
			if (outputArg != null)
			{
				// Find its type
				int argIndex = this.ArgumentNames.ToList().IndexOf(outputArg);
				if (argIndex >= 0 && argIndex < this.ArgumentType.Count())
				{
					return this.ArgumentType.ElementAt(argIndex);
				}
			}

			return "";
		}

		private string DetermineMediaType()
		{
			// Look at filepath for sub path \Image\ or \Audio\, else return Diverse
			var lowerPath = this.Filepath.ToLowerInvariant();
			if (lowerPath.Contains(@"\image\") || lowerPath.Contains("/image/"))
			{
				return "IMG";
			}
			else if (lowerPath.Contains(@"\audio\") || lowerPath.Contains("/audio/"))
			{
				return "AUD";
			}
			else if (lowerPath.Contains(@"\video\") || lowerPath.Contains("/video/"))
			{
				return "VID";
			}
			else
			{
				return "DIV";
			}
		}

		private string DetermineInputPointerType()
		{
			// Look for first argument that is a pointer (ends with *)
			var pointerArg = this.ArgumentType.FirstOrDefault(t => t.EndsWith("*"));
			if (pointerArg != null)
			{
				return pointerArg;
			}
			return string.Empty;
		}

		private string DetermineOutputPointerType()
		{
			// Take last arg that is a pointer (ends with *)
			var pointerArg = this.ArgumentType.LastOrDefault(t => t.EndsWith("*"));
			if (pointerArg != null)
			{
				return pointerArg;
			}

			return "void*";
		}


	}
}