namespace RackTrail.Desktop;

/// <summary>Used under the renderer lock. Active leases remain valid across eviction or cache clearing.</summary>
internal sealed class ImageResourceCache<T> where T : class, IDisposable
{
    private sealed class Entry
    {
        public required string Key;
        public required T Value;
        public long Weight;
        public int Leases;
        public bool Retired;
        public LinkedListNode<Entry>? Position;
    }
    public sealed class Lease : IDisposable
    {
        private Action? release;
        public T Value { get; }
        internal Lease(T value, Action release) { Value = value; this.release = release; }
        public void Dispose() { Interlocked.Exchange(ref release, null)?.Invoke(); }
    }
    private readonly Dictionary<string, Entry> entries = new(StringComparer.Ordinal);
    private readonly LinkedList<Entry> recent = new();
    private readonly long budget;
    private readonly int limit;
    private long weight;
    public ImageResourceCache(long budget = 64L * 1024 * 1024, int limit = 256) { this.budget = budget; this.limit = limit; }
    public Lease? Acquire(string key)
    {
        if (!entries.TryGetValue(key, out var entry)) return null;
        recent.Remove(entry.Position!); recent.AddLast(entry.Position!); entry.Leases++;
        return new(entry.Value, () => { entry.Leases--; if (entry.Retired && entry.Leases == 0) entry.Value.Dispose(); });
    }
    public Lease Add(string key, T value, long decodedBytes)
    {
        var entry = new Entry { Key = key, Value = value, Weight = decodedBytes + (long)key.Length * sizeof(char), Leases = 1 };
        entry.Position = recent.AddLast(entry); entries.Add(key, entry); weight += entry.Weight;
        while (entries.Count > limit || weight > budget)
        {
            var oldest = recent.First!.Value; recent.RemoveFirst(); entries.Remove(oldest.Key); weight -= oldest.Weight;
            oldest.Retired = true; if (oldest.Leases == 0) oldest.Value.Dispose();
        }
        return new(value, () => { entry.Leases--; if (entry.Retired && entry.Leases == 0) entry.Value.Dispose(); });
    }
    public void Clear()
    {
        foreach (var entry in entries.Values) { entry.Retired = true; if (entry.Leases == 0) entry.Value.Dispose(); }
        entries.Clear(); recent.Clear(); weight = 0;
    }
}
