using OOCL.Image.OpenCl;
using System;
using System.Runtime.InteropServices;
using System.Security.Claims;
using System.Text.Json.Serialization;

namespace OOCL.Image.Shared
{
	public class OpenClMemoryInfo
	{
		public Guid Id { get; set; } = Guid.Empty;
		public IEnumerable<string> Pointers { get; set; } = [];
		public IEnumerable<string> Lengths { get; set; } = [];

		public string IndexPointer { get; set; } = "0";

		public string IndexLength { get; set; } = "0";
		public int Count { get; set; } = 0;

		public string TotalLength { get; set; } = "0";

		public int DataTypeSize { get; set; } = 0;
		public string DataTypeName { get; set; } = "object";

		public string TotalSizeBytes { get; set; } = "0";


		public OpenClMemoryInfo()
		{
			// Empty default ctor
		}

		public OpenClMemoryInfo(OpenClMem? obj = null)
		{
			if (obj == null)
			{
				return;
			}

			var memObj = obj as OpenClMem;
			if (memObj == null)
			{
				return;
			}

			this.Id = memObj.Id;
			this.Pointers = memObj.Pointers;
			this.Lengths = memObj.Lengths;
			this.IndexPointer = memObj.IndexHandle;
			this.IndexLength = memObj.IndexLength;
			this.Count = memObj.GetCount();
			this.TotalLength = memObj.TotalLength;
			this.DataTypeSize = memObj.TypeSize;
			this.DataTypeName = memObj.Type;
			this.TotalSizeBytes = memObj.Size;
		}
	}
}