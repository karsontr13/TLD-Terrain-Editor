using System.Collections.Generic;
using UnityEngine;

namespace LongDarkTerrain
{
    internal static class BrushEditor
    {
        const float DabInterval = 0.05f;
        const int UndoDepth = 50;

        static BrushMode s_Mode = BrushMode.Raise;
        static float s_Radius = 6f, s_Strength = 1f, s_Offset;
        static Stroke s_Stroke;
        static List<Heightmap.Backup> s_StrokeUndo;
        static float s_Target, s_NextDab;
        static string s_Info = "";
        static readonly List<(Stroke stroke, List<Heightmap.Backup> undo)> s_History = new List<(Stroke, List<Heightmap.Backup>)>();
        static readonly Overlay.Line s_Outer = new Overlay.Line("Brush", 0.1f);
        static readonly Overlay.Line s_Inner = new Overlay.Line("BrushInner", 0.06f);

        static string ModeName(BrushMode m) => m switch
        {
            BrushMode.Raise => L.T("Yükselt", "Raise"),
            BrushMode.Lower => L.T("Alçalt", "Lower"),
            BrushMode.Flatten => L.T("Düzle", "Flatten"),
            BrushMode.Fill => L.T("Doldur (kar dolgusu)", "Fill (snow fill)"),
            _ => L.T("Yumuşat", "Smooth"),
        };

        static Color ModeColor() => s_Mode switch
        {
            BrushMode.Raise => new Color(0.4f, 1f, 0.4f),
            BrushMode.Lower => new Color(1f, 0.45f, 0.35f),
            BrushMode.Flatten => new Color(0.4f, 0.8f, 1f),
            BrushMode.Fill => Color.white,
            _ => new Color(1f, 0.85f, 0.4f),
        };

        public static void Leave()
        {
            EndStroke(TerrainEditorMod.Scene);
            s_Outer.Hide();
            s_Inner.Hide();
        }

        public static void Update(string scene)
        {
            if (Keys.NextBrush)
            {
                s_Mode = (BrushMode)(((int)s_Mode + 1) % 5);
                Hud.Message(L.T("Fırça: ", "Brush: ") + ModeName(s_Mode));
            }
            float wheel = Input.mouseScrollDelta.y;
            if (wheel != 0f)
            {
                if (Keys.Strength) s_Strength = Mathf.Clamp(s_Strength + Mathf.Sign(wheel) * 0.1f, 0.1f, 3f);
                else s_Radius = Mathf.Clamp(s_Radius + Mathf.Sign(wheel) * (Keys.Fast ? 3f : 0.5f), 1f, 40f);
            }
            if (Keys.LevelUp) s_Offset += Keys.Fast ? 1f : 0.25f;
            if (Keys.LevelDown) s_Offset -= Keys.Fast ? 1f : 0.25f;
            if (Keys.Undo && s_Stroke == null) Undo(scene);

            bool aimed = Overlay.Aim(out var hit);
            if (aimed)
            {
                s_Outer.Set(Overlay.Ring(hit, s_Radius, 48), ModeColor());
                s_Inner.Set(Overlay.Ring(hit, s_Radius * 0.5f, 32), ModeColor() * 0.8f);
            }
            else
            {
                s_Outer.Hide();
                s_Inner.Hide();
            }

            bool levelled = s_Mode == BrushMode.Flatten || s_Mode == BrushMode.Fill;
            s_Info = $"{ModeName(s_Mode)}   " + L.T($"boyut {s_Radius:F1} m   güç {s_Strength:F1}", $"size {s_Radius:F1} m   strength {s_Strength:F1}")
                   + (levelled ? L.T($"   seviye {s_Offset:+0.00;-0.00} m", $"   level {s_Offset:+0.00;-0.00} m") : "");

            if (Keys.Painting && aimed) Paint(hit);
            else if (s_Stroke != null) EndStroke(scene);
        }

        static void Paint(Vector3 hit)
        {
            if (s_Stroke == null)
            {
                s_Stroke = new Stroke { Mode = (int)s_Mode };
                s_StrokeUndo = new List<Heightmap.Backup>();
                s_Target = Overlay.GroundHeight(hit, hit.y) + s_Offset;
                s_NextDab = 0f;
            }
            if (Time.time < s_NextDab) return;
            float dt = s_NextDab == 0f ? DabInterval : Mathf.Min(Time.time - (s_NextDab - DabInterval), 0.2f);
            s_NextDab = Time.time + DabInterval;
            bool metres = s_Mode == BrushMode.Raise || s_Mode == BrushMode.Lower;
            float amount = metres ? 1.5f * s_Strength * dt : Mathf.Clamp01(3f * s_Strength * dt);
            Heightmap.Paint(s_Mode, hit.x, hit.z, s_Radius, amount, s_Target, s_StrokeUndo);
            s_Stroke.Dabs.Add(new[] { Round(hit.x), Round(hit.z), Round(s_Radius), (float)System.Math.Round(amount, 5), Round(s_Target) });
        }

        static float Round(float f) => (float)System.Math.Round(f, 3);

        static void EndStroke(string scene)
        {
            if (s_Stroke == null) return;
            Heightmap.Sync();
            if (s_Stroke.Dabs.Count > 0 && scene != null)
            {
                StrokeStore.Add(scene, s_Stroke);
                s_History.Add((s_Stroke, s_StrokeUndo));
                if (s_History.Count > UndoDepth) s_History.RemoveAt(0);
            }
            s_Stroke = null;
            s_StrokeUndo = null;
        }

        static void Undo(string scene)
        {
            if (s_History.Count == 0)
            {
                Hud.Message(L.T("Bu oturumda geri alınacak fırça darbesi yok.", "No brush stroke to undo in this session."));
                return;
            }
            var (stroke, undo) = s_History[s_History.Count - 1];
            s_History.RemoveAt(s_History.Count - 1);
            Heightmap.Restore(undo);
            StrokeStore.Remove(scene, stroke);
            Hud.Message(L.T("Son fırça darbesi geri alındı.", "Last brush stroke undone."));
        }

        public static void OnGUI()
        {
            string note = s_Mode == BrushMode.Fill
                ? L.T("Doldur: zemin, darbenin başladığı yerin seviyesine (+ayar) kadar yükselir, asla kazılmaz",
                      "Fill: the ground rises to the level where the stroke starts (+offset), it is never dug")
                : "";
            string paint = Keys.Name("paint"), brush = Keys.Name("brush"), fast = Keys.Name("fast"), strength = Keys.Name("strength");
            string up = Keys.Name("up"), down = Keys.Name("down"), undo = Keys.Name("undo"), toggle = Keys.Name("toggle");
            string help = L.T(
                $"{paint} (basılı) fırçala   {brush} fırça türü   tekerlek boyut ({fast} hızlı)   {strength}+tekerlek güç   {up}/{down} seviye\n{undo} son darbeyi geri al   {toggle} sonraki mod",
                $"{paint} (hold) paint   {brush} brush   wheel size ({fast} faster)   {strength}+wheel strength   {up}/{down} level\n{undo} undo last stroke   {toggle} next mode");
            Overlay.Panel(L.T("ARAZİ DÜZENLEYİCİ", "TERRAIN EDITOR"), s_Info, note, help);
        }
    }
}
