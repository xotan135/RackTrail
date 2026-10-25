using System.Diagnostics;
using System.Globalization;

namespace RackTrail.Desktop;

public sealed class PerformanceRecorder
{
    private sealed record Sample(string Operation, double Milliseconds, long Bytes);
    private readonly List<Sample> samples = [];
    public void Record(string operation, long start, long allocatedBefore)
    {
        if (samples.Count < 20000) samples.Add(new(operation, Stopwatch.GetElapsedTime(start).TotalMilliseconds,
            Math.Max(0, GC.GetAllocatedBytesForCurrentThread() - allocatedBefore)));
    }
    public void RecordValue(string operation,double milliseconds,long bytes){if(samples.Count<20000)samples.Add(new(operation,milliseconds,bytes));}
    public void Write(string directory)
    {
        Directory.CreateDirectory(directory);
        var lines = new List<string> { "operation,samples,p50_ms,p95_ms,max_ms,mean_allocated_bytes" };
        foreach (var group in samples.GroupBy(s => s.Operation))
        {
            var ordered = group.Select(s => s.Milliseconds).Order().ToArray();
            double Percentile(double ratio) => ordered[Math.Min(ordered.Length - 1, (int)Math.Ceiling(ordered.Length * ratio) - 1)];
            lines.Add(FormattableString.Invariant($"{group.Key},{ordered.Length},{Percentile(.5):0.###},{Percentile(.95):0.###},{ordered[^1]:0.###},{group.Average(s => s.Bytes):0}"));
        }
        File.WriteAllLines(Path.Combine(directory, "interaction.csv"), lines);
        using var process = Process.GetCurrentProcess();
        File.WriteAllText(Path.Combine(directory, "machine.txt"),
            $"OS: {System.Runtime.InteropServices.RuntimeInformation.OSDescription}\n" +
            $"Architecture: {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}\n" +
            $"Processors: {Environment.ProcessorCount}\nRuntime: {Environment.Version}\n" +
            $"Managed bytes: {GC.GetTotalMemory(false).ToString(CultureInfo.InvariantCulture)}\n" +
            $"Working set bytes: {process.WorkingSet64.ToString(CultureInfo.InvariantCulture)}\n" +
            "Frame timings measure UI drawing submission, not GPU presentation. Input-queue timings measure the coalesced application's queue, not hardware-to-screen latency. Samples are capped at 20000.\n");
    }
}
