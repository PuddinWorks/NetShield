using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace NetShield
{
    public sealed partial class NetShieldWindow
    {
        [SerializeField] int tab;
        bool showRules, showSources, showManagement, showHistory, showProfiles, showWizard;
        string rangeQuery = "";
        string[] rangeResults = Array.Empty<string>();
        void DrawGeneralExtras(NetShieldSettings s)
        {
            Toggle("Offline mode", s.offline, v => s.offline = v);
            using (new EditorGUI.DisabledScope(s.offline))
            {
                Toggle("Public IP lookup (ipify)", s.publicLookup, v => s.publicLookup = v);
                using (new EditorGUI.DisabledScope(!s.publicLookup))
                {
                    Toggle("Continuous detection", s.continuous, v => s.continuous = v);
                    if (s.continuous)
                    {
                        float interval = Mathf.Clamp(EditorGUILayout.FloatField("Check interval (sec)", s.checkInterval), 5, 86400);
                        if (interval != s.checkInterval) Edit(() => s.checkInterval = interval);
                        Toggle("Respond only on changes", s.onlyOnChange, v => s.onlyOnChange = v);
                    }
                }
            }
            Toggle("Persist between scenes", s.persistent, v => s.persistent = v);
            if (s.persistent && target.transform.parent != null) EditorGUILayout.HelpBox("Persistent managers must be root GameObjects.", MessageType.Info);
            Toggle("Connection history", s.history, v => s.history = v);
            if (s.history)
            {
                int limit = Mathf.Clamp(EditorGUILayout.IntField("History limit", s.historyLimit), 1, 500);
                if (limit != s.historyLimit) Edit(() => s.historyLimit = limit);
            }
        }
        void DrawActionExtras(NetShieldSettings s)
        {
            Toggle("Only trigger once per detection", s.once, v => s.once = v);
            float delay = Mathf.Clamp(EditorGUILayout.FloatField("Action delay (sec)", s.actionDelay), 0, 600);
            if (delay != s.actionDelay) Edit(() => s.actionDelay = delay);
            float cooldown = Mathf.Clamp(EditorGUILayout.FloatField("Cooldown (sec)", s.cooldown), 0, 86400);
            if (cooldown != s.cooldown) Edit(() => s.cooldown = cooldown);
            Toggle("Require fresh database", s.requireFresh, v => s.requireFresh = v);
            Toggle("Respond to incomplete checks", s.failClosed, v => s.failClosed = v);
            var so = new SerializedObject(target); so.Update();
            var props = so.FindProperty("settings");
            EditorGUILayout.PropertyField(props.FindPropertyRelative("additionalActions"), new GUIContent("Additional actions"), true);
            if (s.failClosed) EditorGUILayout.PropertyField(props.FindPropertyRelative("failureActions"), new GUIContent("Failure actions"), true);
            showRules = EditorGUILayout.Foldout(showRules, "Category / provider overrides", true);
            if (showRules)
            {
                EditorGUILayout.LabelField("Specific provider overrides VPN; otherwise default actions apply. Multiple matches combine unique actions.", small);
                var rules = props.FindPropertyRelative("actionRules");
                var categories = new[] { "VPN", NetShieldDatabase.GenericVPN, NetShieldDatabase.Proxy, NetShieldDatabase.Tor, NetShieldDatabase.Datacenter, NetShieldDatabase.Custom, "Simulation" }.Concat(NetShieldDatabase.Shared.Providers).Distinct().ToArray();
                for (int i = 0; i < rules.arraySize; i++)
                {
                    var rule = rules.GetArrayElementAtIndex(i);
                    var match = rule.FindPropertyRelative("match");
                    EditorGUILayout.BeginHorizontal();
                    var choices = categories.Contains(match.stringValue) ? categories : categories.Concat(new[] { match.stringValue }).ToArray();
                    int current = Array.IndexOf(choices, match.stringValue);
                    int next = EditorGUILayout.Popup(Mathf.Max(0, current), choices);
                    if (next != current) match.stringValue = choices[next];
                    bool remove = GUILayout.Button("-", GUILayout.Width(24));
                    EditorGUILayout.EndHorizontal();
                    if (remove) { rules.DeleteArrayElementAtIndex(i); break; }
                    EditorGUILayout.PropertyField(rule.FindPropertyRelative("actions"), true);
                }
                if (GUILayout.Button("Add override"))
                {
                    int i = rules.arraySize++; var rule = rules.GetArrayElementAtIndex(i);
                    rule.FindPropertyRelative("match").stringValue = "VPN";
                    var actions = rule.FindPropertyRelative("actions"); actions.arraySize = 1; actions.GetArrayElementAtIndex(0).enumValueIndex = 0;
                }
            }
            so.ApplyModifiedProperties();
            bool Uses(DetectionAction action) => s.action == action || s.additionalActions.Contains(action) || s.actionRules.Any(r => r != null && r.actions.Contains(action)) || (s.failClosed && s.failureActions.Contains(action));
            so.Update();
            if (Uses(DetectionAction.Objects))
            {
                foreach (var key in new[] { "enableObjects", "disableObjects", "toggleObjects" }) EditorGUILayout.PropertyField(so.FindProperty(key), true);
            }
            if (Uses(DetectionAction.UnityEvent)) EditorGUILayout.PropertyField(so.FindProperty("onAction"), true);
            if (Uses(DetectionAction.LoadScene)) EditorGUILayout.PropertyField(so.FindProperty("settings").FindPropertyRelative("sceneName"), new GUIContent("Scene name / path"));
            so.ApplyModifiedProperties();
        }
        void DrawProfiles(NetShieldSettings s)
        {
            BeginCard("Setup and profiles");
            showWizard = EditorGUILayout.Foldout(showWizard, "Quick setup", true);
            if (showWizard)
            {
                EditorGUILayout.LabelField("Choose a starting configuration. Then update or import your approved IP lists, configure actions and run a test.", wrap);
                if (GUILayout.Button("Local API only")) Edit(() => { s.enabled = true; s.publicLookup = false; s.offline = true; s.action = DetectionAction.DoNothing; s.continuous = false; });
                if (GUILayout.Button("Local connection monitoring")) Edit(() => { s.enabled = true; s.offline = false; s.publicLookup = true; s.checkOnStart = true; s.continuous = true; s.action = DetectionAction.JustLog; });
                if (GUILayout.Button("Test using custom IPs")) Edit(() => { s.enabled = true; s.publicLookup = false; s.custom = true; if (!s.customRanges.Contains("192.0.2.10")) s.customRanges.Add("192.0.2.10"); s.action = DetectionAction.JustLog; });
            }
            showProfiles = EditorGUILayout.Foldout(showProfiles, "Save / load settings profiles", true);
            if (showProfiles)
            {
                EditorGUILayout.LabelField("Profiles include settings and custom IP lists, but not scene objects or UnityEvents. Store them privately if they include sensitive ranges.", small);
                if (GUILayout.Button("Save profile")) FileOperation(() => { var path = EditorUtility.SaveFilePanel("Save profile", "", "NetShield-Profile", "json"); if (path.Length > 0) File.WriteAllText(path, JsonUtility.ToJson(s, true)); });
                if (GUILayout.Button("Load profile")) FileOperation(() => { var path = EditorUtility.OpenFilePanel("Load profile", "", "json"); if (path.Length > 0) { var profile = new NetShieldSettings(); JsonUtility.FromJsonOverwrite(File.ReadAllText(path), profile); Edit(() => target.settings = profile); } });
            }
            EndCard();
        }
        void DrawHistory()
        {
            showHistory = EditorGUILayout.Foldout(showHistory, "Recent checks", true);
            if (!showHistory) return;
            if (GUILayout.Button("Clear history")) target.ClearHistory();
            foreach (var entry in target.History.Reverse()) EditorGUILayout.LabelField(entry, wrap);
        }
        void FileOperation(Action operation)
        {
            try { operation(); } catch (Exception e) { notice = e.Message; }
        }
        void DrawDatabaseExtras(NetShieldDatabase db)
        {
            EditorGUILayout.LabelField("Unique ranges: " + db.UniqueEntryCount.ToString("N0") + " - Named providers: " + db.LoadedProviderCount, small);
            EditorGUILayout.LabelField("Cache: " + (db.CacheBytes / 1024f).ToString("N1") + " KB", small);
            EditorGUILayout.LabelField("Last successful feed update: " + (db.LastUpdated == DateTime.MinValue ? "Never" : db.LastUpdated.ToLocalTime().ToString("g")), small);
            if (target != null)
            {
                var s = target.settings;
                var dates = NetShieldDatabase.Feeds.Where(f => s.SourceEnabled(f.id)).Select(f => db.GetStatus(f.id).updated).ToArray();
                EditorGUILayout.LabelField("Next refresh eligible: " + (dates.Length == 0 ? "No enabled feeds" : dates.Min() == DateTime.MinValue ? "Now - missing feeds" : dates.Min().AddHours(Mathf.Clamp(s.refreshHours, 1, 168)).ToLocalTime().ToString("g")), small);
                showSources = EditorGUILayout.Foldout(showSources, "Custom sources and imports", true);
                if (showSources)
                {
                    EditorGUILayout.LabelField("Use HTTPS URLs you trust. Plain text, CSV and JSON IP tokens are accepted. Custom sources classify as Custom, not as named VPNs.", small);
                    var so = new SerializedObject(target); so.Update();
                    var sources = so.FindProperty("settings").FindPropertyRelative("customSources");
                    for (int i = 0; i < sources.arraySize; i++)
                    {
                        var item = sources.GetArrayElementAtIndex(i);
                        EditorGUILayout.PropertyField(item.FindPropertyRelative("name"));
                        var url = item.FindPropertyRelative("url");
                        EditorGUI.BeginChangeCheck(); EditorGUILayout.PropertyField(url);
                        if (EditorGUI.EndChangeCheck()) { item.FindPropertyRelative("ranges").arraySize = 0; item.FindPropertyRelative("updated").longValue = 0; item.FindPropertyRelative("error").stringValue = ""; }
                        EditorGUILayout.PropertyField(item.FindPropertyRelative("enabled"));
                        EditorGUILayout.LabelField(item.FindPropertyRelative("ranges").arraySize + " cached ranges", small);
                        var error = item.FindPropertyRelative("error").stringValue;
                        if (!string.IsNullOrEmpty(error)) EditorGUILayout.LabelField(error, small);
                        if (GUILayout.Button("Remove source")) { sources.DeleteArrayElementAtIndex(i); break; }
                    }
                    if (GUILayout.Button("Add custom source")) { int i = sources.arraySize++; var item = sources.GetArrayElementAtIndex(i); item.FindPropertyRelative("name").stringValue = "Custom source"; item.FindPropertyRelative("url").stringValue = ""; item.FindPropertyRelative("enabled").boolValue = true; item.FindPropertyRelative("ranges").arraySize = 0; item.FindPropertyRelative("updated").longValue = 0; item.FindPropertyRelative("error").stringValue = ""; }
                    so.ApplyModifiedProperties();
                    if (GUILayout.Button("Import IP list")) FileOperation(() => { var path = EditorUtility.OpenFilePanel("Import TXT, CSV or JSON", "", ""); if (path.Length == 0) return; var ranges = NetShieldDatabase.ParseImport(File.ReadAllText(path)); Edit(() => s.customRanges = s.customRanges.Concat(ranges).Distinct().ToList()); notice = ranges.Count + " valid ranges imported into Custom."; });
                }
            }
            showManagement = EditorGUILayout.Foldout(showManagement, "Manage database", true);
            if (!showManagement) return;
            rangeQuery = EditorGUILayout.TextField(rangeQuery);
            if (GUILayout.Button("Search database")) rangeResults = db.SearchRanges(rangeQuery);
            foreach (var item in rangeResults) EditorGUILayout.SelectableLabel(item, GUILayout.Height(EditorGUIUtility.singleLineHeight));
            if (rangeResults.Length == 100) EditorGUILayout.LabelField("Showing up to 100 results.", small);
            using (new EditorGUI.DisabledScope(db.IsRefreshing || EditorApplication.isPlayingOrWillChangePlaymode))
            {
                if (GUILayout.Button("Backup database")) FileOperation(() => { var path = EditorUtility.SaveFilePanel("Backup database", "", "NetShield-Backup", "json"); if (path.Length > 0) File.WriteAllText(path, db.ExportSnapshot()); });
                if (GUILayout.Button("Restore database")) FileOperation(() => { var path = EditorUtility.OpenFilePanel("Restore database", "", "json"); if (path.Length > 0) db.RestoreSnapshot(File.ReadAllText(path)); });
                if (GUILayout.Button("Clear downloaded cache") && EditorUtility.DisplayDialog("Clear cache", "Remove downloaded database data? Custom settings and build snapshots are retained.", "Clear", "Cancel")) FileOperation(db.ClearCache);
                if (GUILayout.Button("Remove build snapshot") && EditorUtility.DisplayDialog("Remove snapshot", "Remove the bundled database from future builds?", "Remove", "Cancel")) AssetDatabase.DeleteAsset(root + "/Runtime/Resources/NetShieldDatabase.txt");
            }
        }
        void Code(string heading, string description, string code)
        {
            BeginCard(heading);
            EditorGUILayout.LabelField(description, wrap);
            var style = new GUIStyle(EditorStyles.textArea) { wordWrap = true, fontSize = 11, padding = new RectOffset(8, 8, 8, 8) };
            var height = Mathf.Max(48, style.CalcHeight(new GUIContent(code), Mathf.Max(80, position.width - 58)));
            EditorGUILayout.SelectableLabel(code, style, GUILayout.Height(height + 8));
            if (GUILayout.Button("Copy code", EditorStyles.miniButton)) EditorGUIUtility.systemCopyBuffer = code;
            EndCard();
        }
        void DrawApi()
        {
            EditorGUILayout.LabelField("Developer API", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Supply an IP from your own game, backend or trusted transport. No networking integration is required. Checks run locally against the loaded database. Call from Unity's main thread.", wrap);
            Code("Check an IP", "Synchronous, no network requests or actions.", "using NetShield;\n\nvar result = NetShieldAPI.CheckIP(\"192.0.2.10\");\nif (result.Detected)\n    UnityEngine.Debug.Log(string.Join(\", \", result.activeMatches));");
            Code("Refresh and check", "Optional database refresh; does not send the supplied IP to a lookup service.", "var result = await NetShieldAPI.CheckIPAsync(playerIp);\nif (!result.complete)\n    return;\nif (result.Detected)\n    HandleDetection(result);");
            Code("Use manager settings", "InspectAddress never executes actions. CheckAddressAndApply explicitly invokes local actions in Play Mode.", "global::NetShield.NetShield manager;\n\nvar result = manager.InspectAddress(playerIp);\nvar applied = manager.CheckAddressAndApply(playerIp);");
            Code("Offline check", "Disable updates and discovery. Uses cached or imported data.", "var settings = new NetShieldSettings { offline = true };\nvar result = NetShieldAPI.CheckIP(playerIp, settings);");
            Code("Events", "Subscribe to the manager's result events or use the serialized UnityEvents.", "manager.Detected += HandleDetection;\nmanager.Checked += HandleCheck;\n\nmanager.Detected -= HandleDetection;\nmanager.Checked -= HandleCheck;");
            BeginCard("Result fields");
            EditorGUILayout.LabelField("status - Disabled, NoMatch, Detected, Incomplete, InvalidAddress, Whitelisted\ncomplete - Required source freshness\naddress - Normalized checked IP\nmatches - All enabled-source matches\nactiveMatches - Matches after detection switches\nDetected - True when a detection rule matched", wrap);
            if (GUILayout.Button("Open complete API documentation")) EditorUtility.RevealInFinder(root + "/Documentation/API.md");
            EndCard();
        }
        bool showProviderLabels;
        void DrawThirdParty()
        {
            float width = NetShieldThirdPartyEditor.AvailableWidth(position.width);
            BeginCard("Third-party sources");
            NetShieldThirdPartyEditor.DrawDocuments(width);
            GUILayout.Space(6);
            showProviderLabels = EditorGUILayout.Foldout(showProviderLabels, "VPN provider labels", true);
            if (showProviderLabels)
            {
                NetShieldThirdPartyEditor.DrawWrapped(string.Join(", ", NetShieldDatabase.Shared.Providers), width);
                NetShieldThirdPartyEditor.DrawWrapped("Names identify networks only; no endorsement or affiliation is implied. Coverage varies.", width);
            }
            EndCard();
        }
    }
}
