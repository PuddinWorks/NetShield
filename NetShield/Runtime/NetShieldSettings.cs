using System;
using System.Collections.Generic;
using UnityEngine;

namespace NetShield
{

    public enum DetectionAction { DoNothing, JustLog, Objects, QuitGame, UnityEvent, LoadScene }

    public enum DetectionStatus { Disabled, NoMatch, Detected, Incomplete, InvalidAddress, Whitelisted }

    [Serializable]
    public sealed class NetShieldActionRule
    {
        public string match = "Other VPN";
        public List<DetectionAction> actions = new List<DetectionAction> { DetectionAction.DoNothing };
    }

    [Serializable]
    public sealed class NetShieldCustomSource
    {
        public string name = "Custom source";
        public string url = "";
        public bool enabled = true;
        public List<string> ranges = new List<string>();
        public long updated;
        public string error = "";
    }

    [Serializable]

    public sealed class NetShieldSettings
    {
        public bool enabled = true;
        public bool checkOnStart = true;
        public bool ipv4 = true;
        public bool ipv6 = true;
        public bool vpn = true;
        public bool unknownVpn = true;
        public bool proxy = true;
        public bool tor = true;
        public bool datacenter = false;
        public bool custom = true;
        public bool autoUpdate = true;
        public bool publicLookup = false;
        public bool offline = false;
        public bool continuous = false;
        public bool onlyOnChange = false;
        public bool persistent = false;
        public bool once = true;
        public bool requireFresh = false;
        public bool failClosed = false;
        public bool history = true;
        public int historyLimit = 50;
        public float checkInterval = 60;
        public float actionDelay = 0;
        public float cooldown = 5;
        public string sceneName = "";
        public List<DetectionAction> additionalActions = new List<DetectionAction>();
        public List<DetectionAction> failureActions = new List<DetectionAction> { DetectionAction.DoNothing };
        public List<NetShieldActionRule> actionRules = new List<NetShieldActionRule>();
        public List<string> disabledSources = new List<string>();
        public List<NetShieldCustomSource> customSources = new List<NetShieldCustomSource>();
        public bool SourceEnabled(string id) => !disabledSources.Contains(id);
        public void SetSource(string id, bool value) { disabledSources.RemoveAll(x => x == id); if (!value) disabledSources.Add(id); }
        public int refreshHours = 6;
        public int timeoutSeconds = 12;
        public DetectionAction action = DetectionAction.JustLog;
        public List<string> disabledProviders = new List<string>();
        public List<string> customRanges = new List<string>();
        public List<string> whitelist = new List<string>();
        public bool ProviderEnabled(string name) { return !disabledProviders.Exists(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase)); }
        public void SetProvider(string name, bool value)
        {
            disabledProviders.RemoveAll(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase));
            if (!value) disabledProviders.Add(name);
        }
    }

    [Serializable]

    public sealed class NetShieldResult
    {
        public string address;
        public DetectionStatus status;
        public string[] matches = Array.Empty<string>();
        public string[] activeMatches = Array.Empty<string>();
        public bool complete;
        public string AddressFamily => string.IsNullOrEmpty(address) ? "Unknown" : address.Contains(",") ? "Multiple" : address.Contains(":") ? "IPv6" : "IPv4";
        public bool Detected { get { return status == DetectionStatus.Detected; } }
        public override string ToString() { return status + (string.IsNullOrEmpty(address) ? "" : " | " + address) + (activeMatches.Length == 0 ? "" : " | " + string.Join(", ", activeMatches)); }
    }

    [Serializable]

    public sealed class NetShieldResultEvent : UnityEngine.Events.UnityEvent<NetShieldResult> { }
}
