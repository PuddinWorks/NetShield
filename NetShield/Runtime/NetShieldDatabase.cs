using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;

namespace NetShield
{

    public sealed class NetShieldDatabase
    {
        public const string GenericVPN = "Other VPN";
        public const string Proxy = "Proxy";
        public const string Tor = "Tor";
        public const string Datacenter = "Datacenter";
        public const string Custom = "Custom";
        public static readonly string[] BuiltInProviders = { "AirVPN", "Astrill", "AtlasVPN", "AvastVPN", "AzireVPN", "CactusVPN", "CyberGhost", "ExpressVPN", "FastVPN", "FreedomeVPN", "GooseVPN", "Hide.me", "Hidester", "HMA", "HotspotShield", "IPVanish", "IVPN", "Mullvad", "NordVPN", "NortonVPN", "OVPN", "PerfectPrivacy", "PIA", "PrivateVPN", "ProtonVPN", "PureVPN", "RiseupVPN", "SaferVPN", "Seed4.me", "Speedify", "StrongVPN", "Surfshark", "TigerVPN", "TorGuard", "TunnelBear", "VeePN", "VPN.ac", "VPNUnlimited", "VyprVPN", "Windscribe", "ZenMate" };
        public sealed class Feed
        {
            public readonly string id, label, url, category;
            public Feed(string id, string label, string url, string category) { this.id = id; this.label = label; this.url = url; this.category = category; }
        }
        static Feed Named(string file, string label) { return new Feed(file, label, "https://raw.githubusercontent.com/Joe12387/open-source-vpn-ip-lists/master/" + file + ".txt", label); }
        const string X4 = "https://raw.githubusercontent.com/X4BNet/lists_vpn/main/output/";
        public static readonly Feed[] Feeds = {
            Named("airvpn", "AirVPN"), Named("ivpn", "IVPN"), Named("mullvad", "Mullvad"), Named("nordvpn", "NordVPN"), Named("ovpn", "OVPN"), Named("pia", "PIA"), Named("protonvpn", "ProtonVPN"), Named("riseupvpn", "RiseupVPN"), Named("surfshark", "Surfshark"), Named("windscribe", "Windscribe"),
            new Feed("providers", "Provider ranges", "https://raw.githubusercontent.com/millshre5/vpnips/main/vpn_ip_provider_list.csv", "CSV"),
            new Feed("vpn4", "Other VPN IPv4", X4 + "vpn/ipv4.txt", GenericVPN), new Feed("vpn6", "Other VPN IPv6", X4 + "vpn/ipv6.txt", GenericVPN),
            new Feed("dc4", "Hosting / VPN IPv4", X4 + "datacenter/ipv4.txt", Datacenter), new Feed("dc6", "Hosting / VPN IPv6", X4 + "datacenter/ipv6.txt", Datacenter),
            new Feed("proxy", "Public proxies", "https://raw.githubusercontent.com/proxmint/free-proxy-list/main/proxies/all.txt", Proxy), new Feed("tor", "Tor exit nodes", "https://check.torproject.org/torbulkexitlist", Tor)
        };
        [Serializable] sealed class FeedData { public string id; public string text; public long updated; }
        [Serializable] sealed class LegacySnapshot { public List<FeedData> feeds = new List<FeedData>(); }
        [Serializable] sealed class SourceStamp { public string id; public long updated; }
        [Serializable] sealed class SourceRanges { public string id; public List<string> ranges = new List<string>(); }
        [Serializable] sealed class ProviderGroup { public string name; public List<SourceRanges> sources = new List<SourceRanges>(); }
        [Serializable] sealed class Snapshot { public int version = 2; public List<SourceStamp> sources = new List<SourceStamp>(); public List<ProviderGroup> groups = new List<ProviderGroup>(); }
        public sealed class FeedStatus { public string label, error; public int count; public DateTime updated; }
        sealed class Node { public Node zero, one; public HashSet<string> tags; }
        sealed class Index
        {
            readonly Node v4 = new Node(), v6 = new Node();
            public void Add(byte[] bytes, int bits, string tag)
            {
                Node n = bytes.Length == 4 ? v4 : v6;
                for (int i = 0; i < bits; i++)
                {
                    if ((bytes[i / 8] & (128 >> (i % 8))) == 0) { if (n.zero == null) n.zero = new Node(); n = n.zero; }
                    else { if (n.one == null) n.one = new Node(); n = n.one; }
                }
                if (n.tags == null) n.tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                n.tags.Add(tag);
            }
            public HashSet<string> Find(byte[] bytes)
            {
                var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                Node n = bytes.Length == 4 ? v4 : v6;
                for (int i = 0; n != null; i++)
                {
                    if (n.tags != null) result.UnionWith(n.tags);
                    if (i == bytes.Length * 8) break;
                    n = (bytes[i / 8] & (128 >> (i % 8))) == 0 ? n.zero : n.one;
                }
                return result;
            }
        }
        public static NetShieldDatabase Shared { get; } = new NetShieldDatabase();
        readonly Dictionary<string, FeedData> data = new Dictionary<string, FeedData>();
        readonly Dictionary<string, FeedStatus> status = new Dictionary<string, FeedStatus>();
        readonly HashSet<string> providers = new HashSet<string>(BuiltInProviders, StringComparer.OrdinalIgnoreCase);
        Index index = new Index();
        bool loaded;
        Task refreshTask;
        public bool IsRefreshing { get { return refreshTask != null && !refreshTask.IsCompleted; } }
        public string CacheError { get; private set; }
        public string[] Providers { get { return providers.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray(); } }
        public int EntryCount { get { return status.Values.Sum(x => x.count); } }
        public FeedStatus GetStatus(string id) { FeedStatus s; return status.TryGetValue(id, out s) ? s : new FeedStatus(); }
        string CachePath { get { return Path.Combine(Application.persistentDataPath, "NetShield", "database-v1.json"); } }

        public long CacheBytes => File.Exists(CachePath) ? new FileInfo(CachePath).Length : 0;
        public int Revision { get; private set; }
        public int UniqueEntryCount { get; private set; }
        public DateTime LastUpdated => status.Count == 0 ? DateTime.MinValue : status.Values.Max(x => x.updated);
        public int LoadedProviderCount { get; private set; }
        public static List<string> ParseImport(string text)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(text)) return result.ToList();
            foreach (var line in text.Split('\n'))
            {
                string clean = line.Split('#')[0];
                foreach (string token in Regex.Split(clean, "[\\s,;\\[\\]{}\"']+"))
                {
                    if (TryRange(token, out var bytes, out var bits)) result.Add(FormatRange(bytes, bits));
                }
            }
            return result.OrderBy(x => x, StringComparer.Ordinal).ToList();
        }
        public void ClearCache()
        {
            if (IsRefreshing) return;
            if (File.Exists(CachePath)) File.Delete(CachePath);
            data.Clear(); status.Clear(); loaded = true; Rebuild();
        }
        public void RestoreSnapshot(string json)
        {
            if (IsRefreshing) return;
            var candidate = new NetShieldDatabase { loaded = true };
            candidate.Merge(json);
            if (candidate.data.Count == 0) throw new InvalidDataException("No recognized feed data.");
            data.Clear(); status.Clear();
            foreach (var item in candidate.data) data[item.Key] = item.Value;
            loaded = true; Rebuild(); SaveCache();
        }
        public string[] SearchRanges(string query, int limit = 100)
        {
            if (string.IsNullOrWhiteSpace(query)) return Array.Empty<string>();
            Load();
            var snapshot = BuildSnapshot();
            return snapshot.groups.SelectMany(g => g.sources.SelectMany(s => s.ranges.Select(r => new { g.name, range = r })))
                .Where(x => x.name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 || x.range.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 || Contains(x.range, query))
                .Select(x => x.name + " - " + x.range).Distinct().Take(Mathf.Clamp(limit, 1, 500)).ToArray();
        }

        public static bool TryAddress(string text, out IPAddress address)
        {
            address = null;
            if (string.IsNullOrWhiteSpace(text)) return false;
            text = text.Trim();
            if (text.IndexOf('%') >= 0) return false;
            if (!text.Contains(":"))
            {
                string[] parts = text.Split('.');
                if (parts.Length != 4) return false;
                foreach (string p in parts) { byte b; if (p.Length == 0 || (p.Length > 1 && p[0] == '0') || !byte.TryParse(p, NumberStyles.None, CultureInfo.InvariantCulture, out b)) return false; }
            }
            if (!IPAddress.TryParse(text, out address)) return false;
            if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
            return address.AddressFamily == AddressFamily.InterNetwork || address.AddressFamily == AddressFamily.InterNetworkV6;
        }
        public static bool TryRange(string text, out byte[] bytes, out int bits)
        {
            bytes = null; bits = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;
            var parts = text.Trim().Split('/'); IPAddress address;
            if (parts.Length > 2 || !TryAddress(parts[0], out address)) return false;
            bytes = address.GetAddressBytes(); bits = bytes.Length * 8;
            if (parts.Length == 2)
            {
                if (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out bits)) return false;
                if (bytes.Length == 4 && parts[0].Contains(":")) { if (bits < 96) return false; bits -= 96; }
                if (bits < 0 || bits > bytes.Length * 8) return false;
            }
            return true;
        }
        public static bool Contains(string range, string ip)
        {
            byte[] bytes; int bits; IPAddress addr;
            if (!TryRange(range, out bytes, out bits) || !TryAddress(ip, out addr)) return false;
            byte[] target = addr.GetAddressBytes(); if (bytes.Length != target.Length) return false;
            for (int i = 0; i < bits; i++) if ((bytes[i / 8] & (128 >> (i % 8))) != (target[i / 8] & (128 >> (i % 8)))) return false;
            return true;
        }
        static string NormalizeProvider(string name)
        {
            name = name.Trim().Trim('"');
            if (name.Equals("OVPN.com", StringComparison.OrdinalIgnoreCase)) return "OVPN";
            if (name.Equals("Private Internet Access", StringComparison.OrdinalIgnoreCase)) return "PIA";
            return BuiltInProviders.FirstOrDefault(x => x.Equals(name, StringComparison.OrdinalIgnoreCase)) ?? name;
        }
        static string ProxyAddress(string value)
        {
            int scheme = value.IndexOf("://", StringComparison.Ordinal); if (scheme >= 0) value = value.Substring(scheme + 3);
            if (value.StartsWith("[")) { int end = value.IndexOf(']'); return end > 0 ? value.Substring(1, end - 1) : ""; }
            IPAddress address; if (TryAddress(value, out address) || (value.Contains("/") && TryRange(value, out _, out _))) return value;
            int colon = value.LastIndexOf(':'); return colon > 0 ? value.Substring(0, colon) : value;
        }
        static int Parse(Feed feed, string text, Action<byte[], int, string> add)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            int count = 0;
            using (var reader = new StringReader(text))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    line = line.Trim().TrimStart('\uFEFF'); int comment = line.IndexOf('#'); if (comment >= 0) line = line.Substring(0, comment).Trim();
                    if (line.Length == 0) continue;
                    string tag = feed.category, range = line;
                    if (tag == "CSV") { var fields = line.Split(','); if (fields.Length != 2) continue; range = fields[0].Trim().Trim('"'); tag = NormalizeProvider(fields[1]); if (tag.Length == 0 || tag.Length > 100 || (tag.Equals(Tor, StringComparison.OrdinalIgnoreCase) || tag.Equals(Custom, StringComparison.OrdinalIgnoreCase) || tag.Equals("Whitelist", StringComparison.OrdinalIgnoreCase) || tag.IndexOfAny(new[] { '\r', '\n', ',', '|' }) >= 0)) continue; }
                    if (tag == Proxy) range = ProxyAddress(range);
                    byte[] bytes; int bits;
                    if (!TryRange(range, out bytes, out bits) || bits < (bytes.Length == 4 ? 8 : 16)) continue;
                    count++; if (add != null) add(bytes, bits, tag);
                }
            }
            return count;
        }
        public void Load()
        {
            if (loaded) return; loaded = true;
            try { var asset = Resources.Load<TextAsset>("NetShieldDatabase"); if (asset != null) Merge(asset.text); } catch (Exception e) { CacheError = e.Message; }
            bool migrate = false;
            string reformatted = null;
            try
            {
                if (File.Exists(CachePath))
                {
                    string json = File.ReadAllText(CachePath); Merge(json);
                    migrate = json.Contains("\"feeds\"") && !json.Contains("\"groups\"");
                    if (!migrate && json.Contains("\"groups\""))
                    {
                        string compact = FormatSnapshotRows(json);
                        if (compact != json) reformatted = compact;
                    }
                }
            }
            catch (Exception e) { CacheError = e.Message; }
            Rebuild();
            if (migrate && data.Count > 0)
            {
                try
                {
                    if (!File.Exists(CachePath + ".legacy")) File.Copy(CachePath, CachePath + ".legacy");
                    SaveCache();
                }
                catch (Exception e) { CacheError = e.Message; }
            }
            else if (reformatted != null) WriteCache(reformatted);
        }
        void AcceptFeed(FeedData item)
        {
            if (item == null || item.updated <= 0 || item.updated > DateTime.UtcNow.AddDays(1).Ticks) return;
            Feed f = Feeds.FirstOrDefault(x => x.id == item.id); FeedData old;
            if (f != null && (!data.TryGetValue(item.id, out old) || item.updated > old.updated) && Parse(f, item.text, null) > 0) data[item.id] = item;
        }
        void Merge(string json)
        {
            var snapshot = JsonUtility.FromJson<Snapshot>(json);
            if (snapshot != null && snapshot.sources != null && snapshot.sources.Count > 0 && snapshot.groups != null)
            {
                foreach (var source in snapshot.sources)
                {
                    if (source == null) continue;
                    var feed = Feeds.FirstOrDefault(x => x.id == source.id);
                    if (feed == null) continue;
                    var lines = new System.Text.StringBuilder();
                    foreach (var group in snapshot.groups)
                    {
                        if (group == null || string.IsNullOrEmpty(group.name) || group.sources == null) continue;
                        if (feed.category != "CSV" && group.name != feed.category) continue;
                        if (group.name.IndexOfAny(new[] { '\r', '\n', ',', '|' }) >= 0) continue;
                        foreach (var entries in group.sources)
                        {
                            if (entries == null || entries.id != source.id || entries.ranges == null) continue;
                            foreach (string range in entries.ranges)
                            {
                                if (string.IsNullOrEmpty(range) || range.IndexOfAny(new[] { '\r', '\n', ',', '|' }) >= 0) continue;
                                lines.Append(range);
                                if (feed.category == "CSV") lines.Append(',').Append(group.name);
                                lines.AppendLine();
                            }
                        }
                    }
                    AcceptFeed(new FeedData { id = source.id, updated = source.updated, text = lines.ToString() });
                }
                return;
            }
            var legacy = JsonUtility.FromJson<LegacySnapshot>(json);
            if (legacy != null && legacy.feeds != null) foreach (var item in legacy.feeds) AcceptFeed(item);
        }
        static string FormatRange(byte[] bytes, int bits)
        {
            for (int i = bits; i < bytes.Length * 8; i++) bytes[i / 8] &= (byte)~(128 >> (i % 8));
            return new IPAddress(bytes).ToString() + (bits == bytes.Length * 8 ? "" : "/" + bits);
        }
        Snapshot BuildSnapshot()
        {
            var snapshot = new Snapshot();
            var groups = new Dictionary<string, ProviderGroup>(StringComparer.OrdinalIgnoreCase);
            foreach (var feed in Feeds)
            {
                if (!data.TryGetValue(feed.id, out var item)) continue;
                snapshot.sources.Add(new SourceStamp { id = feed.id, updated = item.updated });
                var perFeed = new Dictionary<string, SourceRanges>(StringComparer.OrdinalIgnoreCase);
                Parse(feed, item.text, (bytes, bits, tag) =>
                {
                    if (!groups.TryGetValue(tag, out var group)) groups[tag] = group = new ProviderGroup { name = tag };
                    if (!perFeed.TryGetValue(tag, out var entries))
                    {
                        perFeed[tag] = entries = new SourceRanges { id = feed.id };
                        group.sources.Add(entries);
                    }
                    entries.ranges.Add(FormatRange(bytes, bits));
                });
            }
            snapshot.groups = groups.Values.OrderBy(g => g.name, StringComparer.OrdinalIgnoreCase).ToList();
            return snapshot;
        }
        const int AddressesPerLine = 50;
        static readonly Regex RangeArrayPattern = new Regex(@"^(?<indent>[ \t]*)""ranges""[ \t]*:[ \t]*\[(?<body>\s*(?:""[0-9a-fA-F:./]+""\s*(?:,\s*""[0-9a-fA-F:./]+""\s*)*)?)\]", RegexOptions.Multiline);
        static readonly Regex RangeValuePattern = new Regex(@"""(?<value>[0-9a-fA-F:./]+)""");
        static void AppendRangeRows(System.Text.StringBuilder text, IEnumerable<string> ranges, string indent, string newline)
        {
            int count = 0;
            foreach (string range in ranges)
            {
                if (count == 0) text.Append(indent);
                else if (count % AddressesPerLine == 0) text.Append(',').Append(newline).Append(indent);
                else text.Append(", ");
                text.Append('"').Append(range).Append('"');
                count++;
            }
            if (count > 0) text.Append(newline);
        }
        static string FormatSnapshotRows(string json)
        {
            string newline = json.Contains("\r\n") ? "\r\n" : "\n";
            return RangeArrayPattern.Replace(json, match =>
            {
                string indent = match.Groups["indent"].Value;
                var ranges = RangeValuePattern.Matches(match.Groups["body"].Value).Cast<Match>().Select(x => x.Groups["value"].Value);
                var text = new System.Text.StringBuilder();
                text.Append(indent).Append("\"ranges\": [");
                if (ranges.Any())
                {
                    text.Append(newline);
                    AppendRangeRows(text, ranges, indent + "    ", newline);
                    text.Append(indent);
                }
                return text.Append(']').ToString();
            });
        }
        public string ExportGroupedText(NetShieldSettings settings)
        {
            Load(); var snapshot = BuildSnapshot(); var text = new System.Text.StringBuilder();
            foreach (var group in snapshot.groups)
            {
                text.Append('[').Append(group.name).AppendLine("]");
                AppendRangeRows(text, group.sources.SelectMany(x => x.ranges).Distinct().OrderBy(x => x, StringComparer.Ordinal), "", Environment.NewLine);
                text.AppendLine();
            }
            foreach (var section in new[] { new { name = Custom, ranges = settings.customRanges.Concat(settings.customSources.Where(x => x != null).SelectMany(x => x.ranges)).ToList() }, new { name = "Whitelist", ranges = settings.whitelist } })
            {
                text.Append('[').Append(section.name).AppendLine("]");
                var valid = new List<string>();
                foreach (var range in section.ranges) if (TryRange(range, out var bytes, out var bits)) valid.Add(FormatRange(bytes, bits));
                AppendRangeRows(text, valid, "", Environment.NewLine);
                text.AppendLine();
            }
            return text.ToString();
        }
        void Rebuild()
        {
            var next = new Index(); var unique = new HashSet<string>(); var named = new HashSet<string>(StringComparer.OrdinalIgnoreCase); providers.Clear(); providers.UnionWith(BuiltInProviders);
            foreach (var f in Feeds)
            {
                FeedData item; FeedStatus s = GetStatus(f.id); s.label = f.label; s.count = 0;
                if (data.TryGetValue(f.id, out item))
                {
                    s.updated = new DateTime(item.updated, DateTimeKind.Utc);
                    s.count = Parse(f, item.text, (bytes, bits, tag) => { next.Add(bytes, bits, f.id + "|" + tag); unique.Add(FormatRange(bytes, bits)); if (IsProvider(tag)) { providers.Add(tag); named.Add(tag); } });
                }
                status[f.id] = s;
            }
            index = next; UniqueEntryCount = unique.Count; LoadedProviderCount = named.Count; Revision++;
        }
        public string ExportSnapshot() { Load(); return FormatSnapshotRows(JsonUtility.ToJson(BuildSnapshot(), true)); }
        public static bool IsProvider(string tag) { return tag != GenericVPN && tag != Proxy && tag != Tor && tag != Datacenter && tag != Custom; }
        public Task RefreshAsync(NetShieldSettings settings, bool force = false)
        {
            if (settings.offline || !settings.enabled) return Task.CompletedTask; Load(); if (IsRefreshing) return refreshTask;
            refreshTask = RefreshInternal(settings, force); return refreshTask;
        }
        async Task RefreshInternal(NetShieldSettings settings, bool force)
        {
            int hours = Mathf.Clamp(settings.refreshHours, 1, 168), timeout = Mathf.Clamp(settings.timeoutSeconds, 3, 60);
            var queue = Feeds.Where(f => settings.SourceEnabled(f.id) && (force || !data.ContainsKey(f.id) || DateTime.UtcNow.Ticks - data[f.id].updated >= TimeSpan.FromHours(hours).Ticks)).ToArray();
            foreach (var source in settings.customSources.Where(x => x != null && x.enabled))
            {
                if (!force && source.updated > 0 && DateTime.UtcNow.Ticks - source.updated < TimeSpan.FromHours(hours).Ticks) continue;
                if (!Uri.TryCreate(source.url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) { source.error = "A valid HTTPS URL is required."; continue; }
                try
                {
                    var ranges = ParseImport(await GetTextAsync(source.url, timeout));
                    if (ranges.Count == 0) throw new InvalidDataException("No valid ranges; previous data retained.");
                    source.ranges = ranges; source.updated = DateTime.UtcNow.Ticks; source.error = "";
                }
                catch (Exception e) { source.error = e.Message; }
            }
            if (queue.Length == 0) return;
            for (int i = 0; i < queue.Length; i += 5) await Task.WhenAll(queue.Skip(i).Take(5).Select(f => Download(f, timeout)));
            Rebuild();
            SaveCache();
        }
        void SaveCache() { try { WriteCache(ExportSnapshot()); } catch (Exception e) { CacheError = e.Message; } }
        void WriteCache(string json)
        {
            try { Directory.CreateDirectory(Path.GetDirectoryName(CachePath)); File.WriteAllText(CachePath + ".tmp", json); File.Copy(CachePath + ".tmp", CachePath, true); File.Delete(CachePath + ".tmp"); CacheError = null; } catch (Exception e) { CacheError = e.Message; }
        }
        async Task Download(Feed feed, int timeout)
        {
            var s = GetStatus(feed.id); s.label = feed.label; status[feed.id] = s;
            try
            {
                string text = await GetTextAsync(feed.url, timeout);
                if (Parse(feed, text, null) == 0) throw new InvalidDataException("No valid IP entries; previous data retained.");
                data[feed.id] = new FeedData { id = feed.id, text = text, updated = DateTime.UtcNow.Ticks }; s.error = null;
            }
            catch (Exception e) { s.error = e.Message; }
        }
        public static async Task<string> GetTextAsync(string url, int timeout)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) throw new ArgumentException("HTTPS URL required.");
            using (var request = UnityWebRequest.Get(url))
            {
                request.timeout = Mathf.Clamp(timeout, 3, 60); var operation = request.SendWebRequest();
                while (!operation.isDone) await Task.Delay(25);
                if (request.result != UnityWebRequest.Result.Success) throw new IOException(request.error ?? "Request failed");
                if (request.downloadedBytes > 32000000) throw new IOException("Response exceeds size limit");
                return request.downloadHandler.text;
            }
        }
        public bool IsComplete(NetShieldSettings settings)
        {
            return IsCompleteCore(settings);
        }
        bool IsCompleteCore(NetShieldSettings settings)
        {
            Load();
            foreach (var f in Feeds)
            {
                if (!settings.SourceEnabled(f.id)) continue;
                bool needed = f.category == "CSV" ? settings.vpn : f.category == GenericVPN ? settings.vpn && settings.unknownVpn : f.category == Proxy ? settings.proxy : f.category == Tor ? settings.tor : f.category == Datacenter ? settings.datacenter : settings.vpn && settings.ProviderEnabled(f.category);
                if ((f.id.EndsWith("4") && !settings.ipv4) || (f.id.EndsWith("6") && !settings.ipv6)) needed = false;
                var s = GetStatus(f.id);
                if (needed && (s.count == 0 || DateTime.UtcNow - s.updated > TimeSpan.FromHours(Mathf.Clamp(settings.refreshHours, 1, 168)))) return false;
            }
            if (settings.custom && settings.customSources.Any(x => x != null && x.enabled && (x.updated <= 0 || x.ranges.Count == 0 || DateTime.UtcNow.Ticks - x.updated > TimeSpan.FromHours(Mathf.Clamp(settings.refreshHours, 1, 168)).Ticks))) return false;
            return true;
        }
        public static string RunSelfTests()
        {
            int checks = 0;
            Action<bool, string> expect = (passed, name) => { if (!passed) throw new InvalidOperationException(name); checks++; };
            expect(Contains("192.0.2.0/24", "192.0.2.255"), "IPv4 last address");
            expect(!Contains("192.0.2.0/24", "192.0.3.0"), "IPv4 outside range");
            expect(Contains("2001:db8:12::/48", "2001:db8:12:ffff::1"), "IPv6 inside range");
            expect(!Contains("2001:db8:12::/48", "2001:db8:13::1"), "IPv6 outside range");
            expect(Contains("192.0.2.1", "::ffff:192.0.2.1"), "Mapped IPv4");
            expect(!Contains("192.0.2.1/33", "192.0.2.1"), "Invalid prefix");
            var fixture = new NetShieldDatabase { loaded = true };
            Action<string, string> add = (id, text) => fixture.data[id] = new FeedData { id = id, text = text, updated = DateTime.UtcNow.Ticks };
            add("providers", "cidr,vpn\n192.0.2.0/25,ProtonVPN\n2001:db8:12::/48,Mullvad");
            add("vpn4", "192.0.2.0/24"); add("dc4", "192.0.2.0/24\n203.0.113.0/24");
            add("proxy", "socks5://198.51.100.10:1080"); add("tor", "198.51.100.20");
            fixture.Rebuild(); var settings = new NetShieldSettings { datacenter = true };
            expect(fixture.Check("192.0.2.10", settings).activeMatches.SequenceEqual(new[] { "ProtonVPN" }), "Named VPN takes priority");
            settings.SetProvider("ProtonVPN", false);
            expect(!fixture.Check("192.0.2.10", settings).Detected, "Disabled provider does not fall through");
            expect(fixture.Check("192.0.2.200", settings).Detected, "Generic VPN");
            expect(fixture.Check("2001:db8:12::1", settings).Detected, "IPv6 named VPN");
            expect(fixture.Check("198.51.100.10", settings).activeMatches.Contains(Proxy), "Proxy parser");
            expect(fixture.Check("198.51.100.20", settings).activeMatches.Contains(Tor), "Tor parser");
            expect(fixture.Check("203.0.113.10", settings).activeMatches.Contains(Datacenter), "Datacenter match");
            settings.whitelist.Add("203.0.113.0/24");
            expect(fixture.Check("203.0.113.10", settings).status == DetectionStatus.Whitelisted, "Whitelist precedence");
            settings.ipv6 = false; expect(fixture.Check("2001:db8:12::1", settings).status == DetectionStatus.Disabled, "IPv6 switch");
            expect(fixture.Check("not-an-ip", settings).status == DetectionStatus.InvalidAddress, "Invalid address");
            expect(fixture.Check("198.51.100.99", settings).status == DetectionStatus.Incomplete, "Missing feed status");
            settings.customRanges.Add("198.51.100.99");
            expect(fixture.Check("198.51.100.99", settings).activeMatches.Contains(Custom), "Custom range");
            settings.custom = false;
            expect(!fixture.Check("198.51.100.99", settings).Detected, "Custom switch");
            settings.custom = true; settings.whitelist.Add("198.51.100.99");
            expect(fixture.Check("198.51.100.99", settings).status == DetectionStatus.Whitelisted, "Custom whitelist precedence");
            var restored = new NetShieldDatabase { loaded = true };
            restored.Merge(fixture.ExportSnapshot()); restored.Rebuild();
            expect(restored.EntryCount == fixture.EntryCount, "Grouped snapshot count round trip");
            settings.SetProvider("ProtonVPN", true);
            expect(restored.Check("192.0.2.10", settings).activeMatches.Contains("ProtonVPN"), "Grouped snapshot provider round trip");
            expect(restored.Check("198.51.100.10", settings).activeMatches.Contains(Proxy), "Grouped snapshot proxy round trip");
            expect(fixture.ExportGroupedText(settings).Contains("[Whitelist]"), "Grouped text export");
            var rowsFixture = new Snapshot();
            var group = new ProviderGroup { name = "NordVPN" };
            var entries = new SourceRanges { id = "nordvpn", ranges = Enumerable.Range(1, 101).Select(x => "192.0.2." + x).ToList() };
            group.sources.Add(entries); rowsFixture.groups.Add(group);
            string originalJson = JsonUtility.ToJson(rowsFixture, true);
            string compactJson = FormatSnapshotRows(originalJson);
            var rowCounts = compactJson.Split('\n').Select(line => RangeValuePattern.Matches(line).Count).Where(count => count > 0).ToArray();
            expect(rowCounts.SequenceEqual(new[] { 50, 50, 1 }), "50 addresses per JSON row");
            expect(JsonUtility.FromJson<Snapshot>(compactJson).groups[0].sources[0].ranges.SequenceEqual(entries.ranges), "Compact JSON preserves every address");
            expect(FormatSnapshotRows(compactJson) == compactJson, "Compact JSON formatting is idempotent");
            var reportRows = new System.Text.StringBuilder();
            AppendRangeRows(reportRows, entries.ranges, "", "\n");
            expect(reportRows.ToString().Split('\n').Select(line => RangeValuePattern.Matches(line).Count).Where(count => count > 0).SequenceEqual(new[] { 50, 50, 1 }), "50 addresses per export row");
            var imported = ParseImport("[\"192.0.2.10\", \"2001:db8::/32\"]\n192.0.2.10,invalid");
            expect(imported.Count == 2 && imported.Contains("2001:db8::/32"), "Import and deduplication");
            settings.SetSource("providers", false);
            expect(!fixture.Check("192.0.2.10", settings).matches.Contains("ProtonVPN"), "Disabled source excluded");
            settings.SetSource("providers", true);
            expect(fixture.Check("192.0.2.10", settings).matches.Contains("ProtonVPN"), "Reenabled source included");
            return checks + " logic tests passed.";
        }
        public NetShieldResult Check(string ip, NetShieldSettings settings)
        {
            Load(); var result = new NetShieldResult { address = ip, complete = IsComplete(settings) }; IPAddress address;
            if (!settings.enabled) { result.status = DetectionStatus.Disabled; return result; }
            if (!TryAddress(ip, out address)) { result.status = DetectionStatus.InvalidAddress; return result; }
            result.address = address.ToString();
            if ((address.AddressFamily == AddressFamily.InterNetwork && !settings.ipv4) || (address.AddressFamily == AddressFamily.InterNetworkV6 && !settings.ipv6)) { result.status = DetectionStatus.Disabled; return result; }
            if (settings.whitelist.Any(x => Contains(x, result.address))) { result.status = DetectionStatus.Whitelisted; return result; }
            var matches = new HashSet<string>(index.Find(address.GetAddressBytes()).Select(x => x.Split(new[] { '|' }, 2)).Where(x => x.Length == 2 && settings.SourceEnabled(x[0])).Select(x => x[1]), StringComparer.OrdinalIgnoreCase);
            if (settings.customRanges.Any(x => Contains(x, result.address)) || settings.customSources.Any(x => x != null && x.enabled && x.ranges.Any(r => Contains(r, result.address)))) matches.Add(Custom);
            bool named = matches.Any(IsProvider); bool knownVpn = named || matches.Contains(GenericVPN);
            result.matches = matches.OrderBy(x => x).ToArray();
            result.activeMatches = matches.Where(x => x == Custom ? settings.custom : x == GenericVPN ? settings.vpn && settings.unknownVpn && !named : x == Proxy ? settings.proxy : x == Tor ? settings.tor : x == Datacenter ? settings.datacenter && !knownVpn : settings.vpn && settings.ProviderEnabled(x)).OrderBy(x => x).ToArray();
            result.status = result.activeMatches.Length > 0 ? DetectionStatus.Detected : result.complete ? DetectionStatus.NoMatch : DetectionStatus.Incomplete;
            return result;
        }
    }
}
