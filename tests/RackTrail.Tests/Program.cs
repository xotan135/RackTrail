using RackTrail.Core;
using System.Net;
using System.Text.RegularExpressions;
var checks = 0;
void Assert(bool value, string description) { if (!value) throw new Exception(description); checks++; Console.WriteLine("PASS " + description); }
var sample = SampleNetwork.Create();
var text = ProjectStorage.Serialize(sample); var restored = ProjectStorage.Deserialize(text);
Assert(text == ProjectStorage.Serialize(restored), "JSON round trip preserves properties, positions, membership and connections");
restored.Connections[0].Route = [new(1, 2), new(20, 40)]; Assert(ProjectStorage.Clone(restored).Connections[0].Route.SequenceEqual(restored.Connections[0].Route), "Custom routes survive serialization");
Assert(!text.Contains("D:\\"), "Project contains no machine paths");
Assert(NetworkValidator.Validate(sample).Count == 0, "Realistic sample network validates");
Assert(IpNetwork.TryParse("2001:db8::/64", out var v6) && v6!.Contains(IPAddress.Parse("2001:db8::abcd")) && !v6.Contains(IPAddress.Parse("2001:db9::1")), "IPv6 containment");
Assert(!IpNetwork.TryParse("10.0.0.0/33", out _) && !IpNetwork.TryParse("::/129", out _), "Invalid prefix lengths rejected");
var broken = ProjectStorage.Clone(sample); broken.Nodes.Find(n => n.Id == "app")!.Addresses = "10.10.10.10, invalid, 10.9.1.1"; broken.Nodes.Find(n => n.Id == "users")!.Cidr = "10.10.10.128/25";
var issues = NetworkValidator.Validate(broken); Assert(issues.Any(i => i.Message.Contains("duplicate")), "Duplicate addresses detected"); Assert(issues.Any(i => i.Message.Contains("invalid address")), "Invalid IP detected"); Assert(issues.Any(i => i.Message.Contains("outside")), "Outside-subnet devices detected"); Assert(issues.Any(i => i.Message.Contains("overlaps")), "Overlapping subnets detected");
broken.Nodes.Find(n => n.Id == "app")!.AllowValidationExceptions = true; Assert(NetworkValidator.Validate(broken).Where(i => i.NodeId == "app").All(i => i.Excepted), "Intentional exceptions retained");
var ipv6 = new Project { Nodes = [new() { Id = "net", Kind = NodeKind.Subnet, Cidr = "2001:db8::/64" }, new() { Id = "dev", Kind = NodeKind.Server, ParentId = "net", Addresses = "2001:db8::1" }] }; Assert(NetworkValidator.Validate(ipv6).Count == 0, "IPv6 device subnet validation");
foreach (int width in new[] { 80, 120, 160 }) foreach (bool unicode in new[] { false, true })
{
    var options = new TextExportOptions(Width: width, Unicode: unicode, WindowsLineEndings: false); var export = TextExporter.Outline(sample, options); var lines = export.Split('\n');
    Assert(lines.All(l => l.Length <= width), $"{width} column width / unicode={unicode}");
    Assert(export == TextExporter.Outline(sample, options), "Deterministic text export");
    Assert(unicode || export.All(c => c <= 127), "Strict ASCII character contract");
    var borderWidths = new Dictionary<int, int>(); bool aligned = true; int boxCount = 0; foreach (var line in lines) { var trimmed = line.TrimStart(); int indent = line.Length - trimmed.Length; if (trimmed.StartsWith(unicode ? "┌" : "+") && trimmed.Contains(unicode ? '─' : '-')) { borderWidths[indent] = line.Length; boxCount++; } else if (trimmed.StartsWith(unicode ? "└" : "+") && trimmed.Contains(unicode ? '─' : '-') || trimmed.StartsWith(unicode ? "│" : "|") && line.EndsWith(unicode ? "│" : "|") && trimmed.Length > 1) aligned &= borderWidths.TryGetValue(indent, out int expected) && line.Length == expected; }
    Assert(boxCount > 0 && aligned, "Box borders and content align at configured width");
    var ledger=export[export.IndexOf("NETWORK CONNECTIONS",StringComparison.Ordinal)..].Split("\n\n");
    foreach (var c in sample.Connections) { var a = sample.Nodes.First(n => n.Id == c.From); var b = sample.Nodes.First(n => n.Id == c.To); Assert(ledger.Any(block=>block.Contains(a.Name)&&block.Contains(b.Name)&&block.Contains(c.Purpose)), "Connection endpoint and label represented together: " + c.From + " -> " + c.To); }
    Assert(export.Contains("HEADQUARTERS") && export.Contains("Server subnet") && export.Contains("10.10.10.0/24"), "Site / subnet hierarchy exported");
}
var longProject = ProjectStorage.Clone(sample); longProject.Nodes[0].Name = new string('é', 300); Assert(TextExporter.Outline(longProject, new(80)).Split('\n').All(l => l.TrimEnd('\r').Length <= 80), "Unsupported and oversized labels wrap predictably");
Assert(!Regex.IsMatch(TextExporter.Outline(sample, new(WindowsLineEndings: true)), "(?<!\\r)\\n"), "Windows line endings");
Assert(TextExporter.Markdown(sample, new()).StartsWith("```text\r\n"), "Markdown fenced export");
var svg = VisualExporter.Svg(sample); Assert(svg.Contains("<polyline") && svg.Contains("10.10.10.10") && svg.Contains("xmlns="), "SVG includes connections and device metadata");
var collapse = ProjectStorage.Clone(sample); collapse.Nodes.Find(n => n.Id == "hq")!.Collapsed = true; Assert(!collapse.Visible(collapse.Nodes.Find(n => n.Id == "ad")!) && collapse.VisibleEndpoint("ad")!.Id == "hq", "Nested collapse reroutes connection endpoints");
var invalid = ProjectStorage.Clone(sample); invalid.Nodes.Find(n => n.Id == "hq")!.ParentId = "servers"; try { ProjectStorage.Deserialize(ProjectStorage.Serialize(invalid)); Assert(false, "Cycle rejected"); } catch (InvalidDataException) { Assert(true, "Container cycles rejected"); }
var path = Path.Combine(Environment.CurrentDirectory, "artifacts", "tests", "racktrail-test-" + Guid.NewGuid() + ".json"); try { await ProjectStorage.SaveAsync(sample, path); Assert(ProjectStorage.Serialize(await ProjectStorage.LoadAsync(path)) == text, "Atomic file save and reopen"); } finally { if (File.Exists(path)) File.Delete(path); }
var legacy=ProjectStorage.Deserialize("""{"FormatVersion":1,"Name":"Legacy","Nodes":[{"Id":"s","Name":"Switch","Kind":"Switch"}],"Connections":[]}""");Assert(!legacy.Nodes[0].IsStack&&legacy.Nodes[0].StackMembers.Count==0,"Older projects load as standalone switches");
var stacked=ProjectStorage.Clone(sample);var stack=stacked.Nodes.Find(n=>n.Id=="core")!;stack.Addresses="10.10.0.5";stack.ManagementInterface="ssh://core.example.test";stack.StackTechnology="Cisco stack";
stack.StackMembers=Enumerable.Range(1,5).Select(number=>new StackMember{Id=$"member-{number}",Number=number,Name=$"Physical switch {number}",Model="Recorded Cisco model",SerialNumber=$"SERIAL{number}",Role=number==1?"Active":number==2?"Standby":"Member"}).ToList();
var stackLink=stacked.Connections.First(c=>c.From=="core"&&c.To=="ad");stackLink.FromStackMemberId="member-3";stackLink.FromPort="Gi3/0/24";stackLink.ToPort="eth0";
var stackRoundTrip=ProjectStorage.Clone(stacked);Assert(stackRoundTrip.Nodes.Find(n=>n.Id=="core")!.StackMembers[2].SerialNumber=="SERIAL3"&&stackRoundTrip.Connections.First(c=>c.Id==stackLink.Id).FromStackMemberId=="member-3","Stack inventory and member connections round-trip");
Assert(NetworkValidator.Validate(stacked).Count==0,"Five members share one management address without duplicate IP warnings");
stack.StackMembers[2].Number=7;Assert(stackLink.LabelFor(stacked).Contains("member 7")&&stackLink.FromStackMemberId=="member-3","Member renumbering preserves physical connection identity");
var invalidStack=ProjectStorage.Clone(stacked);invalidStack.Nodes.Find(n=>n.Id=="core")!.StackMembers[2].Number=1;Assert(NetworkValidator.Validate(invalidStack).Any(i=>i.Message.Contains("duplicate stack member number")),"Duplicate member numbers are detected");
invalidStack.Connections.First(c=>c.Id==stackLink.Id).FromStackMemberId="missing";Assert(NetworkValidator.Validate(invalidStack).Any(i=>i.Message.Contains("missing stack member")),"Stale member connection references are detected");
foreach(var unicode in new[]{false,true}){var exported=TextExporter.Outline(stacked,new(80,unicode,WindowsLineEndings:false));Assert(exported.Contains("5 members")&&exported.Contains("SERIAL3")&&exported.Contains("Gi3/0/24")&&exported.Contains("member 7"),"Stack inventory and port-specific links appear in detailed export");Assert(exported.Split('\n').All(l=>l.Length<=80),"Stack details respect export column limit");var overview=TextExporter.Outline(stacked,new(80,unicode,Detailed:false));Assert(overview.Contains("5 members")&&!overview.Contains("SERIAL3"),"Stack overview summarizes member inventory");}
stack.ShowStackMembers=true;Assert(VisualExporter.Svg(stacked).Contains("Physical switch 3"),"SVG includes expanded stack members");var memberRoute=MapLayout.Route(stacked,stackLink);Assert(memberRoute[0].Y==stack.Y+78+24*4,"Expanded connections route to their physical member row");
stack.ShowStackMembers=false;Assert(!VisualExporter.Svg(stacked).Contains("Physical switch 3"),"Collapsed stack remains a single visual device");
var printer=new Node{Id="printer",Kind=NodeKind.Printer,Name="Office printer",Vendor="HP",Addresses="10.10.20.50",ParentId="users"};var printerProject=ProjectStorage.Clone(sample);printerProject.Nodes.Add(printer);printerProject.Connections.Add(new(){From="core",To=printer.Id,Purpose="Printing"});Assert(!printer.IsContainer&&ProjectStorage.Clone(printerProject).Nodes.Last().Kind==NodeKind.Printer,"Printer is a serializable connectable device, not a container");
Assert(NetworkValidator.Validate(printerProject).Count==0&&TextExporter.Outline(printerProject,new()).Contains("Office printer / Printer"),"Printer participates in subnet validation and text export");
Assert(DeviceIcons.ResolveKey(new(){Vendor="Cisco Systems"})=="cisco","Vendor icon automatic matching");Assert(DeviceIcons.ResolveKey(new(){OperatingSystem="Windows Server 2025"})=="windows"&&DeviceIcons.ResolveKey(new(){OperatingSystem="Gentoo Linux"})=="gentoo","OS and distribution matching");
Assert(DeviceIcons.ResolveKey(new(){Vendor="Cisco",OperatingSystem="Windows",IconKey="linux"})=="linux"&&DeviceIcons.ResolveKey(new(){Vendor="Cisco",IconKey="device"})=="device","Manual icon override and standard icon choice");
Assert(DeviceIcons.ResolveKey(new(){Vendor="Unknown vendor"})=="device","Unknown vendor falls back to standard icon");
foreach(var key in DeviceIcons.BuiltInKeys)Assert(DeviceIcons.Vector(key)?.Paths.Count>0,"Bundled vector available: "+key);
var portablePng=Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aZSkAAAAASUVORK5CYII=");
var portable=new Node{Kind=NodeKind.Printer,Vendor="Acme Corp",OperatingSystem="Windows",EmbeddedIconKey="acme",IconPngBase64=Convert.ToBase64String(portablePng)};Assert(DeviceIcons.ResolveKey(portable)=="acme"&&DeviceIcons.EmbeddedPng(portable)!.SequenceEqual(portablePng),"Embedded custom vendor choice takes precedence over OS after moving project");
portable.IconKey="device";Assert(DeviceIcons.EmbeddedPng(portable)==null,"Stale embedded icon does not override standard choice");
printerProject.Nodes.Last().IconKey="hp";Assert(VisualExporter.Svg(printerProject).Contains("data-device-icon=\"hp\""),"SVG contains selected vendor vector icon");

var geometry=new Project();var outer=new Node{Kind=NodeKind.Site,Width=300,Height=180};var inner=new Node{Kind=NodeKind.Subnet,X=900,Y=900,Width=240,Height=160};var child=new Node{Kind=NodeKind.AccessPoint,X=-800,Y=-900,Width=180,Height=76};geometry.Nodes.AddRange([outer,inner,child]);Containment.Assign(geometry,inner,outer.Id);Containment.Assign(geometry,child,inner.Id);
Assert(Containment.IsInside(child,inner)&&Containment.IsInside(inner,outer),"Assignment places outside devices inside nested containers and grows ancestors");
Containment.Move(geometry,child,-9999,-9999);Assert(Containment.IsInside(child,inner),"Child drag respects left and top content boundary");Containment.Move(geometry,child,9999,9999);Assert(Containment.IsInside(child,inner),"Child drag respects right and bottom boundary");
Containment.Resize(geometry,inner,100,60);Assert(Containment.IsInside(child,inner),"Resize cannot exclude existing children");double relativeX=child.X-outer.X,relativeY=child.Y-outer.Y;Containment.Move(geometry,outer,-2000,-2000);Assert(outer.X<0&&child.X-outer.X==relativeX&&child.Y-outer.Y==relativeY,"Root moves left of origin with all descendants");
var bounds=VisualExporter.Bounds(geometry);Assert(bounds.X<=outer.X-40&&bounds.Y<=outer.Y-40&&VisualExporter.Svg(geometry).Contains($"viewBox=\"{bounds.X} {bounds.Y}"),"Negative positions are included in SVG bounds");
bool cycleRejected=false;try{Containment.Assign(geometry,outer,inner.Id);}catch(InvalidOperationException){cycleRejected=true;}Assert(cycleRejected,"Container assignment rejects cycles");
inner.Collapsed=true;double expandedHeight=inner.Height;MapLayout.Arrange(geometry);inner.Collapsed=false;Assert(inner.Height>=expandedHeight&&Containment.IsInside(child,inner),"Auto layout retains expanded container geometry when collapsed");
var hostProject=new Project();var hostNode=new Node{Id="host",Kind=NodeKind.Hypervisor,HypervisorPlatform="Hyper-V",Width=460,Height=240};var guestNode=new Node{Id="guest",Kind=NodeKind.VirtualMachine,HostId="host"};hostProject.Nodes.AddRange([hostNode,guestNode]);Containment.Assign(hostProject,guestNode,hostNode.Id);hostProject.Connections.Add(new(){From=hostNode.Id,To=guestNode.Id,Kind=ConnectionKind.Hosting});var hostClone=ProjectStorage.Clone(hostProject);
Assert(hostClone.Nodes[1].HostId=="host"&&hostClone.Connections[0].Kind==ConnectionKind.Hosting,"Hypervisor host and hosting relation serialize");Assert(MapLayout.Route(hostProject,hostProject.Connections[0])[0].Y<guestNode.Y,"Hosting route starts in host header");hostNode.Collapsed=true;Assert(!hostProject.Visible(guestNode)&&MapLayout.Route(hostProject,hostProject.Connections[0]).Count==0,"Collapsed host hides guest and internal hosting link");
var modelNode=new Node{Vendor="Ubiquiti",Model="U6 Pro",EmbeddedIconKey="u6pro",IconPngBase64="AQID"};Assert(DeviceIcons.ResolveKey(modelNode)=="ubiquiti"&&!modelNode.UseDeviceImage,"Model image does not automatically replace default device appearance");modelNode.UseDeviceImage=true;Assert(DeviceIcons.ResolveKey(modelNode)=="u6pro","Explicit image mode selects matching model image");modelNode.Shape=DeviceShape.Circle;hostProject.Nodes.Add(modelNode);Assert(ProjectStorage.Clone(hostProject).Nodes.Last().UseDeviceImage&&ProjectStorage.Clone(hostProject).Nodes.Last().Shape==DeviceShape.Circle,"Image mode and standard shape persist separately");
var phonesProject=ProjectStorage.Clone(sample);
var phones=new Node{Id="phones",Kind=NodeKind.Phones,Name="Office phones",ParentId="users"};
phonesProject.Nodes.Add(phones);
phonesProject.Connections.Add(new(){From="core",To=phones.Id,Purpose="Voice"});
var phonesClone=ProjectStorage.Clone(phonesProject);
Assert(phonesClone.Nodes.Last().Kind==NodeKind.Phones&&phonesClone.Nodes.Last().ParentId=="users"&&phonesClone.Connections.Last().To=="phones","Grouped phones preserve subnet membership and connection through save/reopen");
Assert(!phones.CanContainNodes&&NetworkValidator.Validate(phonesProject).Count==0,"Grouped phones validate without individual handset addresses");
Assert(TextExporter.Outline(phonesProject,new()).Contains("Office phones / Phones")&&VisualExporter.Svg(phonesProject).Contains("data-device-icon=\"phones\""),"Phone group and symbol appear in text and SVG exports");
phones.Addresses="10.99.0.5";
Assert(NetworkValidator.Validate(phonesProject).Any(i=>i.NodeId=="phones"&&i.Message.Contains("outside")),"Optional phone address is validated against its subnet");
Assert(DeviceIcons.ResolveKey(new(){Kind=NodeKind.Phones,IconKey="device"})=="phones","Standard Phones card keeps its phone symbol");
var scanAddresses=ScanTargets.Parse("192.168.50.0/29,192.168.50.2","192.168.50.1,192.168.50.4-192.168.50.5");
Assert(string.Join(",",scanAddresses.Select(a=>a.ToString()))=="192.168.50.2,192.168.50.3,192.168.50.6","Target subnets omit network/broadcast, deduplicate and honor exclusions");
Assert(ScanTargets.Parse("10.0.0.0/31").Count==2&&ScanTargets.Parse("10.0.0.1/32").Count==1,"Point-to-point and single host targets are supported");
Assert(ScanTargets.Parse("192.168.1.10-192.168.1.20","192.168.1.0/28").Count==5,"CIDR exclusions apply across explicit ranges");
foreach(var invalidTarget in new[]{"","10.0.0.0/16","2001:db8::/64","10.0.0.20-10.0.0.1","224.0.0.1","127.1","10.0.0.1/33"}){try{ScanTargets.Parse(invalidTarget);Assert(false,"Invalid scan target rejected");}catch(ArgumentException){Assert(true,"Invalid or oversized scan target rejected: "+invalidTarget);}}
try{ScanTargets.Parse("10.0.0.1","10.0.0.0/24");Assert(false,"Empty target set rejected");}catch(ArgumentException){Assert(true,"Excluding every target does not run a scan");}
var scanReport=await NetworkDiscovery.ScanAsync(ScanTargets.Parse("192.168.50.1-192.168.50.4"),probe:(ip,ct)=>Task.FromResult<long?>(ip.ToString().EndsWith(".2")?null:5),resolve:(ip,ct)=>Task.FromResult(ip.ToString().EndsWith(".1")?"desk.example":""));
Assert(scanReport.Hosts.Count==3&&scanReport.Completed==4&&!scanReport.Cancelled&&scanReport.Hosts[0].Hostname=="desk.example","Discovery captures only ping replies with optional hostnames using controlled probes");
using(var scanCancel=new CancellationTokenSource()){scanCancel.Cancel();var stopped=await NetworkDiscovery.ScanAsync(scanAddresses,cancellationToken:scanCancel.Token,probe:(ip,ct)=>throw new Exception("Cancelled scan must not probe"));Assert(stopped.Cancelled&&stopped.Hosts.Count==0,"Cancelled scan does not contact targets");}
var probeErrors=await NetworkDiscovery.ScanAsync(scanAddresses,probe:(ip,ct)=>throw new System.Net.NetworkInformation.PingException("Unavailable"),resolve:(ip,ct)=>Task.FromResult(""));
Assert(probeErrors.Errors==scanAddresses.Count&&probeErrors.Hosts.Count==0,"Probe errors are reported separately from unreachable addresses");
var scanProject=new Project{Nodes=[new(){Id="scan-net",Kind=NodeKind.Subnet,Cidr="192.168.50.0/24",Width=460,Height=240},new(){Kind=NodeKind.Device,Addresses="192.168.50.1"}]};
var imported=DiscoveryImport.Add(scanProject,scanReport.Hosts,"scan-net");
Assert(imported.Count==2&&imported.All(n=>n.Kind==NodeKind.Device&&n.ParentId=="scan-net")&&NetworkValidator.Validate(scanProject).Count==0,"Review import skips existing addresses and assigns generic devices to subnet");
Assert(DiscoveryImport.Add(scanProject,scanReport.Hosts,"scan-net").Count==0,"Repeated discovery import creates no duplicate devices");
Assert(DiscoveryImport.Add(scanProject,[new("10.9.0.1","outside",2)],"scan-net").Count==0,"Subnet import skips out-of-subnet results");
Assert(ProjectStorage.Clone(scanProject).Nodes.Count(n=>n.Kind==NodeKind.Device)==3,"Imported devices round-trip through save/reopen");
var spatial=new Project{Name="Spatial test",Nodes=[
new(){Id="top",Name="Internet",Kind=NodeKind.Internet,X=160,Y=-200},
new(){Id="left",Name="Left device",Kind=NodeKind.Device,X=-100,Y=100,Addresses="10.1.0.2"},
new(){Id="right",Name="Right device",Kind=NodeKind.Device,X=450,Y=100,Addresses="10.1.0.3"}],Connections=[new(){Id="a",From="top",To="left",Purpose="Left link"},new(){Id="b",From="top",To="right",Purpose="Right link"},new(){Id="c",From="left",To="right",Purpose="Redundant link"}]};
var spatialBefore=ProjectStorage.Serialize(spatial);
var spatialText=TextExporter.Export(spatial,new(120,WindowsLineEndings:false));
var diagram=spatialText[..spatialText.IndexOf("CONNECTION KEY",StringComparison.Ordinal)];
Assert(diagram.IndexOf("Internet",StringComparison.Ordinal)<diagram.IndexOf("Left device",StringComparison.Ordinal),"Map export retains top-to-bottom placement with negative coordinates");
var spatialRows=diagram.Split('\n');var leftRow=Array.FindIndex(spatialRows,l=>l.Contains("Left device"));var rightRow=Array.FindIndex(spatialRows,l=>l.Contains("Right device"));
Assert(leftRow==rightRow&&spatialRows[leftRow].IndexOf("Left device",StringComparison.Ordinal)<spatialRows[rightRow].IndexOf("Right device",StringComparison.Ordinal),"Devices placed side by side remain on the same text row in left-to-right order");
Assert(spatialText.Contains("Left link")&&spatialText.Contains("Right link")&&spatialText.Contains("Redundant link")&&!spatialText.Contains("[route not drawn]"),"Map draws actual cyclic connections and keeps exact link details");
Assert(ProjectStorage.Serialize(spatial)==spatialBefore&&spatialText==TextExporter.Export(spatial,new(120,WindowsLineEndings:false)),"Spatial export is deterministic and never changes map geometry");
Assert(spatialText.All(c=>c<=127)&&TextExporter.Export(spatial,new(Unicode:true)).Contains('┌'),"Map supports strict ASCII and Unicode boxes");
Assert(!Regex.IsMatch(TextExporter.Export(spatial,new()),"(?<!\\r)\\n"),"Spatial export respects Windows line endings");
var nestedSpatial=ProjectStorage.Clone(sample);var spatialContainer=nestedSpatial.Nodes.Find(n=>n.Id=="hq")!;spatialContainer.Collapsed=true;
var collapsedSpatial=TextExporter.Export(nestedSpatial,new());var collapsedDiagram=collapsedSpatial[..collapsedSpatial.IndexOf("CONNECTION KEY",StringComparison.Ordinal)];
Assert(collapsedDiagram.Contains("[+] HEADQUARTERS")&&!collapsedDiagram.Contains("AD / DNS"),"Collapsed containers hide descendants in the spatial diagram");
Assert(Regex.Replace(collapsedSpatial,@"\s+"," ").Contains("AD / DNS"),"Connections to hidden children retain their exact endpoints in the connection key");
Assert(TextExporter.Export(sample,new(Layout:TextExportLayout.Outline))==TextExporter.Outline(sample,new()),"Original fixed-width outline remains selectable");
Assert(TextExporter.Export(new(),new()).Contains("no visible"),"Empty maps export without a layout exception");
var crowdedSpatial=ProjectStorage.Clone(spatial);crowdedSpatial.Nodes[0].Name=new string('A',140);
Assert(TextExporter.Export(crowdedSpatial,new(80)).Contains(new string('A',20)),"Long device names wrap inside readable spatial boxes");
var memberSpatial=new Project{Nodes=[new(){Id="s",Name="Member stack",Kind=NodeKind.Switch,Width=260,Height=180,ShowStackMembers=true,StackMembers=[new(){Id="m1",Number=1,Name="First member"},new(){Id="m2",Number=2,Name="Second member"}]},new(){Id="d",Name="Peer",Kind=NodeKind.Device,X=400}],Connections=[new(){From="s",To="d",FromStackMemberId="m2"}]};
var memberDiagram=TextExporter.Export(memberSpatial,new()).Split("CONNECTION KEY")[0].Split('\n');
var stackTitle=memberDiagram.First(l=>l.Contains("Member stack"));int stackLeft=stackTitle.IndexOf('|'),stackRight=stackTitle.IndexOf('|',stackLeft+1);
Assert(memberDiagram.First(l=>l.Contains("02 Second member"))[stackRight]=='+',"Expanded stack connection attaches at its assigned member row");
var hostedSpatial=new Project{Nodes=[new(){Id="h",Kind=NodeKind.Hypervisor,Name="Hyper-V",Width=460,Height=240},new(){Id="g",Kind=NodeKind.VirtualMachine,Name="Guest",ParentId="h",X=20,Y=70}],Connections=[new(){From="h",To="g",Kind=ConnectionKind.Hosting}]};
var hostedText=TextExporter.Export(hostedSpatial,new());
Assert(hostedText.Contains("[hosting]")&&!hostedText.Contains("[route not drawn]"),"Hosting links render inside the host container");
var excessiveDepth=new Project{Nodes=Enumerable.Range(0,65).Select(i=>new Node{Id="depth-"+i,Kind=NodeKind.Site,ParentId=i==0?null:"depth-"+(i-1)}).ToList()};
bool depthBlocked=false;try{ProjectStorage.Deserialize(ProjectStorage.Serialize(excessiveDepth));}catch(InvalidDataException){depthBlocked=true;}
var tinyIcon=Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aZSkAAAAASUVORK5CYII=");
var oversizedIcon=(byte[])tinyIcon.Clone();System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(oversizedIcon.AsSpan(16,4),100000);System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(oversizedIcon.AsSpan(20,4),100000);
var maliciousIcon=new Node{IconKey="oversized",EmbeddedIconKey="oversized",IconPngBase64=Convert.ToBase64String(oversizedIcon)};
bool imageBlocked=DeviceIcons.EmbeddedPng(maliciousIcon)==null;
Console.WriteLine($"Boundary reproduction: excessive depth blocked={depthBlocked}; oversized embedded image blocked={imageBlocked}");
Assert(depthBlocked,"Deep acyclic project is rejected before containment repair");
Assert(imageBlocked,"Compact image with huge dimensions is rejected before native decoding");
Project Chain(int count)=>new(){Nodes=Enumerable.Range(0,count).Select(i=>new Node{Id="chain-"+i,Kind=NodeKind.Site,ParentId=i==0?null:"chain-"+(i-1)}).ToList()};
bool RejectsProject(Project value){try{ProjectStorage.Deserialize(ProjectStorage.Serialize(value));return false;}catch(InvalidDataException){return true;}}
var boundaryChain=Chain(ProjectLimits.MaximumContainmentDepth+1);boundaryChain.Nodes.Reverse();
var boundaryLoaded=ProjectStorage.Deserialize(ProjectStorage.Serialize(boundaryChain));Containment.Repair(boundaryLoaded);
Assert(boundaryLoaded.Nodes.All(n=>n.ParentId==null||Containment.IsInside(n,boundaryLoaded.Nodes.Find(p=>p.Id==n.ParentId)!)),"Reverse-ordered chain at depth limit repairs iteratively with all descendants inside");
Assert(RejectsProject(Chain(ProjectLimits.MaximumContainmentDepth+2)),"A chain one parent link over the limit is rejected");
var broadProject=new Project{Nodes=Enumerable.Range(0,ProjectLimits.MaximumNodes).Select(i=>new Node{Id="broad-"+i,Kind=i==0?NodeKind.Site:NodeKind.Device,ParentId=i==0?null:"broad-0"}).ToList()};
var broadLoaded=ProjectStorage.Deserialize(ProjectStorage.Serialize(broadProject));Containment.Repair(broadLoaded);
Assert(broadLoaded.Nodes.Count==ProjectLimits.MaximumNodes&&broadLoaded.Nodes.Skip(1).All(n=>Containment.IsInside(n,broadLoaded.Nodes[0])),"Broad shallow project at node limit loads and repairs");
broadProject.Nodes.Add(new(){Id="over-node-limit"});
Assert(RejectsProject(broadProject),"A project one node over the budget is rejected");
var directCycle=Chain(2);directCycle.Nodes[0].ParentId=directCycle.Nodes[1].Id;string cycleBefore=ProjectStorage.Serialize(directCycle);
bool directCycleBlocked=false;try{Containment.Repair(directCycle);}catch(InvalidDataException){directCycleBlocked=true;}
Assert(directCycleBlocked&&ProjectStorage.Serialize(directCycle)==cycleBefore,"Direct in-memory cycle is rejected before repair changes geometry");
var reparent=Chain(ProjectLimits.MaximumContainmentDepth+1);var branch=new Node{Id="branch",Kind=NodeKind.Site};var leaf=new Node{Id="branch-leaf",ParentId=branch.Id,Kind=NodeKind.Device};reparent.Nodes.AddRange([branch,leaf]);
bool branchBlocked=false;try{Containment.Assign(reparent,branch,"chain-"+(ProjectLimits.MaximumContainmentDepth-1));}catch(InvalidDataException){branchBlocked=true;}
Assert(branchBlocked&&branch.ParentId==null,"Reparenting checks the full descendant depth before changing membership");
Containment.Assign(reparent,branch,"chain-"+(ProjectLimits.MaximumContainmentDepth-2));
Assert(ProjectLimits.ValidateHierarchy(reparent)[leaf.Id]==ProjectLimits.MaximumContainmentDepth,"Valid subtree assignment at the exact depth limit still works");
Assert(ImageLimits.Allows(2048,2048)&&!ImageLimits.Allows(2048,2048,8)&&!ImageLimits.Allows(long.MaxValue,2),"Decoded pixel budget accepts its exact limit without overflow");
foreach(var dimensions in new[]{(0u,1u),(1u,0u),(2049u,1u),(1u,2049u),(uint.MaxValue,uint.MaxValue)}){
var header=(byte[])tinyIcon.Clone();System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(16,4),dimensions.Item1);System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(20,4),dimensions.Item2);
maliciousIcon.IconPngBase64=Convert.ToBase64String(header);
Assert(DeviceIcons.EmbeddedPng(maliciousIcon)==null,"Invalid or excessive image dimensions rejected: "+dimensions);
}
var highDepth=(byte[])tinyIcon.Clone();System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(highDepth.AsSpan(16,4),2048);System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(highDepth.AsSpan(20,4),2048);highDepth[24]=16;highDepth[25]=6;
Assert(!ImageLimits.AllowsPng(highDepth),"High bit-depth PNG is rejected when decoded-byte budget is exceeded");
maliciousIcon.IconPngBase64=Convert.ToBase64String(tinyIcon[..24]);Assert(DeviceIcons.EmbeddedPng(maliciousIcon)==null,"Truncated PNG metadata rejected");
maliciousIcon.IconPngBase64="not base64!";Assert(DeviceIcons.EmbeddedPng(maliciousIcon)==null,"Malformed base64 keeps safe fallback behavior");
maliciousIcon.IconPngBase64=Convert.ToBase64String(oversizedIcon);Assert(!VisualExporter.Svg(new(){Nodes=[maliciousIcon]}).Contains("data:image"),"SVG does not forward an oversized embedded image");
maliciousIcon.IconPngBase64=Convert.ToBase64String(tinyIcon);Assert(DeviceIcons.EmbeddedPng(maliciousIcon)!.SequenceEqual(tinyIcon),"Valid small embedded PNG remains portable and byte-preserving");
Console.WriteLine($"\n{checks} checks passed.");
