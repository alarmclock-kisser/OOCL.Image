using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Collections.Concurrent;
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

        // Ctor with options
        public ImageCollection(bool saveMemory = false, int defaultWidth = 720, int defaultHeight = 480)
        {
            this.DefaultWidth = Math.Max(defaultWidth, 360); // Min is 360px width
            this.DefaultHeight = Math.Max(defaultHeight, 240); // Min is 240px height
            this.SaveMemory = saveMemory;
            if (this.SaveMemory)
            {
                Console.WriteLine("ImageCollection: Memory saving enabled. All images will be disposed on add.");
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



        public static SixLabors.ImageSharp.Size GetSharpSize(int height, int width)
        {
            width = Math.Clamp(width, 1, 32768);
            height = Math.Clamp(height, 1, 32768);

            return new SixLabors.ImageSharp.Size(width, height);
        }

        public static SixLabors.ImageSharp.Color? GetSharpColor(System.Drawing.Color color)
        {
            if (color == System.Drawing.Color.Empty)
            {
                return null;
            }

            return SixLabors.ImageSharp.Color.FromRgba(color.R, color.G, color.B, color.A);
        }

        public static SixLabors.ImageSharp.Color GetSharpColor(string hexColor = "#00000000")
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

        public static System.Drawing.Color GetDrawingColor(SixLabors.ImageSharp.Color color)
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




	}
}