using System.Reflection;
using System.Xml.Linq;
using System.Text;
using System.Text.RegularExpressions;
namespace RackTrail.Core;

public record VectorIcon(string Key, string Color, double Width, double Height, IReadOnlyList<string> Paths);

public static class DeviceIcons
{
    private static readonly string[] builtInKeys = ["cisco", "windows", "gentoo", "linux", "ubuntu", "debian", "apple", "hp", "dell", "ubiquiti", "accesspoint", "hardware", "phones"];
    public static IReadOnlyList<string> BuiltInKeys => builtInKeys;
    private static readonly Dictionary<string, VectorIcon> vectors = LoadVectors();
    private static Dictionary<string, VectorIcon> LoadVectors()
    {
        var colors = new Dictionary<string, string> { ["cisco"]="#38bdf8", ["windows"]="#38bdf8", ["gentoo"]="#b5a1e6", ["linux"]="#f5cc62", ["ubuntu"]="#f77943", ["debian"]="#f06485", ["apple"]="#d7e2f0", ["hp"]="#38bdf8", ["dell"]="#38bdf8", ["ubiquiti"]="#38bdf8" };
        var result = new Dictionary<string, VectorIcon>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in builtInKeys)
        {
            using var stream = typeof(DeviceIcons).Assembly.GetManifestResourceStream($"RackTrail.Core.Icons.{key}.svg");
            if (stream == null) continue;
            var root = XDocument.Load(stream).Root!;
            var box = (root.Attribute("viewBox")?.Value ?? "0 0 24 24").Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(s=>double.Parse(s,System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            var paths = root.Descendants().Where(e=>e.Name.LocalName=="path").Select(e=>e.Attribute("d")?.Value).OfType<string>().Select(NormalizePath).ToList();
            result[key] = new(key, colors[key], box[2], box[3], paths);
        }
        result["accesspoint"]=new("accesspoint","#d7e2f0",24,24,["M12 1 A11 11 0 1 0 12 23 A11 11 0 1 0 12 1 Z M12 4 A8 8 0 1 1 12 20 A8 8 0 1 1 12 4 Z", "M10 10 H14 V14 H10 Z"]);
        result["hardware"]=new("hardware","#7caaf0",24,24,["M4 1 H20 V23 H4 Z M6 3 V21 H18 V3 Z", "M8 6 H16 V8 H8 Z M8 11 H16 V13 H8 Z M8 16 H11 V18 H8 Z"]);
        result["phones"]=new("phones","#7caaf0",24,24,["M5 1 H9 V7 H7 V17 H9 V23 H5 L2 20 V4 Z", "M11 5 H22 V21 H11 Z M13 7 V11 H20 V7 Z M13 13 H15 V15 H13 Z M18 13 H20 V15 H18 Z M13 17 H15 V19 H13 Z M18 17 H20 V19 H18 Z"]);
        result["phone"]=new("phone","#7caaf0",24,24,["M5 1 H10 V7 H7 V17 H10 V23 H5 L2 20 V4 Z M14 5 H20 V19 H14 V16 H17 V8 H14 Z"]);
        result["type-server"]=result["hardware"] with{Key="type-server"};
        result["type-switch"]=new("type-switch","#7caaf0",24,24,["M1 5 H23 V19 H1 Z M3 7 V17 H21 V7 Z M5 11 H7 V15 H5 Z M9 11 H11 V15 H9 Z M13 11 H15 V15 H13 Z M17 11 H19 V15 H17 Z"]);
        result["type-router"]=new("type-router","#7caaf0",24,24,["M10 1 H14 V7 H17 L12 12 L7 7 H10 Z M10 23 H14 V17 H17 L12 12 L7 17 H10 Z M1 10 V14 H7 V17 L12 12 L7 7 V10 Z M23 10 V14 H17 V17 L12 12 L17 7 V10 Z"]);
        result["type-firewall"]=new("type-firewall","#f29b72",24,24,["M1 2 H23 V22 H1 Z M3 4 V8 H10 V4 Z M12 4 V8 H21 V4 Z M3 10 V14 H6 V10 Z M8 10 V14 H16 V10 Z M18 10 V14 H21 V10 Z M3 16 V20 H10 V16 Z M12 16 V20 H21 V16 Z"]);
        result["type-workstation"]=new("type-workstation","#7caaf0",24,24,["M1 2 H23 V17 H14 V20 H19 V23 H5 V20 H10 V17 H1 Z M3 4 V15 H21 V4 Z"]);
        result["type-printer"]=new("type-printer","#7caaf0",24,24,["M5 1 H19 V7 H23 V18 H19 V23 H5 V18 H1 V7 H5 Z M7 3 V7 H17 V3 Z M3 9 V16 H5 V13 H19 V16 H21 V9 Z M7 15 V21 H17 V15 Z"]);
        result["iot"]=new("iot","#b5a1e6",24,24,["M5 5 H19 V19 H5 Z M9 9 H15 V15 H9 Z M2 7 H5 V9 H2 Z M2 15 H5 V17 H2 Z M19 7 H22 V9 H19 Z M19 15 H22 V17 H19 Z M7 2 H9 V5 H7 Z M15 2 H17 V5 H15 Z M7 19 H9 V22 H7 Z M15 19 H17 V22 H15 Z"]);
        result["unknown"]=new("unknown","#93a4bc",24,24,["M6 3 H18 V6 H6 Z M15 6 H18 V12 H15 Z M10 10 H18 V13 H13 V16 H10 Z M10 19 H13 V22 H10 Z"]);
        return result;
    }
    public static VectorIcon? Vector(string key) => vectors.GetValueOrDefault(key);
    private static string NormalizePath(string input)
    {
        // Expand packed SVG arc flags so both Avalonia and Skia accept upstream paths.
        var output=new StringBuilder();int position=0,parameter=0;char command=' ';
        var number=new Regex(@"\G[-+]?(?:\d*\.\d+|\d+\.?\d*)(?:[eE][-+]?\d+)?",RegexOptions.CultureInvariant);
        while(position<input.Length)
        {
            char c=input[position];if(char.IsWhiteSpace(c)||c==','){position++;continue;}
            if(char.IsLetter(c)){command=c;parameter=0;output.Append(c);position++;continue;}
            if((command is 'a' or 'A')&&(parameter%7 is 3 or 4))
            {
                if(c is not ('0' or '1'))throw new InvalidDataException("Invalid SVG arc flag.");output.Append(' ').Append(c);position++;parameter++;continue;
            }
            var match=number.Match(input,position);if(!match.Success)throw new InvalidDataException("Invalid SVG path.");output.Append(' ').Append(match.Value);position+=match.Length;parameter++;
        }
        return output.ToString();
    }
    public static string TypeKey(NodeKind kind) => kind switch{
        NodeKind.Server or NodeKind.Hypervisor or NodeKind.VirtualMachine=>"type-server",
        NodeKind.Switch=>"type-switch",NodeKind.Router=>"type-router",NodeKind.Firewall=>"type-firewall",
        NodeKind.AccessPoint=>"accesspoint",NodeKind.Workstation=>"type-workstation",NodeKind.Printer=>"type-printer",
        NodeKind.Phone=>"phone",NodeKind.Phones=>"phones",NodeKind.IoT=>"iot",NodeKind.Device=>"unknown",_=>"device"};
    public static string Normalize(string value) => new(value.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
    public static string ResolveKey(Node node, IEnumerable<string>? availableKeys=null)
    {
        if (node.IsContainer || node.IconKey == "device") return node.Kind switch{NodeKind.Phone=>"phone",NodeKind.Phones=>"phones",NodeKind.IoT=>"iot",NodeKind.Device=>"unknown",_=>"device"};
        if (node.IconKey != "auto") return node.IconKey;
        if(!node.UseDeviceImage&&(node.Classification!=null||node.Kind is NodeKind.Phone or NodeKind.IoT or NodeKind.Device))return TypeKey(node.Kind);
        var keys=(availableKeys??builtInKeys).Concat(node.IconPngBase64.Length>0&&node.EmbeddedIconKey.Length>0?new[]{node.EmbeddedIconKey}:Array.Empty<string>()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        string Match(string text)
        {
            var normalized=Normalize(text);
            if(normalized.Length==0)return "";
            return keys.OrderByDescending(k=>Normalize(k).Length).ThenBy(k=>k,StringComparer.Ordinal).FirstOrDefault(k=>Normalize(k).Length>0&&normalized.StartsWith(Normalize(k),StringComparison.Ordinal))??"";
        }
        if(node.UseDeviceImage){
            string product=Normalize(node.Model);if(product.StartsWith("ubiquiti",StringComparison.Ordinal))product=product[8..];if(product.StartsWith("airmax",StringComparison.Ordinal))product=product[6..];
            var alias=product switch {var p when p.StartsWith("ns5ac",StringComparison.Ordinal)||p.StartsWith("nanostation5ac",StringComparison.Ordinal)||p.StartsWith("nanostationac",StringComparison.Ordinal)=>"nanostation-ac","loco5ac" or "nanostationloco5ac"=>"nanostation-loco5ac","uapacpro" or "unifiacpro"=>"unifi-ac-pro","u6pro" or "unifiu6pro"=>"unifi-u6-pro",_=>""};
            if(alias.Length>0&&keys.Contains(alias,StringComparer.OrdinalIgnoreCase))return alias;
            var model=Match(node.Model);if(model.Length>0)return model;
            if(node.Kind==NodeKind.AccessPoint&&Normalize(node.Vendor).StartsWith("ubiquiti",StringComparison.Ordinal)&&keys.Contains("unifi-ap",StringComparer.OrdinalIgnoreCase))return "unifi-ap";
        }

        var vendor=Match(node.Vendor);if(vendor.Length>0)return vendor;
        var os=Match(node.OperatingSystem);if(os.Length>0)return os;
        if(node.IsStack && node.StackTechnology.Contains("Cisco",StringComparison.OrdinalIgnoreCase))return "cisco";
        if(node.IconPngBase64.Length>0 && (Normalize(node.Vendor).StartsWith(Normalize(node.EmbeddedIconKey),StringComparison.Ordinal)||Normalize(node.OperatingSystem).StartsWith(Normalize(node.EmbeddedIconKey),StringComparison.Ordinal)) && node.EmbeddedIconKey.Length>0)return node.EmbeddedIconKey;
        if(node.Kind==NodeKind.Phone)return "phone";
        if(node.Kind==NodeKind.Phones)return "phones";
        if(node.Kind==NodeKind.IoT)return "iot";
        if(node.Kind==NodeKind.Device)return "unknown";
        return node.UseDeviceImage?(node.Kind==NodeKind.AccessPoint?"accesspoint":"hardware"):"device";
    }
    public static byte[]? EmbeddedPng(Node node)
    {
        var key=ResolveKey(node);
        if(node.IconPngBase64.Length==0 || node.IconPngBase64.Length>2*1024*1024 || !key.Equals(node.EmbeddedIconKey,StringComparison.OrdinalIgnoreCase))return null;
        try{var bytes=Convert.FromBase64String(node.IconPngBase64);return ImageLimits.AllowsPng(bytes)?bytes:null;}catch(FormatException){return null;}
    }
}
