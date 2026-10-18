namespace RackTrail.Core;

public static class ProjectLimits
{
    public const int MaximumNodes = 2000;
    public const int MaximumContainmentDepth = 32;

    public static Dictionary<string,int> ValidateHierarchy(Project project)
    {
        if(project.Nodes is null||project.Nodes.Count>MaximumNodes)throw new InvalidDataException($"Projects support at most {MaximumNodes} nodes.");
        var nodes=new Dictionary<string,Node>(StringComparer.Ordinal);
        foreach(var node in project.Nodes){if(node is null||string.IsNullOrWhiteSpace(node.Id))throw new InvalidDataException("Missing node identifier.");if(!nodes.TryAdd(node.Id,node))throw new InvalidDataException("Duplicate node identifiers.");}
        var children=new Dictionary<string,List<Node>>(StringComparer.Ordinal);var queue=new Queue<Node>();
        foreach(var node in project.Nodes){if(node.ParentId is not {} parent){queue.Enqueue(node);continue;}if(!nodes.TryGetValue(parent,out var container)||!container.CanContainNodes)throw new InvalidDataException("Missing container.");if(!children.TryGetValue(parent,out var list))children[parent]=list=[];list.Add(node);}
        var depths=new Dictionary<string,int>(StringComparer.Ordinal);
        while(queue.TryDequeue(out var node)){int depth=node.ParentId is {} parent?depths[parent]+1:0;if(depth>MaximumContainmentDepth)throw new InvalidDataException($"Container nesting supports at most {MaximumContainmentDepth} levels.");depths.Add(node.Id,depth);if(children.TryGetValue(node.Id,out var list))foreach(var child in list)queue.Enqueue(child);}
        if(depths.Count!=nodes.Count)throw new InvalidDataException("Container cycle.");
        return depths;
    }
}
