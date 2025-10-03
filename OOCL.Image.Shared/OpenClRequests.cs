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
}