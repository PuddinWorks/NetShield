# NetShield 2.0.3

VPN, proxy and Tor detection for Unity. Supports IPv4 and IPv6.

Made by PuddinWorks.

## Getting started

1. Place the NetShield folder in your project's Assets folder.
2. Open Tools > NetShield.
3. Add a NetShield Manager to your scene if you want automatic checks and actions.
4. Enable the IP sources you want to use and update the database, or import your own lists.
5. Choose what happens when a match is found.

The API works without a scene manager. Public IP lookup is optional and off by default. Offline mode disables network requests.

## Window

- Config: detection switches, actions, database, whitelist and IP tester.
- API: C# examples and usage instructions.
- Third-Party: source credits and licenses.

## Check an IP

```csharp
using NetShield;

var result = NetShieldAPI.CheckIP("192.0.2.10");
if (result.Detected)
    UnityEngine.Debug.Log(string.Join(", ", result.activeMatches));
```

Use CheckIPAsync for checks that may refresh the database. Check result.complete before relying on the result. NetShield does not collect remote player IPs or manage multiplayer connections.

## Notes

- No IP database is bundled. Enable sources or import your own data first.
- IP detection is not always accurate. Local checks can be bypassed.
- Review source licenses before sharing downloaded data. External services can see your connection IP.
- Unity 2021.3 LTS or newer is the target; platform compatibility has not been fully tested.
- When updating, keep your existing .meta files. For older versions, check the release instructions before merging.

## Links

- [Documentation](https://netshield.katzulabs.com/)
- [GitHub](https://github.com/PuddinWorks/NetShield)
- [API reference](Documentation/API.md)
- [Third-party sources](Third-Party%20Notices.txt)
- [License](LICENSE)
