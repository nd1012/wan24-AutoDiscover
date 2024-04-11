# wan24-AutoDiscover

This is a micro-webservice which supports a small part of the Microsoft 
Exchange POX autodiscover standard, which allows email clients to receive 
automatic configuration information for an email account.

It was created using .NET 8 and ASP.NET. You find a published release build 
for each published release on GitHub as ZIP file download for self-hosting.

The webservice is designed for working with dynamic MTA configurations and 
tries to concentrate on the basics for fast request handling and response. All 
required informations will be held in memory, so no database or filesystem 
access is required for request handling.

## Usage

### Pre-requirements

This app is a .NET 8 app and needs the ASP.NET runtime environment.

### How to get it

For example on a Debian Linux server:

```bash
mkdir /home/autodiscover
cd /home/autodiscover
wget https://github.com/nd1012/wan24-AutoDiscover/releases/download/v1.1.0/wan24-AutoDiscover.v1.1.0.zip
unzip wan24-AutoDiscover.v1.1.0.zip
rm wan24-AutoDiscover.v1.1.0.zip
```

### `appsettings.json`

The `appsettings.json` file contains the  webservice configuration. The 
`DiscoveryConfig` is a `wan24.AutoDiscovery.Models.DiscoveryConfig` object. An 
example:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "Kestrel": {
    "Endpoints": {
      "AutoDiscover": {
        "Url": "http://127.0.0.1:5000"
      }
    }
  },
  "AllowedHosts": "*",
  "DiscoveryConfig": {
    "PreForkResponses": 10,
    "StreamPoolCapacity": 10,
    "KnownProxies": [
      "127.0.0.1"
    ],
    "Discovery": {
      "localhost": {
        "AcceptedDomains": [
          "wan24.de",
          "wan-solutions.de"
        ],
        "Protocols": [
          {
            "Type": "IMAP",
            "Server": "imap.wan24.de",
            "Port": 993
          },
          {
            "Type": "SMTP",
            "Server": "smtp.wan24.de",
            "Port": 587
          }
        ]
      }
    }
  }
}
```

Since the webservice should only listen local and be proxied by a real 
webserver (like Apache2), there is a `wan24.AutoDiscover.Models.DomainConfig` 
for `localhost`, which produces POX response for the allowed domains 
`wan24.de` and `wan-solutions.de` in this example (you should use your own 
domain names instead).

The email client configuration will get an `IMAP` and a `SMTP` server pre-
configuration, which contains the alias of the requested email address as 
login name and has all the other defaults from a 
`wan24.AutoDiscover.Models.Protocol` instance.

With the `PreForkResponses` value you can define a number of pre-forked POX 
response XML documents to serve faster responses.

Any change to this file will cause an automatic reload of the `DomainConfig` 
section.

For serving a request, the `DomainConfig` will be looked up 

1. by the email address domain part
1. by the served request hostname
1. by any `DomainConfig` which has the email address domain part in the 
`AcceptedDomains` property, which contains a list of accepted domain names
1. by the `DomainConfig` with an empty domain name as key

Any unmatched `DomainConfig` will cause a `Bad request` http response.

Find the online documentation of the used types here:

- [`DiscoveryConfig`](https://nd1012.github.io/wan24-AutoDiscover/api/wan24.AutoDiscover.Models.DiscoveryConfig.html)
- [`DomainConfig`](https://nd1012.github.io/wan24-AutoDiscover/api/wan24.AutoDiscover.Models.DomainConfig.html)
- [`Protocol`](https://nd1012.github.io/wan24-AutoDiscover/api/wan24.AutoDiscover.Models.Protocol.html)

### Run as systemd service

On a Debian Linux host you can run the `wan24-AutoDiscover` microservice using 
systemd:

```bash
dotnet wan24AutoDiscover.dll autodiscover systemd > /etc/systemd/system/autodiscover.service
systemctl enable autodiscover
systemctl start autodiscover
systemctl status autodiscover
```

### Apache2 proxy setup

Create the file `/etc/apache2/sites-available/autodiscover.conf`:

```txt
<VirtualHost [IP]:443>
        ServerName [DOMAIN]
        SSLEngine on
        SSLCertificateFile /path/to/fullchain.pem
        SSLCertificateKeyFile /path/to/privkey.pem
        ProxyPreserveHost On
        ProxyPass / http://127.0.0.1:5000/
        ProxyPassReverse / http://127.0.0.1:5000/
</VirtualHost>
```

Replace `[IP]` with your servers public IP address and `[DOMAIN]` with your 
domain name which you'd like to use for serving autodiscover.

Then activate the proxy:

```bash
a2enmod proxy
a2ensite autodiscover
systemctl restart apache2
```

### POX request and response

This is an example POX request to `/autodiscover/autodiscover.xml`:

```xml
<Autodiscover xmlns="https://schemas.microsoft.com/exchange/autodiscover/outlook/requestschema/2006">
   <Request>
     <EMailAddress>alias@wan24.de</EMailAddress>
     <AcceptableResponseSchema>https://schemas.microsoft.com/exchange/autodiscover/outlook/responseschema/2006a</AcceptableResponseSchema>
   </Request>
 </Autodiscover>
```

The response with the demo `appsettings.json`:

```xml
<Autodiscover xmlns="http://schemas.microsoft.com/exchange/autodiscover/responseschema/2006">
    <Response xmlns="https://schemas.microsoft.com/exchange/autodiscover/outlook/responseschema/2006a">
        <Account>
            <AccountType>email</AccountType>
            <Action>settings</Action>
            <Protocol>
                <Type>IMAP</Type>
                <Server>imap.wan24.de</Server>
                <Port>993</Port>
                <LoginName>alias</LoginName>
                <SPA>off</SPA>
                <SSL>on</SSL>
                <AuthRequired>on</AuthRequired>
            </Protocol>
            <Protocol>
                <Type>SMTP</Type>
                <Server>smtp.wan24.de</Server>
                <Port>587</Port>
                <LoginName>alias</LoginName>
                <SPA>off</SPA>
                <SSL>on</SSL>
                <AuthRequired>on</AuthRequired>
            </Protocol>
        </Account>
    </Response>
</Autodiscover>
```

### Required DNS configuration

In order to make autodiscover working in an email client, you'll need to 
create a SRV record for your email domain - example:

```txt
_autodiscover._tcp      1D IN SRV 0 0 443 [MTA-DOMAIN].
```

The domain `wan24.de` uses this record, for example:

```txt
_autodiscover._tcp      1D IN SRV 0 0 443 mail.wan24.de.
```

### CLI API

The `wan24-AutoDiscover` has a small built in CLI API, which can do some 
things for you:

#### Create a systemd service file

```bash
dotnet wan24AutoDiscover.dll autodiscover systemd > /etc/systemd/system/autodiscover.service
```

#### Parse a Postfix virtual configuration file

```bash
dotnet wan24AutoDiscover.dll autodiscover postfix < /etc/postfix/virtual > /home/autodiscover/postfix.json
```

#### Display version number

```bash
dotnet wan24AutoDiscover.dll autodiscover version
```

#### Upgrade online

Check for an available newer version only:

```bash
dotnet wan24AutoDiscover.dll autodiscover upgrade -checkOnly
```

**NOTE**: The command will exit with code #2, if an update is available online.

Upgrade with user interaction:

```bash
dotnet wan24AutoDiscover.dll autodiscover upgrade
```

Upgrade without user interaction:

```bash
dotnet wan24AutoDiscover.dll autodiscover upgrade -noUserInteraction
```

#### Display detailed CLI API usage instructions

```bash
dotnet wan24AutoDiscover.dll help -details
```

## Login name mapping

If the login name isn't the email address or the alias of the given email 
address, you can create a login name mapping per domain and/or protocol, by 
defining a mapping from the email address or alias to the login name. During 
lookup the protocol mapping and then the domain mapping will be used by trying 
the email address and then the alias as key.

### Automatic email address to login user mapping with Postfix

If your Postfix virtual email mappings are stored in a hash text file, you can 
create an email mapping from is using

```bash
dotnet wan24AutoDiscover.dll autodiscover postfix < /etc/postfix/virtual > /home/autodiscover/postfix.json
```

Then you can add the `postix.json` to your `appsettings.json`:

```json
{
	...
  "DiscoveryConfig": {
	...
	"EmailMappings": "/home/autodiscover/postfix.json",
	...
  }
}
```

The configuration will be reloaded, if the `postfix.json` file changed, so be 
sure to re-create the `postfix.json` file as soon as the `virtual` file was 
changed. If you don't want that, set `WatchEmailMappings` to `false`.

### Additionally watched files

You can set a list of additionally watched file paths to `WatchFiles` in your 
`appsettings.json` file. When any file was changed, the configuration will be 
reloaded.

### Pre-reload command execution

To execute a command before reloading a changed configration, set the 
`PreReloadCommand` value in your `appsettings.json` like this:

```json
{
	...
  "DiscoveryConfig": {
	...
	"PreReloadCommand": ["/command/to/execute", "argument1", "argument2", ...],
	...
  }
}
```

## Automatic online upgrades

You can upgrade `wan24-AutoDiscover` online and automatic. For this some steps 
are recommended:

1. Create sheduled task for auto-upgrade (daily, for example)
1. Stop the service before installing the newer version
1. Start the service after installing the newer version

The sheduled auto-upgrade task should execute this command on a Debian Linux 
server, for example:

```bash
dotnet /home/autodiscover/wan24AutoDiscover.dll autodiscover upgrade -noUserInteraction --preCommand systemctl stop autodiscover --postCommand systemctl start autodiscover
```

If the upgrade download failed, nothing will happen - the upgrade won't be 
installed only and being re-tried at the next sheduled auto-upgrade time.

If the upgrade installation failed, the post-upgrade command won't be 
executed, and the autodiscover service won't run. This'll give you the chance 
to investigate the broken upgrade and optional restore the running version 
manually.

**CAUTION**: The auto-upgrade is being performed using the GitHub repository. 
There are no security checks at present - so if the repository was hacked, you 
could end up with upgrading to a compromised software which could harm your 
system!

The upgrade setup should be done in less than a second, if everything was fine.

## Manual upgrade

1. Download the latest release ZIP file from GitHub
1. Extract the ZIP file to a temporary folder
1. Stop the autodiscover service, if running
1. Create a backup of your current installation
1. Copy all extracted files/folders excluding `appsettings.json` to your 
installation folder
1. Remove files/folders that are no longer required and perform additional 
upgrade steps, which are required for the new release (see upgrade 
instructions)
1. Start the autodiscover service
1. Delete the previously created backup and the temporary folder

These steps are being executed during an automatic upgrade like described 
above also.
