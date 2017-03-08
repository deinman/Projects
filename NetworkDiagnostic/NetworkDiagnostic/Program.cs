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
            var ok = true;

            var host = Dns.GetHostEntryAsync(Dns.GetHostName());
            
            foreach (var ip in host.Result.AddressList)
            {
                if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                {
                    if (!PingAddress(ip, 10))
                    {
                        ok = false;
                    }
                }
            }
            
            if (NetworkInterface.GetIsNetworkAvailable())
            {
                if (ok)
                {
                    if (!PingAddress(GetDefaultGateway(), 100))
                    {
                        ok = false;
                    }
                }

                if (ok)
                {
                    if (!PingAddress(IPAddress.Parse("8.8.8.8"), 1000))
                    {
                        ok = false;
                    }
                }

                if (ok)
                {
                    if (!PingAddress("www.google.com", 2000))
                    {
                        ok = false;
                    }
                }
            }
            else
            {
                Console.WriteLine("No network is available to test.\n");
            }

            Console.WriteLine("Tests complete.");
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

        private static bool PingAddress(IPAddress destination, int timeout)
        {
            var response = false;

            Ping pingSender = InitializePing();

            Console.WriteLine($"Pinging {destination.ToString()}...");

            var reply = pingSender.SendPingAsync(destination, timeout);

            if (reply.Result.Status == IPStatus.Success)
            {
                Console.WriteLine($"Ping to {destination.ToString()} successful!");

                Console.WriteLine($"\tRoundTripTime: {reply.Result.RoundtripTime}ms");

                response = true;
            }
            else
            {
                Console.WriteLine($"Ping to {destination.ToString()} timed out.");
            }

            Console.WriteLine();

            return response;
        }

        private static bool PingAddress(string destination, int timeout)
        {
            var response = false;

            Ping pingSender = InitializePing();

            Console.WriteLine($"Pinging {destination}...");

            var reply = pingSender.SendPingAsync(destination, timeout);

            if (reply.Result.Status == IPStatus.Success)
            {
                Console.WriteLine($"Ping to {destination} successful!");

                Console.WriteLine($"\tRoundTripTime: {reply.Result.RoundtripTime}ms");

                response = true;
            }
            else
            {
                Console.WriteLine($"Ping to {destination} timed out.");
            }

            Console.WriteLine();

            return response;
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