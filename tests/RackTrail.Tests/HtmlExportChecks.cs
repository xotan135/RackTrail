using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using RackTrail.Core;

internal static class HtmlExportChecks
{
    public static void Run(Action<bool,string> check)
    {
        var p=SampleNetwork.Create();var before=ProjectStorage.Serialize(p);var html=HtmlExporter.Create(p);
        check(html.StartsWith("<!doctype html>")&&html.Contains("<svg role=\"img\"")&&html.Contains("HEADQUARTERS"),"HTML export contains the actual map in a standalone document");
        check(html.Contains("id=\"zoom-in\"")&&html.Contains("id=\"fit\"")&&html.Contains("id=\"print\"")&&html.Contains("pointermove"),"HTML viewer includes zoom, fit, print and drag controls");
        check(!Regex.IsMatch(html,"<(?:script|link)[^>]+(?:src|href)=",RegexOptions.IgnoreCase)&&!Regex.IsMatch(html,"(?:fetch|XMLHttpRequest|WebSocket)\\s*\\("),"HTML viewer does not load external scripts, styles, or services");
        var script=Regex.Match(html,"<script>(.*?)</script>",RegexOptions.Singleline).Groups[1].Value;
        var hash=Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(script)));
        check(html.Contains("script-src 'sha256-"+hash+"'")&&html.Contains("default-src 'none'"),"Content security policy authorizes only the exact built-in viewer script");
        check(ProjectStorage.Serialize(p)==before,"HTML export leaves project geometry and settings unchanged");
        string malicious="</script><img src=x onerror=alert(1)> & \"quoted\"";p.Name=malicious;p.Nodes[0].Name=malicious;
        html=HtmlExporter.Create(p);
        check(!html.Contains(malicious)&&html.Contains(WebUtility.HtmlEncode(malicious))&&Regex.Matches(html,"<script>").Count==1,"Project and device text cannot inject browser markup or scripts");
        var site=p.Nodes.Single(n=>n.Id=="hq");site.Collapsed=true;
        html=HtmlExporter.Create(p);check(!html.Contains("AD / DNS")&&html.Contains("HEADQUARTERS"),"Collapsed map contents stay hidden in HTML export");
        site.Collapsed=false;
        var icon=Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a5WQAAAAASUVORK5CYII=");
        p.Nodes[0].IconPngBase64=Convert.ToBase64String(icon);
        p.Nodes[0].EmbeddedIconKey=DeviceIcons.ResolveKey(p.Nodes[0]);
        html=HtmlExporter.Create(p);check(html.Contains("href=\"data:image/png;base64,"+Convert.ToBase64String(icon)),"Device pictures are embedded inside the HTML file");
        p.Connections.Clear();p.Connections.AddRange(Enumerable.Range(0,31).Select(i=>new Connection{From="wan",To="fw",Purpose="Hidden overview connection"}));
        check(!HtmlExporter.Create(p).Contains("Hidden overview connection")&&HtmlExporter.Create(p,true).Contains("Hidden overview connection"),"HTML export follows automatic and explicit connection-label settings");
        var empty=HtmlExporter.Create(new());check(empty.Contains("0 visible items")&&empty.Contains("viewBox=\"0 0 800 600\""),"An empty map produces a valid HTML viewer");
    }
}
