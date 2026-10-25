using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using RackTrail.Core;
using System.Globalization;
using System.Diagnostics;
namespace RackTrail.Desktop;

public class MapCanvas : Control, IDisposable
{
    static MapCanvas(){AffectsRender<MapCanvas>(ThemeVariantScope.ActualThemeVariantProperty);}
    public EditorViewModel Vm { get; }
    public double Zoom { get; set; } = .65;
    public Vector Pan { get; set; } = new(40, 35);
    public bool Snap { get; set; } = true;
    public bool Exporting { get; set; }
    internal bool DiagnosticDrawAll {get;set;}
    private bool allLabels;
    public bool ShowAllConnectionLabels {get=>allLabels;set{if(allLabels==value)return;allLabels=value;captionRequestKey=null;InvalidateVisual();}}
    public bool FocusConnections { get; set; } = true;
    private Connection? hoveredConnection;
    internal bool RelatedConnection(Connection c)
    {
        bool Selected(string id)=>Index.Endpoints.GetValueOrDefault(id) is {} n&&(Vm.Selection.Contains(n.Id)||Index.Parents[n.Id].Any(a=>Vm.Selection.Contains(a.Id)));
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
    private readonly RenderResources resources=new();
    private readonly Dictionary<string,(double X,double Y,double Width,double Height,NodePresentation Value)> presentations=[];
    private NodePresentation Presentation(Node n)
    {
        if(!presentations.TryGetValue(n.Id,out var entry)||entry.X!=n.X||entry.Y!=n.Y||entry.Width!=n.Width||entry.Height!=n.DisplayHeight)
        {var value=NodePresentation.Create(n);entry=(n.X,n.Y,n.Width,n.DisplayHeight,value);presentations[n.Id]=entry;}
        return entry.Value;
    }
    private readonly RouteWorker worker;
    private SceneIndex? scene;
    private Project? indexedProject;
    private long indexedRevision=-1, requestedRevision=-1, observedSceneRevision=-1;
    private RoutingSnapshot? routes;
    private readonly Dictionary<string,ConnectionCaption> captions=[];
    private string? captionRequestKey;
    private bool routesComplete;
    private Dictionary<string,Connection> connectionsById=[];
    private Dictionary<string,int> connectionRanks=[];
    private Dictionary<string,long> connectionLayers=[];
    private readonly DispatcherTimer pointerTimer=new(){Interval=TimeSpan.FromMilliseconds(16)};
    private Point last,start,pendingPosition;
    private bool dragging,panning,marquee,pendingPointer,disposed,previewDirty;
    private Rect selectionRect;
    private Node? resizing;
    private MoveSession? moveSession;
    private RoutingSnapshot? gesturePreview;
    private HashSet<string> gestureLinks=[];
    private SceneIndex Index {get{EnsureScene();return scene!;}}
    internal void SetDiagnosticRoutes(RoutingSnapshot snapshot){EnsureScene();worker.Cancel();requestedRevision=indexedRevision;routesComplete=true;routes=snapshot;captions.Clear();captionRequestKey=null;}
    internal RoutingSnapshot CurrentRoutes {get{EnsureScene();return routes!;}}
    private void EnsureScene()
    {
        if(disposed)return;
        long modelRevision=HasActiveGesture?observedSceneRevision:SceneChanges.Revision(Vm.Project);
        if(scene==null||!ReferenceEquals(indexedProject,Vm.Project)||indexedRevision!=Vm.GeometryRevision||observedSceneRevision!=modelRevision||scene.Nodes.Count!=Vm.Project.Nodes.Count||connectionsById.Count!=Vm.Project.Connections.Count)
        {
            worker.Cancel();indexedProject=Vm.Project;indexedRevision=Vm.GeometryRevision;observedSceneRevision=SceneChanges.Refresh(Vm.Project);
            presentations.Clear();scene=new SceneIndex(Vm.Project);routesComplete=false;gesturePreview=null;gestureLinks.Clear();routes=ConnectionRouter.Preview(Vm.Project,scene);captions.Clear();captionRequestKey=null;
            connectionsById=Vm.Project.Connections.ToDictionary(c=>c.Id);
            connectionRanks=Vm.Project.Connections.Select((c,i)=>(c.Id,Rank:i)).ToDictionary(x=>x.Id,x=>x.Rank);
            connectionLayers=Vm.Project.Connections.ToDictionary(c=>c.Id,c=>new[]{scene.Endpoints.GetValueOrDefault(c.From),scene.Endpoints.GetValueOrDefault(c.To)}.OfType<Node>().Select(n=>scene.Layers[n.Id]).DefaultIfEmpty(0).Max());
            requestedRevision=-1;previewDirty=false;
        }
        if(!dragging&&resizing==null&&requestedRevision!=indexedRevision)
        {
            requestedRevision=indexedRevision;
            worker.Request(indexedRevision,ProjectSnapshot.Capture(Vm.Project),allLabels);
        }
    }
    private IReadOnlyList<MapPoint> DisplayRoute(Connection c) => gesturePreview!=null&&gestureLinks.Contains(c.Id)?gesturePreview.Route(c.Id):CurrentRoutes.Route(c.Id);
    private bool VisibleConnection(Connection c) => Index.Endpoints.GetValueOrDefault(c.From) is {} a&&Vm.MatchesFilter(a)&&Index.Endpoints.GetValueOrDefault(c.To) is {} b&&Vm.MatchesFilter(b);
    private Rect Handle(Node n)=>new(n.X+n.Width-12/Zoom,n.Y+n.DisplayHeight-12/Zoom,12/Zoom,12/Zoom);
    private static readonly Cursor ArrowCursor=new(StandardCursorType.Arrow),ResizeCursor=new(StandardCursorType.BottomRightCorner);
    public MapCanvas(EditorViewModel vm)
    {
        Vm=vm;Focusable=true;ClipToBounds=true;
        worker=new RouteWorker(result=>{if(disposed||!ReferenceEquals(indexedProject,Vm.Project)||result.Revision!=Vm.GeometryRevision||observedSceneRevision!=SceneChanges.Revision(Vm.Project)||dragging||resizing!=null)return;Performance?.RecordValue(result.CaptionsOnly?"captions-background":"routing-and-captions-background",result.RoutingMilliseconds,result.AllocatedBytes);if(!result.CaptionsOnly){routes=result.Routes;routesComplete=true;captions.Clear();captionRequestKey=null;}foreach(var entry in result.Captions)captions[entry.Key]=entry.Value;InvalidateVisual();},message=>{Vm.Status="Routing preview retained: "+message;Vm.Notify();});
        vm.Changed+=OnModelChanged;
        pointerTimer.Tick+=(_,_)=>FlushPointer();
    }
    private readonly HashSet<string> lastSelection=[];
    private string? lastSelectedConnection;
    private long lastAppearanceRevision=-1;
    private NodeKind? lastTypeFilter;
    private ClassificationConfidence? lastConfidenceFilter;
    private void OnModelChanged()
    {
        if(disposed)return;
        if(HasActiveGesture&&(!ReferenceEquals(indexedProject,Vm.Project)||indexedRevision!=Vm.GeometryRevision))
        {dragging=false;resizing=null;moveSession=null;pendingPointer=false;pointerTimer.Stop();Cursor=ArrowCursor;}
        bool geometry=scene==null||!ReferenceEquals(indexedProject,Vm.Project)||indexedRevision!=Vm.GeometryRevision||(!HasActiveGesture&&observedSceneRevision!=SceneChanges.Revision(Vm.Project))||scene.Nodes.Count!=Vm.Project.Nodes.Count||connectionsById.Count!=Vm.Project.Connections.Count;
        bool appearance=lastAppearanceRevision!=Vm.AppearanceRevision;lastAppearanceRevision=Vm.AppearanceRevision;
        bool selection=!lastSelection.SetEquals(Vm.Selection)||lastSelectedConnection!=Vm.SelectedConnection?.Id;
        bool filter=lastTypeFilter!=Vm.TypeFilter||lastConfidenceFilter!=Vm.ConfidenceFilter;
        lastSelection.Clear();lastSelection.UnionWith(Vm.Selection);lastSelectedConnection=Vm.SelectedConnection?.Id;lastTypeFilter=Vm.TypeFilter;lastConfidenceFilter=Vm.ConfidenceFilter;
        EnsureScene();if(geometry||appearance||selection||filter)InvalidateVisual();
    }
    public void Dispose(){if(disposed)return;disposed=true;dragging=panning=marquee=pendingPointer=false;resizing=null;moveSession=null;pointerTimer.Stop();Vm.Changed-=OnModelChanged;worker.Dispose();}
    public Point World(Point p) => new((p.X - Pan.X) / Zoom, (p.Y - Pan.Y) / Zoom);
    internal List<Node> PaintOrder() => Index.PaintOrder.Where(Vm.MatchesFilter).ToList();
    internal Node? NodeAt(Point point) => Index.NodeBounds.Query(new(point.X,point.Y,0,0)).Select(id=>Index.Nodes[id]).Where(Vm.MatchesFilter).OrderByDescending(n=>Index.PaintRanks[n.Id]).FirstOrDefault();
    public PerformanceRecorder? Performance {get;set;}
    private long pointerQueuedAt,pointerAllocated;
    public bool HasActiveGesture=>dragging||resizing!=null;
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
    public void Fit() { var size = VisualExporter.Bounds(Vm.Project,CurrentRoutes); Zoom = Math.Clamp(Math.Min((Bounds.Width - 50) / size.Width, (Bounds.Height - 50) / size.Height), .15, 1.5); Pan = new(25-size.X*Zoom, 25-size.Y*Zoom); InvalidateVisual(); }
    public override void Render(DrawingContext dc)
    {
        long frameStarted=Stopwatch.GetTimestamp(),frameAllocated=Performance==null?0:GC.GetAllocatedBytesForCurrentThread();
        base.Render(dc); bool light = ActualThemeVariant == Avalonia.Styling.ThemeVariant.Light && !Exporting;
        dc.FillRectangle(resources.Brush(light?"#CBD2DA":"#101722"),Bounds);
        if (!Exporting) { var grid = resources.Brush(light?"#ADB9C6":"#273447"); double step = 30 * Zoom; if (step >= 8) for (double x = Pan.X % step; x < Bounds.Width; x += step) for (double y = Pan.Y % step; y < Bounds.Height; y += step) dc.DrawEllipse(grid, null, new Point(x, y), 1, 1); }
        using (dc.PushTransform(Matrix.CreateScale(Zoom, Zoom) * Matrix.CreateTranslation(Pan)))
        {
            EnsureScene();
            if(previewDirty)
            {
                scene!.RefreshBounds();
                var changed=moveSession?.AffectedConnections??Vm.Project.Connections;
                gestureLinks=changed.Select(c=>c.Id).ToHashSet();
                gesturePreview=new RoutingSnapshot(changed.ToDictionary(c=>c.Id,c=>MapLayout.PreviewRoute(Vm.Project,c,scene)));
                captions.Clear();captionRequestKey=null;previewDirty=false;
            }
            double margin=24/Zoom;var origin=World(new(0,0));var viewport=new VisualBox(origin.X-margin,origin.Y-margin,Bounds.Width/Zoom+margin*2,Bounds.Height/Zoom+margin*2);
            var nodeCandidates=DiagnosticDrawAll?Index.PaintOrder.Select(n=>n.Id).ToHashSet():Index.NodeBounds.Query(viewport);
            var linkCandidates=DiagnosticDrawAll?connectionsById.Keys.ToHashSet():CurrentRoutes.Segments.Query(viewport);
            if(gesturePreview!=null){linkCandidates.ExceptWith(gestureLinks);linkCandidates.UnionWith(gesturePreview.Segments.Query(viewport));}
            var visibleNodes=nodeCandidates.Select(id=>Index.Nodes[id]).Where(Vm.MatchesFilter).OrderBy(n=>Index.PaintRanks[n.Id]).ToLookup(n=>Index.Layers[n.Id]);
            var visibleLinks=linkCandidates.Select(id=>connectionsById[id]).Where(VisibleConnection).ToLookup(c=>connectionLayers[c.Id]);
            if(!dragging&&resizing==null&&routesComplete)
            {
                var wanted=visibleLinks.SelectMany(g=>g).Where(c=>ShowConnectionLabel(c)&&!captions.ContainsKey(c.Id)).Select(c=>c.Id).Order(StringComparer.Ordinal).ToArray();
                string key=string.Join(",",wanted);
                if(wanted.Length>0&&captionRequestKey!=key){captionRequestKey=key;worker.RequestCaptions(indexedRevision,ProjectSnapshot.Capture(Vm.Project),routes!,wanted,captions.Values.Select(c=>c.Box).ToArray());}
            }
            foreach(var layerId in visibleNodes.Select(g=>g.Key).Concat(visibleLinks.Select(g=>g.Key)).Distinct().Order())
            {
                var layer=visibleNodes[layerId];
                foreach(var n in layer.Where(n=>n.CanContainNodes))DrawNode(dc,n,light);
                foreach(var c in visibleLinks[layerId].OrderBy(c=>!Exporting&&RelatedConnection(c)?1:0).ThenBy(c=>connectionRanks[c.Id]))
                {
                    bool related=RelatedConnection(c),focus=FocusConnections&&!Exporting&&(Vm.Selection.Count>0||Vm.SelectedConnection!=null);
                    using var opacity=dc.PushOpacity(focus&&!related ? .2 : 1);
                    var route=DisplayRoute(c);var pen=resources.Pen(related&&!Exporting?"#f5c66e":"#688bb5",related&&!Exporting?2.6:1.6);
                    var gap=resources.Pen(light?"#CBD2DA":"#101722",(related&&!Exporting?2.6:1.6)+4);
                    for(int i=1;i<route.Count;i++)dc.DrawLine(gap,new(route[i-1].X,route[i-1].Y),new(route[i].X,route[i].Y));
                    for(int i=1;i<route.Count;i++)dc.DrawLine(pen,new(route[i-1].X,route[i-1].Y),new(route[i].X,route[i].Y));
                    if(!dragging&&resizing==null&&ShowConnectionLabel(c)&&captions.TryGetValue(c.Id,out var caption))
                    {
                        var box=caption.Box;dc.DrawRectangle(resources.Brush(light?"#DDE3EA":"#101722"),null,new Rect(box.X,box.Y,box.Width,box.Height),3,3);
                        resources.Label(dc,new(caption.Text,box.X+6,box.Y+3,box.Width-12,10),light?"#3D526C":"#b7c9e0");
                    }
                }
                foreach(var n in layer.Where(n=>!n.CanContainNodes))DrawNode(dc,n,light);
            }
            if(dragging&&ShowGuides&&!Exporting)
            {
                var guidePen=new Pen(new SolidColorBrush(Color.Parse(light?"#9B287B":"#FF88D4")),1/Zoom,DashStyle.Dash);
                var guideCandidates=Index.NodeBounds.Query(viewport);
                foreach(var guide in moveSession?.Guides(1.5/Zoom,n=>Vm.MatchesFilter(n)&&guideCandidates.Contains(n.Id))??Enumerable.Empty<AlignmentGuide>())
                    dc.DrawLine(guidePen,guide.Vertical?new(guide.Position,guide.Start):new(guide.Start,guide.Position),guide.Vertical?new(guide.Position,guide.End):new(guide.End,guide.Position));
            }
            if (marquee) dc.DrawRectangle(new SolidColorBrush(Color.Parse("#224a90e2")), new Pen(Brushes.CornflowerBlue, 1), selectionRect);
        }
        Performance?.Record(dragging?"frame-drag":resizing!=null?"frame-resize":panning?"frame-pan":"frame-idle",frameStarted,frameAllocated);
    }
    private void DrawNode(DrawingContext dc, Node n, bool light)
    {
        var color = Color.Parse(MapEditing.ValidColor(n.AccentColor)?n.AccentColor:light?DeviceIconRenderer.TypeColor(n.Kind):VisualExporter.Color(n)); var rect = new Rect(n.X, n.Y, n.Width, n.DisplayHeight);
        bool image=n.UseDeviceImage&&!n.CanContainNodes;
        var fill=resources.Brush(light?"#DDE3EA":"#202d40");var outline=resources.Pen(!Exporting&&Vm.Selection.Contains(n.Id)?"#e8c873":color.ToString(),2);
        if(!image&&!n.CanContainNodes&&n.Shape==DeviceShape.Circle)dc.DrawEllipse(fill,outline,rect.Center,rect.Width/2,rect.Height/2);
        else if(!image) dc.DrawRectangle(resources.Brush(n.IsContainer?(light?"#BEC9D6":"#172130"):(light?"#DDE3EA":"#202d40")), resources.Pen(!Exporting&&Vm.Selection.Contains(n.Id)?"#e8c873":color.ToString(),!Exporting&&Vm.Selection.Contains(n.Id)?2.5:1), rect, n.Shape==DeviceShape.Rectangle?0:10, n.Shape==DeviceShape.Rectangle?0:10);
        var presentation=Presentation(n);var icon=presentation.Icon;
        if(!DeviceIconRenderer.Draw(dc,n,new Rect(icon.X,icon.Y,icon.Width,icon.Height),light))
        {
            using(dc.PushTransform(Matrix.CreateScale(icon.Width/20,icon.Height/20)*Matrix.CreateTranslation(icon.X-(n.X+12)*icon.Width/20,icon.Y-(n.Y+16)*icon.Height/20)))DrawIcon(dc,n,outline,light);
        }
        foreach(var line in presentation.Labels)DrawLabel(dc,line,light?"#162438":"#edf3fa");
        if(!Exporting&&Vm.Selection.Contains(n.Id)){if(image)dc.DrawRectangle(null,outline,rect,4,4);if(!n.Collapsed&&!Index.Locked[n.Id])dc.DrawRectangle(Brushes.CornflowerBlue,null,Handle(n));}
        if(!Exporting&&Index.Locked[n.Id])
        {
            var lockPen=new Pen(new SolidColorBrush(color),1.5);double lx=n.X+n.Width-22,ly=n.Y+14;
            dc.DrawRectangle(null,lockPen,new Rect(lx+2,ly,6,7),3,3);dc.DrawRectangle(new SolidColorBrush(color),null,new Rect(lx,ly+5,10,8),2,2);
        }
    }
    private void DrawLabel(DrawingContext dc,VisualLabel label,string color) => resources.Label(dc,label,color);

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
        base.OnPointerPressed(e);FlushPointer(); Focus(); last = e.GetPosition(this); start = World(last); var point = e.GetCurrentPoint(this);
        if (point.Properties.IsMiddleButtonPressed || point.Properties.IsRightButtonPressed) { panning = true; e.Pointer.Capture(this); return; }
        var n = NodeAt(start);
        var handle=n!=null&&Vm.Selection.Contains(n.Id)&&!Index.Locked[n.Id]&&!n.Collapsed&&Handle(n).Contains(start)?n:null;
        if(handle!=null){resizing=handle;worker.Cancel();requestedRevision=-1;Vm.Checkpoint();e.Pointer.Capture(this);return;}
        if (n != null)
        {
            if (Vm.ConnectMode && !n.IsContainer) { Vm.Connect(n); return; }
            if (e.ClickCount == 2 && n.CanContainNodes) { Vm.Edit(() => n.Collapsed = !n.Collapsed); return; }
            if (!Vm.Selection.Contains(n.Id) || e.KeyModifiers.HasFlag(KeyModifiers.Control)) Vm.Select(n, e.KeyModifiers.HasFlag(KeyModifiers.Control));
            if(Index.Movable[n.Id]){Vm.Checkpoint();moveSession=new MoveSession(Vm.Project,Vm.Selection,Index);worker.Cancel();requestedRevision=-1;dragging = true;}
        }
        else { var connection = ConnectionAt(start); if (connection != null) { Vm.Selection.Clear(); Vm.SelectedConnection = connection; Vm.Notify(); return; } Vm.Select(null); marquee = true; selectionRect = new(start, start); }
        e.Pointer.Capture(this);
    }
    private static bool HitRoute(IReadOnlyList<MapPoint> route,Point point,double tolerance)
    {
        for(int i=1;i<route.Count;i++)
        {
            var a=route[i-1];var b=route[i];double dx=b.X-a.X,dy=b.Y-a.Y,length=dx*dx+dy*dy;
            double ratio=length==0?0:Math.Clamp(((point.X-a.X)*dx+(point.Y-a.Y)*dy)/length,0,1);
            double x=point.X-a.X-ratio*dx,y=point.Y-a.Y-ratio*dy;
            if(x*x+y*y<=tolerance*tolerance)return true;
        }
        return false;
    }

    private Connection? ConnectionAt(Point point)
    {
        double tolerance=6/Zoom;
        return CurrentRoutes.Segments.Query(new(point.X-tolerance,point.Y-tolerance,tolerance*2,tolerance*2))
            .Select(id=>connectionsById[id]).Where(VisibleConnection).OrderByDescending(c=>connectionRanks[c.Id])
            .FirstOrDefault(c=>HitRoute(DisplayRoute(c),point,tolerance));
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if(!pendingPointer){pointerQueuedAt=Stopwatch.GetTimestamp();pointerAllocated=Performance==null?0:GC.GetAllocatedBytesForCurrentThread();}
        pendingPosition=e.GetPosition(this);pendingPointer=true;
        if(!pointerTimer.IsEnabled)pointerTimer.Start();
    }
    private void FlushPointer()
    {
        pointerTimer.Stop();if(!pendingPointer||disposed)return;pendingPointer=false;
        Performance?.Record("input-queue",pointerQueuedAt,pointerAllocated);
        long inputStarted=Stopwatch.GetTimestamp(),inputAllocated=Performance==null?0:GC.GetAllocatedBytesForCurrentThread();
        var operation=dragging?"input-drag":resizing!=null?"input-resize":panning?"input-pan":"input-hover";
        var pos=pendingPosition;var delta=(pos-last)/Zoom;bool redraw=false;
        try
        {
            if(!dragging&&!panning&&!marquee&&resizing==null)
            {
                var world=World(pos);var hover=NodeAt(world)==null?ConnectionAt(world):null;
                redraw=hover!=hoveredConnection;hoveredConnection=hover;
            }
            if(panning){Pan+=pos-last;redraw=pos!=last;}
            if(dragging){bool moved=moveSession?.Move(delta.X,delta.Y)==true;previewDirty|=moved;redraw=moved;}
            if(resizing!=null){var world=World(pos);Containment.Resize(Vm.Project,resizing,world.X-resizing.X,world.Y-resizing.Y,Index);previewDirty=true;redraw=true;}
            if(marquee){selectionRect=new Rect(start,World(pos)).Normalize();redraw=true;}
            var hit=NodeAt(World(pos));var cursor=resizing!=null||hit!=null&&Vm.Selection.Contains(hit.Id)&&!hit.Collapsed&&!Index.Locked[hit.Id]&&Handle(hit).Contains(World(pos))?ResizeCursor:ArrowCursor;
            if(Cursor!=cursor)Cursor=cursor;
            last=pos;if(redraw)InvalidateVisual();
            Performance?.Record(operation,inputStarted,inputAllocated);
        }
        catch(Exception ex)when(ex is InvalidDataException or InvalidOperationException or ArgumentException)
        {EndGesture();Vm.Status="Map gesture stopped: "+ex.Message;Vm.Notify();}
    }
    private void EndGesture()
    {
        pointerTimer.Stop();pendingPointer=false;bool edited=dragging||resizing!=null;
        dragging=panning=marquee=false;resizing=null;moveSession=null;
        if(edited){Vm.Touch();Vm.RefreshDetails();}else EnsureScene();
        InvalidateVisual();
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if(dragging||panning||marquee||resizing!=null){if(!pendingPointer){pointerQueuedAt=Stopwatch.GetTimestamp();pointerAllocated=Performance==null?0:GC.GetAllocatedBytesForCurrentThread();}pendingPosition=e.GetPosition(this);pendingPointer=true;FlushPointer();}
        if(Snap&&dragging)moveSession?.Snap();
        if(marquee)
        {
            var area=new VisualBox(selectionRect.X,selectionRect.Y,selectionRect.Width,selectionRect.Height);
            foreach(var id in Index.NodeBounds.Query(area)){var n=Index.Nodes[id];if(Vm.MatchesFilter(n)&&selectionRect.Contains(new Point(n.X+n.Width/2,n.Y+n.DisplayHeight/2)))Vm.Selection.Add(id);}
            Vm.Notify();
        }
        EndGesture();e.Pointer.Capture(null);
    }
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e){FlushPointer();EndGesture();base.OnPointerCaptureLost(e);}
    protected override void OnPointerExited(PointerEventArgs e){if(hoveredConnection!=null){hoveredConnection=null;InvalidateVisual();}base.OnPointerExited(e);}
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e){if(HasActiveGesture||panning||marquee)EndGesture();pointerTimer.Stop();pendingPointer=false;worker.Cancel();requestedRevision=-1;base.OnDetachedFromVisualTree(e);}
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e) { long started=Stopwatch.GetTimestamp(),allocated=Performance==null?0:GC.GetAllocatedBytesForCurrentThread();FlushPointer(); var point = e.GetPosition(this); var world = World(point); Zoom = Math.Clamp(Zoom * Math.Pow(1.15, e.Delta.Y), .15, 3); Pan = new(point.X - world.X * Zoom, point.Y - world.Y * Zoom); InvalidateVisual();Performance?.Record("input-zoom",started,allocated); e.Handled = true; }
}
