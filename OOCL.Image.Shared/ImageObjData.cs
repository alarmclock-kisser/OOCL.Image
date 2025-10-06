using OOCL.Image.Core;

namespace OOCL.Image.Shared
{
	public class ImageObjData
	{
		public Guid Id { get; set; } = Guid.Empty;
		public DateTime DataCreatedAt { get; set; } = DateTime.MinValue;

		public int Width { get; set; } = 0;
		public int Height { get; set; } = 0;
		public string MimeType { get; set; } = "image/png";
		public string Base64Data { get; set; } = string.Empty;

		public double FrameBase64SizeKb { get; set; } = 0.0f;
		public float ScalingFactor { get; set; } = 1.0f;


		public ImageObjData()
		{
			// Parameterloser Konstruktor für die Serialisierung

			this.FrameBase64SizeKb = this.Base64Data.Length > 0 ? (this.Base64Data.Length * 3 / 4.0) / 1024.0 : 0.0;
		}

		public ImageObjData(ImageObj? obj, string format = "png")
		{
			if (obj == null)
			{
				return;
			}

			this.Id = obj.Id;
			this.DataCreatedAt = DateTime.Now;

			this.Width = obj.Width;
			this.Height = obj.Height;

			if (!ImageCollection.SupportedFormats.Contains(format.ToLower()))
			{
				format = "png";
			}

			this.MimeType = "image/" + format;
			this.Base64Data = obj.AsBase64Image(format).GetAwaiter().GetResult() ?? string.Empty;

			if (!string.IsNullOrEmpty(this.Base64Data))
			{
				int padding = this.Base64Data.EndsWith("==") ? 2 : this.Base64Data.EndsWith("=") ? 1 : 0;
				double bytes = (this.Base64Data.Length * 3 / 4.0) - padding;
				this.FrameBase64SizeKb = bytes / 1024.0;
			}
			this.ScalingFactor = obj.ScalingFactor;
		}
	}
}