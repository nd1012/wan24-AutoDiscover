# wan24-AutoDiscover

This is a micro-webservice which supports a small part of the Microsoft 
Exchange POX autodiscover standard, which allows email clients to receive 
automatic configuration information for an email account.

It was created using .NET 8 and ASP.NET. You find a published release build 
for each published release on GitHub as ZIP download for self-hosting.

## Usage

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

Documentation references:

- [`DiscoveryConfig`](https://nd1012.github.io/wan24-AutoDiscover/api/wan24.AutoDiscover.Models.DiscoveryConfig.html)
- [`DomainConfig`](https://nd1012.github.io/wan24-AutoDiscover/api/wan24.AutoDiscover.Models.DomainConfig.html)
- [`Protocol`](https://nd1012.github.io/wan24-AutoDiscover/api/wan24.AutoDiscover.Models.Protocol.html)

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

### Run as systemd service

On a Debian Linux host you can run the `wan24-AutoDiscover` microservice using 
systemd:

```bash
dotnet wan24AutoDiscover.dll autodiscover systemd > /etc/systemd/system/autodiscover.service
systemctl enable autodiscover
systemctl start autodiscover
systemctl status autodiscover
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

### CLI API

The `wan24-AutoDiscover` has a small built in CLI API, which can do some 
things for you:

#### Create a systemd service file

```bash
dotnet wan24AutoDiscover.dll autodiscover systemd > /etc/systemd/system/autodiscover.service
```
