using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Media;
using RackTrail.Core;

namespace RackTrail.Desktop;

internal static class ContainerPicker
{
    public static List<ContainerChoice> Populate(ComboBox combo, Project project, IEnumerable<Node> candidates, string? emptyLabel=null, string? selectedId=null)
    {
        var choices=ContainerChoices.Create(project,candidates,emptyLabel);
        combo.ItemTemplate=new FuncDataTemplate<ContainerChoice>((choice,_)=>
        {
            if(choice==null)return new TextBlock();
            var panel=new StackPanel{Margin=new Thickness(Math.Min(choice.Depth,8)*14,4,0,4),Spacing=2,MaxWidth=440};
            panel.Children.Add(new TextBlock{Text=(choice.Depth>0?"↳ ":"")+choice.Path,TextWrapping=TextWrapping.Wrap,FontWeight=choice.Depth==0?FontWeight.SemiBold:FontWeight.Normal});
            if(choice.Details.Length>0)panel.Children.Add(new TextBlock{Text=choice.Details,FontSize=11,TextWrapping=TextWrapping.Wrap,Opacity=.8});
            return panel;
        });
        combo.ItemsSource=choices;
        combo.SelectedIndex=choices.FindIndex(c=>c.Id==selectedId);
        if(combo.SelectedIndex<0&&choices.Count>0)combo.SelectedIndex=0;
        combo.MaxDropDownHeight=480;
        return choices;
    }
}
