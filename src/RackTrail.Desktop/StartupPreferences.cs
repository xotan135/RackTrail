using System.Text.Json;

namespace RackTrail.Desktop;

public sealed class StartupPreferences
{
    public bool AskToLoadSample { get; set; } = true;
    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RackTrail", "preferences.json");
    public static StartupPreferences Load(string? path = null)
    {
        try { return JsonSerializer.Deserialize<StartupPreferences>(File.ReadAllText(path ?? DefaultPath)) ?? new(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }
    public void Save(string? path = null)
    {
        path ??= DefaultPath;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(this));
    }
}
