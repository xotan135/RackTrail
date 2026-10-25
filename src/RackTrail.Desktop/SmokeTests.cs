using RackTrail.Core;
using SkiaSharp;
namespace RackTrail.Desktop;

public static class SmokeTests
{
    public static async Task Run(string directory)
    {
        Directory.CreateDirectory(directory); int count = 0;
        void Check(bool condition, string name) { if (!condition) throw new Exception(name); count++; Console.WriteLine("PASS " + name); }
        EditingWorkflowChecks.Run(Check);
        var vm = new EditorViewModel(); Check(vm.Project.Nodes.Count==0&&!vm.Dirty,"Launch defaults to a clean blank map");vm.Replace(SampleNetwork.Create()); var subnet = vm.Add(NodeKind.Subnet, 100, 500); vm.Edit(() => { subnet.Cidr = "192.168.50.0/24"; subnet.VlanId = "50"; subnet.Name = "Lab subnet"; }); var device = vm.Add(NodeKind.Server, 150, 580); vm.Edit(() => { device.ParentId = subnet.Id; device.Name = "Lab server"; device.Addresses = "192.168.50.10"; }); Check(NetworkValidator.Validate(vm.Project).Count == 0, "Create subnet, device, membership and IP");
        var core = vm.Project.Nodes.First(n => n.Id == "core"); vm.Connect(core); vm.Connect(device); Check(vm.Project.Connections.Any(c => c.From == core.Id && c.To == device.Id), "Connect devices");
        vm.Edit(() => device.Role = "Lab services"); vm.Undo(); Check(vm.Project.Nodes.First(n => n.Id == device.Id).Role == "", "Undo property edit"); vm.Redo(); Check(vm.Project.Nodes.First(n => n.Id == device.Id).Role == "Lab services", "Redo property edit");
        vm.Selection.Clear(); vm.Selection.Add(subnet.Id); vm.Copy(); vm.Paste(); Check(vm.Selection.Count == 2, "Copy and paste container with child"); var pasted = vm.Project.Nodes.Single(n=>vm.Selection.Contains(n.Id)&&n.IsContainer); Check(vm.Project.Nodes.Single(n=>vm.Selection.Contains(n.Id)&&!n.IsContainer).ParentId==pasted.Id&&pasted.Id!=subnet.Id, "Paste hierarchy retained"); vm.Undo();
        vm.Selection.Clear(); vm.Selection.Add("core"); vm.Selection.Add("ad"); int links = vm.Project.Connections.Count; vm.Copy(); vm.Paste(); var copiedLink = vm.Project.Connections.Last(); Check(vm.Project.Connections.Count == links + 1 && vm.Selection.Contains(copiedLink.From) && vm.Selection.Contains(copiedLink.To), "Copy remaps internal connection endpoints"); vm.Undo();
        vm.Selection.Clear(); vm.Selection.Add(subnet.Id); vm.Delete(); Check(!vm.Project.Nodes.Any(n => n.Id == device.Id) && !vm.Project.Connections.Any(c => c.To == device.Id), "Container delete removes descendants and incident links"); vm.Undo(); Check(vm.Project.Nodes.Any(n => n.Id == device.Id), "Undo deletion restores hierarchy");
        var path = Path.Combine(directory, "workflow.networkmap.json"); await ProjectStorage.SaveAsync(vm.Project, path); var reopened = await ProjectStorage.LoadAsync(path); Check(ProjectStorage.Serialize(vm.Project) == ProjectStorage.Serialize(reopened), "Save and reopen edited project");
        await File.WriteAllTextAsync(Path.Combine(directory, "workflow.txt"), TextExporter.Export(reopened, new())); await File.WriteAllTextAsync(Path.Combine(directory, "workflow.svg"), VisualExporter.Svg(reopened)); PngExporter.Save(reopened, Path.Combine(directory, "workflow.png")); using var bitmap = SKBitmap.Decode(Path.Combine(directory, "workflow.png")); Check(bitmap.Width >= 800 && bitmap.Height >= 600, "PNG decodes with expected dimensions"); Check(bitmap.GetPixel(0, 0) == SKColor.Parse("#101722"), "PNG background rendered");
        var sample = SampleNetwork.Create(); await ProjectStorage.SaveAsync(sample, Path.Combine(directory, "sample.networkmap.json")); await File.WriteAllTextAsync(Path.Combine(directory, "sample-ascii.txt"), TextExporter.Export(sample, new(80))); await File.WriteAllTextAsync(Path.Combine(directory, "sample-unicode.txt"), TextExporter.Export(sample, new(120, true))); await File.WriteAllTextAsync(Path.Combine(directory, "sample.svg"), VisualExporter.Svg(sample)); PngExporter.Save(sample, Path.Combine(directory, "sample.png"));
        Check(Branding.CreateIcon()!=null,"Embedded RackTrail window/taskbar icon loads");
        var stackVm=new EditorViewModel();stackVm.Replace(SampleNetwork.Create());var stack=stackVm.AddSwitchStack();Check(stack.IsStack&&stack.StackMembers.Count==5,"Create five-member switch stack");stackVm.Edit(()=>{stack.Addresses="10.10.0.5";stack.ManagementInterface="ssh://core.example.test";stack.ShowStackMembers=true;stack.Width=280;stack.Y=260;});
        var target=stackVm.Project.Nodes.First(n=>n.Id=="ad");stackVm.Connect(stack);stackVm.Connect(target);var stackConnection=stackVm.Project.Connections.Last();stackVm.Edit(()=>{stackConnection.FromStackMemberId=stack.StackMembers[2].Id;stackConnection.FromPort="Gi3/0/24";stackConnection.ToPort="eth0";});
        Check(!stackVm.RemoveStackMember(stack,stack.StackMembers[2]),"Referenced stack member cannot be removed");stackVm.AddStackMember(stack);Check(stack.StackMembers.Count==6,"Add stack member");Check(stackVm.RemoveStackMember(stack,stack.StackMembers.Last())&&stack.StackMembers.Count==5,"Remove unconnected member");
        stackVm.Selection.Clear();stackVm.Selection.Add(stack.Id);stackVm.Selection.Add(target.Id);stackVm.Copy();stackVm.Paste();Check(stackVm.Project.Connections.Last().FromStackMemberId==stack.StackMembers[2].Id&&NetworkValidator.Validate(stackVm.Project).All(i=>!i.Message.Contains("missing stack member")),"Stack copying preserves scoped member references");stackVm.Undo();
        var stackedSample=ProjectStorage.Clone(stackVm.Project);var savedStack=stackedSample.Nodes.First(n=>n.Id==stack.Id);foreach(var member in savedStack.StackMembers)member.Model="Cisco (enter model)";
        MapLayout.Arrange(stackedSample);await ProjectStorage.SaveAsync(stackedSample,Path.Combine(directory,"stacked-switch.networkmap.json"));await File.WriteAllTextAsync(Path.Combine(directory,"stacked-switch.txt"),TextExporter.Export(stackedSample,new(80)));await File.WriteAllTextAsync(Path.Combine(directory,"stacked-switch.svg"),VisualExporter.Svg(stackedSample));PngExporter.Save(stackedSample,Path.Combine(directory,"stacked-switch.png"));
        using var stackBitmap=SKBitmap.Decode(Path.Combine(directory,"stacked-switch.png"));Check(stackBitmap!=null&&stackBitmap.Height>=VisualExporter.Size(stackedSample).Height-1,"Expanded stack PNG renders without cropping members");
        var iconsFolder=Path.Combine(directory,"icons");IconLibrary.WriteBuiltIns(iconsFolder);var library=new IconLibrary(iconsFolder);Check(DeviceIcons.BuiltInKeys.All(key=>library.Images.ContainsKey(key)),"Installed icons folder discovers bundled vendor/OS images");
        File.Copy(Path.Combine(iconsFolder,"cisco.png"),Path.Combine(iconsFolder,"acme.png"),true);await File.WriteAllTextAsync(Path.Combine(iconsFolder,"broken.png"),"invalid PNG");library.Reload();Check(library.Images.ContainsKey("acme")&&library.Warnings.Any(w=>w.StartsWith("broken.png")),"End-user icons are discovered and corrupt files are skipped");
        var iconVm=new EditorViewModel();var printer=iconVm.Add(NodeKind.Printer);printer.Vendor="Acme Corp";printer.OperatingSystem="Windows";library.ApplyTo(iconVm.Project);Check(printer.EmbeddedIconKey=="acme"&&DeviceIcons.EmbeddedPng(printer)!=null,"Automatic custom vendor icon is embedded in project");
        var iconProject=ProjectStorage.Clone(iconVm.Project);var movedPrinter=iconProject.Nodes.First(n=>n.Id==printer.Id);var emptyLibrary=new IconLibrary(Path.Combine(directory,"missing-icons-folder"));emptyLibrary.ApplyTo(iconProject);Check(DeviceIcons.ResolveKey(movedPrinter)=="acme"&&DeviceIcons.EmbeddedPng(movedPrinter)!=null,"Custom icon survives missing install folder on another computer");
        printer.IconKey="gentoo";library.ApplyTo(iconVm.Project);Check(DeviceIcons.ResolveKey(printer)=="gentoo"&&printer.EmbeddedIconKey=="gentoo","Manual logo selection replaces automatic vendor icon");iconVm.Selection.Clear();iconVm.Selection.Add(printer.Id);iconVm.Copy();iconVm.Paste();Check(DeviceIcons.EmbeddedPng(iconVm.Selected!)!=null,"Copy and paste retain embedded icon");
        PngExporter.Save(iconProject,Path.Combine(directory,"printer-custom-icon.png"));Check(VisualExporter.Svg(iconProject).Contains("data:image/png;base64,"),"SVG embeds portable custom icon image");
        using var changes=typeof(App).Assembly.GetManifestResourceStream("RackTrail.Desktop.CHANGELOG.md");
        string releaseHistory=changes==null?"":new StreamReader(changes).ReadToEnd();
        Check(releaseHistory.Contains("1.2.0"),"About version history is embedded");
        string releaseVersion=System.Diagnostics.FileVersionInfo.GetVersionInfo(typeof(App).Assembly.Location).ProductVersion!.Split('+')[0];
        Check(releaseHistory.Contains("## "+releaseVersion+" "),"App version matches a released entry in embedded version history");
        var iconGallery=new Project{Name="Device icon gallery"};int column=0;foreach(var key in DeviceIcons.BuiltInKeys){iconGallery.Nodes.Add(new(){Kind=NodeKind.Workstation,Name=key,IconKey=key,X=40+(column%4)*220,Y=40+(column/4)*120});column++;}iconGallery.Nodes.Add(new(){Kind=NodeKind.Printer,Name="Standard printer",X=40+(column%4)*220,Y=40+(column/4)*120,IconKey="device"});PngExporter.Save(iconGallery,Path.Combine(directory,"device-icons.png"));await File.WriteAllTextAsync(Path.Combine(directory,"device-icons.svg"),VisualExporter.Svg(iconGallery));
        foreach(int size in new[]{16,24,32,48,64,128,256})
        {
            using var brandBitmap=SKBitmap.Decode(BrandArtwork.IconPng(size));Check(brandBitmap.Width==size&&brandBitmap.Height==size,$"RT application icon renders at {size}px");
        }
        Check(BrandArtwork.MarkSvg(true).Contains(BrandArtwork.Ice)&&BrandArtwork.MarkSvg(false).Contains(BrandArtwork.Navy),"Monogram foreground adapts to light and dark surfaces");
        Check(BrandUiChecks.Completed,"Native logo geometry and light/dark UI previews render");

        var hyperVm=new EditorViewModel();hyperVm.Replace(new());var hv=hyperVm.Add(NodeKind.Hypervisor,-500,-200);var guest=hyperVm.AddVirtualMachine(hv);var guest2=hyperVm.AddVirtualMachine(hv);Check(Containment.IsInside(guest,hv)&&Containment.IsInside(guest2,hv)&&guest2.Y>=guest.Y+guest.DisplayHeight,"Guests assigned to host are inside and do not overlap");
        hyperVm.Select(hv);hyperVm.MoveSelection(-600,-400);Check(hv.X==-1100&&Containment.IsInside(guest,hv),"Free host and guests move past former left boundary");hyperVm.Select(guest);hyperVm.MoveSelection(-9999,9999);hyperVm.SnapSelection();Check(Containment.IsInside(guest,hv),"Mouse movement and snapping retain guest containment");
        hyperVm.Select(hv);hyperVm.Copy();hyperVm.Paste();var copiedHost=hyperVm.Project.Nodes.Single(n=>hyperVm.Selection.Contains(n.Id)&&n.Kind==NodeKind.Hypervisor);Check(hyperVm.Project.Nodes.Where(n=>hyperVm.Selection.Contains(n.Id)&&n.Kind==NodeKind.VirtualMachine).All(n=>n.HostId==copiedHost.Id&&n.ParentId==copiedHost.Id),"Copying host remaps guest membership and host identity");hyperVm.Undo();hv=hyperVm.Project.Nodes.First(n=>n.Kind==NodeKind.Hypervisor);guest=hyperVm.Project.Nodes.First(n=>n.Id==guest.Id);var otherHost=hyperVm.Add(NodeKind.Hypervisor,500,500);hyperVm.AssignHost(guest,otherHost.Id);Check(guest.HostId==otherHost.Id&&guest.ParentId==otherHost.Id&&hyperVm.Project.Connections.Count(c=>c.Kind==ConnectionKind.Hosting&&c.To==guest.Id)==1,"Host reassignment replaces hosting link and moves guest inside");
        hyperVm.AssignHost(guest,null);Check(guest.HostId==null&&guest.ParentId==null&&!hyperVm.Project.Connections.Any(c=>c.Kind==ConnectionKind.Hosting&&c.To==guest.Id),"Detach guest clears hosting relationship");
        var negative=new Project();var negativeNode=new Node{Kind=NodeKind.AccessPoint,X=-700,Y=-300,Vendor="Ubiquiti",Model="U6 Pro",UseDeviceImage=true,Height=132};negative.Nodes.Add(negativeNode);File.Copy(Path.Combine(iconsFolder,"ubiquiti.png"),Path.Combine(iconsFolder,"u6pro.png"),true);library.Reload();library.ApplyTo(negative);Check(negativeNode.EmbeddedIconKey=="u6pro"&&DeviceIcons.EmbeddedPng(negativeNode)!=null,"Custom model image embeds only after image mode is selected");PngExporter.Save(negative,Path.Combine(directory,"negative-device-image.png"));using var negativeBitmap=SKBitmap.Decode(Path.Combine(directory,"negative-device-image.png"));Check(Enumerable.Range(40,180).Any(x=>Enumerable.Range(50,90).Any(y=>negativeBitmap.GetPixel(x,y)!=SKColor.Parse("#101722"))),"Negative-coordinate PNG renders device image without cropping");
        await ProjectStorage.SaveAsync(hyperVm.Project,Path.Combine(directory,"hypervisors.networkmap.json"));
        var preferencePath=Path.Combine(directory,"preferences.json");Check(StartupPreferences.Load(Path.Combine(directory,"absent-preferences.json")).AskToLoadSample,"First launch offers sample prompt");new StartupPreferences{AskToLoadSample=false}.Save(preferencePath);Check(!StartupPreferences.Load(preferencePath).AskToLoadSample,"Don't ask again preference persists across launches");
        var phonesVm=new EditorViewModel();
        var voiceSubnet=phonesVm.Add(NodeKind.Subnet);voiceSubnet.Cidr="192.168.60.0/24";
        var phones=phonesVm.Add(NodeKind.Phones);phonesVm.AssignContainer(phones,voiceSubnet.Id);
        var voiceSwitch=phonesVm.Add(NodeKind.Switch);phonesVm.Connect(voiceSwitch);phonesVm.Connect(phones);
        Check(phones.Name.StartsWith("Phones")&&NetworkValidator.Validate(phonesVm.Project).Count==0,"Add and connect grouped Phones card in subnet without handset IPs");
        phonesVm.Select(phones);phonesVm.Copy();phonesVm.Paste();
        Check(phonesVm.Project.Nodes.Count(n=>n.Kind==NodeKind.Phones)==2,"Copy and paste Phones card");
        phonesVm.Undo();Check(phonesVm.Project.Nodes.Count(n=>n.Kind==NodeKind.Phones)==1,"Undo pasted Phones card");
        await ProjectStorage.SaveAsync(phonesVm.Project,Path.Combine(directory,"phones.networkmap.json"));
        var savedPhones=await ProjectStorage.LoadAsync(Path.Combine(directory,"phones.networkmap.json"));
        PngExporter.Save(savedPhones,Path.Combine(directory,"phones.png"));
        using var phoneBitmap=SKBitmap.Decode(Path.Combine(directory,"phones.png"));
        var savedPhone=savedPhones.Nodes.Single(n=>n.Kind==NodeKind.Phones);var phoneIcon=NodePresentation.Create(savedPhone).Icon;var phoneBounds=VisualExporter.Bounds(savedPhones);
        Check(phoneBitmap!=null&&Enumerable.Range((int)(phoneIcon.X-phoneBounds.X),(int)phoneIcon.Width).Any(x=>Enumerable.Range((int)(phoneIcon.Y-phoneBounds.Y),(int)phoneIcon.Height).Any(y=>phoneBitmap.GetPixel(x,y).Blue>150&&phoneBitmap.GetPixel(x,y).Red>80)),"Reopened Phones card renders its centered symbol in PNG");
        var discoveryVm=new EditorViewModel();
        discoveryVm.Edit(()=>DiscoveryImport.Add(discoveryVm.Project,[new("192.168.99.10","test-device",4),new("192.168.99.11","",2)]));
        Check(discoveryVm.Project.Nodes.Count==2&&discoveryVm.Project.Nodes.All(n=>n.Kind==NodeKind.Device),"Review import creates generic device cards without inferred connections");
        discoveryVm.Undo();Check(discoveryVm.Project.Nodes.Count==0,"One undo removes the entire discovery import");
        discoveryVm.Redo();Check(discoveryVm.Project.Nodes.Count==2&&discoveryVm.Project.Nodes.First().Hostname=="test-device","Redo restores discovered addresses and hostnames");
        var boundedVm=new EditorViewModel();var safeDevice=boundedVm.Add(NodeKind.Device);boundedVm.Saved();var beforeInvalidEdit=ProjectStorage.Serialize(boundedVm.Project);
        bool rejectedEdit=false;try{boundedVm.Edit(()=>{for(int i=0;i<ProjectLimits.MaximumNodes;i++)boundedVm.Project.Nodes.Add(new(){Id="too-many-"+i});});}catch(InvalidDataException){rejectedEdit=true;}
        Check(rejectedEdit&&!boundedVm.Dirty&&ProjectStorage.Serialize(boundedVm.Project)==beforeInvalidEdit&&boundedVm.Selected?.Id==safeDevice.Id,"Rejected over-budget edit rolls back project, dirty flag and selection");
        var limitPaste=new Project{Nodes=Enumerable.Range(0,ProjectLimits.MaximumNodes).Select(i=>new Node{Id="paste-"+i,Kind=NodeKind.Device}).ToList()};
        boundedVm.Replace(limitPaste);boundedVm.Select(limitPaste.Nodes[0]);boundedVm.Copy();bool pasteBlocked=false;
        try{boundedVm.Paste();}catch(InvalidDataException){pasteBlocked=true;}
        Check(pasteBlocked&&boundedVm.Project.Nodes.Count==ProjectLimits.MaximumNodes,"Paste cannot leave an over-budget project in memory");
        var unsafePng=File.ReadAllBytes(Path.Combine(iconsFolder,"windows.png"));System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(unsafePng.AsSpan(16,4),100000);
        var unsafeNode=new Node{IconKey="oversized",EmbeddedIconKey="oversized",IconPngBase64=Convert.ToBase64String(unsafePng)};
        Check(DeviceIcons.EmbeddedPng(unsafeNode)==null,"Oversized embedded PNG is stopped at shared boundary before renderer checks");
        using(var safeBitmap=new SKBitmap(32,32)){using var safeCanvas=new SKCanvas(safeBitmap);Check(!DeviceIconRenderer.Draw(safeCanvas,unsafeNode,new SKRect(0,0,16,16)),"Skia renderer refuses oversized embedded PNG");}
        Check(BrandUiChecks.EmbeddedImageGuardVerified,"Avalonia renderer refuses oversized embedded PNG");
        boundedVm.RunUserAction(boundedVm.Paste);Check(boundedVm.Project.Nodes.Count==ProjectLimits.MaximumNodes&&boundedVm.Status.Contains("2000"),"User paste rejects excess nodes with a message");
        boundedVm.RunUserAction(()=>boundedVm.Add(NodeKind.Device));Check(boundedVm.Project.Nodes.Count==ProjectLimits.MaximumNodes&&!boundedVm.Dirty,"User add rejects excess nodes without unhandled exception or dirtying map");
        Check(BrandUiChecks.ScanReviewVerified,"Scan UI configures SNMP, corrects type, creates subnet with parent, and undoes as one change");
        var classificationVm=new EditorViewModel();
        var identified=classificationVm.Add(NodeKind.Device);identified.Addresses="192.0.2.12";
        classificationVm.Edit(()=>DiscoveryImport.Update(classificationVm.Project,[new("192.0.2.12","cam.example",2){Facts=new(){SystemDescription="network camera"}}]));
        Check(identified.Kind==NodeKind.IoT&&identified.Classification?.Confidence==ClassificationConfidence.Medium,"Discovery update applies classification to an existing device");
        classificationVm.Edit(()=>DeviceClassifier.SetManual(identified,NodeKind.Printer));
        classificationVm.Edit(()=>DiscoveryImport.Update(classificationVm.Project,[new("192.0.2.12","cam.example",2){Facts=new(){SystemDescription="network camera"}}]));
        Check(identified.Kind==NodeKind.Printer&&identified.Classification?.AutomaticType==NodeKind.IoT,"Editor rescan preserves correction and updates automatic evidence");
        classificationVm.Undo();classificationVm.Undo();
        Check(classificationVm.Selected?.Kind==NodeKind.IoT&&classificationVm.Selected.Classification?.ManualType==null,"Undo restores classification and removes manual correction");
        classificationVm.TypeFilter=NodeKind.Printer;
        Check(!classificationVm.Visible(classificationVm.Selected!)&&classificationVm.MatchesFilter(new(){Kind=NodeKind.Subnet}),"Type filtering hides unmatched devices while retaining containers");
        classificationVm.TypeFilter=NodeKind.IoT;classificationVm.ConfidenceFilter=ClassificationConfidence.High;
        Check(!classificationVm.Visible(classificationVm.Selected!),"Confidence filter applies to map devices");
        var newTypes=new Project{Nodes=new[]{NodeKind.Phone,NodeKind.IoT,NodeKind.Device}.Select((kind,i)=>new Node{Id="type-"+i,Name=DeviceClassifier.Label(kind),Kind=kind,X=i*220}).ToList()};
        PngExporter.Save(newTypes,Path.Combine(directory,"classification-types.png"));
        Check(VisualExporter.Svg(newTypes).Contains("data-device-icon=\"iot\"")&&VisualExporter.Svg(newTypes).Contains("data-device-icon=\"unknown\""),"New device symbols render in PNG and SVG");
        PerformanceWorkflowChecks.Run(Check);
        Console.WriteLine($"{count} desktop workflow checks passed.");
    }
}
