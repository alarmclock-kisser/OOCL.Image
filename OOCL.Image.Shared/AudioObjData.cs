using OOCL.Image.Core;

namespace OOCL.Image.Shared
{
	public class AudioObjData
	{
		public Guid Id { get; set; } = Guid.Empty;

		public float[] Samples { get; set; } = [];

		public IEnumerable<float[]> Chunks { get; set; } = [];
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
			this.Samples = obj.Data;

			this.SizeInMb = obj.SizeInMb;
		}
	}
}
