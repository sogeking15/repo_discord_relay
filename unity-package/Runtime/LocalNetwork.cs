using System.Net;
using System.Net.Sockets;

namespace DiscordRelayKit
{
    public static class LocalNetwork
    {
        // Doesn't actually send anything (UDP "connect" just asks the OS to pick
        // a route), but the local endpoint it resolves to is the LAN IP other
        // devices on the same network can reach this machine at - more reliable
        // than Dns.GetHostEntry, which often returns a VPN or virtual adapter.
        public static string GetLanIPAddress()
        {
            try
            {
                using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                socket.Connect("8.8.8.8", 65530);
                return ((IPEndPoint)socket.LocalEndPoint).Address.ToString();
            }
            catch
            {
                return "127.0.0.1";
            }
        }
    }
}
