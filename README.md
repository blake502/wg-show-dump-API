# wg-show-dump-API
Provides an API for your WireGuard connections!

It works by establishing an SSH connection to a server running WireGuard (even if WireGuard is running within a Docker container), then parsing the output from WireGuard's "dump" command (`wg show all dump`). The parsed information is available via the API!

**Note**: This project does not support any other WireGuard commands or features. It merely queries WireGuard for peer information.

# Purpose
I created this project to monitor my [WireGuard](https://github.com/wireguard) connections with [Homepage](https://github.com/gethomepage/homepage). There are likely other uses.

# Info
The default port is `6543`

You may bind it to another port using Docker.

## Usage:
The API has two methods.

- `/peer?id={publickey}`
- `/peers`

## /peer

If you wish to retrieve the information for a specific peer, use `/peer?id={publickey}`

### Example:

`curl 127.0.0.1:6543/peer?id=oIAKCJalI/ucmM1lsoWL+I08isk91HsIkmal/Jndsas=`

### Response:
```
{
    "interfaceName": "wg0",
    "publicKey": "oIAKCJalI/ucmM1lsoWL+I08isk91HsIkmal/Jndsas=",
    "presharedKey": "(hidden)",
    "endpoint": "308.92.33.71:62382",
    "allowedIPs": "10.80.80.1/32",
    "latestHandshake": "2026-05-07T21:47:05.0000000+00:00",
    "transferRx": 24096548,
    "transferTx": 166495904,
    "persistentKeepAlive": false
}
```

**Note**: Public keys may contain plus signs. Even though `+` signs will be interpretted as spaces by the server, the server will treat spaces as `+`. Therefore, there is no need to use `%2B` to represent the `+`.

## /peers

If you wish to retrieve the information for all peers, use `/peers`

### Example:

`curl 127.0.0.1:6543/peers`

### Reponse:
```
[
    {
        "interfaceName": "wg0",
        "publicKey": "oIAKCJalI/ucmM1lsoWL+I08isk91HsIkmal/Jndsas=",
        "presharedKey": "(hidden)",
        "endpoint": "308.92.33.71:62382",
        "allowedIPs": "10.80.80.1/32",
        "latestHandshake": "2026-05-07T21:47:05.0000000+00:00",
        "transferRx": 24096548,
        "transferTx": 166495904,
        "persistentKeepAlive": false
    },
    {
        "interfaceName": "wg0",
        "publicKey": "oIAKCJalI/ucmM1lsoWL+I08isk91HsIkmal/Jndsas=",
        "presharedKey": "(hidden)",
        "endpoint": "458.42.77.19:39182",
        "allowedIPs": "10.80.80.1/32",
        "latestHandshake": "2026-05-07T21:47:05.0000000+00:00",
        "transferRx": 166495904,
        "transferTx": 24096548,
        "persistentKeepAlive": false
    }
]
```
# Docker
This service is designed to be used with Docker

There are several things you should consider configuring.
## Environment Variables
- `SSH_IP` The IP/hostname of the WireGuard server (default `127.0.0.1`)
- `SSH_USERNAME` The SSH username (default `root`)
- `SSH_PASSWORD` The SSH password **Note**: Do not use this if you're using key file [see next seciton]
- `SSH_MIN_REFRESH` Time in seconds to cache entries before querying WireGuard again (default `10`)
- `SSH_WG_COMMAND` WireGuard "dump" command. If WireGuard is running in Docker container, use `docker exec wireguard wg show all dump` (replacing `wireguard` with the name of your WireGuard container). You may also want to modify this to query a specific adapter (For example, `wg0` instead of `all`) (default `wg show all dump`)

## Key file
A private key file may be used in place of a password for the SSH connection. The key file must be mounted at `/app/key`

**Note**: `key` is a file, not a directory

# Example docker-compose.yml
```
services:
  wireguardshowdumpapi:
    image: wireguardshowdumpapi
    container_name: wireguardshowdumpapi
    ports:
      - 6543:6543
    volumes:
      - ./key:/app/key #Optional, you may use a password instead.
    environment:
      - SSH_IP=192.168.1.5 #The IP address where WireGuard is running, must be SSH-able
      - SSH_USERNAME=root #The SSH username
      - SSH_PASSWORD=passw0rd #The SSH password. Do not use if using key file!
      - SSH_MIN_REFRESH=10 #Time in seconds to cache entries before querying WireGuard again
      - SSH_WG_COMMAND=docker exec wireguard wg show all dump #WireGuard "dump" command
```

# Homepage example
If you wish to use this with [Homepage](https://github.com/gethomepage/homepage), your service entry may look like this:
```
- WireGuard:
  icon: wireguard.png
  widget:
    type: customapi
    url: http://127.0.0.1:6543/peer?id=oIAKCJalI/ucmM1lsoWL+I08isk91HsIkmal/Jndsas=
    refreshInterval: 1000
    method: GET
    mappings:
      - field: latestHandshake
        label: Last Handshake
        format: relativeDate
        style: short
        numeric: auto
      - field: transferRx
        label: Received
        format: bytes
      - field: transferTx
        label: Sent
        format: bytes
```

# Extra info

Uses [SSH.NET](https://github.com/sshnet/SSH.NET) and [ASP.NET Core](https://dotnet.microsoft.com/en-us/apps/aspnet)
