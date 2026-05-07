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
                IP = "127.0.0.1";

            if (username == null)
                username = "root";

            if (wgCommand == null)
                username = "wg show all dump";

            string? minRefreshString = Environment.GetEnvironmentVariable("SSH_MIN_REFRESH");

            if (minRefreshString != null)
                try
                {
                    MinRefreshTime = Convert.ToInt32(minRefreshString);
                }
                catch
                {
                    MinRefreshTime = 10;
                }

            Console.WriteLine("IP: {0}\nSSH Username: {1}\nMinimum Refresh Time: {2}", IP, username, MinRefreshTime.ToString());

            //Web App init
            var builder = WebApplication.CreateBuilder(args);
            var app = builder.Build();

            //Answer on /peer
            app.MapGet("/peer", (string id) =>
            {
                //Hacky, but feeding an ID treats pluses as spaces. So we'll intentionally treat spaces as pluses.
                id = id.Replace(" ", "+");

                //Grab peer info
                PeerInfo peerInfo = getPeerInfo(id);

                //Send the info
                return new
                {
                    publickey = peerInfo.publicKey,
                    endpoint = peerInfo.endpoint,
                    latest_handshake = peerInfo.latestHandshake,
                    bytes_received = peerInfo.transferRx,
                    bytes_sent = peerInfo.transferTx
                };
            });

            //Begin app on port 6543
            app.Run("http://0.0.0.0:6543");
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
                    string interfaceName = line.Split(' ')[0];
                    string publicKey = line.Split(" ")[1];
                    string presharedKey = line.Split(" ")[2];
                    string endpoint = line.Split(" ")[3];
                    string allowedIPs = line.Split(" ")[4];
                    string latestHandshake = line.Split(" ")[5];
                    string transferRx = line.Split(" ")[6];
                    string transferTx = line.Split(" ")[7];
                    string persistentKeepAlive = line.Split(" ")[8];

                    PeerInfo peerInfo = new PeerInfo();

                    peerInfo.interfaceName = interfaceName;
                    peerInfo.publicKey = publicKey;
                    peerInfo.presharedKey = presharedKey;
                    peerInfo.endpoint = endpoint;
                    peerInfo.allowedIPs = allowedIPs;
                    peerInfo.latestHandshake = DateTimeOffset.FromUnixTimeSeconds(Convert.ToInt32(latestHandshake)).DateTime;
                    peerInfo.transferRx = Convert.ToInt32(transferRx);
                    peerInfo.transferTx = Convert.ToInt32(transferTx);
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

                    //Update peer info
                    getPeerInfos();
                }

                //Locate and return the peer with the matching ID
                foreach (PeerInfo peerInfo in peerInfos)
                    if (peerInfo.publicKey == id)
                        return peerInfo;

                //Throw an exception if no peer is found
                //TODO: Better handling, but it's fine for now
                throw new Exception();
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
        public int transferRx;
        public int transferTx;
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
