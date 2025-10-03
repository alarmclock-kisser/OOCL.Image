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

		public ImageObjDto(IEnumerable<int> bytesAsInts, string fileName, string contentType)
		{
			byte[] bytes = bytesAsInts.Select(b => (byte)b).AsParallel().ToArray();

			var task = ConvertToImageObjAsync(bytes, fileName, contentType).ConfigureAwait(false);
			task.GetAwaiter().OnCompleted(() =>
			{
				var result = task.GetAwaiter().GetResult();
				this.Id = result.Id;
				this.Info = result.Info;
				this.Data = result.Data;
			});
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
