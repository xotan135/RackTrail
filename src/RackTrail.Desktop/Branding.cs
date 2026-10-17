using Avalonia.Controls;
using Avalonia.Platform;
namespace RackTrail.Desktop;
public static class Branding
{
    public static WindowIcon CreateIcon() => new(AssetLoader.Open(new Uri("avares://RackTrail/Assets/RackTrail.ico")));
}
