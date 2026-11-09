using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using MelonLoader.Utils;

namespace LongDarkTerrain
{
    internal sealed class Stroke
    {
        public int Mode { get; set; }
        public List<float[]> Dabs { get; set; } = new List<float[]>();
    }

    internal sealed class StrokeFile
    {
        public int Version { get; set; } = 1;
        public List<Stroke> Strokes { get; set; } = new List<Stroke>();
    }

    internal static class StrokeStore
    {
        static readonly JsonSerializerOptions s_Json = new JsonSerializerOptions { WriteIndented = false };
        const string Unsaved = "_unsaved";
        static string s_Scene, s_Slot;
        static StrokeFile s_File = new StrokeFile();

        public static string Folder => Path.Combine(MelonEnvironment.UserDataDirectory, "TerrainEditor");

        public static string CurrentSaveName()
        {
            try { return Il2Cpp.SaveGameSystem.GetCurrentSaveName(); }
            catch { return null; }
        }

        static string FolderName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return Unsaved;
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name;
        }

        public static string Slot => FolderName(CurrentSaveName());

        public static void Discard(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            try
            {
                string dir = Path.Combine(Folder, FolderName(name));
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, true);
                    TerrainEditorMod.Log.Msg($"The edits of save \"{name}\" were removed.");
                }
            }
            catch (System.Exception e) { TerrainEditorMod.Log.Warning($"Removing the edits of save \"{name}\" failed: {e.Message}"); }
            s_Scene = null;
            s_File = new StrokeFile();
        }

        public static string SlotFolder => Path.Combine(Folder, Slot);

        public static bool SlotKnown => Slot != Unsaved;

        static string PathFor(string scene) => Path.Combine(SlotFolder, scene + ".json");

        public static StrokeFile For(string scene)
        {
            string slot = Slot;
            if (scene == s_Scene && (s_Slot == slot || s_Slot == Unsaved)) { s_Slot = slot; return s_File; }
            s_Scene = scene;
            s_Slot = slot;
            s_File = new StrokeFile();
            var path = PathFor(scene);
            if (SlotKnown && File.Exists(path))
            {
                try { s_File = JsonSerializer.Deserialize<StrokeFile>(File.ReadAllText(path), s_Json) ?? new StrokeFile(); }
                catch (System.Exception e) { TerrainEditorMod.Log.Warning($"{path} could not be read: {e.Message}"); }
            }
            if (s_File.Strokes == null) s_File.Strokes = new List<Stroke>();
            return s_File;
        }

        static void Save()
        {
            if (s_Scene == null) return;
            try
            {
                string path = PathFor(s_Scene);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, JsonSerializer.Serialize(s_File, s_Json));
            }
            catch (System.Exception e) { TerrainEditorMod.Log.Warning($"Writing the terrain edits failed: {e.Message}"); }
        }

        public static void Add(string scene, Stroke stroke)
        {
            For(scene).Strokes.Add(stroke);
            Save();
        }

        public static void Remove(string scene, Stroke stroke)
        {
            For(scene).Strokes.Remove(stroke);
            Save();
        }

        public static void Replay(string scene)
        {
            var file = For(scene);
            if (file.Strokes.Count == 0) return;
            var timer = System.Diagnostics.Stopwatch.StartNew();
            Heightmap.Replay(file.Strokes);
            TerrainEditorMod.Log.Msg($"{scene}: {file.Strokes.Count} stroke(s) applied in {timer.ElapsedMilliseconds} ms.");
        }
    }
}
