using System;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace NetShield
{
    internal static class NetShieldConnectionStatus
    {
        static bool checking;
        static int generation;
        internal static bool IsChecking => checking;
        static string ipv4, ipv6;
        static DateTime updated;
        static readonly System.Collections.Generic.Dictionary<string, NetShieldResult> previewResults = new System.Collections.Generic.Dictionary<string, NetShieldResult>();
        static int previewRevision = -1;
        static double nextPreviewRefresh;
        static readonly NetShieldSettings VpnLookup = new NetShieldSettings { proxy = false, tor = false, datacenter = false, custom = false, autoUpdate = false };

        internal static void Reset() { generation++; ipv4 = null; ipv6 = null; updated = default; checking = false; previewResults.Clear(); }

        static async Task<string> Fetch(string url, AddressFamily family)
        {
            try
            {
                string response = await NetShieldDatabase.GetTextAsync(url, 12);
                if (NetShieldDatabase.TryAddress(response, out var address) && address.AddressFamily == family) return address.ToString();
            }
            catch { }
            return null;
        }

        static async void Refresh()
        {
            if (checking) return;
            checking = true;
            int requestGeneration = generation;
            try
            {
                var results = await Task.WhenAll(Fetch("https://api.ipify.org", AddressFamily.InterNetwork), Fetch("https://api6.ipify.org", AddressFamily.InterNetworkV6));
                if (requestGeneration == generation) { ipv4 = results[0]; ipv6 = results[1]; updated = DateTime.Now; previewResults.Clear(); }
            }
            finally
            {
                if (requestGeneration == generation) checking = false;
                UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
            }
        }

        internal static void Draw(bool offline = false)
        {
            if (offline) { EditorGUILayout.LabelField("Offline mode - public IP lookup disabled", EditorStyles.wordWrappedMiniLabel); return; }
            var db = NetShieldDatabase.Shared;
            db.Load();
            if (previewRevision != db.Revision || EditorApplication.timeSinceStartup >= nextPreviewRefresh)
            {
                previewResults.Clear();
                previewRevision = db.Revision;
                nextPreviewRefresh = EditorApplication.timeSinceStartup + 10;
            }
            DrawAddress(db, "IPv4", ipv4);
            DrawAddress(db, "IPv6", ipv6);
            using (new EditorGUI.DisabledScope(checking))
                if (GUILayout.Button(checking ? "Checking IP..." : "Check public IP (ipify)", GUILayout.Height(23))) Refresh();
            if (updated != default(DateTime)) EditorGUILayout.LabelField("Checked " + updated.ToString("HH:mm:ss"), EditorStyles.miniLabel);
            EditorGUILayout.LabelField("Public IP of this editor - preview only", EditorStyles.miniLabel);
        }

        static void DrawAddress(NetShieldDatabase db, string family, string address)
        {
            EditorGUILayout.BeginVertical();
            EditorGUILayout.LabelField(family, EditorStyles.boldLabel);
            if (string.IsNullOrEmpty(address))
            {
                EditorGUILayout.LabelField(checking ? "Checking..." : updated == default(DateTime) ? "Not checked" : "Unavailable", EditorStyles.miniLabel);
            }
            else
            {
                EditorGUILayout.SelectableLabel(address, EditorStyles.textField, GUILayout.Height(EditorGUIUtility.singleLineHeight));
                if (!previewResults.TryGetValue(address, out var result))
                {
                    result = db.Check(address, VpnLookup);
                    previewResults[address] = result;
                }
                var providers = result.matches.Where(NetShieldDatabase.IsProvider).ToArray();
                bool vpn = providers.Length > 0 || result.matches.Contains(NetShieldDatabase.GenericVPN);
                EditorGUILayout.LabelField("VPN: " + (vpn ? "Detected" : result.complete ? "Not detected" : "Unknown - incomplete database"), EditorStyles.wordWrappedLabel);
                if (vpn) EditorGUILayout.LabelField("Provider: " + (providers.Length > 0 ? string.Join(", ", providers) : "Unidentified VPN"), EditorStyles.wordWrappedLabel);
                else EditorGUILayout.LabelField("Provider: " + (result.complete ? "None identified" : "Unknown"), EditorStyles.miniLabel);
                if (result.matches.Contains(NetShieldDatabase.Proxy)) EditorGUILayout.LabelField("Proxy - Detected", EditorStyles.miniLabel);
                if (result.matches.Contains(NetShieldDatabase.Tor)) EditorGUILayout.LabelField("Tor - Detected", EditorStyles.miniLabel);
                if (vpn && !result.complete) EditorGUILayout.LabelField("Some VPN sources are missing or stale.", EditorStyles.wordWrappedMiniLabel);
            }
            EditorGUILayout.EndVertical();
        }
    }
}
