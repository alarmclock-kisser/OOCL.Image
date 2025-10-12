namespace OOCL.Image.Shared
{
    public class ExecuteOnImageRequest
    {
        public Guid ImageId { get; set; }
        public string KernelName { get; set; } = string.Empty;
        public Dictionary<string,string>? Arguments { get; set; }
        public ImageObjDto? OptionalImage { get; set; }
        public float? Rescale { get; set; }
    }

    public class CreateImageRequest
    {
        public int Width { get; set; } = 480;
        public int Height { get; set; } = 360;
        public string KernelName { get; set; } = "mandelbrot00";
        public string BaseColorHex { get; set; } = "#000000";
        public Dictionary<string,string>? Arguments { get; set; }
        public float? Rescale { get; set; }
    }

    public class AudioTimestretchRequest
    {
        public Guid AudioId { get; set; } = Guid.Empty;
        public string KernelName { get; set; } = "timestretch_double03";
        public int ChunkSize { get; set; } = 16384;
        public float Overlap { get; set; } = 0.5f;
		public double SpeedFactor { get; set; } = 1.0f;
        public float InitialBpm { get; set; } = 0.0f;
        public AudioObjDto? OptionalAudio { get; set; }
	}

	public class ExecuteOnAudioRequest
    {
        public Guid AudioId { get; set; } = Guid.Empty;
        public string KernelName { get; set; } = string.Empty;
		public int ChunkSize { get; set; } = 0;
		public float Overlap { get; set; } = 0.0f;
		public Dictionary<string,string>? Arguments { get; set; }
        public AudioObjDto? OptionalAudio { get; set; }
	}
}