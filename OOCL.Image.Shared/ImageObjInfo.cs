using OOCL.Image.Core;
using System.Text.Json.Serialization;

namespace OOCL.Image.Shared
{
	public class SizeInfo
	{
		public int Width { get; set; } = 0;
		public int Height { get; set; } = 0;



		public SizeInfo()
		{
			// Parameterless constructor for serialization
		}

		[JsonConstructor]
		public SizeInfo(int width, int height)
		{
			this.Width = width;
			this.Height = height;
		}

		public override string ToString()
		{
			return $"{Width} x {Height}";
		}

		public float AspectRatio()
		{
			if (Height == 0)
			{
				return 0.0f;
			}
			return (float)Width / (float)Height;
		}
	}

	public class ImageObjInfo
	{
		public Guid Id { get; set; } = Guid.Empty;
		public DateTime CreatedAt { get; set; } = DateTime.MinValue;
		public string FilePath { get; set; } = string.Empty;
		public string Name { get; set; } = string.Empty;

		public SizeInfo Size { get; set; } = new SizeInfo { Width = 0, Height = 0 };
		public int Channels { get; set; } = 4;
		public int BitsPerChannel { get; set; } = 8;
		public int BitsPerPixel { get; set; } = 32;

		public float FrameSizeMb { get; set; } = 0.0f;
		public float FrameBase64SizeMb { get; set; } = 0.0f;
		public float ScalingFactor { get; set; } = 1.0f;

		public bool OnHost { get; set; } = false;
		public bool OnDevice { get; set; } = false;
		public string Pointer { get; set; } = "0";

		public double LastProcessingTimeMs { get; set; } = 0.0;

		public ImageObjInfo()
		{
			// Parameterless constructor for serialization
		}

		public ImageObjInfo(ImageObj? obj)
		{
			if (obj == null)
			{
				return;
			}

			this.Id = obj.Id;
			this.CreatedAt = obj.CreatedAt;

			this.FilePath = !string.IsNullOrEmpty(obj.FilePath) ? Path.GetFullPath(obj.FilePath) : "no file path provided";
			this.Name = Path.GetFileName(this.FilePath);
			if (string.IsNullOrEmpty(this.Name))
			{
				this.Name = this.FilePath;
			}
			if (string.IsNullOrEmpty(this.Name))
			{
				this.Name = obj.Name;
			}
			if (string.IsNullOrEmpty(this.Name))
			{
				this.Name = obj.Id.ToString();
			}

			this.Size = new SizeInfo { Width = obj.Width, Height = obj.Height };
			this.Channels = obj.Channels;
			this.BitsPerChannel = obj.Bitdepth / obj.Channels;
			this.BitsPerPixel = obj.Bitdepth;
			this.FrameSizeMb = obj.SizeMb;
			this.FrameBase64SizeMb = obj.SizeMb * 1.37f;
			this.ScalingFactor = obj.ScalingFactor;
			this.OnHost = obj.OnHost;
			this.OnDevice = obj.OnDevice;
			this.Pointer = obj.Pointer.ToString();
			this.LastProcessingTimeMs = obj.ElapsedProcessingTime;

			// Round last processing time to 3 decimal places
			this.LastProcessingTimeMs = Math.Round(this.LastProcessingTimeMs, 3);
		}
	}
}