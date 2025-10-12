using OOCL.Image.Core;

namespace OOCL.Image.Shared
{
	public class AudioObjDto
	{
		public Guid Id { get; set; } = Guid.Empty;

		public AudioObjInfo Info { get; set; } = new();
		public AudioObjData Data { get; set; } = new();



		public AudioObjDto()
		{
			// Empty ctor
		}


		public AudioObjDto(AudioObj? obj, bool includeData = false, int compressionBits = 0, bool musLaw = true)
		{
			if (obj == null)
			{
				return;
			}

			this.Id = obj.Id;
			this.Info = new AudioObjInfo(obj);
			
			if (includeData)
			{
				this.Data = new AudioObjData(obj, compressionBits, musLaw);
			}
		}

		public AudioObjDto(AudioObj? obj, int chunkSize, float overlap)
		{
			if (obj == null)
			{
				return;
			}

			this.Id = obj.Id;
			this.Info = new AudioObjInfo(obj);

			this.Data = AudioObjData.FromObjectWithChunksAsync(obj, chunkSize, overlap).Result;
		}

	}
}
