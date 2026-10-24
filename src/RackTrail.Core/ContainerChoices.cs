namespace RackTrail.Core;

public record ContainerChoice(string? Id, string Path, string Details, int Depth);

public static class ContainerChoices
{
    public static List<ContainerChoice> Create(Project p, IEnumerable<Node> candidates, string? emptyLabel=null)
    {
        var eligible=candidates.Select(n=>n.Id).ToHashSet();
        var children=p.Nodes.ToLookup(n=>n.ParentId??"");
        var choices=new List<ContainerChoice>();
        if(emptyLabel!=null)choices.Add(new(null,emptyLabel,"",0));
        void Visit(Node n,int depth)
        {
            if(eligible.Contains(n.Id))
            {
                string path=string.Join(" › ",p.Ancestors(n).Reverse().Append(n).Select(a=>a.Name));
                string details=string.Join(" · ",new[]{n.DisplayType,n.VlanId.Length>0?"VLAN "+n.VlanId:"",n.Cidr,MapEditing.IsLocked(p,n)?"Locked":""}.Where(v=>v.Length>0));
                choices.Add(new(n.Id,path,details,depth));
            }
            foreach(var child in children[n.Id].OrderBy(c=>c.Name,StringComparer.OrdinalIgnoreCase))Visit(child,depth+1);
        }
        foreach(var root in children[""].OrderBy(n=>n.Name,StringComparer.OrdinalIgnoreCase))Visit(root,0);
        var duplicates=choices.Where(c=>c.Id!=null).GroupBy(c=>(c.Path,c.Details)).Where(g=>g.Count()>1).SelectMany(g=>g).Select(c=>c.Id).ToHashSet();
        return choices.Select(c=>duplicates.Contains(c.Id)?c with {Details=c.Details+" · ID "+c.Id}:c).ToList();
    }
}
