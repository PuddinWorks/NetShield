using UnityEditor;
using UnityEngine;

namespace NetShield
{
    [CustomEditor(typeof(NetShield))]
    [CanEditMultipleObjects]
    public sealed class NetShieldControllerInspector : Editor
    {
        static string PreferenceKey => "NetShield.ShowControllerSettings." + Application.dataPath;
        internal static bool ShowSettings
        {
            get => EditorPrefs.GetBool(PreferenceKey, false);
            set
            {
                EditorPrefs.SetBool(PreferenceKey, value);
                UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
            }
        }

        void OnEnable()
        {
            foreach (var item in targets)
                if (item is NetShield controller) NetShieldEditorAssets.ApplyControllerIcon(controller);
        }

        public override bool RequiresConstantRepaint() => NetShieldConnectionStatus.IsChecking || NetShieldDatabase.Shared.IsRefreshing;

        public override void OnInspectorGUI()
        {
            GUILayout.Space(4);
            EditorGUILayout.LabelField("Current connection", EditorStyles.boldLabel);
            NetShieldConnectionStatus.Draw(((NetShield)target).settings.offline);
            GUILayout.Space(6);
            if (GUILayout.Button("Open NetShield Config", GUILayout.Height(30)))
                NetShieldWindow.OpenFor((NetShield)target);
            if (!ShowSettings) return;
            GUILayout.Space(6);
            serializedObject.Update();
            DrawPropertiesExcluding(serializedObject, "m_Script");
            serializedObject.ApplyModifiedProperties();
        }
    }
}
