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

        static SshClient? client = null;

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
                wgCommand = "wg show all dump";
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

            while (true)
            {
                /*infinite loop, sue me*/
                try
                {
                    if (client == null)
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
                            //Otherwise, use provided password
                            client = new SshClient(IP, username, password);

                        //Connect
                        client.Connect();
                    }
                    else
                        if (!client.IsConnected)
                            client.Dispose();
                }
                catch
                {
                    //Dispose of client (manual "using")
                    if (client != null)
                        client.Dispose();
                }

                //No need to check excessively, 1 sec is good
                Thread.Sleep(1000);
            }
        }

        static void getPeerInfos()
        {
            //Clear cached peer info
            peerInfos.Clear();

            if (client == null)
            {
                Console.WriteLine("SSH client not initialized!");
                return;
            }

            if (!client.IsConnected)
            {
                Console.WriteLine("SSH client not connected!");
                return;
            }

            try
            {
                //Send wg dump command (IE: "wg show all dump" or "docker exec wireguard wg show all dump")
                using SshCommand cmd = client.RunCommand(wgCommand);

                //"wg show all dump" returns a tab-delimited sheet
                foreach (string line in cmd.Result.Split("\n"))
                {
                    //Parse results
                    string[] split = line.Split("\t");

                    //Valid peer entries have 9 columns
                    if (split.Length < 9)
                        continue;

                    //The sheet does not contain a header
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

                    //If "0", it has not connected since wg started
                    //Keep the default value "Never" if never connected
                    if (latestHandshake != "0")
                    {
                        //Convert to long
                        long latestHandshakeLong = Convert.ToInt64(latestHandshake);
                        //Convert to DateTime
                        DateTime latestHandshakeDateTime = DateTimeOffset.FromUnixTimeSeconds(latestHandshakeLong).DateTime;
                        //Convert to local time
                        latestHandshakeDateTime = latestHandshakeDateTime.ToLocalTime();
                        //Convert to string and apply property
                        peerInfo.latestHandshake = latestHandshakeDateTime.ToString("o");
                    }

                    //Convert transferRx text to long (yes, it must be a long)
                    peerInfo.transferRx = Convert.ToInt64(transferRx);
                    //Convert transferTx text to long (yes, it must be a long)
                    peerInfo.transferTx = Convert.ToInt64(transferTx);
                    //Convert persistentKeepAlive to bool (always either "on" or "off" even for disconnected clients)
                    peerInfo.persistentKeepAlive = persistentKeepAlive != "off";

                    peerInfos.Add(peerInfo);
                }

                Console.WriteLine("Successfully parsed {0} peers!", peerInfos.Count);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Something went wrong while trying to refresh info from WireGuard!");
                Console.WriteLine("Please report this bug!");
                Console.WriteLine(ex.ToString());
                Console.WriteLine(ex.Message);
            }
        }

        static PeerInfo getPeerInfo(string id)
        {
            //Use lock object to keep multiple requests from blasting SSH commands
            lock (refreshLockObject)
            {
                //Make sure the minimum refresh time has passed since the last refresh
                //Otherwise, cached entries will be returned
                if (DateTime.Now.Subtract(lastRefresh).TotalSeconds > MinRefreshTime)
                {
                    //Record last refresh time
                    lastRefresh = DateTime.Now;

                    Console.WriteLine("Refreshing information...");

                    //Update peer info
                    getPeerInfos();
                }
                else
                    Console.WriteLine("Using cached information...");

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
        //Using a string instead of DateTime allows us to use "Never"
        //instead of 1970 for clients that have not connected since wg started
        public string latestHandshake;
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
            latestHandshake = "Never";
            transferRx = 0;
            transferTx = 0;
            persistentKeepAlive = false;
        }
    }
}
