# NetShield API

All examples use Unity's main thread. No network framework is required. NetShield does not acquire remote player addresses. Obtain a trustworthy address yourself and implement your own disconnect or reject logic. Do not use client-submitted IPs as authoritative identity.

## Synchronous lookup

```csharp
using NetShield;

var result = NetShieldAPI.CheckIP("192.0.2.10");
if (result.Detected)
    UnityEngine.Debug.Log(string.Join(", ", result.activeMatches));
```

The supplied IP is not transmitted to a third-party classification service. This uses cached or bundled data, no network requests and no manager actions. `NetShieldAPI.IsAvailable` is retained for API compatibility and returns true. `CheckIP` does not require a scene manager.

## Async refresh and lookup

```csharp
var result = await NetShieldAPI.CheckIPAsync(playerIp);
if (!result.complete)
    return;
if (result.Detected)
    HandleDetection(result);
```

Async refresh may download enabled data sources if autoUpdate is enabled and offline is false. Supply an existing manager's settings or custom settings as the optional second parameter. Refresh is shared across callers; await it before checking. A concurrent refresh may have used a different manager's source settings, so check completeness and retry if necessary.

## Offline API

```csharp
var settings = new NetShieldSettings { offline = true };
settings.customRanges.Add("192.0.2.10");
var result = NetShieldAPI.CheckIP("192.0.2.10", settings);
```

For custom-only operation disable VPN, proxy, Tor and datacenter categories and any unneeded sources. Without downloaded data, a custom match can still be detected, but `complete` may be false if other enabled categories lack coverage.

## Manager

```csharp
global::NetShield.NetShield manager;
var result = manager.InspectAddress(playerIp);
var refreshed = await manager.InspectAddressAsync(playerIp);
var applied = manager.CheckAddressAndApply(playerIp);
```

Assign the manager reference in your component. InspectAddress and InspectAddressAsync never execute local actions. CheckAddressAndApply does in Play Mode, including configured delay/cooldown/once rules. For remote clients, use InspectAddress or NetShieldAPI and handle enforcement yourself. Manager state, history and once/cooldown are shared for its local check stream, not per remote player. CheckConnectionAsync discovers the local machine's public IP only when opted in and not offline. `CheckConnection()` is an inspector-friendly wrapper.

## Results

- `status`: Disabled, NoMatch, Detected, Incomplete, InvalidAddress, Whitelisted.
- `Detected`: true if an enabled rule matched; check `complete` to evaluate freshness independently.
- `address`: normalized address, or combined local addresses for dual-stack detection.
- `matches`: categories found in enabled source data before category/provider switches.
- `activeMatches`: matches after switches and priority rules.
- `complete`: enabled required feeds are present and fresh; not a guarantee of coverage.

A stale positive can be returned as Detected. Require fresh database affects manager response, not raw classification. Whitelist always overrides matching. A disabled category is not evidence a connection is safe. Missing sources can give Incomplete. Results are mutable; treat callbacks as read-only.

## Events

```csharp
manager.Checked += HandleCheck;
manager.Detected += HandleDetection;
manager.Checked -= HandleCheck;
manager.Detected -= HandleDetection;
```

Serialized UnityEvents: onChecked, onDetected, onAction, onCleared and onFailed. `onAction` is invoked by the Unity Event action. `onCleared` occurs after a prior detection when a subsequent check is NoMatch or Whitelisted. `onFailed` reports incomplete checks or stale positives when freshness is required. History is bounded, in memory, without raw IPs. Raw result addresses may be personal data; handle and log appropriately.

## Database

`NetShieldDatabase.Shared.RefreshAsync(settings, true)` forces enabled-source refresh unless offline. `ClearCache`, `RestoreSnapshot`, `ExportSnapshot`, `ExportGroupedText`, `SearchRanges`, `ParseImport`, `RunSelfTests` are available for management. `TryAddress`, `TryRange` and `Contains` are pure parsing helpers rather than detection APIs. Manage lists and their licenses before exporting data. Database operations are not thread-safe; call from the Unity main thread.
