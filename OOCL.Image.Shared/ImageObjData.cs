using OOCL.Image.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

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

		public float FrameBase64SizeMb { get; set; } = 0.0f;
		public float ScalingFactor { get; set; } = 1.0f;


		public ImageObjData()
		{
			// Parameterloser Konstruktor für die Serialisierung
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
		}
	}
}