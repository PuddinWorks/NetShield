using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace NetShield
{
    [InitializeOnLoad]
    internal static class NetShieldThirdPartyEditor
    {
        sealed class Section
        {
            internal string title, body;
            internal readonly string[] links;
            internal bool open;
            internal Section(string title, string body)
            {
                this.title = title; this.body = body;
                var urls = new List<string>();
                foreach (Match match in Regex.Matches(body, @"https://[^\s,)]+"))
                {
                    string url = match.Value.TrimEnd('.', ';');
                    if (!urls.Contains(url)) urls.Add(url);
                }
                links = urls.ToArray();
            }
        }

        static string root;
        static Section[] notices;
        static GUIStyle textStyle, buttonStyle;
        static readonly Dictionary<string, string> wrapped = new Dictionary<string, string>();

        static NetShieldThirdPartyEditor() { EditorApplication.projectChanged += Invalidate; }

        static void Invalidate()
        {
            notices = null;
            wrapped.Clear();
            root = null;
        }

        internal static string Root
        {
            get
            {
                if (!string.IsNullOrEmpty(root) && Directory.Exists(root)) return root;
                foreach (var guid in AssetDatabase.FindAssets("NetShieldThirdPartyEditor t:MonoScript"))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (path.EndsWith("/Editor/NetShieldThirdPartyEditor.cs", StringComparison.Ordinal)) return root = Path.GetDirectoryName(Path.GetDirectoryName(path)).Replace('\\', '/');
                }
                return root = "Assets/NetShield";
            }
        }
        static string Read(string relative)
        {
            try { return File.ReadAllText(Root + "/" + relative).Replace("\r\n", "\n"); }
            catch (Exception) { return "Document unavailable. Reimport NetShield."; }
        }
        static Section[] Notices()
        {
            if (notices != null) return notices;
            var blocks = Regex.Split(Read("Third-Party Notices.txt").Trim(), @"\n[ \t]*\n");
            var sections = new List<Section>();
            for (int i = 0; i < blocks.Length; i++)
            {
                string block = blocks[i].Trim();
                if (block.Length == 0) continue;
                int newline = block.IndexOf('\n');
                string heading = i == 0 ? "Source notice" : i == 1 ? "Before using source data" : block.StartsWith("GitHub raw", StringComparison.Ordinal) ? "Hosting and custom sources" : block.StartsWith("Data is normalized", StringComparison.Ordinal) ? "Redistribution and attribution" : newline > 0 ? block.Substring(0, newline).Split(new[] { " - " }, StringSplitOptions.None)[0] : "Additional notice";
                if (heading == "Joe12387/open-source-vpn-ip-lists") heading = "Open Source VPN IP Lists";
                if (heading == "millshre5/vpnips") heading = "VPNIPs";
                if (heading == "X4BNet/lists_vpn") heading = "X4BNet / IPLocate";
                sections.Add(new Section(heading, block));
            }
            return notices = sections.ToArray();
        }
        internal static float AvailableWidth(float viewWidth)
        {
            return Mathf.Max(80, Mathf.Floor(viewWidth - 58));
        }
        static GUIStyle TextStyle
        {
            get { if (textStyle == null) textStyle = new GUIStyle(EditorStyles.wordWrappedLabel) { richText = false, wordWrap = true, stretchWidth = false }; return textStyle; }
        }
        static string BreakLongWords(string text, float width, GUIStyle style)
        {
            string key = Mathf.FloorToInt(width) + "|" + text;
            string cached;
            if (wrapped.TryGetValue(key, out cached)) return cached;
            var measure = new GUIStyle(style) { wordWrap = false };
            float limit = Mathf.Max(20, width - 12);
            string result = Regex.Replace(text, @"\S+", m =>
            {
                if (measure.CalcSize(new GUIContent(m.Value)).x <= limit) return m.Value;
                var output = new System.Text.StringBuilder();
                var part = new System.Text.StringBuilder();
                foreach (char c in m.Value)
                {
                    if (part.Length > 0 && measure.CalcSize(new GUIContent(part.ToString() + c)).x > limit)
                    {
                        output.Append(part).Append('\n'); part.Length = 0;
                    }
                    part.Append(c);
                }
                return output.Append(part).ToString();
            });
            if (wrapped.Count > 128) wrapped.Clear();
            wrapped[key] = result;
            return result;
        }
        internal static void DrawWrapped(string text, float width)
        {
            var content = new GUIContent(BreakLongWords(text, width, TextStyle));
            float height = Mathf.Max(EditorGUIUtility.singleLineHeight, TextStyle.CalcHeight(content, width));
            EditorGUILayout.LabelField(content, TextStyle, GUILayout.Width(width), GUILayout.Height(height + 2));
        }
        static void DrawSections(Section[] sections, float width)
        {
            foreach (var section in sections)
            {
                var style = EditorStyles.foldout;
                var heading = new GUIContent(section.title, section.title);
                if (style.CalcSize(heading).x > width)
                {
                    var wrappedHeading = new GUIStyle(style) { wordWrap = true, fixedHeight = 0 };
                    float height = Mathf.Max(EditorGUIUtility.singleLineHeight, wrappedHeading.CalcHeight(heading, Mathf.Max(40, width - 16))) + 5;
                    var rect = EditorGUILayout.GetControlRect(false, height, GUILayout.Width(width));
                    section.open = EditorGUI.Foldout(rect, section.open, heading, true, wrappedHeading);
                }
                else
                {
                    var rect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight + 5, GUILayout.Width(width));
                    section.open = EditorGUI.Foldout(rect, section.open, heading, true);
                }
                if (section.open)
                {
                    DrawWrapped(section.body, width);
                    for (int i = 0; i < section.links.Length; i++)
                    {
                        string url = section.links[i];
                        if (Button("Open source link " + (i + 1), width, url)) Application.OpenURL(url);
                    }
                    GUILayout.Space(5);
                }
            }
        }
        static bool Button(string label, float width, string tooltip = "", float minimumHeight = 22)
        {
            if (buttonStyle == null) buttonStyle = new GUIStyle(GUI.skin.button) { wordWrap = true, fixedHeight = 0 };
            var content = new GUIContent(label, tooltip);
            float height = Mathf.Max(minimumHeight, buttonStyle.CalcHeight(content, width) + 4);
            return GUILayout.Button(content, buttonStyle, GUILayout.Width(width), GUILayout.Height(height));
        }
        internal static void DrawDocuments(float width)
        {
            EditorGUILayout.LabelField("Third-party notices", EditorStyles.boldLabel);
            DrawSections(Notices(), width);
            GUILayout.Space(5);
            if (Button("Open full source notices", width)) EditorUtility.RevealInFinder(Root + "/Third-Party Notices.txt");
        }
    }
}
