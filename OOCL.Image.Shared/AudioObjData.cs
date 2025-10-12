using OOCL.Image.Core;

namespace OOCL.Image.Shared
{
	public class AudioObjData
	{
		public Guid Id { get; set; } = Guid.Empty;

		public byte[] Samples { get; set; } = [];
		public string Length { get; set; } = "0";

		public IEnumerable<byte[]> Chunks { get; set; } = [];
		public int ChunkSize { get; set; } = 0;
		public float Overlap { get; set; } = 0.0f;

		public double SizeInMb { get; set; }

		public AudioObjData()
		{
			// Empty ctor
		}

		public AudioObjData(AudioObj? obj)
		{
			if (obj == null)
			{
				return;
			}

			this.Id = obj.Id;
			this.ChunkSize = obj.ChunkSize;
			this.Overlap = obj.OverlapSize / (obj.ChunkSize > 0 ? obj.ChunkSize : 1);
			this.Samples = obj.GetBytes();
			this.SizeInMb = obj.SizeInMb;
		}


		public static async Task<AudioObjData> FromObjectWithDataAsync(AudioObj? obj, bool keepData = true)
		{
			if (obj == null)
			{
				return new AudioObjData();
			}

			var data = new AudioObjData(obj)
			{
				Samples = await obj.GetBytesAsync(),
				Chunks = []
			};

			if (!keepData)
			{
				obj.SetData([]);
			}

			return data;
		}

		public static async Task<AudioObjData> FromObjectWithChunksAsync(AudioObj? obj, int chunkSize = 2048, float overlap = 0.5f, bool keepData = true)
		{
			if (obj == null)
			{
				return new AudioObjData();
			}

			var data = new AudioObjData(obj)
			{
				Samples = [],
				Chunks = await obj.GetChunksBytes(chunkSize, overlap),
				ChunkSize = chunkSize,
				Overlap = overlap
			};

			return data;
		}
	}
}
