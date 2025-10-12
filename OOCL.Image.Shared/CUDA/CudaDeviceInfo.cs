
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace OOCL.Image.Shared.CUDA
{
	public class CudaDeviceInfo
	{
		public int DeviceId { get; set; } = -1;

		public string DeviceName { get; set; } = string.Empty;
		public string TotalGlobalMemory { get; set; } = string.Empty;
		public string SharedMemoryPerBlock { get; set; } = string.Empty;
		public string ComputeCapability { get; set; } = string.Empty;
		public string ClockRate { get; set; } = string.Empty;
		public string MultiProcessorCount { get; set; } = string.Empty;
		public string MaxThreadsPerMultiProcessor { get; set; } = string.Empty;
		public string MaxThreadsPerBlock { get; set; } = string.Empty;
		public string MaxBlockDim { get; set; } = string.Empty;
		public string MaxGridDim { get; set; } = string.Empty;
		public string TotalConstantMemory { get; set; } = string.Empty;
		public string WarpSize { get; set; } = string.Empty;
		public string MemoryBusWidth { get; set; } = string.Empty;
		public string L2CacheSize { get; set; } = string.Empty;
		public string MaxTexture1D { get; set; } = string.Empty;
		public string MaxTexture2D { get; set; } = string.Empty;
		public string MaxTexture3D { get; set; } = string.Empty;


		public CudaDeviceInfo()
		{
			// Empty ctor
		}

		public override string ToString()
		{
			string nl = Environment.NewLine;
			return $"DeviceId: {this.DeviceId}{nl}" +
				   $"DeviceName: {this.DeviceName}{nl}" +
				   $"TotalGlobalMemory: {this.TotalGlobalMemory}{nl}" +
				   $"SharedMemoryPerBlock: {this.SharedMemoryPerBlock}{nl}" +
				   $"ComputeCapability: {this.ComputeCapability}{nl}" +
				   $"ClockRate: {this.ClockRate}{nl}" +
				   $"MultiProcessorCount: {this.MultiProcessorCount}{nl}" +
				   $"MaxThreadsPerMultiProcessor: {this.MaxThreadsPerMultiProcessor}{nl}" +
				   $"MaxThreadsPerBlock: {this.MaxThreadsPerBlock}{nl}" +
				   $"MaxBlockDim: {this.MaxBlockDim}{nl}" +
				   $"MaxGridDim: {this.MaxGridDim}{nl}" +
				   $"TotalConstantMemory: {this.TotalConstantMemory}{nl}" +
				   $"WarpSize: {this.WarpSize}{nl}" +
				   $"MemoryBusWidth: {this.MemoryBusWidth}{nl}" +
				   $"L2CacheSize: {this.L2CacheSize}{nl}" +
				   $"MaxTexture1D: {this.MaxTexture1D}{nl}" +
				   $"MaxTexture2D: {this.MaxTexture2D}{nl}" +
				   $"MaxTexture3D: {this.MaxTexture3D}";
		}

	}
}
