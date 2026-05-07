using Renci.SshNet;

namespace wg_show_dump_API
{
    public class Program
    {
        //I know, I know... This probably shouldn't be "static."
        static List<PeerInfo> peerInfos = new List<PeerInfo>();

        static DateTime lastRefresh = DateTime.MinValue;
        static object refreshLockObject = new Object();

        static string? IP = Environment.GetEnvironmentVariable("SSH_IP");
        static string? username = Environment.GetEnvironmentVariable("SSH_USERNAME");
        static string? password = Environment.GetEnvironmentVariable("SSH_PASSWORD");
        static string? wgCommand = Environment.GetEnvironmentVariable("SSH_WG_COMMAND");

        static int? MinRefreshTime;

        public static void Main(string[] args)
        {
            //Validate environment variables
            if (IP == null)
            {
                Console.WriteLine("No SSH IP provided. Defaulting to 127.0.0.1\nPlease use the SSH_IP environment variable to provide one.");
                IP = "127.0.0.1";
            }

            if (username == null)
            {
                Console.WriteLine("No SSH username provided. Defaulting to root\nPlease use the SSH_USERNAME environment variable to provide one.");
                username = "root";
            }

            if (wgCommand == null)
            {
                Console.WriteLine("No SSH command provided. Defaulting to \"wg show all dump\"\nYou may use the SSH_WG_COMMAND environment variable to configure one if you run WireGuard in a Docker container.");
                username = "wg show all dump";
            }

            string? minRefreshString = Environment.GetEnvironmentVariable("SSH_MIN_REFRESH");

            if (minRefreshString != null)
                try
                {
                    MinRefreshTime = Convert.ToInt32(minRefreshString);
                }
                catch
                {
                    Console.WriteLine("No minimum refresh time provided. Defaulting to 10 seconds.\nYou may use use the SSH_MIN_REFRESH environment variable to configure this.");
                    MinRefreshTime = 10;
                }

            Console.WriteLine("wg-show-dump-API will use these settings:\nIP: {0}\nSSH Username: {1}\nMinimum Refresh Time: {2} seconds", IP, username, MinRefreshTime.ToString());

            //Web App init
            Console.WriteLine("Creating WebApplication...");
            var builder = WebApplication.CreateBuilder();
            var app = builder.Build();

            //Answer on /peer
            Console.WriteLine("Mapping /peer...");
            app.MapGet("/peer", (string id) =>
            {
                Console.WriteLine("Connection received!");

                //Hacky, but feeding an ID treats pluses as spaces. So we'll intentionally treat spaces as pluses.
                id = id.Replace(" ", "+");

                //Grab peer info
                Console.WriteLine("Gathering peer information...");
                PeerInfo peerInfo = getPeerInfo(id);

                //Send the info
                return new
                {
                    interfaceName = peerInfo.interfaceName,
                    publicKey = peerInfo.publicKey,
                    presharedKey = peerInfo.presharedKey,
                    endpoint = peerInfo.endpoint,
                    allowedIPs = peerInfo.allowedIPs,
                    latestHandshake = peerInfo.latestHandshake,
                    transferRx = peerInfo.transferRx,
                    transferTx = peerInfo.transferTx,
                    persistentKeepAlive = peerInfo.persistentKeepAlive
                };
            });

            //Begin app on port 6543
            Console.WriteLine("Starting server...");
            app.RunAsync("http://0.0.0.0:6543");

            Console.WriteLine("Listening on port 6543!");

            while (true) { /*infinite loop, sue me*/ }
        }


        //Posssible TODO
        //Keep SSH client connected instead of reestablishing connection every refresh
        static void getPeerInfos()
        {
            //Clear caches peer info
            peerInfos.Clear();

            //Not using "using" to dispose of SshClient because the constructor
            //is different depending on whether we're using password or key file
            SshClient client = null;

            //Try as a manual "using"
            try
            {
                if (password == null)
                {
                    //If no password provided, use key file
                    //This will throw an exception if no key file is provided
                    //We're already in a "try" statement, so I'm not too
                    //worried about validating whether the file exists
                    var keyFile = new PrivateKeyFile("key");
                    client = new SshClient(IP, username, keyFile);
                }
                else
                {
                    //Otherwise, use provided password
                    client = new SshClient(IP, username, password);
                }

                //Connect
                client.Connect();

                //Send wg command (IE: "wg" or "docker exec wireguard wg")
                using SshCommand cmd = client.RunCommand(wgCommand);

                //TODO: Parse "wg show all dump" instead
                //Parse results

                foreach (string line in cmd.Result.Split("\n"))
                {
                    string[] split = line.Split("\t");

                    if (split.Length < 9)
                        continue;

                    string interfaceName = split[0];
                    string publicKey = split[1];
                    string presharedKey = split[2];
                    string endpoint = split[3];
                    string allowedIPs = split[4];
                    string latestHandshake = split[5];
                    string transferRx = split[6];
                    string transferTx = split[7];
                    string persistentKeepAlive = split[8];

                    PeerInfo peerInfo = new PeerInfo();

                    peerInfo.interfaceName = interfaceName;
                    peerInfo.publicKey = publicKey;
                    peerInfo.presharedKey = presharedKey;
                    peerInfo.endpoint = endpoint;
                    peerInfo.allowedIPs = allowedIPs;
                    peerInfo.latestHandshake = DateTimeOffset.FromUnixTimeSeconds(Convert.ToInt64(latestHandshake)).DateTime;
                    peerInfo.transferRx = Convert.ToInt64(transferRx);
                    peerInfo.transferTx = Convert.ToInt64(transferTx);
                    peerInfo.persistentKeepAlive = persistentKeepAlive != "off";

                    peerInfos.Add(peerInfo);
                }

                //Disconnect cleanly
                client.Disconnect();
            }
            finally
            {
                //Dispose of client (manual "using")
                if (client != null)
                    client.Dispose();
            }
        }

        static PeerInfo getPeerInfo(string id)
        {
            //Use lock object to keep multiple requests from hammering SSH commands
            lock (refreshLockObject)
            {
                //Make sure the minimum refresh time has passed since the last refresh
                //Otherwise, cached entries will be returned
                if (DateTime.Now.Subtract(lastRefresh).TotalSeconds > MinRefreshTime)
                {
                    //Record last refresh time
                    lastRefresh = DateTime.Now;

                    Console.WriteLine("Gathering new information...");

                    //Update peer info
                    getPeerInfos();
                }
                else
                {
                    Console.WriteLine("Using cached information...");
                }

                //Locate and return the peer with the matching ID
                foreach (PeerInfo peerInfo in peerInfos)
                    if (peerInfo.publicKey == id)
                        return peerInfo;

                Console.WriteLine("Could not find matching peer!");

                //No peer is found
                return new PeerInfo();
            }
        }
    }

    class PeerInfo
    {
        public string interfaceName;
        public string publicKey;
        public string presharedKey;
        public string endpoint;
        public string allowedIPs;
        public DateTime latestHandshake;
        public long transferRx;
        public long transferTx;
        public bool persistentKeepAlive;

        public PeerInfo()
        {
            interfaceName = "n/a";
            publicKey = "n/a";
            presharedKey = "n/a";
            endpoint = "n/a";
            allowedIPs = "n/a";
            latestHandshake = DateTime.MinValue;
            transferRx = 0;
            transferTx = 0;
            persistentKeepAlive = false;
        }
    }
}
