using System.Net;
using System.Numerics;
namespace RackTrail.Core;

public record ValidationIssue(string NodeId, string Message, bool Excepted);
public record IpNetwork(BigInteger Start, BigInteger End, int Bits)
{
    public static bool TryParse(string text, out IpNetwork? network)
    {
        network = null; var parts = text.Split('/');
        if (parts.Length != 2 || !IPAddress.TryParse(parts[0], out var ip) || !int.TryParse(parts[1], out int prefix)) return false;
        int bits = ip.GetAddressBytes().Length * 8;
        if (prefix < 0 || prefix > bits) return false;
        var value = Number(ip); var size = BigInteger.One << (bits - prefix); var start = value / size * size;
        network = new(start, start + size - 1, bits); return true;
    }
    public static BigInteger Number(IPAddress ip) => new(ip.GetAddressBytes(), true, true);
    public bool Contains(IPAddress ip) => ip.GetAddressBytes().Length * 8 == Bits && Number(ip) >= Start && Number(ip) <= End;
}
public static class NetworkValidator
{
    public static List<ValidationIssue> Validate(Project p)
    {
        var issues = new List<ValidationIssue>(); var ips = new Dictionary<string, Node>(); var nets = new List<(Node Node, IpNetwork Net)>();
        void Add(Node n, string message) => issues.Add(new(n.Id, message, n.AllowValidationExceptions));
        foreach (var n in p.Nodes)
        {
            if (n.StackMembers.Count > 0)
            {
                if (n.Kind != NodeKind.Switch) Add(n, $"{n.Name}: stack members must belong to a switch");
                if (n.StackMembers.Any(m => m.Number < 1)) Add(n, $"{n.Name}: stack member numbers must be positive");
                if (n.StackMembers.Select(m => m.Number).Distinct().Count() != n.StackMembers.Count) Add(n, $"{n.Name}: duplicate stack member number");
                if (n.StackMembers.Select(m => m.Id).Distinct().Count() != n.StackMembers.Count) Add(n, $"{n.Name}: duplicate stack member identifier");
            }
            foreach (var text in n.Ips)
            {
                if (!IPAddress.TryParse(text, out var ip)) { Add(n, $"{n.Name}: invalid address {text}"); continue; }
                var key = ip.ToString(); if (ips.TryGetValue(key, out var previous)) Add(n, $"{n.Name}: duplicate {key} with {previous.Name}"); else ips[key] = n;
                var subnet = p.Ancestors(n).FirstOrDefault(a => a.Kind == NodeKind.Subnet);
                if (subnet != null && IpNetwork.TryParse(subnet.Cidr, out var net) && !net!.Contains(ip)) Add(n, $"{n.Name}: {text} is outside {subnet.Cidr}");
            }
            if (n.Kind == NodeKind.Subnet)
            {
                if (!IpNetwork.TryParse(n.Cidr, out var net)) Add(n, $"{n.Name}: invalid CIDR {n.Cidr}");
                else { nets.Add((n, net!)); if (n.Gateway.Length > 0 && (!IPAddress.TryParse(n.Gateway, out var gateway) || !net!.Contains(gateway))) Add(n, $"{n.Name}: gateway is invalid or outside subnet"); }
                if (n.VlanId.Length > 0 && (!int.TryParse(n.VlanId, out var vlan) || vlan < 1 || vlan > 4094)) Add(n, $"{n.Name}: VLAN must be 1–4094");
            }
        }
        for (int i = 0; i < nets.Count; i++) for (int j = i + 1; j < nets.Count; j++) if (nets[i].Net.Bits == nets[j].Net.Bits && nets[i].Net.Start <= nets[j].Net.End && nets[j].Net.Start <= nets[i].Net.End) Add(nets[j].Node, $"{nets[j].Node.Name}: overlaps {nets[i].Node.Name}");
        foreach(var c in p.Connections)
        {
            void CheckMember(string nodeId,string? memberId)
            {
                if(memberId == null) return;
                var node = p.Nodes.Find(n => n.Id == nodeId);
                if(node != null && (!node.IsStack || !node.StackMembers.Any(m => m.Id == memberId))) Add(node,$"{node.Name}: connection references a missing stack member");
            }
            CheckMember(c.From,c.FromStackMemberId);CheckMember(c.To,c.ToStackMemberId);
        }
        return issues;
    }
}
