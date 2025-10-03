using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using Color = SixLabors.ImageSharp.Color;
using Size = SixLabors.ImageSharp.Size;

namespace OOCL.Image.Core
{
    public class ImageCollection : IDisposable
    {
        private readonly ConcurrentDictionary<Guid, ImageObj> images = [];
        private readonly object lockObj = new();

        public IReadOnlyCollection<ImageObj> Images => this.images.Values.ToList();

        public ImageObj? this[Guid guid]
        {
            get
            {
                this.images.TryGetValue(guid, out ImageObj? imgObj);
                return imgObj;
            }
        }

        public ImageObj? this[string name]
        {
            get
            {
                lock (this.lockObj)
                {
                    return this.images.Values.FirstOrDefault(img => img.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                }
            }
        }

        public ImageObj? this[int index]
        {
            get
            {
                lock (this.lockObj)
                {
                    return this.images.Values.ElementAtOrDefault(index);
                }
            }
        }

        // Options
        public string ImportPath { get; set; } = string.Empty;
        public string ExportPath { get; set; } = string.Empty;
        public bool SaveMemory { get; set; } = false;
        public int DefaultWidth { get; set; } = 720;
        public int DefaultHeight { get; set; } = 480;
        public int MaxImages { get; set; } = 0;
        public bool ServerSidedData { get; set; } = false;

		// Ctor with options
		public ImageCollection(bool saveMemory = false, int defaultWidth = 720, int defaultHeight = 480, int maxImages = 0, bool loadResources = false, bool serverSidedData = false)
        {
            this.DefaultWidth = Math.Max(defaultWidth, 360); // Min is 360px width
            this.DefaultHeight = Math.Max(defaultHeight, 240); // Min is 240px height
            this.MaxImages = Math.Max(maxImages, 0); // 0 means no limit
			this.SaveMemory = saveMemory;
            this.ServerSidedData = serverSidedData;
			if (this.SaveMemory)
            {
                Console.WriteLine("ImageCollection: Memory saving enabled. All images will be disposed on add.");
            }

            if (loadResources & serverSidedData)
			{
                var _ = this.LoadResourcesAsync().Result;
			}
		}

        public bool Add(ImageObj imgObj)
        {
            if (this.SaveMemory)
            {
                // Dispose every image
                lock (this.lockObj)
                {
                    foreach (var i in this.images.Values)
                    {
                        i.Dispose();
                    }

                    this.images.Clear();
                }
            }

            return this.images.TryAdd(imgObj.Id, imgObj);
        }

        public bool Remove(Guid guid)
        {
            bool result = this.images.TryRemove(guid, out ImageObj? imgObj);
            if (result && imgObj != null)
            {
                imgObj.Dispose();
                Console.WriteLine($"Removed and disposed image '{imgObj.Name}' (ID: {imgObj.Id}).");
            }
            else
            {
                Console.WriteLine($"Failed to remove image with ID: {guid}. It might not exist.");
            }

            return result;
        }

        public async Task Clear()
        {
            await Task.Run(() =>
            {
                lock (this.lockObj)
                {
                    foreach (var imgObj in this.images.Values)
                    {
                        imgObj.Dispose();
                        Console.WriteLine($"Disposed image '{imgObj.Name}' (Guid: {imgObj.Id}).");
                    }

                    this.images.Clear();
                }
            });
        }

        public async Task<IEnumerable<Guid>?> LoadResourcesAsync(string? customResourcesPath = null)
        {
            if (!string.IsNullOrWhiteSpace(customResourcesPath))
            {
                if (Directory.Exists(customResourcesPath))
                {
                    customResourcesPath = Path.GetFullPath(customResourcesPath);
                    Console.WriteLine($"LoadResourcesAsync: Using custom Resources directory at '{customResourcesPath}'");
                }
                else
                {
                    Console.WriteLine($"LoadResourcesAsync: Custom Resources directory not found at '{customResourcesPath}'");
                    return null;
                }
			}
            else
            {
				// Try get Repo\Resources directory relative to current executing assembly
				customResourcesPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "OOCL.Image.Core", "Resources");
				if (!Directory.Exists(customResourcesPath))
				{
					// If not in DEV environment, try relative to EXE
					customResourcesPath = Path.Combine(AppContext.BaseDirectory, "Resources");
					if (!Directory.Exists(customResourcesPath))
					{
						Console.WriteLine($"LoadResourcesAsync: Resources directory not found at '{customResourcesPath}'");
						return null;
					}
				}
			}

            string[] resourceImageFiles = Directory.GetFiles(customResourcesPath)
                .Where(file => SupportedFormats.Contains(Path.GetExtension(file).TrimStart('.').ToLower()))
                .ToArray();
			if (resourceImageFiles.Length <= 0)
			{
                Console.WriteLine($"LoadResourcesAsync: No supported image files found in Resources directory at '{customResourcesPath}'");
                return null;
			}

			// Load async & parallel
            var loadTasks = resourceImageFiles.Select(file => this.LoadImage(file)).ToArray();
            var loadedImages = await Task.WhenAll(loadTasks);

            var loadedGuids = loadedImages.Where(img => img != null).Select(img => img!.Id).ToList();
            Console.WriteLine($"LoadResourcesAsync: Loaded {loadedGuids.Count} images from Resources directory at '{customResourcesPath}'");

			return loadedGuids;
		}

		public void Dispose()
        {
            this.Clear().Wait();
            GC.SuppressFinalize(this);
        }

        public async Task<ImageObj?> LoadImage(string filePath)
        {
            if (!File.Exists(filePath))
            {
                Console.WriteLine($"LoadImage: File not found or path empty: {filePath}");
                return null;
            }

            ImageObj obj;

            try
            {
                obj = await Task.Run(() =>
                {
                    return new ImageObj(filePath);
                });
            }
            catch (Exception ex)
            {
                try
                {
                    obj = new ImageObj(filePath);
                }
                catch (Exception innerEx)
                {
                    Console.WriteLine($"Error creating ImageObj from file '{filePath}': {innerEx.Message}");
                    return null;
                }

                Console.WriteLine($"Error loading image from file '{filePath}': {ex.Message}");
                return null;
            }

            if (this.Add(obj))
            {
                Console.WriteLine($"Loaded and added image '{obj.Name}' (ID: {obj.Id}) from file.");
                return obj;
            }

            // obj.Dispose();
            Console.WriteLine($"Failed to add image '{obj.Name}' (ID: {obj.Id}). An image with this ID might already exist.");
            return null;
        }

        public async Task<ImageObj?> PopEmpty(Size? size = null, bool add = false)
        {
            size ??= new Size(1080, 1920);
            int number = this.images.Count + 1;
            int digits = (int) Math.Log10(number) + 1;

            ImageObj imgObj;

            try
            {
                imgObj = await Task.Run(() =>
                {
                    lock (this.lockObj)
                    {
                        return new ImageObj(new byte[size.Value.Width * size.Value.Height * 4], size.Value.Width, size.Value.Height, $"EmptyImage_{number.ToString().PadLeft(digits, '0')}");
                    }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error creating empty image: {ex.Message}");
                return null;
            }

            if (add)
            {
                if (this.Add(imgObj))
                {
                    Console.WriteLine($"Created and added empty image '{imgObj.Name}' (ID: {imgObj.Id}) with size {size.Value.Width}x{size.Value.Height}.");
                    return imgObj;
                }

                imgObj.Dispose();
                Console.WriteLine($"Failed to add empty image '{imgObj.Name}' (ID: {imgObj.Id}). An image with this ID might already exist.");
                return null;
            }

            Console.WriteLine($"Created empty image '{imgObj.Name}' (ID: {imgObj.Id}) with size {size.Value.Width}x{size.Value.Height}, but not added to collection.");
            return imgObj;
        }

        public async Task<string?> ExportImage(Guid guid, string? exportPath = null, string format = "png")
        {
            exportPath ??= this.ExportPath;
            ImageObj? obj = this[guid];
            if (obj != null)
            {
                return await obj.Export(exportPath, format);
            }

            return null;
        }

        public async Task<int> CleanupOldImages(int maxImages = 1)
        {
            return await Task.Run(() =>
            {
                lock (this.lockObj)
                {
                    int removedCount = 0;
                    while (this.images.Count > maxImages)
                    {
                        var oldest = this.images.Values.OrderBy(img => img.CreatedAt).FirstOrDefault();
                        if (oldest != null)
                        {
                            if (this.images.TryRemove(oldest.Id, out _))
                            {
                                oldest.Dispose();
                                removedCount++;
                                Console.WriteLine($"Cleaned up and disposed old image '{oldest.Name}' (ID: {oldest.Id}).");
                            }
                        }
                        else
                        {
                            break; // No more images to remove
                        }
                    }
                    return removedCount;
				}
			});
		}



        public static Size GetSharpSize(int height, int width)
        {
            width = Math.Clamp(width, 1, 32768);
            height = Math.Clamp(height, 1, 32768);

            return new Size(width, height);
        }

        public static Color? GetSharpColor(System.Drawing.Color color)
        {
            if (color == System.Drawing.Color.Empty)
            {
                return null;
            }

            return SixLabors.ImageSharp.Color.FromRgba(color.R, color.G, color.B, color.A);
        }

        public static Color GetSharpColor(string hexColor = "#00000000")
        {
            if (string.IsNullOrWhiteSpace(hexColor))
            {
                hexColor = "#00000000";
            }
            if (!hexColor.StartsWith("#"))
            {
                hexColor = "#" + hexColor;
            }
            try
            {
                return SixLabors.ImageSharp.Color.ParseHex(hexColor);
            }
            catch
            {
                return SixLabors.ImageSharp.Color.FromRgba(0, 0, 0, 0);
            }
        }

        public static System.Drawing.Color GetDrawingColor(Color color)
        {
            var rgba = color.ToPixel<Rgba32>();
            return System.Drawing.Color.FromArgb(rgba.A, rgba.R, rgba.G, rgba.B);
        }

        // Fügen Sie diese statische Eigenschaft in die Klasse ImageCollection ein
        public static readonly HashSet<string> SupportedFormats =
        [
        "png",
        "jpg",
        "jpeg",
        "bmp",
        "gif",
        "tiff"
        ];


		public static int[] GetRgbFromHexColor(string hexColor)
		{
			if (string.IsNullOrWhiteSpace(hexColor))
			{
				return [0, 0, 0];
			}

			// Remove # if present
			if (hexColor.StartsWith("#"))
			{
				hexColor = hexColor[1..];
			}

			try
			{
				if (hexColor.Length == 6)
				{
					// RRGGBB format
					int r = Convert.ToInt32(hexColor.Substring(0, 2), 16);
					int g = Convert.ToInt32(hexColor.Substring(2, 2), 16);
					int b = Convert.ToInt32(hexColor.Substring(4, 2), 16);

					Console.WriteLine($"Resolved hex-Color: #{hexColor} to R: {r} G: {g} B: {b}");
					return [r, g, b];
				}
				else if (hexColor.Length == 8)
				{
					// AARRGGBB format - extract RGB and ignore alpha
					int r = Convert.ToInt32(hexColor.Substring(2, 2), 16);
					int g = Convert.ToInt32(hexColor.Substring(4, 2), 16);
					int b = Convert.ToInt32(hexColor.Substring(6, 2), 16);
					int a = Convert.ToInt32(hexColor.Substring(0, 2), 16);

					Console.WriteLine($"Resolved hex-Color: #{hexColor} to R: {r} G: {g} B: {b} A: {a}");
					return [r, g, b, a];
				}
				else if (hexColor.Length == 3)
				{
					// RGB shorthand format
					int r = Convert.ToInt32(hexColor[0].ToString() + hexColor[0].ToString(), 16);
					int g = Convert.ToInt32(hexColor[1].ToString() + hexColor[1].ToString(), 16);
					int b = Convert.ToInt32(hexColor[2].ToString() + hexColor[2].ToString(), 16);

					Console.WriteLine($"Resolved hex-Color: #{hexColor} to R: {r} G: {g} B: {b}");
					return [r, g, b];
				}
				else
				{
					Console.WriteLine($"Invalid hex color length: {hexColor} (Expected 3, 6 or 8 characters)");
					return [0, 0, 0];
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Could not resolve hex-Color: {hexColor} - Error: {ex.Message}");
				return [0, 0, 0];
			}
		}

        public async Task<int> ApplyImagesLimitAsync()
        {
            if (this.MaxImages > 0 && this.images.Count > this.MaxImages)
            {
                return await this.CleanupOldImages(this.MaxImages);
			}

            return 0;
		}



		/*public static async Task<string?> SerializeImageAsBase64(ImageObj obj, string format = "png", float scale = 1.0f, bool createAsNewObj = false, bool inParallel = true)
        {
            // Check imageobj data
            if (obj.Img == null)
            {
                return null;
            }

            format = format.ToLowerInvariant().Trim('.');
			if (!ImageCollection.SupportedFormats.Contains(format))
            {
				Console.WriteLine($"SerializeImageAsBase64: Unsupported format specified('{format}'), defaulting to PNG.");
				format = "png";
			}

            Stopwatch sw = Stopwatch.StartNew();

			try
            {
				// 1) Clone
				var image = await Task.Run(obj.Img.CloneAs<Rgba32>);
				if (image == null)
				{
					return null;
				}

                byte[] bytes = [];

				// 2) Scale if scale differs from 1.0f by more than 0.01f
				if (Math.Abs(scale - 1.0f) > 0.01f)
				{
					int newWidth = (int) (image.Width * scale);
					int newHeight = (int) (image.Height * scale);
					newWidth = Math.Clamp(newWidth, 1, 32768);
					newHeight = Math.Clamp(newHeight, 1, 32768);

					if (inParallel)
                    {
						// 3a) GetBytes in parallel, transform in parallel
						bytes = (await obj.GetBytes()).ToArray();
						if (bytes.LongLength <= 0)
						{
							image.Dispose();
							return null;
						}

						byte[] resizedBytes = new byte[(newWidth * newHeight * obj.Bitdepth)];
                        await Task.Run(() =>
                        {
                            Parallel.For(0, newHeight, y =>
                            {
                                for (int x = 0; x < newWidth; x++)
                                {
                                    int srcX = (int) (x / scale);
                                    int srcY = (int) (y / scale);
                                    srcX = Math.Clamp(srcX, 0, image.Width - 1);
                                    srcY = Math.Clamp(srcY, 0, image.Height - 1);
                                    int srcIndex = (srcY * image.Width + srcX) * obj.Bitdepth;
                                    int destIndex = (y * newWidth + x) * obj.Bitdepth;
                                    if (srcIndex + obj.Bitdepth <= bytes.Length && destIndex + obj.Bitdepth <= resizedBytes.Length)
                                    {
                                        Array.Copy(bytes, srcIndex, resizedBytes, destIndex, obj.Bitdepth);
                                    }
                                }
                            });
                        });

                        image.Dispose();

						obj.Metrics["rescale_parallel"] = sw.Elapsed.TotalMilliseconds;
					}
					else
                    {
						// 3b) Resize in single thread async using ImageSharp
						await Task.Run(() =>
						{
							image.Mutate(ctx => ctx.Resize(newWidth, newHeight));
						});

						obj.Metrics["rescale"] = sw.Elapsed.TotalMilliseconds;
					}

					sw.Restart();
				}

                string? base64 = string.Empty;

				// 4) Serialize as string
				if (inParallel)
				{
					// 4a) Aufteilung in Chunks (dieser Teil ist korrekt)
					byte[][] byteChunks = await Task.Run(() =>
					{
						int processorCount = Environment.ProcessorCount;
						int chunkSize = (int) Math.Ceiling((double) bytes.Length / processorCount);
						return Enumerable.Range(0, processorCount)
							.Select(i => bytes.Skip(i * chunkSize).Take(chunkSize).ToArray())
							.Where(chunk => chunk.Length > 0)
							.ToArray();
					});

					// 4b) Base64-Kodierung in Parallel und Zusammenfügen des Strings
					base64 = await Task.Run(() =>
					{
						// Ergebnis-Array zum Speichern der Base64-Teil-Strings
						string[] base64Parts = new string[byteChunks.Length];

						// Parallele Kodierung der Byte-Arrays in Base64-Strings
						Parallel.For(0, byteChunks.Length, i =>
						{
							// Korrektur: Base64-String direkt im Ergebnis-Array speichern
							base64Parts[i] = Convert.ToBase64String(byteChunks[i]);
						});

						// Die Base64-Teil-Strings zu einem finalen String zusammenfügen
						return string.Concat(base64Parts);
					});

                    obj.Metrics["serialize_parallel"] = sw.Elapsed.TotalMilliseconds;
				}
				else
                {
                    using var ms = new MemoryStream();
                    IImageEncoder encoder = format.ToLower() switch
                    {
                        "png" => new SixLabors.ImageSharp.Formats.Png.PngEncoder(),
                        "jpeg" or "jpg" => new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder(),
                        "gif" => new SixLabors.ImageSharp.Formats.Gif.GifEncoder(),
                        "tiff" or "tif" => new SixLabors.ImageSharp.Formats.Tiff.TiffEncoder(),
						_ => new SixLabors.ImageSharp.Formats.Bmp.BmpEncoder()
                    };

                    await image.SaveAsync(ms, encoder);
                    bytes = ms.ToArray();

                    base64 = await Task.Run(() =>
                    {
                        return Convert.ToBase64String(bytes);
                    });

					obj.Metrics["serialize"] = sw.Elapsed.TotalMilliseconds;
				}

                if (!createAsNewObj)
                {
                    obj.SetImage(image);
				}
                else
                {
                    var newObj = new ImageObj(image, obj.Name + "_serialized");
				}

				// DON'T dispose image!
				return base64;
			}
			catch (Exception ex)
            {
                Console.WriteLine($"Error serializing image '{obj.Name}' (ID: {obj.Id}) to base64: {ex.Message}");
                return null;
            }
            finally
            {
                sw.Stop();
			}
		}*/


		public static async Task<string?> SerializeBase64Async(ImageObj obj, string format = "png", float scale = 1.0f)
		{
			Stopwatch sw = Stopwatch.StartNew();
			Image<Rgba32>? image = null;

			if (obj.Img == null)
			{
				sw.Stop();
				Console.WriteLine($"SerializeBase64Async: ImageObj '{obj.Name}' has no image data. Duration: {sw.ElapsedMilliseconds}ms");
				return null;
			}

			format = format.ToLowerInvariant().Trim('.');
			if (!ImageCollection.SupportedFormats.Contains(format))
			{
				Console.WriteLine($"SerializeBase64Async: Unsupported format specified('{format}'), defaulting to PNG.");
				format = "png";
			}

			scale = Math.Clamp(scale, 0.005f, 100.0f);

			try
			{
				// 1) Clone - (Rechenintensiv, Task.Run ist gut)
				image = await Task.Run(() => obj.Img.CloneAs<Rgba32>());
				if (image == null)
				{
					return null;
				}

				// 2) Optionally scale
				if (Math.Abs(scale - 1.0f) > 0.01f)
				{
					int newWidth = (int) (image.Width * scale);
					int newHeight = (int) (image.Height * scale);
					newWidth = Math.Clamp(newWidth, 1, 32768);
					newHeight = Math.Clamp(newHeight, 1, 32768);

					await Task.Run(() =>
					{
						image.Mutate(ctx => ctx.Resize(newWidth, newHeight));
					});
				}

				// 3) Serialize as Base64
				using var ms = new MemoryStream();

				// 3a) Wähle Encoder (verbesserte Encoder-Initialisierung)
				IImageEncoder encoder = format.ToLower() switch
				{
					"png" => new SixLabors.ImageSharp.Formats.Png.PngEncoder(),
					"jpeg" or "jpg" => new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder(),
					"gif" => new SixLabors.ImageSharp.Formats.Gif.GifEncoder(),
					"tiff" or "tif" => new SixLabors.ImageSharp.Formats.Tiff.TiffEncoder(),
					_ => new SixLabors.ImageSharp.Formats.Png.PngEncoder()
				};

				// 3b) Speichern in MemoryStream
				await image.SaveAsync(ms, encoder);

				// 3c) Convert to Base64
				byte[] bytes = ms.ToArray();
				string base64 = Convert.ToBase64String(bytes);
				return base64;
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Error serializing image '{obj.Name}' (ID: {obj.Id}) to base64: {ex.Message}");
				return null;
			}
			finally
			{
				image?.Dispose();
				sw.Stop();
				Console.WriteLine($"SerializeBase64Async: Image '{obj.Name}' (Format: {format}, Scale: {scale}x) duration: {sw.ElapsedMilliseconds}ms");
			}
		}

		public static async Task<ImageObj?> DeserializeBase64Async(string base64, string? expectedFormat = null, string? name = null)
		{
			Stopwatch sw = Stopwatch.StartNew();

			if (string.IsNullOrWhiteSpace(base64))
			{
				sw.Stop();
				Console.WriteLine($"DeserializeBase64Async: Input string is empty. Duration: {sw.ElapsedMilliseconds}ms");
				return null;
			}

            expectedFormat ??= "png";
            name ??= "deserialized_";

			string formatFromMime = expectedFormat;
			int endMimeType = base64.IndexOf(',');
			if (endMimeType > 0 && base64.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
			{
				string mimePart = base64[5..endMimeType];
				base64 = base64[(endMimeType + 1)..];
				if (mimePart.Contains('/'))
				{
					formatFromMime = mimePart.Split('/')[1].Split(';')[0];
				}
			}

			expectedFormat = formatFromMime.ToLowerInvariant().Trim('.');
			if (!ImageCollection.SupportedFormats.Contains(expectedFormat))
			{
				Console.WriteLine($"DeserializeBase64Async: Unsupported format '{expectedFormat}' extracted, defaulting to PNG.");
				expectedFormat = "png";
			}

			try
			{
				byte[] imageBytes = Convert.FromBase64String(base64);

				// 2) Load using ImageSharp
				Image<Rgba32> image = await Task.Run(() =>
				{
					return SixLabors.ImageSharp.Image.Load<Rgba32>(imageBytes);
				});

				if (image == null)
				{
					return null;
				}

				var imageObj = new ImageObj(image, name);
                if (string.IsNullOrEmpty(imageObj.Name) || imageObj.Name == "unknown")
                {
                    imageObj.Name = imageObj.Id.ToString();
				}

				return imageObj;
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Error deserializing base64 to image '{name}': {ex.Message}");
				return null;
			}
			finally
			{
				sw.Stop();
				Console.WriteLine($"DeserializeBase64Async: Image '{name}' (Expected Format: {expectedFormat}) duration: {sw.ElapsedMilliseconds}ms");
			}
		}

        public static async Task<ImageObj?> CreateFromData(IEnumerable<byte> bytes, string? file = null)
        {
            if (bytes == null || !bytes.Any())
            {
                return null;
            }
            ImageObj obj;
            try
            {
                SixLabors.ImageSharp.Image<Rgba32>? img = await Task.Run(() =>
                {
                    return SixLabors.ImageSharp.Image.Load<Rgba32>(bytes.ToArray());
                });
                if (img == null)
                {
                    return null;
				}

                obj = new ImageObj(img, file ?? "serialized_");
			}
            catch (Exception ex)
            {
                Console.WriteLine($"Error creating ImageObj from byte array: {ex.Message}");
                return null;
            }

            return obj;
		}

		public static async Task<ImageObj?> CreateFromBase64(string base64String, float? scale = null, string? name = null)
        {
            if (string.IsNullOrWhiteSpace(base64String))
            {
                return null;
			}

            scale ??= 1.0f;

			ImageObj? obj = await DeserializeBase64Async(base64String, null, name);
            if (obj != null && Math.Abs(scale.Value - 1.0f) > 0.01f && obj.Img != null)
            {
                await obj.ResizeAsync(scale.Value);
			}

            return obj;
		}

	}
}