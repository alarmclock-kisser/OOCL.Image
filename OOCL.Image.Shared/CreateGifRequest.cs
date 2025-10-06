namespace OOCL.Image.Shared
{
	public class CreateGifRequest
	{
		public IEnumerable<Guid>? Ids { get; set; }
		public IEnumerable<ImageObjDto>? Dtos { get; set; }
		public int FrameRate { get; set; } = 10;
		public double Rescale { get; set; } = 1.0;
		public bool DoLoop { get; set; } = false;

		public CreateGifRequest()
		{
			// Empty constructor for serialization purposes
		}
	}
}
