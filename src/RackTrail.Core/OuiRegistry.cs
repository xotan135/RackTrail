namespace RackTrail.Core;
public static class OuiRegistry
{
    public static IReadOnlyDictionary<string,string> Parse(string csv)
    {
        if(csv.Length>1_000_000)throw new ArgumentException("Use a MAC vendor CSV under 1 MB.");
        var result=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(var line in csv.Split('\n',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries)) {
            var parts=line.Split(',',2,StringSplitOptions.TrimEntries);
            if(parts.Length!=2)continue;
            string prefix=DeviceClassifier.Mac(parts[0]);
            if(prefix.Length!=6||parts[1].Length==0||parts[1].Length>200)continue;
            result[prefix]=parts[1];
        }
        return result;
    }
}
