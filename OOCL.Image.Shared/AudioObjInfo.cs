using OOCL.Image.Core;

namespace OOCL.Image.Shared
{
    public class AudioObjInfo
    {
        public Guid Id { get; set; } = Guid.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.MinValue;
        public string FilePath { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;

        public int SampleRate { get; set; } = 0;
        public int Channels { get; set; } = 0;
        public int BitDepth { get; set; } = 0;

        public string Length { get; set; } = "0";
        public double DurationSeconds { get; set; } = 0.0;
        public float SizeInMb { get; set; } = 0.0f;

        public bool IsProcessing { get; set; } = false;
        public bool OnHost { get; set; } = false;
        public bool OnDevice { get; set; } = false;
        public string Pointer { get; set; } = "<0>";
        public string Form { get; set; } = "Unknown";


		public float Bpm { get; set; } = 0.0f;
        public float Timing { get; set; } = 0.0f;
        public float ScannedBpm { get; set; } = 0.0f;
        public float ScannedTiming { get; set; } = 0.0f;

        public int Volume { get; set; } = 0;
        public bool Playing { get; set; } = false;
        public bool Paused { get; set; } = false;
        public string CurrentTime { get; set; } = "0:00:00.000";
        public int SamplesPerSecond { get; set; } = 0;

        public double? LastExecutionTime { get; set; } = null;

		public IEnumerable<string> Metrics { get; set; } = [];
        public IEnumerable<string> MetricValues { get; set; } = [];



		public AudioObjInfo()
        {
            // Empty ctor
        }

        public AudioObjInfo(AudioObj? obj)
        {
            if (obj == null)
            {
                return;
            }

            this.Id = obj.Id;
            this.CreatedAt = obj.CreatedAt;
            this.FilePath = obj.FilePath;
            this.Name = obj.Name;
            this.SampleRate = obj.SampleRate;
            this.Channels = obj.Channels;
            this.BitDepth = obj.BitDepth;
            this.Length = obj.Length.ToString();
            this.DurationSeconds = obj.TotalSeconds;
            this.IsProcessing = obj.IsProcessing;
            this.Bpm = obj.Bpm;
            this.Timing = obj.Timing;
            this.ScannedBpm = obj.ScannedBpm;
            this.ScannedTiming = obj.ScannedTiming;
            this.Volume = obj.Volume;
            this.Playing = obj.Playing;
            this.Paused = obj.Paused;
            this.CurrentTime = obj.CurrentTime.ToString(@"hh\:mm\:ss\.fff");

            this.OnDevice = obj.OnDevice;
            this.OnHost = obj.OnHost;
            this.Pointer = obj.Pointer != IntPtr.Zero ? $"<{obj.Pointer.ToString("X")}>": "<0>";
            this.SizeInMb = obj.SizeInMb;
            this.Form = obj.Form.ToString();

			// Get samples per second (sampleRate * channels)
            this.SamplesPerSecond = obj.SampleRate * obj.Channels;

			if (obj.Metrics != null && obj.Metrics.Count > 0)
            {
                this.Metrics = obj.Metrics.Keys.ToList();
                this.MetricValues = obj.Metrics.Values.Select(v => v.ToString()).ToList();
			}

            this.LastExecutionTime = obj.LastExecutionTime;
		}

    }
}
