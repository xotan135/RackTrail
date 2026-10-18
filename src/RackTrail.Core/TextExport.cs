using System.Text;
namespace RackTrail.Core;

public enum TextExportLayout { Map, Outline }
public record TextExportOptions(int Width = 120, bool Unicode = false, bool Detailed = true, bool IncludeIps = true, bool IncludeCidr = true, bool IncludeVlan = true, bool IncludeRoles = true, bool IncludeLabels = true, bool WindowsLineEndings = true, TextExportLayout Layout = TextExportLayout.Map);
public static class TextExporter
{
    public static string Export(Project p, TextExportOptions o) => o.Layout == TextExportLayout.Map ? SpatialTextExporter.Export(p,o) : Outline(p,o);
    public static string Outline(Project p, TextExportOptions o)
    {
        int width = Math.Clamp(o.Width, 40, 240); var lines = new List<string>();
        string Clean(string s) => o.Unicode ? s : new string(s.Normalize(NormalizationForm.FormKD).Select(c => c is >= ' ' and <= '~' ? c : '?').ToArray());
        IEnumerable<string> Wrap(string text, int size) { text = Clean(text).Replace('\r', ' ').Replace('\n', ' '); if (text.Length == 0) { yield return ""; yield break; } while (text.Length > size) { int at = text.LastIndexOf(' ', size - 1, size); if (at < 1) at = size; yield return text[..at]; text = text[at..].TrimStart(); } yield return text; }
        char h = o.Unicode ? '─' : '-', v = o.Unicode ? '│' : '|'; string tl = o.Unicode ? "┌" : "+", tr = o.Unicode ? "┐" : "+", bl = o.Unicode ? "└" : "+", br = o.Unicode ? "┘" : "+";
        var ordered = p.Nodes.OrderBy(n => n.Id, StringComparer.Ordinal).ToList(); var refs = ordered.Select((n, i) => (n.Id, Ref: $"N{i + 1:00}")).ToDictionary(x => x.Id, x => x.Ref);
        void Box(IEnumerable<string> content, int indent) { var items = content.ToList(); int size = Math.Min(width - indent, Math.Max(28, items.Select(s => Clean(s).Length + 4).DefaultIfEmpty(28).Max())); int inner = size - 4; string pad = new(' ', indent); lines.Add(pad + tl + new string(h, inner + 2) + tr); foreach (var text in items) foreach (var line in Wrap(text, inner)) lines.Add(pad + v + " " + line.PadRight(inner) + " " + v); lines.Add(pad + bl + new string(h, inner + 2) + br); }
        List<string> Hierarchy(Node n, int size)
        {
            int inner = size - 4;
            var body = new List<string>();
            void Add(string text) { body.AddRange(Wrap(text, inner)); }
            Add($"[{refs[n.Id]}] {n.Name} / {n.DisplayType}");
            if (o.IncludeCidr && n.Cidr.Length > 0) Add(n.Cidr); if (o.IncludeVlan && n.VlanId.Length > 0) Add("VLAN " + n.VlanId);
            if (o.Detailed) { if (o.IncludeIps && n.Addresses.Length > 0) Add(n.Addresses); if (o.IncludeRoles && n.Role.Length > 0) Add(n.Role); }
            if(n.IsStack)
            {
                if(n.StackTechnology.Length>0)Add(n.StackTechnology);
                if(o.Detailed)
                {
                    if(n.ManagementInterface.Length>0)Add("Shared management: "+n.ManagementInterface);
                    foreach(var member in n.StackMembers.OrderBy(m=>m.Number))
                    {
                        Add(string.Join(" | ",new[]{$"Member {member.Number}",member.Name,member.Model,o.IncludeRoles?member.Role:"",member.Status}.Where(s=>s.Length>0)));
                        if(member.SerialNumber.Length>0)Add("  Serial: "+member.SerialNumber);
                    }
                }
            }
            var children = ordered.Where(x => x.ParentId == n.Id).ToList();
            foreach (var child in children) { body.Add(""); if (o.Detailed && inner >= 28) body.AddRange(Hierarchy(child, inner)); else Add($"Contains [{refs[child.Id]}] {child.Name}"); }
            var result = new List<string> { tl + new string(h, size - 2) + tr }; result.AddRange(body.Select(line => v + " " + line.PadRight(inner) + " " + v)); result.Add(bl + new string(h, size - 2) + br); return result;
        }
        foreach (var line in Wrap(p.Name, width)) lines.Add(line); lines.Add(new string(h, width));
        // Build a spanning forest from real links, never from container membership.
        // Back-links and cycles keep their exact endpoints in the connection ledger.
        lines.Add("TOPOLOGY / actual network links");
        var visited = new HashSet<string>();
        var devices = ordered.Where(n => !n.IsContainer).ToList();
        var adjacency = devices.ToDictionary(n => n.Id, n => p.Connections.Where(c => c.From == n.Id || c.To == n.Id).OrderBy(c => c.From == n.Id ? c.To : c.From, StringComparer.Ordinal).ToList());
        void Topology(Node n, int depth, HashSet<string> path)
        {
            int indent = Math.Min(depth * 4, width - 28);
            if (visited.Contains(n.Id)) { foreach (var line in Wrap($"-> [{refs[n.Id]}] {n.Name} (reference)", width - indent)) lines.Add(new string(' ', indent) + line); return; }
            visited.Add(n.Id); path.Add(n.Id); Box(new[] { $"[{refs[n.Id]}] {n.Name}" }.Concat(o.Detailed && o.IncludeIps && n.Addresses.Length > 0 ? new[] { n.Addresses } : Array.Empty<string>()), indent);
            foreach (var c in adjacency[n.Id]) { var id = c.From == n.Id ? c.To : c.From; if (path.Contains(id)) continue; var next = devices.Find(x => x.Id == id); if (next == null) continue; lines.Add(new string(' ', indent + 2) + v); if (o.IncludeLabels && c.LabelFor(p).Length > 0) foreach (var label in Wrap(c.LabelFor(p), width - indent - 6)) lines.Add(new string(' ', indent + 2) + v + "  " + label); Topology(next, depth + 1, new(path)); }
        }
        foreach (var n in devices.OrderBy(n => n.Kind == NodeKind.Internet ? 0 : n.Kind == NodeKind.Firewall ? 1 : 2).ThenBy(n => n.Id, StringComparer.Ordinal)) if (!visited.Contains(n.Id)) { Topology(n, 0, []); lines.Add(""); }
        lines.Add("HIERARCHY / container membership"); lines.Add("Connections are listed separately below; containment is not a link.");
        int section = 0; foreach (var n in ordered.Where(n => n.ParentId == null)) { lines.Add(""); lines.Add($"SECTION {++section} / {refs[n.Id]}"); lines.AddRange(Hierarchy(n, width)); }
        if (o.Detailed) foreach (var n in ordered.Where(n => p.Ancestors(n).Count() > (width - 28) / 4)) { lines.Add(""); lines.Add($"SECTION {++section} / {refs[n.Id]} / deep hierarchy continuation"); lines.AddRange(Hierarchy(n, width)); }
        lines.Add(""); lines.Add("NETWORK CONNECTIONS / cross-section references"); lines.Add(new string(h, width));
        foreach (var c in p.Connections.OrderBy(c => c.From, StringComparer.Ordinal).ThenBy(c => c.To, StringComparer.Ordinal).ThenBy(c => c.Id, StringComparer.Ordinal))
        {
            var a = p.Nodes.Find(n => n.Id == c.From); var b = p.Nodes.Find(n => n.Id == c.To); if (a == null || b == null) continue;
            string endpoint(Node n) => $"[{refs[n.Id]}] {n.Name}";
            Box(new[] { endpoint(a) }, 2); lines.Add("    " + v);
            if (o.IncludeLabels && c.LabelFor(p).Length > 0) foreach (var s in Wrap(c.LabelFor(p), width - 8)) lines.Add("    " + v + "  " + s);
            lines.Add("    " + v); Box(new[] { endpoint(b) }, 2); lines.Add("");
        }
        if (p.Connections.Count == 0) lines.Add("No network connections defined.");
        return string.Join(o.WindowsLineEndings ? "\r\n" : "\n", lines) + (o.WindowsLineEndings ? "\r\n" : "\n");
    }
    public static string Markdown(Project p, TextExportOptions o) => "```text" + (o.WindowsLineEndings ? "\r\n" : "\n") + Export(p, o) + "```";
}
