# wg-show-dump-API
Provides an API for "wg show all dump" with a Docker container.

Default port: 6543

Usage:
Send a GET command to
`http://ip:6543/peer?id=[publickey]`
example:
`curl 127.0.0.1:6543/peer?id=n1c9803en+08c1n0e8ncais/jdnclans=`

I know what you're thinking... A public key might have `+`s in it, which will be interpretted as a space! Should I use `%20` instead of `+`?

Don't worry, there's a built-in workaround.


# Why?
I created this project to monitor my WireGuard connections with Homepage.


# Example docker-compose.yml

```
services:
  wireguardshowdumpapi:
    image: wireguardshowdumpapi
    container_name: wireguardshowdumpapi
    ports:
      - 6543:6543
    #volumes:
    #  - ./key:/app/key #provide a key file if you don't want to use a password
    environment:
      - SSH_IP=192.168.1.5 #The IP address were wg is running, must be SSH-able (default 127.0.0.1)
      - SSH_USERNAME=root #The SSH username (default root)
      - SSH_PASSWORD=passw0rd #The SSH password. Do not use if using key file
      - SSH_MIN_REFRESH=10 #How long in seconds to cache information before refreshing info from wg (default 10 seconds)
      - SSH_WG_COMMAND=wg show all dump #Command to parse, if wg is running in docker container, use "docker exec wireguard wg show all dump"
```


# Extra info

Uses SSH.NET and ASP.NET