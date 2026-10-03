using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using FeudalSim.Sim;
using FeudalSim.Sim.Commands;
using FeudalSim.Sim.Persistence;
using FeudalSim.Sim.Systems;
using MemoryPack;
using MessagePack;

namespace FeudalSim.Benchmarks;

public static class Program
{
    public static void Main(string[] args) => BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
}

/// <summary>M0-06: MessagePack vs MemoryPack on a 1,500-person snapshot (20 §4.4).</summary>
[MemoryDiagnoser]
[ShortRunJob]
public class SerializationBenchmarks
{
    private SaveImage _image = null!;
    private MpSaveImage _mp = null!;
    private byte[] _msgpackLz4 = [];
    private byte[] _msgpackRaw = [];
    private byte[] _memorypack = [];
    private static readonly MessagePackSerializerOptions Lz4 = MessagePackSerializerOptions.Standard.WithCompression(MessagePackCompression.Lz4BlockArray);

    [GlobalSetup]
    public void Setup()
    {
        var world = new SimWorld(1).AddSystem(new WanderSystem()).AddSystem(new NeedsDecaySystem());
        for (var i = 0; i < 1_500; i++)
        {
            world.Enqueue(new CommandEnvelope(i + 1, 0, CommandSource.Scenario, new SpawnPerson($"Person {i}", i % 40 * 5f, i / 40 * -5f)));
        }

        for (var i = 0; i < 2_000; i++) { world.Step(); }
        _image = SaveCodec.Capture(world);
        _mp = MpSaveImage.From(_image);
        _msgpackLz4 = MessagePackSerializer.Serialize(_image, Lz4);
        _msgpackRaw = MessagePackSerializer.Serialize(_image);
        _memorypack = MemoryPackSerializer.Serialize(_mp);
        Console.WriteLine($"SIZES msgpack+lz4={_msgpackLz4.Length:N0} B, msgpack={_msgpackRaw.Length:N0} B, memorypack={_memorypack.Length:N0} B");
    }

    [Benchmark(Baseline = true)] public byte[] MessagePack_Lz4_Serialize() => MessagePackSerializer.Serialize(_image, Lz4);
    [Benchmark] public byte[] MessagePack_Raw_Serialize() => MessagePackSerializer.Serialize(_image);
    [Benchmark] public byte[] MemoryPack_Serialize() => MemoryPackSerializer.Serialize(_mp);
    [Benchmark] public SaveImage MessagePack_Lz4_Deserialize() => MessagePackSerializer.Deserialize<SaveImage>(_msgpackLz4, Lz4);
    [Benchmark] public SaveImage MessagePack_Raw_Deserialize() => MessagePackSerializer.Deserialize<SaveImage>(_msgpackRaw);
    [Benchmark] public MpSaveImage? MemoryPack_Deserialize() => MemoryPackSerializer.Deserialize<MpSaveImage>(_memorypack);
}

[MemoryPackable]
public sealed partial class MpColumn
{
    public string Name { get; set; } = "";
    public int LayoutVersion { get; set; }
    public int ElementSize { get; set; }
    public byte[] Data { get; set; } = [];
}

[MemoryPackable]
public sealed partial class MpSaveImage
{
    public ulong WorldSeed { get; set; }
    public long Step { get; set; }
    public ulong[] IdCounters { get; set; } = [];
    public string[] Names { get; set; } = [];
    public List<MpColumn> Columns { get; set; } = [];

    public static MpSaveImage From(SaveImage image)
    {
        var people = image.Tables[0];
        return new MpSaveImage
        {
            WorldSeed = image.Header.WorldSeed,
            Step = image.Header.Step,
            IdCounters = image.IdCounters,
            Names = people.Strings[0].Values,
            Columns = people.Columns.Select(c => new MpColumn { Name = c.Name, LayoutVersion = c.LayoutVersion, ElementSize = c.ElementSize, Data = c.Data }).ToList(),
        };
    }
}
