using System.Globalization;
using System.Security;
using System.Text;
namespace RackTrail.Core;

public static class VisualExporter
{
    public static (double X,double Y,double Width,double Height) Bounds(Project p)
    {
        var nodes=p.Nodes.Where(p.Visible).ToList();var points=p.Connections.SelectMany(c=>MapLayout.Route(p,c)).ToList();
        double x=Math.Min(0,nodes.Select(n=>n.X-40).Concat(points.Select(a=>a.X-40)).DefaultIfEmpty(0).Min()),y=Math.Min(0,nodes.Select(n=>n.Y-40).Concat(points.Select(a=>a.Y-40)).DefaultIfEmpty(0).Min());
        return(x,y,Math.Max(800,nodes.Select(n=>n.X+n.Width+40).Concat(points.Select(a=>a.X+40)).DefaultIfEmpty(800).Max()-x),Math.Max(600,nodes.Select(n=>n.Y+n.DisplayHeight+40).Concat(points.Select(a=>a.Y+40)).DefaultIfEmpty(600).Max()-y));
    }
    public static (double Width,double Height) Size(Project p){var b=Bounds(p);return(b.Width,b.Height);}
    public static string Color(Node n) => n.IsContainer ? "#93a4bc" : n.Kind switch { NodeKind.Firewall => "#f29b72", NodeKind.Internet => "#b9a0ef", NodeKind.Server => "#69c7bf", _ => "#7caaf0" };
    public static string Svg(Project p)
    {
        static string F(double n) => n.ToString("0.##", CultureInfo.InvariantCulture); static string E(string s) => SecurityElement.Escape(s) ?? "";
        var size = Bounds(p); var s = new StringBuilder($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{F(size.Width)}\" height=\"{F(size.Height)}\" viewBox=\"{F(size.X)} {F(size.Y)} {F(size.Width)} {F(size.Height)}\"><rect x=\"{F(size.X)}\" y=\"{F(size.Y)}\" width=\"{F(size.Width)}\" height=\"{F(size.Height)}\" fill=\"#101722\"/>");
        void Node(Node n)
        {
            if(!n.UseDeviceImage||n.CanContainNodes){
                if(!n.CanContainNodes&&n.Shape==DeviceShape.Circle)s.Append($"<ellipse cx=\"{F(n.X+n.Width/2)}\" cy=\"{F(n.Y+n.DisplayHeight/2)}\" rx=\"{F(n.Width/2)}\" ry=\"{F(n.DisplayHeight/2)}\" fill=\"#202d40\" stroke=\"{Color(n)}\"/>");
                else s.Append($"<rect x=\"{F(n.X)}\" y=\"{F(n.Y)}\" width=\"{F(n.Width)}\" height=\"{F(n.DisplayHeight)}\" rx=\"{(n.Shape==DeviceShape.Rectangle?0:10)}\" fill=\"{(n.CanContainNodes ? "#172130" : "#202d40")}\" stroke=\"{Color(n)}\"/>");
            }
            bool image=n.UseDeviceImage&&!n.CanContainNodes;double iconSize=image?Math.Max(20,Math.Min(n.Width-24,n.DisplayHeight-58)):20,iconX=image?n.X+(n.Width-iconSize)/2:n.X+12,iconY=n.Y+14;
            string iconKey=DeviceIcons.ResolveKey(n);var icon=DeviceIcons.Vector(iconKey);bool hasIcon=!n.IsContainer&&(icon!=null||DeviceIcons.EmbeddedPng(n)!=null||n.Kind==NodeKind.Printer);
            if(DeviceIcons.EmbeddedPng(n)!=null)s.Append($"<image x=\"{F(iconX)}\" y=\"{F(iconY)}\" width=\"{F(iconSize)}\" height=\"{F(iconSize)}\" href=\"data:image/png;base64,{n.IconPngBase64}\"/>");
            else if(icon!=null){s.Append($"<g data-device-icon=\"{E(iconKey)}\" fill=\"{icon.Color}\" transform=\"translate({F(iconX)} {F(iconY)}) scale({F(iconSize/icon.Width)} {F(iconSize/icon.Height)})\">");foreach(var path in icon.Paths)s.Append($"<path d=\"{E(path)}\"/>");s.Append("</g>");}
            else if(n.Kind==NodeKind.Printer)s.Append($"<g data-device-icon=\"printer\" transform=\"translate({F(iconX)} {F(n.Y+16)})\" fill=\"none\" stroke=\"{Color(n)}\"><rect x=\"3\" width=\"10\" height=\"6\"/><rect y=\"5\" width=\"16\" height=\"8\" rx=\"2\"/><rect x=\"3\" y=\"10\" width=\"10\" height=\"7\"/></g>");
            double labelX=image?n.X+12:n.X+(hasIcon?38:14);double labelY=image?n.Y+iconSize+34:n.Y+27;
            s.Append($"<text x=\"{F(labelX)}\" y=\"{F(labelY)}\" font-family=\"sans-serif\" font-size=\"13\" fill=\"#edf3fa\">{E(n.Name)}</text><text x=\"{F(labelX)}\" y=\"{F(labelY+21)}\" font-family=\"sans-serif\" font-size=\"11\" fill=\"#a6b7cf\">{E(n.IsContainer ? n.Cidr + (n.VlanId.Length > 0 ? "  VLAN " + n.VlanId : "") : n.VisualDetail)}</text>");
            if(n.IsStack&&n.ShowStackMembers)
            {
                int row=0;foreach(var member in n.StackMembers.OrderBy(m=>m.Number))s.Append($"<text x=\"{F(n.X+14)}\" y=\"{F(n.Y+83+24*row++)}\" font-family=\"sans-serif\" font-size=\"11\" fill=\"#a6b7cf\">{E($"{member.Number:00}  {member.Name} / {member.Role}")}</text>");
            }
        }
        foreach (var n in p.Nodes.Where(n => n.CanContainNodes && p.Visible(n)).OrderBy(n => p.Ancestors(n).Count())) Node(n);
        foreach (var c in p.Connections) { var r = MapLayout.Route(p, c); if (r.Count < 2) continue; s.Append($"<polyline points=\"{string.Join(" ", r.Select(x => $"{F(x.X)},{F(x.Y)}"))}\" fill=\"none\" stroke=\"#7397c1\" stroke-width=\"2\"/>"); var mid = r[r.Count / 2]; if(c.Kind!=ConnectionKind.Hosting)s.Append($"<text x=\"{F(mid.X + 5)}\" y=\"{F(mid.Y - 6)}\" font-family=\"sans-serif\" font-size=\"10\" fill=\"#b7c9e0\">{E(c.LabelFor(p))}</text>"); }
        foreach (var n in p.Nodes.Where(n => !n.CanContainNodes && p.Visible(n))) Node(n);
        return s.Append("</svg>").ToString();
    }
}
