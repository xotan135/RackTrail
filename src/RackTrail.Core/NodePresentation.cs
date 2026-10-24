namespace RackTrail.Core;

public record VisualBox(double X,double Y,double Width,double Height);
public record VisualLabel(string Text,double X,double Y,double Width,double Size,bool Center=false);
public record NodePresentation(VisualBox Icon,IReadOnlyList<VisualLabel> Labels)
{
    public static NodePresentation Create(Node n)
    {
        string address=n.Addresses.Length>0?n.Addresses:n.Hostname!=n.Name?n.Hostname:"";
        if(n.CanContainNodes||n.IsStack&&n.ShowStackMembers)
        {
            var lines=new List<VisualLabel>{new((n.CanContainNodes?(n.Collapsed?"▸ ":"▾ "):"")+n.Name,n.X+36,n.Y+13,n.Width-48,13),
                new(n.IsContainer?n.Cidr+(n.VlanId.Length>0?" · VLAN "+n.VlanId:""):n.VisualDetail,n.X+36,n.Y+36,n.Width-48,10)};
            if(n.IsStack&&n.ShowStackMembers){int i=0;foreach(var member in n.StackMembers.OrderBy(m=>m.Number))lines.Add(new($"{member.Number:00}  {member.Name} / {member.Role}",n.X+36,n.Y+70+i++*24,n.Width-48,11));}
            return new(new(n.X+12,n.Y+14,20,20),lines);
        }
        if(n.UseDeviceImage)
        {
            double imageSize=Math.Max(1,Math.Min(n.ImageSize,Math.Min(n.DisplayHeight-8,n.ShowDeviceInfo?n.Width*.36:n.Width-8)));
            var box=new VisualBox(n.ShowDeviceInfo?n.X+4:n.X+(n.Width-imageSize)/2,n.Y+(n.DisplayHeight-imageSize)/2,imageSize,imageSize);
            if(!n.ShowDeviceInfo)return new(box,Array.Empty<VisualLabel>());
            double textX=box.X+imageSize+10,width=Math.Max(1,n.X+n.Width-4-textX);
            double scale=Math.Clamp(Math.Min((n.DisplayHeight-8)/44,width/100),.65,1);
            string model=n.Model.Length>0?n.Model:string.Join(" ",new[]{n.Vendor,n.DisplayType}.Where(s=>s.Length>0));
            var text=new[]{n.Name,model,address}.Where(s=>s.Length>0).ToList();
            double lineHeight=15*scale,top=n.Y+(n.DisplayHeight-text.Count*lineHeight)/2;
            return new(box,text.Select((s,i)=>new VisualLabel(s,textX,top+i*lineHeight,width,(i==0?12:10)*scale)).ToList());
        }
        // Center the complete icon/text group within the usable area of each shape.
        double inset=n.Shape==DeviceShape.Circle?.72:1;
        double contentWidth=Math.Max(1,(n.Width-16)*inset),contentHeight=Math.Max(1,(n.DisplayHeight-12)*inset);
        double scaleCard=Math.Clamp(Math.Min(contentHeight/67,contentWidth/112),.6,1.2);
        double centerX=n.X+n.Width/2,topCard=n.Y+(n.DisplayHeight-67*scaleCard)/2;
        double iconSize=18*scaleCard;
        return new(new(centerX-iconSize/2,topCard,iconSize,iconSize),new[]{
            new VisualLabel(n.Name,centerX-contentWidth/2,topCard+23*scaleCard,contentWidth,13*scaleCard,true),
            new VisualLabel(n.DisplayType+(address.Length>0?" · "+address:""),centerX-contentWidth/2,topCard+41*scaleCard,contentWidth,10*scaleCard,true),
            new VisualLabel($"{(n.Classification?.ManualType.HasValue==true?"Manual · ":"")}{n.Classification?.Confidence??ClassificationConfidence.Low} confidence",centerX-contentWidth/2,topCard+55*scaleCard,contentWidth,9*scaleCard,true)});
    }
}
