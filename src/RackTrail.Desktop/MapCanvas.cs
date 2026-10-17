using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using RackTrail.Core;
using System.Globalization;
namespace RackTrail.Desktop;

public class MapCanvas : Control
{
    public EditorViewModel Vm { get; }
    public double Zoom { get; set; } = .65;
    public Vector Pan { get; set; } = new(40, 35);
    public bool Snap { get; set; } = true;
    public bool Exporting { get; set; }
    private Point last, start;
    private bool dragging, panning, marquee;
    private Rect selectionRect;
    private Node? resizing;
    private Rect Handle(Node n)=>new(n.X+n.Width-12/Zoom,n.Y+n.DisplayHeight-12/Zoom,12/Zoom,12/Zoom);
    public MapCanvas(EditorViewModel vm) { Vm = vm; Focusable = true; ClipToBounds = true; vm.Changed += InvalidateVisual; }
    public Point World(Point p) => new((p.X - Pan.X) / Zoom, (p.Y - Pan.Y) / Zoom);
    public void Fit() { var size = VisualExporter.Bounds(Vm.Project); Zoom = Math.Clamp(Math.Min((Bounds.Width - 50) / size.Width, (Bounds.Height - 50) / size.Height), .15, 1.5); Pan = new(25-size.X*Zoom, 25-size.Y*Zoom); InvalidateVisual(); }
    public override void Render(DrawingContext dc)
    {
        base.Render(dc); bool light = ActualThemeVariant == Avalonia.Styling.ThemeVariant.Light && !Exporting;
        dc.FillRectangle(new SolidColorBrush(light ? Color.Parse("#edf2f8") : Color.Parse("#101722")), Bounds);
        if (!Exporting) { var grid = new SolidColorBrush(light ? Color.Parse("#c7d2e1") : Color.Parse("#273447")); double step = 30 * Zoom; if (step >= 8) for (double x = Pan.X % step; x < Bounds.Width; x += step) for (double y = Pan.Y % step; y < Bounds.Height; y += step) dc.DrawEllipse(grid, null, new Point(x, y), 1, 1); }
        using (dc.PushTransform(Matrix.CreateScale(Zoom, Zoom) * Matrix.CreateTranslation(Pan)))
        {
            foreach (var n in Vm.Project.Nodes.Where(n => n.CanContainNodes && Vm.Project.Visible(n)).OrderBy(n => Vm.Project.Ancestors(n).Count())) DrawNode(dc, n, light);
            foreach (var c in Vm.Project.Connections) { var route = MapLayout.Route(Vm.Project, c); var pen = new Pen(new SolidColorBrush(Color.Parse(Vm.SelectedConnection == c && !Exporting ? "#f5c66e" : "#688bb5")), Vm.SelectedConnection == c ? 3 : 1.6); for (int i = 1; i < route.Count; i++) dc.DrawLine(pen, new(route[i - 1].X, route[i - 1].Y), new(route[i].X, route[i].Y)); if (route.Count > 1 && c.Kind!=ConnectionKind.Hosting) { var mid = route[route.Count / 2]; DrawText(dc, c.LabelFor(Vm.Project), mid.X + 6, mid.Y - 18, 10, light ? "#49647f" : "#a5bbd5"); } }
            foreach (var n in Vm.Project.Nodes.Where(n => !n.CanContainNodes && Vm.Project.Visible(n))) DrawNode(dc, n, light);
            if (marquee) dc.DrawRectangle(new SolidColorBrush(Color.Parse("#224a90e2")), new Pen(Brushes.CornflowerBlue, 1), selectionRect);
        }
    }
    private void DrawNode(DrawingContext dc, Node n, bool light)
    {
        var color = Color.Parse(VisualExporter.Color(n)); var rect = new Rect(n.X, n.Y, n.Width, n.DisplayHeight);
        bool image=n.UseDeviceImage&&!n.CanContainNodes;
        var fill=new SolidColorBrush(Color.Parse(light?"#ffffff":"#202d40"));var outline=new Pen(new SolidColorBrush(!Exporting&&Vm.Selection.Contains(n.Id)?Color.Parse("#e8c873"):color),2);
        if(!image&&!n.CanContainNodes&&n.Shape==DeviceShape.Circle)dc.DrawEllipse(fill,outline,rect.Center,rect.Width/2,rect.Height/2);
        else if(!image) dc.DrawRectangle(new SolidColorBrush(Color.Parse(n.IsContainer ? (light ? "#e3ebf5" : "#172130") : (light ? "#ffffff" : "#202d40"))), new Pen(new SolidColorBrush(!Exporting && Vm.Selection.Contains(n.Id) ? Color.Parse("#e8c873") : color), !Exporting && Vm.Selection.Contains(n.Id) ? 2.5 : 1), rect, n.Shape==DeviceShape.Rectangle?0:10, n.Shape==DeviceShape.Rectangle?0:10);
        if(image){double size=Math.Max(20,Math.Min(n.Width-24,n.DisplayHeight-58));if(!DeviceIconRenderer.Draw(dc,n,new Rect(n.X+(n.Width-size)/2,n.Y+14,size,size))){using(dc.PushTransform(Matrix.CreateScale(size/20,size/20)*Matrix.CreateTranslation(n.X+(n.Width-size)/2-(n.X+12)*size/20,n.Y+14-(n.Y+14)*size/20)))DrawIcon(dc,n,outline);}DrawText(dc,n.Name,n.X+12,n.Y+size+20,13,light?"#162438":"#edf3fa");DrawText(dc,n.VisualDetail,n.X+12,n.Y+size+41,10,light?"#536a84":"#a4b5ce");}
        if(!Exporting&&Vm.Selection.Contains(n.Id)){if(image)dc.DrawRectangle(null,outline,rect,4,4);if(!n.Collapsed)dc.DrawRectangle(Brushes.CornflowerBlue,null,Handle(n));}
        if(image)return;
        DrawIcon(dc, n, new Pen(new SolidColorBrush(color), 1.5));
        using (dc.PushClip(new Rect(n.X + 36, n.Y, n.Width - 42, n.DisplayHeight)))
        {
            DrawText(dc, (n.CanContainNodes ? (n.Collapsed ? "▸ " : "▾ ") : "") + n.Name, n.X + 36, n.Y + 13, 13, light ? "#162438" : "#edf3fa");
            DrawText(dc, n.IsContainer ? n.Cidr + (n.VlanId.Length > 0 ? "  ·  VLAN " + n.VlanId : "") : n.IsStack ? n.VisualDetail : n.Kind + "  ·  " + n.Addresses, n.X + 36, n.Y + 36, 10, light ? "#536a84" : "#a4b5ce");
            if(n.IsStack&&n.ShowStackMembers)
            {
                int row=0;foreach(var member in n.StackMembers.OrderBy(m=>m.Number))DrawText(dc,$"{member.Number:00}  {member.Name}  /  {member.Role}",n.X+36,n.Y+70+row++*24,11,light?"#536a84":"#a4b5ce");
            }
        }
    }
    private static void DrawIcon(DrawingContext dc, Node n, Pen pen)
    {
        double x = n.X + 12, y = n.Y + 16; void Line(double a, double b, double c, double d) => dc.DrawLine(pen, new(x + a, y + b), new(x + c, y + d));
        if(!n.IsContainer&&DeviceIconRenderer.Draw(dc,n,new Rect(x,y-2,20,20)))return;
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
        var handle=Vm.Project.Nodes.LastOrDefault(n=>Vm.Selection.Contains(n.Id)&&Vm.Project.Visible(n)&&!n.Collapsed&&Handle(n).Contains(start));
        if(handle!=null){resizing=handle;Vm.Checkpoint();e.Pointer.Capture(this);return;}
        var n = Vm.Project.Nodes.Where(Vm.Project.Visible).OrderByDescending(x => Vm.Project.Ancestors(x).Count()).FirstOrDefault(x => new Rect(x.X, x.Y, x.Width, x.DisplayHeight).Contains(start));
        if (n != null)
        {
            if (Vm.ConnectMode && !n.IsContainer) { Vm.Connect(n); return; }
            if (e.ClickCount == 2 && n.CanContainNodes) { Vm.Edit(() => n.Collapsed = !n.Collapsed); return; }
            if (!Vm.Selection.Contains(n.Id) || e.KeyModifiers.HasFlag(KeyModifiers.Control)) Vm.Select(n, e.KeyModifiers.HasFlag(KeyModifiers.Control));
            Vm.Checkpoint(); dragging = true;
        }
        else { var connection = Vm.Project.Connections.LastOrDefault(c => HitRoute(MapLayout.Route(Vm.Project, c), start)); if (connection != null) { Vm.Selection.Clear(); Vm.SelectedConnection = connection; Vm.Notify(); return; } Vm.Select(null); marquee = true; selectionRect = new(start, start); }
        e.Pointer.Capture(this);
    }
    private static bool HitRoute(List<MapPoint> route, Point p) { for (int i = 1; i < route.Count; i++) { var a = route[i - 1]; var b = route[i]; if (p.X >= Math.Min(a.X, b.X) - 6 && p.X <= Math.Max(a.X, b.X) + 6 && p.Y >= Math.Min(a.Y, b.Y) - 6 && p.Y <= Math.Max(a.Y, b.Y) + 6) return true; } return false; }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        var pos = e.GetPosition(this); var delta = (pos - last) / Zoom;
        if (panning) Pan += pos - last;
        if(dragging)Vm.MoveSelection(delta.X,delta.Y);
        if(resizing!=null){var world=World(pos);Containment.Resize(Vm.Project,resizing,world.X-resizing.X,world.Y-resizing.Y);}
        Cursor=new Cursor(resizing!=null||Vm.Project.Nodes.Any(n=>Vm.Selection.Contains(n.Id)&&!n.Collapsed&&Handle(n).Contains(World(pos)))?StandardCursorType.BottomRightCorner:StandardCursorType.Arrow);
        if (marquee) selectionRect = new Rect(start, World(pos)).Normalize(); last = pos; InvalidateVisual();
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if(dragging||resizing!=null){if(Snap&&dragging)Vm.SnapSelection();Vm.Touch();Vm.RefreshDetails();}resizing=null;
        if (marquee) { foreach (var n in Vm.Project.Nodes.Where(n => Vm.Project.Visible(n) && selectionRect.Contains(new Point(n.X + n.Width / 2, n.Y + n.Height / 2)))) Vm.Selection.Add(n.Id); Vm.Notify(); }
        dragging = panning = marquee = false; e.Pointer.Capture(null); InvalidateVisual();
    }
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e){if(dragging||resizing!=null){Vm.Touch();Vm.RefreshDetails();}dragging=panning=marquee=false;resizing=null;base.OnPointerCaptureLost(e);InvalidateVisual();}
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e) { var point = e.GetPosition(this); var world = World(point); Zoom = Math.Clamp(Zoom * Math.Pow(1.15, e.Delta.Y), .15, 3); Pan = new(point.X - world.X * Zoom, point.Y - world.Y * Zoom); InvalidateVisual(); e.Handled = true; }
}
