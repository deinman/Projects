using System;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;

namespace NetworkDiagnostic
{
    class Program
    {
        static void Main(string[] args)
        {
            StandardTests();

            Console.ReadLine();
        }

        private static void StandardTests()
        {
            var host = Dns.GetHostEntryAsync(Dns.GetHostName());

            foreach (var ip in host.Result.AddressList)
            {
                if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                {
                    PingAddress(ip);
                }
            }

            PingAddress(GetDefaultGateway());

            PingAddress(IPAddress.Parse("8.8.8.8"));

            PingAddress("www.google.com");
        }

        private static IPAddress GetDefaultGateway()
        {
            var gateway = NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault();
            if (gateway == null)
            {
                return null;
            }
            return gateway.GetIPProperties().GatewayAddresses.FirstOrDefault().Address;
        }

        private static void PingAddress(IPAddress destination)
        {
            Ping pingSender = InitializePing();

            Console.WriteLine($"Pinging {destination.ToString()}...");

            var reply = pingSender.SendPingAsync(destination);

            if (reply.Result.Status == IPStatus.Success)
            {
                Console.WriteLine($"Ping to {destination.ToString()} successful!");

                Console.WriteLine($"\tRoundTripTime: {reply.Result.RoundtripTime}ms");
            }

            Console.WriteLine();
        }

        private static void PingAddress(string destination)
        {
            Ping pingSender = InitializePing();

            Console.WriteLine($"Pinging {destination}...");

            var reply = pingSender.SendPingAsync(destination);

            if (reply.Result.Status == IPStatus.Success)
            {
                Console.WriteLine($"Ping to {destination} successful!");

                Console.WriteLine($"\tRoundTripTime: {reply.Result.RoundtripTime}ms");
            }

            Console.WriteLine();
        }

        private static Ping InitializePing()
        {
            var pingSender = new Ping();

            var options = new PingOptions()
            {
                DontFragment = true
            };
            return pingSender;
        }
    }
}