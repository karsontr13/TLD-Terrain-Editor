using System.Collections;
using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(LongDarkTerrain.TerrainEditorMod), "Terrain Editor", "1.0.0", "Hamsi Buglama")]
[assembly: MelonGame("Hinterland", "TheLongDark")]
[assembly: MelonOptionalDependencies("ModSettings")]

namespace LongDarkTerrain
{
    internal enum EditorMode { Off, Terrain, Objects }

    public class TerrainEditorMod : MelonMod
    {
        internal static MelonLogger.Instance Log;
        internal static MelonPreferences_Entry<bool> s_Enabled;
        internal static MelonPreferences_Entry<bool> s_Verbose;

        internal static string Scene { get; private set; }

        internal static EditorMode Mode { get; private set; }

        public override void OnInitializeMelon()
        {
            Log = LoggerInstance;
            var cat = MelonPreferences.CreateCategory("TerrainEditor");
            s_Enabled = cat.CreateEntry("Enabled", true, "Enabled");
            Keys.Create(cat);
            s_Verbose = cat.CreateEntry("Verbose", false, "Verbose logging");
            SettingsRegistration.TryRegister();
            MelonPreferences.Save();
            Log.Msg($"Terrain Editor loaded. Edits are kept in {StrokeStore.Folder}");
        }

        public override void OnSceneWasInitialized(int buildIndex, string sceneName)
        {
            var active = Il2Cpp.GameManager.m_ActiveScene;
            if (string.IsNullOrEmpty(active) || sceneName != active) return;
            if (sceneName.StartsWith("MainMenu") || sceneName == "Empty" || sceneName == "Boot") { Scene = null; return; }
            Scene = sceneName;
            InstancedHider.Clear();
            if (s_Enabled.Value) MelonCoroutines.Start(ReplayWhenReady(sceneName));
        }

        public override void OnSceneWasUnloaded(int buildIndex, string sceneName)
        {
            if (sceneName != Scene) return;
            Scene = null;
            LeaveEditors();
        }

        static IEnumerator ReplayWhenReady(string scene)
        {
            for (int i = 0; i < 300 && Terrain.activeTerrains.Length == 0; i++) yield return null;
            yield return null;
            yield return null;
            if (scene != Scene) yield break;
            try { StrokeStore.Replay(scene); }
            catch (System.Exception e) { Log.Warning($"Replaying the terrain edits failed: {e.Message}"); }
            try
            {
                ObjectEdits.ApplyScene(scene);
                InstancedHider.Apply();
            }
            catch (System.Exception e) { Log.Warning($"Applying the object edits failed: {e.Message}"); }
        }

        public override void OnUpdate()
        {
            if (!s_Enabled.Value) return;
            try
            {
                ObjectEdits.Tick();
                if (Scene != null) InstancedHider.Update();
            }
            catch (System.Exception e) { Log.Warning($"Object edits: {e.Message}"); }

            if (Keys.Toggle)
            {
                LeaveEditors();
                Mode = Mode switch { EditorMode.Off => EditorMode.Terrain, EditorMode.Terrain => EditorMode.Objects, _ => EditorMode.Off };
                Hud.Message(Mode switch
                {
                    EditorMode.Terrain => L.T("Arazi düzenleyici", "Terrain editor"),
                    EditorMode.Objects => L.T("Nesne düzenleyici", "Object editor"),
                    _ => L.T("Düzenleyiciler kapalı", "Editors off"),
                });
            }
            if (Mode == EditorMode.Off || Scene == null) return;
            try
            {
                if (Mode == EditorMode.Terrain) BrushEditor.Update(Scene);
                else ObjectEditor.Update();
            }
            catch (System.Exception e) { Log.Warning($"Editor: {e.Message}"); }
        }

        public override void OnGUI()
        {
            if (!s_Enabled.Value || Scene == null) return;
            if (Mode == EditorMode.Terrain) BrushEditor.OnGUI();
            else if (Mode == EditorMode.Objects) ObjectEditor.OnGUI();
        }

        static void LeaveEditors()
        {
            try
            {
                if (Mode == EditorMode.Terrain) BrushEditor.Leave();
                if (Mode == EditorMode.Objects) ObjectEditor.Leave();
            }
            catch (System.Exception e) { Log.Warning($"Leaving the editor: {e.Message}"); }
        }

        internal static void Debug(string msg)
        {
            if (s_Verbose != null && s_Verbose.Value) Log.Msg(msg);
        }
    }
}
