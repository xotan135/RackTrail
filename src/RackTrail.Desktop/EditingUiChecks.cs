using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using RackTrail.Core;

namespace RackTrail.Desktop;

internal static class EditingUiChecks
{
    public static void Run(string directory)
    {
        void Check(bool pass,string label){if(!pass)throw new InvalidOperationException(label);Console.WriteLine("PASS "+label);}
        var vm=new EditorViewModel();var older=vm.Add(NodeKind.Server,100,100);var newer=vm.Add(NodeKind.Switch,100,100);using var canvas=new MapCanvas(vm);
        Check(canvas.NodeAt(new(120,120))==newer,"New overlapping item receives clicks");
        vm.Select(older);Check(canvas.NodeAt(new(120,120))==newer,"Selection does not silently override explicit layer order");
        vm.ReorderSelection(true);Check(canvas.NodeAt(new(120,120))==older,"Bring to front updates hit testing");
        vm.ReorderSelection(false);Check(canvas.NodeAt(new(120,120))==newer,"Send to back remains effective while selected");
        var container=vm.Add(NodeKind.Subnet,100,100);Check(canvas.NodeAt(new(120,120))==container,"New container is accessible above overlapping devices");
        var child=vm.Add(NodeKind.Server,120,170);vm.AssignContainer(child,container.Id);vm.Select(container);vm.ReorderSelection(true);
        Check(canvas.NodeAt(new(130,180))==child,"Raised container does not cover its child");
        container.Collapsed=true;Check(!canvas.PaintOrder().Contains(child),"Collapsed container hides its descendants from hit testing");
        Check(canvas.NodeAt(new(130,180))!=child,"Direct collapse removes descendants from the spatial hit index");
        container.Collapsed=false;Check(canvas.PaintOrder().Contains(child)&&canvas.NodeAt(new(130,180))==child,"Direct expansion restores descendant paint order and hit testing");
        container.Collapsed=true;
        canvas.Measure(new Size(900,600));canvas.Arrange(new Rect(0,0,900,600));canvas.FocusNode(child);
        Check(vm.Visible(child)&&Math.Abs(child.X*canvas.Zoom+canvas.Pan.X+child.Width*canvas.Zoom/2-450)<.01,"Search focus reveals and centers the result");
        canvas.Pan=new(-1800,700);var added=canvas.AddInView(NodeKind.Printer);
        Check(vm.Selected==added&&canvas.PaintOrder().Last()==added,"Viewport insertion selects and raises the new item");
        Check(vm.Project.Nodes.Where(n=>n!=added&&vm.Visible(n)).All(n=>added.X+added.Width<=n.X||added.X>=n.X+n.Width||added.Y+added.DisplayHeight<=n.Y||added.Y>=n.Y+n.DisplayHeight),"Viewport insertion avoids visible map objects");

        var window=new MainWindow{ShowInTaskbar=false};var model=(EditorViewModel)window.DataContext!;
        var site=model.Add(NodeKind.Site,40,40);site.Name="Site West";site.Width=880;site.Height=300;
        var first=model.Add(NodeKind.Subnet,60,110);first.Name="Data";first.Cidr="192.0.2.0/24";first.VlanId="5";first.Width=266;first.Height=120;model.AssignContainer(first,site.Id);
        var second=model.Add(NodeKind.Subnet,350,110);second.Name="Voice";second.Cidr="198.51.100.0/24";second.VlanId="10";second.Width=266;second.Height=120;model.AssignContainer(second,site.Id);
        window.Show();Avalonia.Threading.Dispatcher.UIThread.RunJobs();window.UpdateLayout();model.Select(first);model.Select(second,true);
        Check(window.GetVisualDescendants().OfType<Button>().Any(b=>b.Content?.ToString()=="HTML"),"Main toolbar exposes standalone HTML export");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();window.UpdateLayout();
        T Find<T>(string name) where T:Control=>window.GetVisualDescendants().OfType<T>().Single(c=>c.Name==name);
        Find<TextBox>("BatchSubnetName").Text="Office VLAN";Find<ComboBox>("BatchSubnetColor").SelectedIndex=3;Find<TextBox>("BatchSubnetHeight").Text="120";
        Find<Button>("ApplyBatchSubnets").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Check(first.Name=="Office VLAN"&&second.AccentColor=="#69C7BF","Multi-selection controls apply batch subnet changes");
        model.Undo();Check(model.Project.Nodes.Find(n=>n.Id==first.Id)!.Name=="Data","Batch UI edit is one undo action");model.Redo();
        var map=window.GetVisualDescendants().OfType<MapCanvas>().Single();map.Fit();
        foreach(var theme in new[]{ThemeVariant.Light,ThemeVariant.Dark})
        {
            window.RequestedThemeVariant=theme;Avalonia.Threading.Dispatcher.UIThread.RunJobs();window.UpdateLayout();
            using var bitmap=new RenderTargetBitmap(new PixelSize((int)window.Bounds.Width,(int)window.Bounds.Height),new Vector(96,96));bitmap.Render(window);bitmap.Save(Path.Combine(directory,theme==ThemeVariant.Dark?"map-editing-dark.png":"map-editing-light.png"));
        }
        model.SetCompact(true,true);Find<TextBox>("MapSearch").Text="198.51.100.0";
        Find<TextBox>("MapSearch").RaiseEvent(new Avalonia.Input.KeyEventArgs{RoutedEvent=Avalonia.Input.InputElement.KeyDownEvent,Key=Avalonia.Input.Key.Enter});
        Check(model.Selected?.Id==second.Id&&model.Visible(model.Selected),"Search Enter jumps to a subnet inside a collapsed site");
        first=model.Project.Nodes.Find(n=>n.Id==first.Id)!;first.Name="Data";
        var secondSite=model.Add(NodeKind.Site,1100,40);secondSite.Name="Site East";
        var sameName=model.Add(NodeKind.Subnet,1120,110);sameName.Name="Data";sameName.Cidr="203.0.113.0/24";sameName.VlanId="5";model.AssignContainer(sameName,secondSite.Id);
        var selectionTarget=model.Add(NodeKind.Server,50,600);model.Select(selectionTarget);Avalonia.Threading.Dispatcher.UIThread.RunJobs();window.UpdateLayout();
        var picker=Find<ComboBox>("ContainerMembership");var choices=picker.ItemsSource!.Cast<ContainerChoice>().ToList();
        Check(choices.Any(c=>c.Path=="Site West › Data")&&choices.Any(c=>c.Path=="Site East › Data"),"Membership dropdown displays distinct nested paths for duplicate names");
        picker.SelectedItem=choices.Single(c=>c.Id==sameName.Id);
        Check(selectionTarget.ParentId==sameName.Id,"Choosing a nested label assigns its exact container ID");
        var preview=new ListBox{ItemsSource=choices,ItemTemplate=picker.ItemTemplate};
        var previewWindow=new Window{Width=620,Height=520,ShowInTaskbar=false,RequestedThemeVariant=ThemeVariant.Dark,Content=preview};
        previewWindow.Show();Avalonia.Threading.Dispatcher.UIThread.RunJobs();previewWindow.UpdateLayout();
        Check(preview.GetVisualDescendants().OfType<TextBlock>().Any(t=>t.Text=="↳ Site East › Data"),"Nested picker template renders the full hierarchy path");
        using(var bitmap=new RenderTargetBitmap(new PixelSize((int)previewWindow.Bounds.Width,(int)previewWindow.Bounds.Height),new Vector(96,96))){bitmap.Render(previewWindow);bitmap.Save(Path.Combine(directory,"nested-container-picker.png"));}
        previewWindow.Close();
        model.Saved();window.Close();
    }
}
