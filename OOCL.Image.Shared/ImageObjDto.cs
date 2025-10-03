using OOCL.Image.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace OOCL.Image.Shared
{
	public class ImageObjDto
	{
		public Guid Id { get; set; } = Guid.Empty;

		public ImageObjInfo Info { get; set; } = new();
		public ImageObjData Data { get; set; } = new();



		public ImageObjDto()
		{
			// Empty constructor for serialization purposes
		}

		public static async Task<ImageObjDto> FromBytesAsync(byte[] bytes, string fileName, string contentType)
		{
			return await ConvertToImageObjAsync(bytes, fileName, contentType);
		}

		public ImageObjDto(ImageObj? obj)
		{
			if (obj == null)
			{
				return;
			}

			var info = new ImageObjInfo(obj);
			var data = new ImageObjData(obj);

			this.Id = info.Id;
			this.Info = info;
			this.Data = data;
		}

		[JsonConstructor]
		public ImageObjDto(ImageObjInfo? info, ImageObjData? data)
		{
			this.Id = info?.Id ?? Guid.Empty;

			if (info == null || data == null)
			{
				return;
			}

			this.Info = info;
			this.Data = data;
		}



		// Private Task to convert
		private static async Task<ImageObjDto> ConvertToImageObjAsync(byte[] bytes, string name, string contentType)
		{
			var obj = await ImageObj.FromBytesAsync(bytes, name, contentType);
			
			var info = new ImageObjInfo(obj);
			var data = new ImageObjData(obj);

			return new ImageObjDto(info, data);
		}


	}
}
