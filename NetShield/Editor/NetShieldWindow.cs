using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NetShield
{

    public sealed partial class NetShieldWindow : EditorWindow
    {
        [SerializeField] NetShield target;
        [SerializeField] bool showProviders, showDatabase, showAdvanced;
        [SerializeField] string search = "", testIp = "";
        Vector2 scroll;
        NetShieldResult testResult;
        Texture2D icon, logo, darkLogo;
        string root;
        GUIStyle headerStyle, small, section, card, wrap, contentStyle, switchStyle;
        int lastDatabaseRevision = -1;
        double nextPlayRepaint;
        bool refreshing;
        string notice;
        static readonly Color Purple = new Color(0.43f, 0.23f, 0.78f);
        static readonly string[] ActionNames = { "Do Nothing", "Just Log", "Objects", "Quit Game", "Unity Event", "Load Scene" };

        [MenuItem("Tools/NetShield")]
        public static void Open()
        {
            OpenFor(null);
        }
        internal static void OpenFor(NetShield controller)
        {
            var inspector = typeof(Editor).Assembly.GetType("UnityEditor.InspectorWindow");
            var window = inspector == null ? GetWindow<NetShieldWindow>("NetShield") : GetWindow<NetShieldWindow>("NetShield", inspector);
            window.minSize = new Vector2(270, 280);
            if (controller != null) window.target = controller;
            window.Show(); window.Focus(); window.Repaint();
        }
        void OnEnable()
        {
            var script = MonoScript.FromScriptableObject(this);
            var scriptPath = AssetDatabase.GetAssetPath(script);
            root = string.IsNullOrEmpty(scriptPath) ? "Assets/NetShield" : Path.GetDirectoryName(Path.GetDirectoryName(scriptPath)).Replace('\\', '/');
            icon = AssetDatabase.LoadAssetAtPath<Texture2D>(root + "/Editor/Art/NetShieldIcon.png");
            logo = AssetDatabase.LoadAssetAtPath<Texture2D>(root + "/Editor/Art/NetShieldLogo.png");
            darkLogo = AssetDatabase.LoadAssetAtPath<Texture2D>(root + "/Editor/Art/NetShieldLogoDark.png");
            titleContent = new GUIContent("NetShield", icon);
            Selection.selectionChanged += SelectionChanged;
            EditorApplication.playModeStateChanged += PlayStateChanged;
            SelectionChanged();
        }
        void OnDisable() { Selection.selectionChanged -= SelectionChanged; EditorApplication.playModeStateChanged -= PlayStateChanged; }
        void PlayStateChanged(PlayModeStateChange state) { Repaint(); }
        void OnInspectorUpdate()
        {
            if (tab != 0) return;
            var db = NetShieldDatabase.Shared;
            if (refreshing || db.IsRefreshing || NetShieldConnectionStatus.IsChecking || lastDatabaseRevision != db.Revision || (EditorApplication.isPlaying && EditorApplication.timeSinceStartup >= nextPlayRepaint))
            {
                lastDatabaseRevision = db.Revision;
                nextPlayRepaint = EditorApplication.timeSinceStartup + 0.5;
                Repaint();
            }
        }
        void SelectionChanged()
        {
            if (Selection.activeGameObject != null && Selection.activeGameObject.scene.IsValid())
            {
                var item = Selection.activeGameObject.GetComponent<NetShield>(); if (item != null) target = item;
            }
            if (target == null) target = FindSceneTarget(); Repaint();
        }
        static NetShield FindSceneTarget()
        {
            var scene = SceneManager.GetActiveScene(); if (!scene.IsValid() || !scene.isLoaded) return null;
            foreach (var go in scene.GetRootGameObjects()) { var item = go.GetComponentInChildren<NetShield>(true); if (item != null) return item; }
            return null;
        }
        void Styles()
        {
            headerStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 22, alignment = TextAnchor.MiddleCenter };
            small = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true };
            section = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12, margin = new RectOffset(0, 0, 4, 6) };
            card = new GUIStyle(GUIStyle.none) { padding = new RectOffset(3, 3, 6, 8), margin = new RectOffset(0, 0, 2, 2) };
            wrap = new GUIStyle(EditorStyles.wordWrappedLabel) { fontSize = 11 };
            contentStyle = new GUIStyle(GUIStyle.none) { padding = new RectOffset(12, 12, 0, 0) };
            switchStyle = new GUIStyle(EditorStyles.label) { wordWrap = true };
        }
        void OnGUI()
        {
            if (card == null) Styles();
            float oldLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = Mathf.Clamp(position.width * 0.42f, 76, 160);
            try { DrawWindow(); }
            finally { EditorGUIUtility.labelWidth = oldLabelWidth; }
        }
        void DrawWindow()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll, false, false);
            EditorGUILayout.BeginVertical(contentStyle, GUILayout.ExpandWidth(true));
            GUILayout.Space(12);
            var activeLogo = EditorGUIUtility.isProSkin && darkLogo != null ? darkLogo : logo;
            float logoWidth = Mathf.Clamp(position.width - 44, 100, 460);
            float logoHeight = activeLogo != null ? logoWidth * activeLogo.height / activeLogo.width : 50;
            var header = GUILayoutUtility.GetRect(1, logoHeight, GUILayout.ExpandWidth(true));
            float fittedWidth = Mathf.Min(header.width, logoWidth);
            var logoRect = new Rect(header.center.x - fittedWidth * .5f, header.y, fittedWidth, header.height);
            if (activeLogo != null) GUI.DrawTexture(logoRect, activeLogo, ScaleMode.ScaleToFit, true);
            else GUI.Label(logoRect, "NetShield", headerStyle);
            GUILayout.Space(8);
            
            int newTab = GUILayout.Toolbar(tab, new[] { "Config", "API", "Third-Party" }, GUILayout.Height(28));
            if (newTab != tab) { tab = newTab; scroll = Vector2.zero; }
            GUILayout.Space(7);
            if (tab == 1) { DrawApi(); FinishWindow(); return; }
            if (tab == 2) { DrawThirdParty(); FinishWindow(); return; }
            var db = NetShieldDatabase.Shared;
            db.Load();
            if (position.width < 350)
            {
                EditorGUILayout.LabelField("NetShield Manager");
                target = (NetShield)EditorGUILayout.ObjectField(target, typeof(NetShield), true);
            }
            else
            {
                EditorGUILayout.BeginHorizontal(); EditorGUILayout.LabelField("NetShield Manager", GUILayout.Width(125));
                target = (NetShield)EditorGUILayout.ObjectField(target, typeof(NetShield), true);
                EditorGUILayout.EndHorizontal();
            }
            GUILayout.Space(6);
            if (target != null && !target.gameObject.scene.IsValid()) target = null;
            BeginCard("Current connection");
            NetShieldConnectionStatus.Draw(target != null && target.settings.offline);
            EndCard();
            if (target == null)
            {
                EditorGUILayout.HelpBox("Add a NetShield Manager to this scene to configure detection and object actions.", MessageType.Info);
                using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
                    if (GUILayout.Button("Add NetShield to scene", GUILayout.Height(29))) CreateController();
            }
            else DrawSettings(db);
            DrawDatabase(db);
            DrawTester(db);
            DrawEditorOptions();
            FinishWindow();
        }
        void FinishWindow()
        {
            if (!string.IsNullOrEmpty(notice)) EditorGUILayout.HelpBox(notice, MessageType.Info);
            GUILayout.Space(8);
            EditorGUILayout.LabelField("NetShield - PuddinWorks", small);
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndScrollView();
        }
        void CreateController()
        {
            var go = new GameObject("NetShield Manager"); Undo.RegisterCreatedObjectUndo(go, "Create NetShield"); target = Undo.AddComponent<NetShield>(go); NetShieldEditorAssets.ApplyControllerIcon(target); Selection.activeGameObject = go; EditorSceneManager.MarkSceneDirty(go.scene);
        }
        void Edit(Action change)
        {
            Undo.RecordObject(target, "NetShield Settings"); change(); EditorUtility.SetDirty(target);
            PrefabUtility.RecordPrefabInstancePropertyModifications(target);
            if (!Application.isPlaying && target.gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(target.gameObject.scene);
        }
        bool Switch(string label, bool value)
        {
            float labelSpace = Mathf.Max(50, position.width - 88);
            float height = Mathf.Max(23, switchStyle.CalcHeight(new GUIContent(label), labelSpace) + 6);
            Rect r = EditorGUILayout.GetControlRect(false, height);
            Rect track = new Rect(r.xMax - 33, r.center.y - 7.5f, 31, 15);
            EditorGUI.LabelField(new Rect(r.x, r.y, Mathf.Max(0, r.width - 40), r.height), label, switchStyle);
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(track, value ? Purple : (EditorGUIUtility.isProSkin ? new Color(.30f,.30f,.32f) : new Color(.62f,.62f,.65f)));
                EditorGUI.DrawRect(new Rect(track.x + (value ? 17 : 2), track.y + 2, 12, 11), Color.white);
            }
            if (GUI.Button(r, GUIContent.none, GUIStyle.none)) return !value;
            return value;
        }
        void Toggle(string label, bool value, Action<bool> setter) { bool next = Switch(label, value); if (next != value) Edit(() => setter(next)); }
        void BeginCard(string label)
        {
            var line = EditorGUILayout.GetControlRect(false, 1);
            EditorGUI.DrawRect(line, EditorGUIUtility.isProSkin ? new Color(.28f, .28f, .28f) : new Color(.68f, .68f, .68f));
            EditorGUILayout.BeginVertical(card);
            EditorGUILayout.LabelField(label, section);
        }
        void DrawEditorOptions()
        {
            BeginCard("Editor");
            bool show = NetShieldControllerInspector.ShowSettings;
            bool next = Switch("Show settings in Inspector", show);
            if (next != show) NetShieldControllerInspector.ShowSettings = next;
            EditorGUILayout.LabelField("Keep the manager Inspector compact, or expose its serialized fields for advanced editing.", small);
            EndCard();
        }
        void EndCard() { EditorGUILayout.EndVertical(); }
        void DrawSettings(NetShieldDatabase db)
        {
            var s = target.settings;
            BeginCard("General");
            Toggle("Enable NetShield", s.enabled, v => s.enabled = v);
            DrawGeneralExtras(s);
            Toggle("Check when game starts", s.checkOnStart, v => s.checkOnStart = v);
            Toggle("IPv4", s.ipv4, v => s.ipv4 = v); Toggle("IPv6", s.ipv6, v => s.ipv6 = v);
            EndCard();
            BeginCard("Detection");
            Toggle("VPN providers", s.vpn, v => s.vpn = v);
            using (new EditorGUI.DisabledScope(!s.vpn))
            {
                Toggle("Other / unidentified VPNs", s.unknownVpn, v => s.unknownVpn = v);
                showProviders = EditorGUILayout.Foldout(showProviders, "Individual VPN providers", true);
                if (showProviders)
                {
                    search = EditorGUILayout.TextField(search, EditorStyles.toolbarSearchField);
                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button("Enable all", EditorStyles.miniButtonLeft)) Edit(() => s.disabledProviders.Clear());
                    if (GUILayout.Button("Disable all", EditorStyles.miniButtonRight)) Edit(() => { foreach (var name in db.Providers) s.SetProvider(name, false); });
                    EditorGUILayout.EndHorizontal();
                    foreach (string name in db.Providers.Where(x => x.IndexOf(search ?? "", StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        string label = name == "PIA" ? "PIA (Private Internet Access)" : name;
                        Toggle(label, s.ProviderEnabled(name), v => s.SetProvider(name, v));
                    }
                }
            }
            Toggle("Public proxies", s.proxy, v => s.proxy = v);
            Toggle("Tor exit nodes", s.tor, v => s.tor = v);
            Toggle("Hosting / datacenters", s.datacenter, v => s.datacenter = v);
            Toggle("Custom IP ranges", s.custom, v => s.custom = v);
            if (s.datacenter) EditorGUILayout.LabelField("Broad ranges can match legitimate hosting users.", small);
            EndCard();
            BeginCard("When detected");
            int next = EditorGUILayout.Popup((int)s.action, ActionNames);
            if (next != (int)s.action) Edit(() => s.action = (DetectionAction)next);
            switch (s.action)
            {
                case DetectionAction.DoNothing: EditorGUILayout.LabelField("Return the result and invoke detection events only.", wrap); break;
                case DetectionAction.JustLog: EditorGUILayout.LabelField("Write the matched categories to the Unity Console.", wrap); break;
                case DetectionAction.Objects: EditorGUILayout.LabelField("Configure target objects below.", wrap); break;
                case DetectionAction.UnityEvent: EditorGUILayout.LabelField("Invokes On Action below.", wrap); break;
                case DetectionAction.LoadScene: EditorGUILayout.LabelField("Loads a scene included in your build.", wrap); break;
                case DetectionAction.QuitGame: EditorGUILayout.HelpBox("Exits the player or stops Editor Play Mode. WebGL cannot close the browser.", MessageType.Warning); break;
            }
            DrawActionExtras(s);
            EndCard();
            BeginCard("Advanced");
            showAdvanced = EditorGUILayout.Foldout(showAdvanced, "Custom IPs, whitelist and events", true);
            if (showAdvanced)
            {
                var serialized = new SerializedObject(target); serialized.Update();
                EditorGUILayout.PropertyField(serialized.FindProperty("settings").FindPropertyRelative("customRanges"), new GUIContent("Custom IP / CIDR ranges"), true);
                EditorGUILayout.PropertyField(serialized.FindProperty("settings").FindPropertyRelative("whitelist"), new GUIContent("IP / CIDR whitelist"), true);
                EditorGUILayout.PropertyField(serialized.FindProperty("onChecked"), true);
                EditorGUILayout.PropertyField(serialized.FindProperty("onDetected"), true);
                EditorGUILayout.PropertyField(serialized.FindProperty("onCleared"), true);
                EditorGUILayout.PropertyField(serialized.FindProperty("onFailed"), true);
                serialized.ApplyModifiedProperties();
            }
            EndCard();
            DrawProfiles(s);
            if (EditorApplication.isPlaying)
            {
                BeginCard("Play Mode");
                EditorGUILayout.LabelField(target.IsChecking ? "Checking connection…" : target.LastResult == null ? "No check yet" : target.LastResult.ToString(), wrap);
                using (new EditorGUI.DisabledScope(target.IsChecking || !s.enabled || !s.publicLookup || s.offline)) if (GUILayout.Button("Check connection")) target.CheckConnection();
                if (GUILayout.Button("Simulate detection") && EditorUtility.DisplayDialog("Simulate detection", "Run the configured action now?", "Run", "Cancel")) target.SimulateDetection();
                DrawHistory();
                EndCard();
            }
        }
        void DrawDatabase(NetShieldDatabase db)
        {
            BeginCard("IP database");
            EditorGUILayout.LabelField(db.EntryCount == 0 ? "No IP database yet" : db.EntryCount.ToString("N0") + " source entries loaded", EditorStyles.boldLabel);
            if (target != null)
            {
                var s = target.settings;
                Toggle("Auto-update on check", s.autoUpdate, v => s.autoUpdate = v);
                int hours = EditorGUILayout.IntSlider("Refresh (hours)", s.refreshHours, 1, 168); if (hours != s.refreshHours) Edit(() => s.refreshHours = hours);
                int timeout = EditorGUILayout.IntSlider("Timeout (sec)", s.timeoutSeconds, 3, 60); if (timeout != s.timeoutSeconds) Edit(() => s.timeoutSeconds = timeout);
                EditorGUILayout.LabelField(db.IsComplete(s) ? "Enabled feeds are current" : "Some enabled feeds are missing or stale", small);
            }
            DrawDatabaseExtras(db);
            using (new EditorGUI.DisabledScope(refreshing || db.IsRefreshing || (target != null && (target.settings.offline || !target.settings.enabled))))
            {
                if (GUILayout.Button(refreshing ? "Updating…" : "Update database now", GUILayout.Height(26))) Refresh(db);
            }
            using (new EditorGUI.DisabledScope(db.EntryCount == 0 || refreshing || db.IsRefreshing || EditorApplication.isPlayingOrWillChangePlaymode))
                if (GUILayout.Button("Include snapshot in build")) Bake(db);
            if (GUILayout.Button("Export grouped IP lists"))
            {
                string path = EditorUtility.SaveFilePanel("Export grouped IP lists", "", "NetShield-IP-lists", "txt");
                if (!string.IsNullOrEmpty(path))
                {
                    try { File.WriteAllText(path, db.ExportGroupedText(target == null ? new NetShieldSettings() : target.settings)); notice = "Grouped IP lists exported."; }
                    catch (Exception e) { notice = e.Message; }
                }
            }
            EditorGUILayout.LabelField("Downloads public lists, caches them locally and can bundle a snapshot for offline checks.", small);
            showDatabase = EditorGUILayout.Foldout(showDatabase, "Source status", true);
            if (showDatabase)
            {
                foreach (var feed in NetShieldDatabase.Feeds)
                {
                    if (target != null) Toggle(feed.label, target.settings.SourceEnabled(feed.id), v => target.settings.SetSource(feed.id, v));
                    var status = db.GetStatus(feed.id);
                    if (target == null) EditorGUILayout.LabelField(feed.label, EditorStyles.boldLabel);
                    EditorGUILayout.LabelField(status.count == 0 ? "Not downloaded" : status.count.ToString("N0") + " entries - " + status.updated.ToLocalTime().ToString("g"), small);
                    if (!string.IsNullOrEmpty(status.error)) EditorGUILayout.LabelField(status.error, small);
                }
                if (!string.IsNullOrEmpty(db.CacheError)) EditorGUILayout.HelpBox(db.CacheError, MessageType.Warning);
            }
            EndCard();
        }
        async void Refresh(NetShieldDatabase db)
        {
            refreshing = true; notice = null; Repaint();
            try { var manager = target; await db.RefreshAsync(manager == null ? new NetShieldSettings() : manager.settings, true); if (manager != null && !Application.isPlaying) { EditorUtility.SetDirty(manager); EditorSceneManager.MarkSceneDirty(manager.gameObject.scene); } if (this != null) notice = "Update finished. Review source status for failed or missing feeds."; }
            catch (Exception e) { if (this != null) notice = e.Message; }
            finally { if (this != null) { refreshing = false; Repaint(); } UnityEditorInternal.InternalEditorUtility.RepaintAllViews(); }
        }
        void Bake(NetShieldDatabase db)
        {
            if (!EditorUtility.DisplayDialog("Redistribution obligations", "Only include data you have permission to redistribute. Review Third-Party notices and license obligations first. This does not grant redistribution rights.", "I have verified rights", "Cancel")) return;
            try
            {
                string directory = root + "/Runtime/Resources"; Directory.CreateDirectory(directory); File.WriteAllText(directory + "/NetShieldDatabase.txt", db.ExportSnapshot()); AssetDatabase.Refresh(); notice = "Database snapshot included in builds. See Documentation/THIRD_PARTY.md before redistribution.";
            }
            catch (Exception e) { notice = e.Message; }
        }
        void DrawTester(NetShieldDatabase db)
        {
            BeginCard("Test an IP");
            testIp = EditorGUILayout.TextField(testIp);
            if (GUILayout.Button("Check IP address")) testResult = db.Check(testIp, target == null ? new NetShieldSettings() : target.settings);
            if (testResult != null)
            {
                EditorGUILayout.LabelField(testResult.ToString(), wrap);
                if (testResult.matches.Length > 0) EditorGUILayout.LabelField("All matches: " + string.Join(", ", testResult.matches), wrap);
                if (!testResult.complete) EditorGUILayout.LabelField("Database coverage is incomplete or stale.", small);
            }
            EditorGUILayout.LabelField("Preview only. No actions are executed.", small);
            if (GUILayout.Button("Run built-in logic tests"))
            {
                try { notice = NetShieldDatabase.RunSelfTests(); } catch (Exception e) { notice = "Test failed: " + e.Message; }
            }
            EndCard();
        }
    }
}
