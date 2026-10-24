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
    public static string Color(Node n) => MapEditing.ValidColor(n.AccentColor) ? n.AccentColor : n.IsContainer ? "#93a4bc" : n.Kind switch { NodeKind.Firewall => "#f29b72", NodeKind.Internet => "#b9a0ef", NodeKind.Server => "#69c7bf", _ => "#7caaf0" };
    public static string Svg(Project p,bool showConnectionLabels=false)
    {
        static string F(double n) => n.ToString("0.##", CultureInfo.InvariantCulture); static string E(string s) => SecurityElement.Escape(s) ?? "";
        var size = Bounds(p); var s = new StringBuilder($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{F(size.Width)}\" height=\"{F(size.Height)}\" viewBox=\"{F(size.X)} {F(size.Y)} {F(size.Width)} {F(size.Height)}\"><rect x=\"{F(size.X)}\" y=\"{F(size.Y)}\" width=\"{F(size.Width)}\" height=\"{F(size.Height)}\" fill=\"#101722\"/>");
        void Node(Node n)
        {
            if(!n.UseDeviceImage||n.CanContainNodes){
                if(!n.CanContainNodes&&n.Shape==DeviceShape.Circle)s.Append($"<ellipse cx=\"{F(n.X+n.Width/2)}\" cy=\"{F(n.Y+n.DisplayHeight/2)}\" rx=\"{F(n.Width/2)}\" ry=\"{F(n.DisplayHeight/2)}\" fill=\"#202d40\" stroke=\"{Color(n)}\"/>");
                else s.Append($"<rect x=\"{F(n.X)}\" y=\"{F(n.Y)}\" width=\"{F(n.Width)}\" height=\"{F(n.DisplayHeight)}\" rx=\"{(n.Shape==DeviceShape.Rectangle?0:10)}\" fill=\"{(n.CanContainNodes ? "#172130" : "#202d40")}\" stroke=\"{Color(n)}\"/>");
            }
            var presentation=NodePresentation.Create(n);var imageBox=presentation.Icon;
            string iconKey=DeviceIcons.ResolveKey(n);var icon=DeviceIcons.Vector(iconKey)??DeviceIcons.Vector(DeviceIcons.TypeKey(n.Kind));
            if(DeviceIcons.EmbeddedPng(n)!=null)s.Append($"<image x=\"{F(imageBox.X)}\" y=\"{F(imageBox.Y)}\" width=\"{F(imageBox.Width)}\" height=\"{F(imageBox.Height)}\" preserveAspectRatio=\"xMidYMid meet\" href=\"data:image/png;base64,{n.IconPngBase64}\"/>");
            else if(icon!=null&&!n.IsContainer){s.Append($"<g data-device-icon=\"{E(icon.Key)}\" fill=\"{icon.Color}\" transform=\"translate({F(imageBox.X)} {F(imageBox.Y)}) scale({F(imageBox.Width/icon.Width)} {F(imageBox.Height/icon.Height)})\">");foreach(var path in icon.Paths)s.Append($"<path d=\"{E(path)}\"/>");s.Append("</g>");}
            int lineNumber=0;
            foreach(var line in presentation.Labels)
            {
                string clipId="label-"+E(n.Id)+"-"+lineNumber++;
                s.Append($"<defs><clipPath id=\"{clipId}\"><rect x=\"{F(line.X)}\" y=\"{F(line.Y)}\" width=\"{F(line.Width)}\" height=\"{F(line.Size*1.4)}\"/></clipPath></defs>");
                s.Append($"<text x=\"{F(line.X+(line.Center?line.Width/2:0))}\" y=\"{F(line.Y+line.Size)}\" text-anchor=\"{(line.Center?"middle":"start")}\" clip-path=\"url(#{clipId})\" font-family=\"sans-serif\" font-size=\"{F(line.Size)}\" fill=\"#edf3fa\">{E(line.Text)}</text>");
            }

        }
        var captions=ConnectionCaptions.Create(p,p.Connections.Where(c=>showConnectionLabels||ConnectionRouter.ShowLabel(p)));
        var connections=p.Connections.ToLookup(c=>MapEditing.ConnectionLayer(p,c));
        foreach(var layer in MapEditing.PaintOrder(p).GroupBy(n=>MapEditing.Layer(p,n)))
        {
            foreach(var n in layer.Where(n=>n.CanContainNodes))Node(n);
            foreach(var c in connections[layer.Key])
            {
                var r=MapLayout.Route(p,c);if(r.Count<2)continue;
                var points=string.Join(" ",r.Select(x=>$"{F(x.X)},{F(x.Y)}"));
                s.Append($"<polyline points=\"{points}\" fill=\"none\" stroke=\"#101722\" stroke-width=\"5.6\"/><polyline points=\"{points}\" fill=\"none\" stroke=\"#7397c1\" stroke-width=\"1.6\"/>");
                if(captions.TryGetValue(c.Id,out var caption))
                {
                    var box=caption.Box;
                    s.Append($"<g data-connection-caption=\"{E(c.Id)}\"><rect x=\"{F(box.X)}\" y=\"{F(box.Y)}\" width=\"{F(box.Width)}\" height=\"{F(box.Height)}\" rx=\"3\" fill=\"#101722\"/><text x=\"{F(box.X+6)}\" y=\"{F(box.Y+14)}\" font-family=\"sans-serif\" font-size=\"10\" fill=\"#b7c9e0\">{E(caption.Text)}</text></g>");
                }
            }
            foreach(var n in layer.Where(n=>!n.CanContainNodes))Node(n);
        }
        return s.Append("</svg>").ToString();
    }
}
