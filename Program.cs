using Microsoft.Extensions.ObjectPool;
using Renci.SshNet;

namespace wg_show_dump_API
{
    public class Program
    {
        //I know, I know... This probably shouldn't be "static."
        static List<PeerInfo> peerInfos = new List<PeerInfo>();

        static DateTime lastRefresh = DateTime.MinValue;
        static object refreshLockObject = new Object();

        static string? ip = Environment.GetEnvironmentVariable("SSH_IP");
        static string? username = Environment.GetEnvironmentVariable("SSH_USERNAME");
        static string? password = Environment.GetEnvironmentVariable("SSH_PASSWORD");
        static string? wgCommand = Environment.GetEnvironmentVariable("SSH_WG_COMMAND");

        static int? minRefreshTime;

        static SshClient? client = null;
        static object sshClientLockObject = new Object();

        public static void Main(string[] args)
        {
            //Validate environment variables
            if (ip == null)
            {
                Console.WriteLine("[WARN] No SSH IP provided. Defaulting to 127.0.0.1\nPlease use the SSH_IP environment variable to provide one.");
                ip = "127.0.0.1";
            }

            if (username == null)
            {
                Console.WriteLine("[WARN] No SSH username provided. Defaulting to root\nPlease use the SSH_USERNAME environment variable to provide one.");
                username = "root";
            }

            if (password == null)
                //If no password provided, look for key file
                if (!File.Exists("key"))
                {
                    //If neither, fatal error
                    Console.WriteLine("[FATAL] Neither a key file nor a password were provided!");
                    Environment.Exit(1);
                }
                else
                    Console.WriteLine("[INFO] Connecting SSH client using key file...");

            if (wgCommand == null)
            {
                Console.WriteLine("[WARN] No SSH command provided. Defaulting to \"wg show all dump\"\nYou may use the SSH_WG_COMMAND environment variable to configure one if you run WireGuard in a Docker container.");
                wgCommand = "wg show all dump";
            }

            string? minRefreshString = Environment.GetEnvironmentVariable("SSH_MIN_REFRESH");

            if (minRefreshString != null)
                try
                {
                    minRefreshTime = Convert.ToInt32(minRefreshString);
                }
                catch
                {
                    Console.WriteLine("[WARN] No minimum refresh time provided. Defaulting to 10 seconds.\nYou may use use the SSH_MIN_REFRESH environment variable to configure this.");
                    minRefreshTime = 10;
                }

            Console.WriteLine("[INFO] wg-show-dump-API will use these setting:");
            Console.WriteLine("[INFO] IP:                       " + ip);
            Console.WriteLine("[INFO] Username:                 " + username);
            Console.WriteLine("[INFO] Authentication Method:    " + ((password == null) ? "Keyfile" : "Password"));
            Console.WriteLine("[INFO] Command:                  " + wgCommand);
            Console.WriteLine("[INFO] Minimum Refresh:          " + minRefreshTime.ToString() + " seconds");

            //Web App init
            Console.WriteLine("[INFO] Creating WebApplication...");
            var builder = WebApplication.CreateBuilder();
            var app = builder.Build();

            //Answer on /peer
            Console.WriteLine("[INFO] Mapping /peer...");
            app.MapGet("/peer", (string id) =>
            {
                Console.WriteLine("[INFO] Connection received!");

                //Hacky, but feeding an ID treats pluses as spaces. So we'll intentionally treat spaces as pluses.
                id = id.Replace(" ", "+");

                //Grab peer info
                Console.WriteLine("[INFO] Gathering peer information...");
                PeerInfo peerInfo = getPeerInfoById(id);

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

            Console.WriteLine("[INFO] Mapping /peers");
            app.MapGet("/peers", () =>
            {
                //Grab peer info
                Console.WriteLine("[INFO] Gathering peer information...");
                updatePeerInfos();

                //Pack peerInfos into an object array to return to client
                object[] peerInfoObjects = new object[peerInfos.Count];

                for(int i = 0; i < peerInfos.Count; i++)
                {
                    PeerInfo peerInfo = peerInfos[i];
                    peerInfoObjects[i] = new
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
                }

                Console.WriteLine("[INFO] Returning info for {0} peer(s).", peerInfos.Count);

                return peerInfoObjects;
            });

            //Build the SSH client
            validateSshClient();

            //Begin app on port 6543
            Console.WriteLine("[INFO] Starting server...");
            app.RunAsync("http://0.0.0.0:6543");

            Console.WriteLine("[INFO] Listening on port 6543!");

            while (true)
            {
                //Infinite loop
                //We need to keep the app running since the WebApplication is running async

                //We can keep the SSH session alive
                validateSshClient();

                //No need to check excessively, 10 sec is good
                Thread.Sleep(10000);
            }
        }

        static void validateSshClient(int recursiveDelay = 1)
        {
            lock (sshClientLockObject)
            {
                try
                {
                    if (client == null)
                    {
                        Console.WriteLine("[INFO] Creating SSH client...");
                        if (password == null)
                        {
                            //If no password provided, look for key file
                            if (File.Exists("key"))
                            {
                                Console.WriteLine("[INFO] Connecting SSH client using key file...");
                                var keyFile = new PrivateKeyFile("key");
                                client = new SshClient(ip, username, keyFile);
                            }
                            else
                            {
                                //If neither, fatal error
                                Console.WriteLine("[FATAL] Neither a key file nor a password were provided!");
                                Environment.Exit(1);
                            }
                        }
                        else
                        {
                            //Otherwise, use provided password
                            Console.WriteLine("[INFO] Connecting SSH client using password...");
                            client = new SshClient(ip, username, password);
                        }

                        //Connect
                        client.Connect();

                        if (client.IsConnected)
                            Console.WriteLine("[INFO] SSH client connected successfully!");
                        else
                            Console.WriteLine("[ERROR] SSH client failed to connect!");
                    }
                    else
                        if (!client.IsConnected)
                        {
                            client.Dispose();
                            Console.WriteLine("[ERROR] SSH client not connected!");
                        }
                }
                catch
                {
                    Console.WriteLine("[ERROR] Something went wrong with the SSH client!");
                    Console.WriteLine("[ERROR] Trying again in {0} seconds...!", recursiveDelay);

                    Thread.Sleep(recursiveDelay * 1000);

                    recursiveDelay *= 2;

                    if (recursiveDelay > 64)
                        recursiveDelay = 64;

                    //Dispose of client
                    if (client != null)
                        client.Dispose();

                    validateSshClient();
                }
            }
        }

        //Does nothing if refresh interval has not passed
        //Otherwise, uses SSH to query WireGuard and parses results
        static void updatePeerInfos()
        {
            //Use lock object to keep multiple requests from blasting SSH commands
            lock (refreshLockObject)
            {
                //Make sure the minimum refresh time has passed since the last refresh
                //Otherwise, return and force cached entries to be used
                if (DateTime.Now.Subtract(lastRefresh).TotalSeconds <= minRefreshTime)
                {
                    Console.WriteLine("[INFO] Using cached information...");
                    return;
                }

                //Record last refresh time
                //This happens BEFORE parsing
                //That way, users can resonably expect a 10 second min refresh to be
                //10 seconds. Not 10 seconds PLUS the time it takes to refresh and parse.
                lastRefresh = DateTime.Now;

                Console.WriteLine("[INFO] Refreshing information...");

                //Clear cached peer info
                peerInfos.Clear();

                try
                {
                    //Ensure the SSH client is alive and well
                    validateSshClient();

                    //Send wg dump command (IE: "wg show all dump" or "docker exec wireguard wg show all dump")
                    Console.WriteLine("[INFO] Running WireGuard command...");
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

                    Console.WriteLine("[INFO] Parsed {0} peer(s)!", peerInfos.Count);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[ERROR] Something went wrong while trying to refresh info from WireGuard!");
                    Console.WriteLine("[ERROR] Please report this bug!");
                    Console.WriteLine("[ERROR] " + ex.ToString());
                    Console.WriteLine("[ERROR] " + ex.Message);
                }
            }
        }

        static PeerInfo getPeerInfoById(string id)
        {
            updatePeerInfos();

            //Locate and return the peer with the matching ID
            foreach (PeerInfo peerInfo in peerInfos)
                if (peerInfo.publicKey == id)
                    return peerInfo;

            Console.WriteLine("[ERROR] Could not find matching peer!");

            //No peer is found
            return new PeerInfo();
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
