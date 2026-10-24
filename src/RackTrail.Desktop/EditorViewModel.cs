using System.ComponentModel;
using RackTrail.Core;
namespace RackTrail.Desktop;

public sealed class EditorViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? Changed;
    public Project Project { get; private set; } = new();
    public HashSet<string> Selection { get; } = [];
    public Node? Selected => Project.Nodes.FirstOrDefault(n => Selection.Contains(n.Id));
    public Connection? SelectedConnection { get; set; }
    public string? FilePath { get; set; }
    public int PropertiesRevision {get;private set;}
    public bool Dirty { get; private set; }
    public bool ConnectMode { get; set; }
    public string? ConnectFrom { get; set; }
    private readonly Stack<string> undo = new(), redo = new();
    private Project copied = new();
    public string Status { get; set; } = "Ready · Drag devices to move · Scroll to zoom · Middle-drag to pan";
    public NodeKind? TypeFilter { get; set; }
    public ClassificationConfidence? ConfidenceFilter { get; set; }
    public bool MatchesFilter(Node node) => node.IsContainer ||
        (!TypeFilter.HasValue||node.Kind==TypeFilter)&&(!ConfidenceFilter.HasValue||(node.Classification?.Confidence??ClassificationConfidence.Low)==ConfidenceFilter);
    public bool Visible(Node node) => Project.Visible(node)&&MatchesFilter(node);
    public void Notify() { PropertyChanged?.Invoke(this, new(null)); Changed?.Invoke(); }
    public void Checkpoint() { undo.Push(ProjectStorage.Serialize(Project)); redo.Clear(); Dirty = true; }
    public void Edit(Action action)
    {
        var before=ProjectStorage.Serialize(Project);bool wasDirty=Dirty;var selection=Selection.ToArray();var connectionId=SelectedConnection?.Id;
        try{action();Containment.Repair(Project);}
        catch
        {
            Project=ProjectStorage.Deserialize(before);Selection.Clear();foreach(var id in selection)Selection.Add(id);
            SelectedConnection=Project.Connections.Find(c=>c.Id==connectionId);Dirty=wasDirty;RefreshDetails();throw;
        }
        undo.Push(before);redo.Clear();Dirty=true;Notify();
    }
    public void RunUserAction(Action action) { try { action(); } catch(InvalidDataException ex) { Status=ex.Message;Notify(); } }
    public void RefreshDetails(){PropertiesRevision++;Notify();}
    public void Touch() { Dirty = true; Notify(); }
    public void RerouteConnections()
    {
        ConnectionRouter.Invalidate(Project);
        Status="Connections rerouted · device positions and custom routes preserved";
        Notify();
    }
    public void Replace(Project p, string? path = null) { var before=ProjectStorage.Serialize(p);Containment.Repair(p);Project = p; FilePath = path; Selection.Clear(); SelectedConnection = null; ConnectMode = false; ConnectFrom = null; undo.Clear(); redo.Clear(); Dirty = before!=ProjectStorage.Serialize(p); Notify(); }
    public void Undo() { if (undo.Count == 0) return; redo.Push(ProjectStorage.Serialize(Project)); Project = ProjectStorage.Deserialize(undo.Pop()); Dirty = true; Notify(); }
    public void Redo() { if (redo.Count == 0) return; undo.Push(ProjectStorage.Serialize(Project)); Project = ProjectStorage.Deserialize(redo.Pop()); Dirty = true; Notify(); }
    public Node Add(NodeKind kind, double x = 100, double y = 100) { var n = new Node { Kind = kind, Name = kind.ToString() + " " + (Project.Nodes.Count + 1), X = x, Y = y }; if (n.CanContainNodes) { n.Width = 460; n.Height = 240; }if(kind==NodeKind.Hypervisor){n.HypervisorPlatform="Hyper-V";n.OperatingSystem="Windows Server";} n.ZOrder=MapEditing.Front(Project); Edit(() => Project.Nodes.Add(n)); Selection.Clear(); Selection.Add(n.Id); Notify(); return n; }
    private void Host(Node vm,string? hostId,bool moveInside)
    {
        var host=Project.Nodes.Find(n=>n.Id==hostId);if(vm.Kind!=NodeKind.VirtualMachine||hostId!=null&&host?.Kind!=NodeKind.Hypervisor)throw new InvalidOperationException("Select a virtual machine and a hypervisor host.");
        var oldHost=Project.Nodes.Find(n=>n.Id==vm.HostId);vm.HostId=hostId;Project.Connections.RemoveAll(c=>c.Kind==ConnectionKind.Hosting&&c.To==vm.Id);
        if(host!=null){Project.Connections.Add(new(){Kind=ConnectionKind.Hosting,From=host.Id,To=vm.Id,Purpose=host.HypervisorPlatform});if(moveInside)Containment.Assign(Project,vm,host.Id);}
        else if(vm.ParentId==oldHost?.Id&&oldHost!=null)Containment.Assign(Project,vm,oldHost.ParentId);
    }
    public void AssignHost(Node vm,string? hostId){Edit(()=>Host(vm,hostId,true));RefreshDetails();}
    public void AssignContainer(Node node,string? id){Edit(()=>{if(node.Kind==NodeKind.VirtualMachine&&Project.Nodes.Find(n=>n.Id==id)?.Kind==NodeKind.Hypervisor)Host(node,id,true);else Containment.Assign(Project,node,id);});RefreshDetails();}
    public Node AddVirtualMachine(Node host)
    {
        if(MapEditing.IsLocked(Project,host))throw new InvalidDataException("Unlock the host before adding virtual machines.");
        if(host.Kind!=NodeKind.Hypervisor)throw new InvalidOperationException("Choose a hypervisor host.");
        var node=new Node{Kind=NodeKind.VirtualMachine,Name="Virtual machine "+(Project.Nodes.Count+1),X=host.X+20,Y=host.Y+70};node.ZOrder=MapEditing.Front(Project);Edit(()=>{Project.Nodes.Add(node);Host(node,host.Id,true);});Select(node);return node;
    }
    public void MoveSelection(double dx,double dy){foreach(var n in Project.Nodes.Where(n=>Selection.Contains(n.Id)&&!Project.Ancestors(n).Any(a=>Selection.Contains(a.Id))).ToList())Containment.Move(Project,n,dx,dy);}
    public void SnapSelection(){foreach(var n in Project.Nodes.Where(n=>Selection.Contains(n.Id)&&!Project.Ancestors(n).Any(a=>Selection.Contains(a.Id))).ToList())Containment.Move(Project,n,Math.Round(n.X/10)*10-n.X,Math.Round(n.Y/10)*10-n.Y);}
    public Node AddSwitchStack(int members = 5, double x = 100, double y = 100)
    {
        var node = new Node { Kind = NodeKind.Switch, Name = "Cisco switch stack", StackTechnology = "Cisco stack", X = x, Y = y, Width = 220 };
        node.StackMembers = Enumerable.Range(1, Math.Clamp(members, 1, 99)).Select(number => new StackMember { Number = number, Name = $"Switch {number}" }).ToList();
        node.ZOrder=MapEditing.Front(Project);Edit(() => Project.Nodes.Add(node));Select(node);return node;
    }
    public void AddStackMember(Node node)
    {
        if(node.Kind != NodeKind.Switch) return;
        int number = Enumerable.Range(1, int.MaxValue).First(number => !node.StackMembers.Any(m => m.Number == number));
        Edit(() => node.StackMembers.Add(new StackMember { Number = number, Name = $"Switch {number}" }));
    }
    public bool RemoveStackMember(Node node, StackMember member)
    {
        if(Project.Connections.Any(c => c.From == node.Id && c.FromStackMemberId == member.Id || c.To == node.Id && c.ToStackMemberId == member.Id)) return false;
        Edit(() => node.StackMembers.Remove(member));return true;
    }
    public void Select(Node? n, bool additive = false) { SelectedConnection = null; if (!additive) Selection.Clear(); if (n != null) { if (additive && !Selection.Add(n.Id)) Selection.Remove(n.Id); else Selection.Add(n.Id); } Notify(); }
    public void Delete() { Edit(() => { if (SelectedConnection != null) { if(SelectedConnection.Kind==ConnectionKind.Hosting&&Project.Nodes.Find(n=>n.Id==SelectedConnection.To) is {} guest)Host(guest,null,true);Project.Connections.Remove(SelectedConnection); SelectedConnection = null; } else { var roots=MapEditing.SelectionRoots(Project,Selection); if(roots.Any(n=>!MapEditing.CanMove(Project,n)))throw new InvalidDataException("Unlock selected items before deleting them."); var ids = new HashSet<string>(Selection); foreach (var n in Project.Nodes) if (Project.Ancestors(n).Any(a => ids.Contains(a.Id))) ids.Add(n.Id); Project.Nodes.RemoveAll(n => ids.Contains(n.Id));foreach(var guest in Project.Nodes.Where(n=>n.HostId!=null&&ids.Contains(n.HostId)))guest.HostId=null; Project.Connections.RemoveAll(c => ids.Contains(c.From) || ids.Contains(c.To)); Selection.Clear(); } }); }
    public void Copy() { var ids = new HashSet<string>(Selection); foreach (var n in Project.Nodes) if (Project.Ancestors(n).Any(a => Selection.Contains(a.Id))) ids.Add(n.Id); copied = ProjectStorage.Clone(Project); copied.Nodes.RemoveAll(n => !ids.Contains(n.Id)); copied.Connections.RemoveAll(c => !ids.Contains(c.From) || !ids.Contains(c.To)); foreach (var n in copied.Nodes){if (n.ParentId != null && !ids.Contains(n.ParentId)) n.ParentId = null;if(n.HostId!=null&&!ids.Contains(n.HostId))n.HostId=null;} }
    public void Paste() { if (copied.Nodes.Count == 0) return; Edit(() => { var clone = ProjectStorage.Clone(copied); var ids = clone.Nodes.ToDictionary(n => n.Id, _ => Guid.NewGuid().ToString("N")); Selection.Clear(); foreach (var n in clone.Nodes) { n.Id = ids[n.Id]; n.ParentId = n.ParentId is { } id ? ids[id] : null;n.HostId=n.HostId is {} host?ids[host]:null; n.X += 30; n.Y += 30; n.ZOrder=MapEditing.Front(Project); Project.Nodes.Add(n); Selection.Add(n.Id); } foreach (var c in clone.Connections) { c.Id = Guid.NewGuid().ToString("N"); c.From = ids[c.From]; c.To = ids[c.To]; c.Route = c.Route.Select(p => new MapPoint(p.X + 30, p.Y + 30)).ToList(); Project.Connections.Add(c); } }); }
    public void Connect(Node n) { if (ConnectFrom == null || !Project.Nodes.Any(x => x.Id == ConnectFrom)) { ConnectFrom = n.Id; Status = "Choose the second device"; } else if (ConnectFrom != n.Id) { Edit(() => Project.Connections.Add(new() { From = ConnectFrom, To = n.Id, Bandwidth = "1 Gbps" })); ConnectFrom = null; ConnectMode = false; Status = "Connection created"; } Notify(); }
    public void Align(bool horizontal) => AlignSelection(horizontal?Alignment.Top:Alignment.Left);
    public void AlignSelection(Alignment alignment) { Edit(()=>MapEditing.ArrangeSelection(Project,MapEditing.SelectionRoots(Project,Selection),alignment));RefreshDetails(); }
    public void Distribute(bool horizontal) { Edit(()=>MapEditing.ArrangeSelection(Project,MapEditing.SelectionRoots(Project,Selection),null,horizontal));RefreshDetails(); }
    public void ReorderSelection(bool front) { if(Selection.Count==0)return;Edit(()=>MapEditing.Reorder(Project,MapEditing.SelectionRoots(Project,Selection),front));RefreshDetails(); }
    public void LockSelection(bool locked) { Edit(()=>{foreach(var n in Project.Nodes.Where(n=>Selection.Contains(n.Id)))n.Locked=locked;});RefreshDetails(); }
    public void FitSelectedContainers() { Edit(()=>{foreach(var n in Project.Nodes.Where(n=>Selection.Contains(n.Id)&&n.CanContainNodes).OrderByDescending(n=>Project.Ancestors(n).Count()))MapEditing.FitContents(Project,n);});RefreshDetails(); }
    public void SetCompact(bool compact, bool all=false)
    {
        var containers=Project.Nodes.Where(n=>n.CanContainNodes&&(all||Selection.Contains(n.Id)||Project.Ancestors(n).Any(a=>Selection.Contains(a.Id)))).ToList();
        if(containers.Count==0)return;
        Edit(()=>{foreach(var n in containers)n.Collapsed=compact;});RefreshDetails();
    }
    public void Reveal(Node n)
    {
        var parents=Project.Ancestors(n).Where(a=>a.Collapsed).ToList();
        if(parents.Count>0)Edit(()=>{foreach(var parent in parents)parent.Collapsed=false;});
        TypeFilter=null;ConfidenceFilter=null;Select(n);RefreshDetails();
    }
    public void EditSubnets(string? name, string? color, double? width, double? height)
    {
        var nodes=Project.Nodes.Where(n=>Selection.Contains(n.Id)&&n.Kind==NodeKind.Subnet).ToList();
        if(nodes.Count==0)throw new InvalidDataException("Select one or more subnets first.");
        if(color!=null&&color.Length>0&&!MapEditing.ValidColor(color))throw new InvalidDataException("Use a color such as #69C7BF, or choose Automatic.");
        if(width.HasValue&&(!double.IsFinite(width.Value)||width<220||width>10000)||height.HasValue&&(!double.IsFinite(height.Value)||height<120||height>10000))throw new InvalidDataException("Width must be 220–10000 and height 120–10000.");
        if((width.HasValue||height.HasValue)&&nodes.Any(n=>MapEditing.IsLocked(Project,n)))throw new InvalidDataException("Unlock selected subnets before changing their sizes.");
        Edit(()=>{foreach(var n in nodes){if(name!=null)n.Name=name;if(color!=null)n.AccentColor=color;if(width.HasValue||height.HasValue)Containment.Resize(Project,n,width??n.Width,height??n.Height);}});RefreshDetails();
    }
    public void Saved() { Dirty = false; Notify(); }
    public void SetDeviceImage(Node n,bool enabled)
    {
        if(n.CanContainNodes)return;
        Edit(()=>{n.UseDeviceImage=enabled;Containment.Resize(Project,n,Math.Max(enabled?180:100,n.Width),enabled?Math.Max(48,n.ImageSize+8):Math.Max(60,n.Height));});RefreshDetails();
    }
    public void ResizeDeviceImage(Node n,double size,bool record=true)
    {
        if(!n.UseDeviceImage||MapEditing.IsLocked(Project,n))return;
        if(!double.IsFinite(size))return;
        size=Math.Clamp(size,16,256);
        void Resize(){double delta=size-n.ImageSize;n.ImageSize=size;Containment.Resize(Project,n,n.ShowDeviceInfo?Math.Max(100,n.Width+delta):size+8,Math.Max(n.ShowDeviceInfo?48:32,size+8));}
        if(record)Edit(Resize);else{Resize();Touch();}
    }
}
