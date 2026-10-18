using System.Text;
namespace RackTrail.Core;

public static class SpatialTextExporter
{
    private sealed record Box(Node Node,int X,int Y,int Right,int Bottom,List<string> Text)
    {
        public int Width=>Right-X+1;
        public int Height=>Bottom-Y+1;
    }
    private sealed class Axis
    {
        private readonly double[] values;
        private readonly int[] positions;
        public int Length=>positions.Length==0?1:positions[^1]+3;
        public Axis(IEnumerable<double> coordinates,IEnumerable<(double Start,double End,int Size)> constraints,double scale,int gapLimit)
        {
            values=coordinates.Distinct().Order().ToArray();positions=new int[values.Length];
            var ends=constraints.Where(c=>c.End>c.Start).GroupBy(c=>c.End).ToDictionary(g=>g.Key,g=>g.ToList());
            for(int i=0;i<values.Length;i++){
                positions[i]=i==0?2:positions[i-1]+Math.Clamp((int)Math.Round((values[i]-values[i-1])*scale),1,gapLimit);
                if(ends.TryGetValue(values[i],out var rules))foreach(var rule in rules)positions[i]=Math.Max(positions[i],positions[Array.BinarySearch(values,rule.Start)]+rule.Size);
            }
        }
        public int At(double value)=>positions[Array.BinarySearch(values,value)];
    }
    public static string Export(Project project,TextExportOptions options)
    {
        string Clean(string text){text=text.Replace('\r',' ').Replace('\n',' ').Replace('\t',' ');if(!options.Unicode)text=text.Replace('·','|').Replace('—','-').Replace('–','-').Replace('’','\'');return options.Unicode?new string(text.Select(c=>char.IsControl(c)?' ':c).ToArray()):new string(text.Normalize(NormalizationForm.FormKD).Select(c=>c is >= ' ' and <= '~'?c:'?').ToArray());}
        List<string> Wrap(string text,int size){var result=new List<string>();text=Clean(text);while(text.Length>size){int at=text.LastIndexOf(' ',size-1,size);if(at<1)at=size;result.Add(text[..at]);text=text[at..].TrimStart();}result.Add(text);return result;}
        string newline=options.WindowsLineEndings?"\r\n":"\n";
        var nodes=project.Nodes.Where(project.Visible).OrderBy(n=>project.Ancestors(n).Count()).ThenBy(n=>n.Id,StringComparer.Ordinal).ToList();
        if(nodes.Count==0)return Clean(project.Name)+newline+"MAP / no visible devices or containers"+newline;
        if(nodes.Any(n=>!double.IsFinite(n.X+n.Y+n.Width+n.DisplayHeight)||n.Width<=0||n.DisplayHeight<=0))return "Map geometry is invalid. Use the outline layout to inspect this project."+newline;
        var references=nodes.OrderBy(n=>n.Id,StringComparer.Ordinal).Select((n,i)=>(n.Id,Ref:$"N{i+1:00}")).ToDictionary(x=>x.Id,x=>x.Ref);
        var memberLines=new Dictionary<(string Node,string Member),int>();var texts=new Dictionary<string,List<string>>();var widths=new Dictionary<string,int>();
        foreach(var node in nodes){
            int width=node.CanContainNodes?36:Math.Clamp(Clean(node.Name).Length+10,24,36);widths[node.Id]=width;
            var text=new List<string>();void Add(string value){if(value.Length>0)text.AddRange(Wrap(value,width-4));}
            Add($"[{references[node.Id]}] {(node.CanContainNodes&&node.Collapsed?"[+] ":"")}{node.Name}");
            if(node.IsContainer){if(options.IncludeCidr)Add(node.Cidr);if(options.IncludeVlan&&node.VlanId.Length>0)Add("VLAN "+node.VlanId);}
            else{if(options.Detailed)Add(node.DisplayType);if(options.IncludeIps)Add(node.Addresses);if(options.Detailed&&options.IncludeRoles)Add(node.Role);}
            if(node.IsStack&&node.ShowStackMembers)foreach(var member in node.StackMembers.OrderBy(m=>m.Number)){memberLines[(node.Id,member.Id)]=text.Count;Add($"{member.Number:00} {member.Name}"+(options.IncludeRoles?" / "+member.Role:""));}
            texts[node.Id]=text;
        }
        var xs=nodes.SelectMany(n=>new[]{n.X,n.X+n.Width}).ToList();var ys=nodes.SelectMany(n=>new[]{n.Y,n.Y+n.DisplayHeight}).ToList();
        var xRules=nodes.Select(n=>(Start:n.X,End:n.X+n.Width,Size:widths[n.Id]-1)).ToList();
        var yRules=nodes.Select(n=>(Start:n.Y,End:n.Y+n.DisplayHeight,Size:texts[n.Id].Count+1+(n.CanContainNodes?1:0))).ToList();
        foreach(var node in nodes){var parent=nodes.Find(n=>n.Id==node.ParentId);if(parent==null||parent.Collapsed)continue;
            if(node.X>parent.X)xRules.Add((parent.X,node.X,2));if(node.X+node.Width<parent.X+parent.Width)xRules.Add((node.X+node.Width,parent.X+parent.Width,2));
            if(node.Y>parent.Y)yRules.Add((parent.Y,node.Y,texts[parent.Id].Count+4));if(node.Y+node.DisplayHeight<parent.Y+parent.DisplayHeight)yRules.Add((node.Y+node.DisplayHeight,parent.Y+parent.DisplayHeight,1));
        }
        double modelWidth=Math.Max(1,xs.Max()-xs.Min());double scale=Math.Clamp(options.Width,40,240)/modelWidth;
        var xAxis=new Axis(xs,xRules,scale,12);var yAxis=new Axis(ys,yRules,scale/2,6);int columns=xAxis.Length,rows=yAxis.Length;
        if((long)columns*rows>300000||columns>3000||rows>3000)return "Map is too large for a readable text grid. Use the outline layout or export SVG/PNG."+newline;
        var boxes=nodes.Select(n=>new Box(n,xAxis.At(n.X),yAxis.At(n.Y),xAxis.At(n.X+n.Width),yAxis.At(n.Y+n.DisplayHeight),texts[n.Id])).ToDictionary(b=>b.Node.Id);
        var borders=new byte[columns*rows];var wires=new byte[borders.Length];var owners=new int[borders.Length];var letters=new char[borders.Length];var blocked=new bool[borders.Length];
        int Index(int x,int y)=>y*columns+x;
        void Edge(byte[] grid,int x,int y,int nx,int ny){if(x<0||y<0||nx<0||ny<0||x>=columns||nx>=columns||y>=rows||ny>=rows)return;grid[Index(x,y)]|=(byte)(nx>x?2:nx<x?8:ny>y?4:1);grid[Index(nx,ny)]|=(byte)(nx>x?8:nx<x?2:ny>y?1:4);}
        foreach(var node in nodes){var box=boxes[node.Id];for(int x=box.X;x<box.Right;x++){Edge(borders,x,box.Y,x+1,box.Y);Edge(borders,x,box.Bottom,x+1,box.Bottom);}for(int y=box.Y;y<box.Bottom;y++){Edge(borders,box.X,y,box.X,y+1);Edge(borders,box.Right,y,box.Right,y+1);}
            for(int y=box.Y;y<=box.Bottom;y++)for(int x=box.X;x<=box.Right;x++)if(!node.CanContainNodes||node.Collapsed)blocked[Index(x,y)]=true;
            for(int line=0;line<box.Text.Count;line++)for(int c=0;c<box.Text[line].Length&&box.X+2+c<box.Right;c++){int at=Index(box.X+2+c,box.Y+1+line);letters[at]=box.Text[line][c];blocked[at]=true;}
        }
        (int X,int Y,int OutX,int OutY) Port(Box a,Box b,string? member=null){int cx=(a.X+a.Right)/2,cy=(a.Y+a.Bottom)/2;
            if(member!=null&&memberLines.TryGetValue((a.Node.Id,member),out int row)){cy=a.Y+1+row;return b.X>=a.X?(a.Right,cy,a.Right+1,cy):(a.X,cy,a.X-1,cy);}
            if(b.Y>a.Bottom)return(cx,a.Bottom,cx,a.Bottom+1);if(b.Bottom<a.Y)return(cx,a.Y,cx,a.Y-1);
            return b.X>=a.X?(a.Right,cy,a.Right+1,cy):(a.X,cy,a.X-1,cy);
        }
        int routingBudget=1_000_000;
        List<int>? Route(int start,int end,int group){
            var queue=new PriorityQueue<int,int>();var costs=new Dictionary<int,int>();var previous=new Dictionary<int,int>();int first=start*4;costs[first]=0;queue.Enqueue(first,0);int limit=0;
            while(queue.TryDequeue(out int state,out _)&&limit++<150000&&routingBudget-->0){int cell=state/4,dir=state%4;if(cell==end){var path=new List<int>();for(int at=state;;at=previous[at]){path.Add(at/4);if(at==first)break;}path.Reverse();return path;}
                int x=cell%columns,y=cell/columns;var next=new[]{(x,y-1),(x+1,y),(x,y+1),(x-1,y)};
                for(int d=0;d<4;d++){var(nx,ny)=next[d];if(nx<0||ny<0||nx>=columns||ny>=rows)continue;int target=Index(nx,ny);if(blocked[target]&&target!=end)continue;int key=target*4+d;int cost=costs[state]+2+(d==dir?0:3)+(wires[target]!=0&&owners[target]!=group?6:0)+(borders[target]!=0?2:0);if(costs.TryGetValue(key,out int old)&&old<=cost)continue;costs[key]=cost;previous[key]=state;int estimate=2*(Math.Abs(nx-end%columns)+Math.Abs(ny-end/columns));queue.Enqueue(key,cost+estimate);}
            }
            return null;
        }
        var ledger=new List<string>();int link=0,unrouted=0;
        foreach(var connection in project.Connections.OrderBy(c=>c.From,StringComparer.Ordinal).ThenBy(c=>c.To,StringComparer.Ordinal).ThenBy(c=>c.Id,StringComparer.Ordinal)){
            var from=project.VisibleEndpoint(connection.From);var to=project.VisibleEndpoint(connection.To);if(from==null||to==null||from.Id==to.Id||!boxes.ContainsKey(from.Id)||!boxes.ContainsKey(to.Id))continue;
            link++;var a=boxes[from.Id];var b=boxes[to.Id];var source=Port(a,b,connection.FromStackMemberId);var target=Port(b,a,connection.ToStackMemberId);
            if(connection.Kind==ConnectionKind.Hosting&&b.Node.ParentId==a.Node.Id&&!a.Node.Collapsed)source=(a.X+1,a.Y+a.Text.Count+1,a.X+1,a.Y+a.Text.Count+2);
            int start=Index(source.OutX,source.OutY),end=Index(target.OutX,target.OutY);
            int group=int.Parse(references[from.Id][1..]);List<int>? path=Route(start,end,group);if(path!=null){path.Insert(0,Index(source.X,source.Y));path.Add(Index(target.X,target.Y));for(int i=1;i<path.Count;i++){int cell=path[i];Edge(wires,path[i-1]%columns,path[i-1]/columns,cell%columns,cell/columns);foreach(int at in new[]{path[i-1],cell})owners[at]=owners[at]==0||owners[at]==group?group:-1;}}
            else unrouted++;
            string detail=options.IncludeLabels?connection.LabelFor(project):"";
            string endpoint(Node visible,string original){var actual=project.Nodes.Find(n=>n.Id==original);return $"[{references[visible.Id]}] {visible.Name}"+(visible.Id!=original?$" / {actual?.Name}":"");}
            ledger.Add($"L{link:00} {endpoint(from,connection.From)} -> {endpoint(to,connection.To)}"+(connection.Kind==ConnectionKind.Hosting?" [hosting]":"")+(detail.Length>0?" | "+detail:"")+(path==null?" [route not drawn]":""));
        }
        char Glyph(int mask){if(!options.Unicode)return mask is 1 or 4 or 5?'|':mask is 2 or 8 or 10?'-':'+';return mask switch{1 or 4 or 5=>'│',2 or 8 or 10=>'─',6=>'┌',12=>'┐',3=>'└',9=>'┘',7=>'├',13=>'┤',14=>'┬',11=>'┴',_=>'┼'};}
        var output=new List<string>{Clean(project.Name),"MAP / canvas arrangement"};
        if(columns>options.Width)output.Add($"Expanded to {columns} columns to keep map labels readable.");
        for(int y=0;y<rows;y++){var line=new char[columns];for(int x=0;x<columns;x++){int at=Index(x,y);int mask=borders[at]|wires[at];line[x]=letters[at]!=0?letters[at]:wires[at]==15&&owners[at]==-1&&borders[at]==0?(options.Unicode?'╳':'x'):mask!=0?Glyph(mask):' ';}output.Add(new string(line).TrimEnd());}
        output.Add("");output.Add("CONNECTION KEY / exact endpoints");output.Add("Line crossings are not junctions. Container borders show membership, not links.");
        if(unrouted>0)output.Add($"{unrouted} crowded connection(s) are listed below without a drawn route.");
        if(ledger.Count==0)output.Add("No visible connections.");foreach(var entry in ledger)output.AddRange(Wrap(entry,Math.Max(columns,40)));
        return string.Join(newline,output)+newline;
    }
}
