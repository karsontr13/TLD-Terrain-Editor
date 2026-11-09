using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace LongDarkTerrain
{
    internal enum BrushMode { Raise, Lower, Flatten, Fill, Smooth }

    internal static class Heightmap
    {
        internal sealed class Backup
        {
            public TerrainData Data;
            public int X0, Z0, W, H;
            public float[] Heights;
        }

        static readonly HashSet<System.IntPtr> s_Dirty = new HashSet<System.IntPtr>();

        sealed class Block
        {
            public TerrainData Td;
            public Vector3 Pos, Size;
            public float Cx, Cz;
            public int X0, Z0, W, H;
            public Il2CppStructArray<float> Heights;
            public System.Action Write, WriteDelayedLod;
        }

        static Block Open(Terrain terrain, float minX, float maxX, float minZ, float maxZ)
        {
            var td = terrain.terrainData;
            var pos = terrain.transform.position;
            var size = td.size;
            if (maxX < pos.x || minX > pos.x + size.x || maxZ < pos.z || minZ > pos.z + size.z) return null;
            int res = td.heightmapResolution;
            float cx = size.x / (res - 1), cz = size.z / (res - 1);
            int x0 = Mathf.Clamp(Mathf.FloorToInt((minX - pos.x) / cx) - 1, 0, res - 1);
            int x1 = Mathf.Clamp(Mathf.CeilToInt((maxX - pos.x) / cx) + 1, 0, res - 1);
            int z0 = Mathf.Clamp(Mathf.FloorToInt((minZ - pos.z) / cz) - 1, 0, res - 1);
            int z1 = Mathf.Clamp(Mathf.CeilToInt((maxZ - pos.z) / cz) + 1, 0, res - 1);
            int w = x1 - x0 + 1, h = z1 - z0 + 1;
            if (w <= 2 || h <= 2) return null;
            var raw = td.GetHeights(x0, z0, w, h);
            var flat = new Il2CppStructArray<float>(raw.Pointer);
            if (flat.Length < w * h) return null;
            return new Block
            {
                Td = td, Pos = pos, Size = size, Cx = cx, Cz = cz, X0 = x0, Z0 = z0, W = w, H = h, Heights = flat,
                Write = () => td.SetHeights(x0, z0, raw),
                WriteDelayedLod = () => td.SetHeightsDelayLOD(x0, z0, raw),
            };
        }

        static float[] Copy(Block b)
        {
            var a = new float[b.W * b.H];
            for (int i = 0; i < a.Length; i++) a[i] = b.Heights[i];
            return a;
        }

        static void Store(Block b, float[] a)
        {
            for (int i = 0; i < a.Length; i++) b.Heights[i] = a[i];
        }

        static bool Dab(float[] a, Block b, BrushMode mode, float x, float z, float radius, float amount, float target)
        {
            int ix0 = Mathf.Max(1, Mathf.FloorToInt((x - radius - b.Pos.x) / b.Cx) - b.X0);
            int ix1 = Mathf.Min(b.W - 2, Mathf.CeilToInt((x + radius - b.Pos.x) / b.Cx) - b.X0);
            int iz0 = Mathf.Max(1, Mathf.FloorToInt((z - radius - b.Pos.z) / b.Cz) - b.Z0);
            int iz1 = Mathf.Min(b.H - 2, Mathf.CeilToInt((z + radius - b.Pos.z) / b.Cz) - b.Z0);
            if (ix1 < ix0 || iz1 < iz0) return false;

            float level = (target - b.Pos.y) / b.Size.y;
            var before = mode == BrushMode.Smooth ? (float[])a.Clone() : a;
            bool changed = false;
            for (int iz = iz0; iz <= iz1; iz++)
            for (int ix = ix0; ix <= ix1; ix++)
            {
                float wx = b.Pos.x + (b.X0 + ix) * b.Cx, wz = b.Pos.z + (b.Z0 + iz) * b.Cz;
                float d = Mathf.Sqrt((wx - x) * (wx - x) + (wz - z) * (wz - z));
                if (d >= radius) continue;
                float falloff = Mathf.SmoothStep(1f, 0f, d / radius);
                int i = iz * b.W + ix;
                float cur = a[i], next = cur;
                switch (mode)
                {
                    case BrushMode.Raise: next = cur + amount * falloff / b.Size.y; break;
                    case BrushMode.Lower: next = cur - amount * falloff / b.Size.y; break;
                    case BrushMode.Flatten: next = Mathf.Lerp(cur, level, Mathf.Clamp01(amount * falloff)); break;
                    case BrushMode.Fill:
                        if (cur < level) next = Mathf.Lerp(cur, level, Mathf.Clamp01(amount * falloff));
                        break;
                    case BrushMode.Smooth:
                        float avg = (before[i - 1] + before[i + 1] + before[i - b.W] + before[i + b.W] + before[i]) / 5f;
                        next = Mathf.Lerp(cur, avg, Mathf.Clamp01(amount * falloff));
                        break;
                }
                next = Mathf.Clamp01(next);
                if (next == cur) continue;
                a[i] = next;
                changed = true;
            }
            return changed;
        }

        public static void Paint(BrushMode mode, float x, float z, float radius, float amount, float target, List<Backup> undo)
        {
            foreach (var terrain in Terrain.activeTerrains)
            {
                try
                {
                    var b = Open(terrain, x - radius, x + radius, z - radius, z + radius);
                    if (b == null) continue;
                    var a = Copy(b);
                    if (undo != null) undo.Add(new Backup { Data = b.Td, X0 = b.X0, Z0 = b.Z0, W = b.W, H = b.H, Heights = (float[])a.Clone() });
                    if (!Dab(a, b, mode, x, z, radius, amount, target)) continue;
                    Store(b, a);
                    b.WriteDelayedLod();
                    s_Dirty.Add(b.Td.Pointer);
                }
                catch (System.Exception e) { TerrainEditorMod.Debug($"Brush on {terrain.name}: {e.Message}"); }
            }
        }

        public static void Replay(IList<Stroke> strokes)
        {
            foreach (var terrain in Terrain.activeTerrains)
            {
                try
                {
                    float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
                    foreach (var st in strokes)
                        foreach (var d in st.Dabs)
                        {
                            if (d == null || d.Length < 5) continue;
                            minX = Mathf.Min(minX, d[0] - d[2]); maxX = Mathf.Max(maxX, d[0] + d[2]);
                            minZ = Mathf.Min(minZ, d[1] - d[2]); maxZ = Mathf.Max(maxZ, d[1] + d[2]);
                        }
                    if (minX > maxX) continue;
                    var b = Open(terrain, minX, maxX, minZ, maxZ);
                    if (b == null) continue;
                    var a = Copy(b);
                    bool changed = false;
                    foreach (var st in strokes)
                        foreach (var d in st.Dabs)
                            if (d != null && d.Length >= 5)
                                changed |= Dab(a, b, (BrushMode)st.Mode, d[0], d[1], d[2], d[3], d[4]);
                    if (!changed) continue;
                    Store(b, a);
                    b.Write();
                }
                catch (System.Exception e) { TerrainEditorMod.Log.Warning($"Terrain edits on {terrain.name}: {e.Message}"); }
            }
        }

        public static void Sync()
        {
            foreach (var terrain in Terrain.activeTerrains)
            {
                var td = terrain.terrainData;
                if (!s_Dirty.Contains(td.Pointer)) continue;
                try { td.SyncHeightmap(); }
                catch (System.Exception e) { TerrainEditorMod.Debug($"Terrain sync: {e.Message}"); }
            }
            s_Dirty.Clear();
        }

        public static void Restore(List<Backup> undo)
        {
            for (int i = undo.Count - 1; i >= 0; i--)
            {
                var u = undo[i];
                if (u.Data == null) continue;
                var raw = u.Data.GetHeights(u.X0, u.Z0, u.W, u.H);
                var flat = new Il2CppStructArray<float>(raw.Pointer);
                for (int k = 0; k < u.Heights.Length && k < flat.Length; k++) flat[k] = u.Heights[k];
                u.Data.SetHeightsDelayLOD(u.X0, u.Z0, raw);
                s_Dirty.Add(u.Data.Pointer);
            }
            Sync();
        }
    }
}
