using RackTrail.Core;

namespace RackTrail.Desktop;

internal static class EditingWorkflowChecks
{
    public static void Run(Action<bool,string> check)
    {
        var vm=new EditorViewModel();var a=vm.Add(NodeKind.Subnet,100,100);var b=vm.Add(NodeKind.Subnet,700,100);var other=vm.Add(NodeKind.Subnet,1300,100);
        a.Name="A";b.Name="B";other.Name="Unchanged";vm.Select(a);vm.Select(b,true);
        vm.EditSubnets("Voice","#69C7BF",266,120);
        check(a.Name=="Voice"&&b.Name=="Voice"&&a.Height==120&&b.Width==266&&other.Name=="Unchanged"&&other.AccentColor=="","Batch subnet edit affects only the selected subnets");
        vm.Undo();check(vm.Project.Nodes.Find(n=>n.Id==a.Id)!.Name=="A"&&vm.Project.Nodes.Find(n=>n.Id==b.Id)!.Name=="B","One Undo restores the complete batch");
        vm.Redo();check(vm.Project.Nodes.Find(n=>n.Id==a.Id)!.Height==120&&vm.Project.Nodes.Find(n=>n.Id==b.Id)!.AccentColor=="#69C7BF","Redo restores batch color and dimensions");
        string before=ProjectStorage.Serialize(vm.Project);bool rejected=false;
        try{vm.EditSubnets("Invalid","oops",300,120);}catch(InvalidDataException){rejected=true;}
        check(rejected&&ProjectStorage.Serialize(vm.Project)==before,"Invalid batch color leaves every selected subnet unchanged");
        vm.LockSelection(true);before=ProjectStorage.Serialize(vm.Project);vm.MoveSelection(100,100);vm.SnapSelection();check(ProjectStorage.Serialize(vm.Project)==before,"Locked multi-selection ignores drag and grid snap");
        rejected=false;try{vm.Delete();}catch(InvalidDataException){rejected=true;}
        check(rejected&&ProjectStorage.Serialize(vm.Project)==before,"Delete cannot accidentally remove locked selected containers");
        vm.LockSelection(false);vm.ReorderSelection(true);var layers=vm.Project.Nodes.Where(n=>vm.Selection.Contains(n.Id)).Select(n=>n.ZOrder).ToArray();
        check(layers.All(z=>z>vm.Project.Nodes.Find(n=>n.Id==other.Id)!.ZOrder),"Bring to front raises the full multi-selection");
        vm.Undo();vm.Redo();check(vm.Project.Nodes.Where(n=>vm.Selection.Contains(n.Id)).Select(n=>n.ZOrder).SequenceEqual(layers),"Front/back operations support Undo and Redo");

        var host=vm.Add(NodeKind.Site,50,800);var subnet=vm.Add(NodeKind.Subnet,70,870);vm.AssignContainer(subnet,host.Id);var child=vm.Add(NodeKind.Server,90,940);vm.AssignContainer(child,subnet.Id);
        var original=vm.Project.Nodes.Select(n=>(n.Id,n.X,n.Y,n.Width,n.Height)).ToArray();vm.SetCompact(true,true);
        check(!vm.Project.Visible(child)&&vm.Project.Nodes.Where(n=>n.CanContainNodes).All(n=>n.Collapsed),"Compact map collapses all containers");
        vm.SetCompact(false,true);check(vm.Project.Visible(child)&&original.SequenceEqual(vm.Project.Nodes.Select(n=>(n.Id,n.X,n.Y,n.Width,n.Height))),"Expanded map restores visibility without moving or resizing saved items");
        vm.SetCompact(true,true);vm.TypeFilter=NodeKind.Printer;vm.ConfidenceFilter=ClassificationConfidence.High;vm.Reveal(child);
        check(vm.Selected==child&&vm.Visible(child)&&!host.Collapsed&&!subnet.Collapsed,"Search reveal expands all ancestors and clears restrictive filters");
        vm.Select(subnet);vm.FitSelectedContainers();check(Containment.IsInside(child,subnet),"Fit selected container retains child membership and padding");
        vm.Copy();vm.Paste();var pasted=vm.Selected!;check(vm.Selection.Count==2&&vm.Project.Nodes.Where(n=>vm.Selection.Contains(n.Id)).All(n=>n.ZOrder>subnet.ZOrder),"Pasted subtree is raised above existing objects");
        vm.Undo();check(!vm.Project.Nodes.Any(n=>n.Id==pasted.Id),"Undo removes the complete pasted subtree");
    }
}
