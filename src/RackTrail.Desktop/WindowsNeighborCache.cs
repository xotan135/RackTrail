using System.Net;
using System.Runtime.InteropServices;
namespace RackTrail.Desktop;
internal static class WindowsNeighborCache
{
    [DllImport("iphlpapi.dll")]private static extern uint GetIpNetTable(IntPtr table,ref int size,bool order);
    public static string Find(IPAddress address) {
        if(!OperatingSystem.IsWindows())return "";
        IntPtr buffer=IntPtr.Zero;
        try {
            int size=0;if(GetIpNetTable(IntPtr.Zero,ref size,false)!=122||size<4||size>4*1024*1024)return "";
            buffer=Marshal.AllocHGlobal(size);if(GetIpNetTable(buffer,ref size,false)!=0)return "";
            int count=Marshal.ReadInt32(buffer);if(count<0||count>(size-4)/24)return "";
            var wanted=address.GetAddressBytes();
            for(int i=0;i<count;i++) {
                var row=IntPtr.Add(buffer,4+i*24);
                if(Marshal.ReadInt32(row,4)!=6||Marshal.ReadInt32(row,20)==2)continue;
                var ip=new byte[4];Marshal.Copy(IntPtr.Add(row,16),ip,0,4);if(!ip.SequenceEqual(wanted))continue;
                var mac=new byte[6];Marshal.Copy(IntPtr.Add(row,8),mac,0,6);return string.Join(":",mac.Select(b=>b.ToString("X2")));
            }
        }catch(Exception ex)when(ex is DllNotFoundException or EntryPointNotFoundException or ExternalException){}
        finally{if(buffer!=IntPtr.Zero)Marshal.FreeHGlobal(buffer);}
        return "";
    }
}
