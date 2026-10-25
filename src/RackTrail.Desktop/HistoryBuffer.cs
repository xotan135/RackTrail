namespace RackTrail.Desktop;

/// <summary>Keep the newest state even when one embedded-image-heavy document exceeds the budget.</summary>
internal sealed class HistoryBuffer
{
    private readonly LinkedList<string> entries = new();
    private readonly long budget;
    private long bytes;
    public HistoryBuffer(long budget = 32L * 1024 * 1024) { this.budget = budget; }
    public int Count => entries.Count;
    public long Bytes => bytes;
    public void Push(string state)
    {
        entries.AddLast(state); bytes += (long)state.Length * sizeof(char);
        while (entries.Count > 1 && (bytes > budget || entries.Count > 100))
        { bytes -= (long)entries.First!.Value.Length * sizeof(char); entries.RemoveFirst(); }
    }
    public string Pop()
    {
        var state = entries.Last!.Value; entries.RemoveLast(); bytes -= (long)state.Length * sizeof(char); return state;
    }
    public void Clear() { entries.Clear(); bytes = 0; }
}
