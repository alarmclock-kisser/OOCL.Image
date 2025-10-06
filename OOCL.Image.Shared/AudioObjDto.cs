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


		public AudioObjDto(AudioObj? obj, bool includeData = false)
		{
			if (obj == null)
			{
				return;
			}

			this.Id = obj.Id;
			this.Info = new AudioObjInfo(obj);
			
			if (includeData)
			{
				this.Data = new AudioObjData(obj);
			}
		}

	}
}
