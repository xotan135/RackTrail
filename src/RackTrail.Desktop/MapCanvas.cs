using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using RackTrail.Core;
using System.Globalization;
namespace RackTrail.Desktop;

public class MapCanvas : Control
{
    static MapCanvas(){AffectsRender<MapCanvas>(ThemeVariantScope.ActualThemeVariantProperty);}
    public EditorViewModel Vm { get; }
    public double Zoom { get; set; } = .65;
    public Vector Pan { get; set; } = new(40, 35);
    public bool Snap { get; set; } = true;
    public bool Exporting { get; set; }
    public bool ShowAllConnectionLabels { get; set; }
    public bool FocusConnections { get; set; } = true;
    private Connection? hoveredConnection;
    internal bool RelatedConnection(Connection c)
    {
        bool Selected(string id)=>Vm.Project.VisibleEndpoint(id) is {} n&&(Vm.Selection.Contains(n.Id)||Vm.Project.Ancestors(n).Any(a=>Vm.Selection.Contains(a.Id)));
        return Vm.SelectedConnection==c||Selected(c.From)||Selected(c.To);
    }
    internal bool ShowConnectionLabel(Connection c)
    {
        if(c.Kind==ConnectionKind.Hosting)return false;
        if(ShowAllConnectionLabels)return true;
        if(!Exporting&&(Vm.SelectedConnection==c||hoveredConnection==c))return true;
        if(!Exporting&&FocusConnections&&(Vm.Selection.Count>0||Vm.SelectedConnection!=null)&&!RelatedConnection(c))return false;
        return ConnectionRouter.ShowLabel(Vm.Project)&&(Exporting||Zoom>=.85);
    }
    // Keep pointer gestures responsive; full obstacle routing resumes on release.
    private List<MapPoint> DisplayRoute(Connection c) => !Exporting&&(dragging||resizing!=null)?MapLayout.PreviewRoute(Vm.Project,c):MapLayout.Route(Vm.Project,c);
    private Point last, start;
    private bool dragging, panning, marquee;
    private Rect selectionRect;
    private Node? resizing;
    private bool VisibleConnection(Connection c) => Vm.Project.VisibleEndpoint(c.From) is {} a&&Vm.MatchesFilter(a)&&Vm.Project.VisibleEndpoint(c.To) is {} b&&Vm.MatchesFilter(b);
    private Rect Handle(Node n)=>new(n.X+n.Width-12/Zoom,n.Y+n.DisplayHeight-12/Zoom,12/Zoom,12/Zoom);
    public MapCanvas(EditorViewModel vm) { Vm = vm; Focusable = true; ClipToBounds = true; vm.Changed += InvalidateVisual; }
    public Point World(Point p) => new((p.X - Pan.X) / Zoom, (p.Y - Pan.Y) / Zoom);
    internal List<Node> PaintOrder() => MapEditing.PaintOrder(Vm.Project).Where(Vm.Visible).ToList();
    internal Node? NodeAt(Point point) => PaintOrder().LastOrDefault(n => new Rect(n.X, n.Y, n.Width, n.DisplayHeight).Contains(point));
    public bool ShowGuides { get; set; } = true;
    public void FocusNode(Node node)
    {
        Vm.Reveal(node);
        Zoom=Math.Clamp(Math.Min(Math.Max(200,Bounds.Width-100)/node.Width,Math.Max(150,Bounds.Height-100)/node.DisplayHeight),.15,1.25);
        Pan=new(Bounds.Width/2-(node.X+node.Width/2)*Zoom,Bounds.Height/2-(node.Y+node.DisplayHeight/2)*Zoom);InvalidateVisual();
    }
    public Node AddInView(NodeKind kind, bool stack=false)
    {
        var center=World(new(Bounds.Width/2,Bounds.Height/2));bool container=new Node{Kind=kind}.CanContainNodes;
        var pos=MapEditing.OpenPosition(Vm.Project,center.X,center.Y,container?460:stack?220:180,container?240:76);
        var node=stack?Vm.AddSwitchStack(5,pos.X,pos.Y):Vm.Add(kind,pos.X,pos.Y);
        var screen=new Rect(node.X*Zoom+Pan.X,node.Y*Zoom+Pan.Y,node.Width*Zoom,node.DisplayHeight*Zoom);
        if(!new Rect(Bounds.Size).Contains(screen)){Pan=new(Bounds.Width/2-(node.X+node.Width/2)*Zoom,Bounds.Height/2-(node.Y+node.DisplayHeight/2)*Zoom);InvalidateVisual();}
        return node;
    }
    public void Fit() { var size = VisualExporter.Bounds(Vm.Project); Zoom = Math.Clamp(Math.Min((Bounds.Width - 50) / size.Width, (Bounds.Height - 50) / size.Height), .15, 1.5); Pan = new(25-size.X*Zoom, 25-size.Y*Zoom); InvalidateVisual(); }
    public override void Render(DrawingContext dc)
    {
        base.Render(dc); bool light = ActualThemeVariant == Avalonia.Styling.ThemeVariant.Light && !Exporting;
        dc.FillRectangle(new SolidColorBrush(light ? Color.Parse("#CBD2DA") : Color.Parse("#101722")), Bounds);
        if (!Exporting) { var grid = new SolidColorBrush(light ? Color.Parse("#ADB9C6") : Color.Parse("#273447")); double step = 30 * Zoom; if (step >= 8) for (double x = Pan.X % step; x < Bounds.Width; x += step) for (double y = Pan.Y % step; y < Bounds.Height; y += step) dc.DrawEllipse(grid, null, new Point(x, y), 1, 1); }
        using (dc.PushTransform(Matrix.CreateScale(Zoom, Zoom) * Matrix.CreateTranslation(Pan)))
        {
            var connections=Vm.Project.Connections.Where(VisibleConnection).ToLookup(c=>MapEditing.ConnectionLayer(Vm.Project,c));
            var captions=dragging||resizing!=null?new Dictionary<string,ConnectionCaption>():ConnectionCaptions.Create(Vm.Project,Vm.Project.Connections.Where(c=>VisibleConnection(c)&&ShowConnectionLabel(c)));
            foreach(var layer in PaintOrder().GroupBy(n=>MapEditing.Layer(Vm.Project,n)))
            {
                foreach(var n in layer.Where(n=>n.CanContainNodes))DrawNode(dc,n,light);
                foreach(var c in connections[layer.Key].OrderBy(c=>!Exporting&&RelatedConnection(c)?1:0))
                {
                    bool related=RelatedConnection(c),focus=FocusConnections&&!Exporting&&(Vm.Selection.Count>0||Vm.SelectedConnection!=null);
                    using var opacity=dc.PushOpacity(focus&&!related ? .2 : 1);
                    var route=DisplayRoute(c);var pen=new Pen(new SolidColorBrush(Color.Parse(related&&!Exporting?"#f5c66e":"#688bb5")),related&&!Exporting?2.6:1.6);
                    var gap=new Pen(new SolidColorBrush(Color.Parse(light?"#CBD2DA":"#101722")),(related&&!Exporting?2.6:1.6)+4);
                    for(int i=1;i<route.Count;i++)dc.DrawLine(gap,new(route[i-1].X,route[i-1].Y),new(route[i].X,route[i].Y));
                    for(int i=1;i<route.Count;i++)dc.DrawLine(pen,new(route[i-1].X,route[i-1].Y),new(route[i].X,route[i].Y));
                    if(captions.TryGetValue(c.Id,out var caption))
                    {
                        var box=caption.Box;dc.DrawRectangle(new SolidColorBrush(Color.Parse(light?"#DDE3EA":"#101722")),null,new Rect(box.X,box.Y,box.Width,box.Height),3,3);
                        DrawText(dc,caption.Text,box.X+6,box.Y+3,10,light?"#3D526C":"#b7c9e0");
                    }
                }
                foreach(var n in layer.Where(n=>!n.CanContainNodes))DrawNode(dc,n,light);
            }
            if(dragging&&ShowGuides&&!Exporting)
            {
                var guidePen=new Pen(new SolidColorBrush(Color.Parse(light?"#9B287B":"#FF88D4")),1/Zoom,DashStyle.Dash);
                foreach(var guide in MapEditing.Guides(Vm.Project,Vm.Selection,1.5/Zoom,Vm.Visible))
                    dc.DrawLine(guidePen,guide.Vertical?new(guide.Position,guide.Start):new(guide.Start,guide.Position),guide.Vertical?new(guide.Position,guide.End):new(guide.End,guide.Position));
            }
            if (marquee) dc.DrawRectangle(new SolidColorBrush(Color.Parse("#224a90e2")), new Pen(Brushes.CornflowerBlue, 1), selectionRect);
        }
    }
    private void DrawNode(DrawingContext dc, Node n, bool light)
    {
        var color = Color.Parse(MapEditing.ValidColor(n.AccentColor)?n.AccentColor:light?DeviceIconRenderer.TypeColor(n.Kind):VisualExporter.Color(n)); var rect = new Rect(n.X, n.Y, n.Width, n.DisplayHeight);
        bool image=n.UseDeviceImage&&!n.CanContainNodes;
        var fill=new SolidColorBrush(Color.Parse(light?"#DDE3EA":"#202d40"));var outline=new Pen(new SolidColorBrush(!Exporting&&Vm.Selection.Contains(n.Id)?Color.Parse("#e8c873"):color),2);
        if(!image&&!n.CanContainNodes&&n.Shape==DeviceShape.Circle)dc.DrawEllipse(fill,outline,rect.Center,rect.Width/2,rect.Height/2);
        else if(!image) dc.DrawRectangle(new SolidColorBrush(Color.Parse(n.IsContainer ? (light ? "#BEC9D6" : "#172130") : (light ? "#DDE3EA" : "#202d40"))), new Pen(new SolidColorBrush(!Exporting && Vm.Selection.Contains(n.Id) ? Color.Parse("#e8c873") : color), !Exporting && Vm.Selection.Contains(n.Id) ? 2.5 : 1), rect, n.Shape==DeviceShape.Rectangle?0:10, n.Shape==DeviceShape.Rectangle?0:10);
        var presentation=NodePresentation.Create(n);var icon=presentation.Icon;
        if(!DeviceIconRenderer.Draw(dc,n,new Rect(icon.X,icon.Y,icon.Width,icon.Height),light))
        {
            using(dc.PushTransform(Matrix.CreateScale(icon.Width/20,icon.Height/20)*Matrix.CreateTranslation(icon.X-(n.X+12)*icon.Width/20,icon.Y-(n.Y+16)*icon.Height/20)))DrawIcon(dc,n,outline,light);
        }
        foreach(var line in presentation.Labels)DrawLabel(dc,line,light?"#162438":"#edf3fa");
        if(!Exporting&&Vm.Selection.Contains(n.Id)){if(image)dc.DrawRectangle(null,outline,rect,4,4);if(!n.Collapsed&&!MapEditing.IsLocked(Vm.Project,n))dc.DrawRectangle(Brushes.CornflowerBlue,null,Handle(n));}
        if(!Exporting&&MapEditing.IsLocked(Vm.Project,n))
        {
            var lockPen=new Pen(new SolidColorBrush(color),1.5);double lx=n.X+n.Width-22,ly=n.Y+14;
            dc.DrawRectangle(null,lockPen,new Rect(lx+2,ly,6,7),3,3);dc.DrawRectangle(new SolidColorBrush(color),null,new Rect(lx,ly+5,10,8),2,2);
        }
    }
    private static void DrawLabel(DrawingContext dc,VisualLabel label,string color)
    {
        if(label.Width<1)return;
        var text=new FormattedText(label.Text,CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Inter"),label.Size,new SolidColorBrush(Color.Parse(color))){MaxTextWidth=label.Width,MaxTextHeight=label.Size*1.4,Trimming=TextTrimming.CharacterEllipsis,TextAlignment=label.Center?TextAlignment.Center:TextAlignment.Left};
        using(dc.PushClip(new Rect(label.X,label.Y,label.Width,label.Size*1.4)))dc.DrawText(text,new(label.X,label.Y));
    }

    public static void DrawIcon(DrawingContext dc, Node n, Pen pen,bool light=false)
    {
        double x = n.X + 12, y = n.Y + 16; void Line(double a, double b, double c, double d) => dc.DrawLine(pen, new(x + a, y + b), new(x + c, y + d));
        if(!n.IsContainer&&DeviceIconRenderer.Draw(dc,n,new Rect(x,y-2,20,20),light))return;
        if (n.IsContainer) { dc.DrawRectangle(null, pen, new Rect(x, y + 3, 16, 12), 2, 2); Line(0, 3, 6, 3); Line(6, 3, 6, 0); Line(6, 0, 13, 0); return; }
        if(n.IsStack){dc.DrawRectangle(null,pen,new Rect(x+2,y,14,9),1,1);dc.DrawRectangle(null,pen,new Rect(x+1,y+5,14,9),1,1);dc.DrawRectangle(null,pen,new Rect(x,y+10,14,9),1,1);Line(3,15,11,15);return;}
        switch (n.Kind)
        {
            case NodeKind.Internet: dc.DrawEllipse(null, pen, new Point(x + 8, y + 8), 8, 8); dc.DrawEllipse(null, pen, new Point(x + 8, y + 8), 4, 8); Line(0, 8, 16, 8); break;
            case NodeKind.Firewall: dc.DrawRectangle(null, pen, new Rect(x, y, 16, 16), 1, 1); Line(0, 5, 16, 5); Line(0, 11, 16, 11); Line(8, 0, 8, 5); Line(5, 5, 5, 11); Line(11, 5, 11, 11); Line(8, 11, 8, 16); break;
            case NodeKind.Workstation: dc.DrawRectangle(null, pen, new Rect(x, y, 16, 11), 1, 1); Line(8, 11, 8, 16); Line(3, 16, 13, 16); break;
            case NodeKind.Storage: dc.DrawRectangle(null, pen, new Rect(x, y + 3, 16, 11)); dc.DrawEllipse(null, pen, new Point(x + 8, y + 3), 8, 3); dc.DrawEllipse(null, pen, new Point(x + 8, y + 14), 8, 3); break;
            case NodeKind.AccessPoint: dc.DrawEllipse(null, pen, new Point(x + 8, y + 6), 7, 6); dc.DrawEllipse(null, pen, new Point(x + 8, y + 6), 3, 3); Line(8, 10, 8, 16); Line(3, 16, 13, 16); break;
            case NodeKind.Router: Line(0, 8, 16, 8); Line(8, 0, 8, 16); Line(0, 8, 3, 5); Line(0, 8, 3, 11); Line(16, 8, 13, 5); Line(16, 8, 13, 11); break;
            case NodeKind.Switch: dc.DrawRectangle(null, pen, new Rect(x, y + 3, 16, 10), 2, 2); for (int i = 0; i < 4; i++) dc.DrawRectangle(null, pen, new Rect(x + 2 + i * 3, y + 7, 1, 3)); break;
            case NodeKind.Printer:dc.DrawRectangle(null,pen,new Rect(x+3,y,10,6));dc.DrawRectangle(null,pen,new Rect(x,y+5,16,8),2,2);dc.DrawRectangle(null,pen,new Rect(x+3,y+10,10,7));Line(5,13,11,13);dc.DrawEllipse(pen.Brush,null,new Point(x+13,y+8),1,1);break;
            default: dc.DrawRectangle(null, pen, new Rect(x + 2, y, 12, 17), 2, 2); Line(4, 5, 12, 5); Line(4, 10, 12, 10); dc.DrawEllipse(pen.Brush, null, new Point(x + 6, y + 14), 1, 1); break;
        }
    }
    public static void DrawText(DrawingContext dc, string text, double x, double y, double size, string color) => dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Inter"), size, new SolidColorBrush(Color.Parse(color))), new(x, y));
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e); Focus(); last = e.GetPosition(this); start = World(last); var point = e.GetCurrentPoint(this);
        if (point.Properties.IsMiddleButtonPressed || point.Properties.IsRightButtonPressed) { panning = true; e.Pointer.Capture(this); return; }
        var n = NodeAt(start);
        var handle=n!=null&&Vm.Selection.Contains(n.Id)&&!MapEditing.IsLocked(Vm.Project,n)&&!n.Collapsed&&Handle(n).Contains(start)?n:null;
        if(handle!=null){resizing=handle;Vm.Checkpoint();e.Pointer.Capture(this);return;}
        if (n != null)
        {
            if (Vm.ConnectMode && !n.IsContainer) { Vm.Connect(n); return; }
            if (e.ClickCount == 2 && n.CanContainNodes) { Vm.Edit(() => n.Collapsed = !n.Collapsed); return; }
            if (!Vm.Selection.Contains(n.Id) || e.KeyModifiers.HasFlag(KeyModifiers.Control)) Vm.Select(n, e.KeyModifiers.HasFlag(KeyModifiers.Control));
            if(MapEditing.CanMove(Vm.Project,n)){Vm.Checkpoint(); dragging = true;}
        }
        else { var connection = Vm.Project.Connections.Where(VisibleConnection).LastOrDefault(c => HitRoute(MapLayout.Route(Vm.Project, c), start)); if (connection != null) { Vm.Selection.Clear(); Vm.SelectedConnection = connection; Vm.Notify(); return; } Vm.Select(null); marquee = true; selectionRect = new(start, start); }
        e.Pointer.Capture(this);
    }
    private static bool HitRoute(List<MapPoint> route, Point p) { for (int i = 1; i < route.Count; i++) { var a = route[i - 1]; var b = route[i]; if (p.X >= Math.Min(a.X, b.X) - 6 && p.X <= Math.Max(a.X, b.X) + 6 && p.Y >= Math.Min(a.Y, b.Y) - 6 && p.Y <= Math.Max(a.Y, b.Y) + 6) return true; } return false; }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        var pos = e.GetPosition(this); var delta = (pos - last) / Zoom;
        if(!dragging&&!panning&&!marquee&&resizing==null){var world=World(pos);hoveredConnection=NodeAt(world)==null?Vm.Project.Connections.Where(VisibleConnection).LastOrDefault(c=>HitRoute(MapLayout.Route(Vm.Project,c),world)):null;}
        if (panning) Pan += pos - last;
        if(dragging)Vm.MoveSelection(delta.X,delta.Y);
        if(resizing!=null){var world=World(pos);Containment.Resize(Vm.Project,resizing,world.X-resizing.X,world.Y-resizing.Y);}
        Cursor=new Cursor(resizing!=null||Vm.Project.Nodes.Any(n=>Vm.Selection.Contains(n.Id)&&!MapEditing.IsLocked(Vm.Project,n)&&!n.Collapsed&&Handle(n).Contains(World(pos)))?StandardCursorType.BottomRightCorner:StandardCursorType.Arrow);
        if (marquee) selectionRect = new Rect(start, World(pos)).Normalize(); last = pos; InvalidateVisual();
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if(dragging||resizing!=null){if(Snap&&dragging)Vm.SnapSelection();Vm.Touch();Vm.RefreshDetails();}resizing=null;
        if (marquee) { foreach (var n in Vm.Project.Nodes.Where(n => Vm.Visible(n) && selectionRect.Contains(new Point(n.X + n.Width / 2, n.Y + n.Height / 2)))) Vm.Selection.Add(n.Id); Vm.Notify(); }
        dragging = panning = marquee = false; e.Pointer.Capture(null); InvalidateVisual();
    }
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e){if(dragging||resizing!=null){Vm.Touch();Vm.RefreshDetails();}dragging=panning=marquee=false;resizing=null;base.OnPointerCaptureLost(e);InvalidateVisual();}
    protected override void OnPointerExited(PointerEventArgs e){hoveredConnection=null;base.OnPointerExited(e);InvalidateVisual();}
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e) { var point = e.GetPosition(this); var world = World(point); Zoom = Math.Clamp(Zoom * Math.Pow(1.15, e.Delta.Y), .15, 3); Pan = new(point.X - world.X * Zoom, point.Y - world.Y * Zoom); InvalidateVisual(); e.Handled = true; }
}
