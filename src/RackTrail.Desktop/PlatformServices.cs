using Avalonia.Controls;
using Avalonia.Platform.Storage;
namespace RackTrail.Desktop;

public interface IPlatformServices
{
    string RecoveryPath { get; }
    Task<string?> OpenProject();
    Task<string?> SavePath(string title, string extension);
    Task CopyText(string text);
    void OpenFolder(string path);
}
public sealed class DesktopPlatformServices(Window window) : IPlatformServices
{
    public string RecoveryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RackTrail", "recovery.racktrail.json");
    public async Task<string?> OpenProject() { var files = await window.StorageProvider.OpenFilePickerAsync(new() { Title = "Open network project", AllowMultiple = false, FileTypeFilter = [new("Network project") { Patterns = ["*.json"] }] }); return files.FirstOrDefault()?.TryGetLocalPath(); }
    public async Task<string?> SavePath(string title, string extension) { var file = await window.StorageProvider.SaveFilePickerAsync(new() { Title = title, DefaultExtension = extension, SuggestedFileName = "racktrail." + extension, ShowOverwritePrompt = true, FileTypeChoices = [new(extension.ToUpperInvariant()) { Patterns = ["*." + extension] }] }); return file?.TryGetLocalPath(); }
    public async Task CopyText(string text) { if (window.Clipboard is { } clipboard) await clipboard.SetTextAsync(text); }
    public void OpenFolder(string path){Directory.CreateDirectory(path);System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path){UseShellExecute=true});}
}
