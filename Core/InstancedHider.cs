using System.Collections.Generic;
using Il2Cpp;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace LongDarkTerrain
{
    internal static class InstancedHider
    {
        sealed class Target
        {
            public string Prefix;
            public Vector3 Pos;
            public bool Moved;
            public Matrix4x4 Delta;
            public bool Matched;
        }

        sealed class Hit
        {
            public Target T;
            public RenderObjectInstanceBatches B;
            public System.IntPtr Batches;
            public ComputeBuffer Mats, Invs;
            public int Index;
            public Matrix4x4 Orig;
            public Unity.Mathematics.float4 OrigPos;
            public Bounds OrigBounds;
            public string Name;
            public Mesh Mesh;
        }

        static readonly List<Target> s_Targets = new List<Target>();
        static readonly List<Hit> s_Hits = new List<Hit>();
        static readonly Dictionary<System.IntPtr, System.IntPtr> s_Done = new Dictionary<System.IntPtr, System.IntPtr>();
        static float s_NextCheck, s_NextWrite;
        const float MatchDist = 1.0f;

        static Il2CppStructArray<Matrix4x4> s_One;
        static Il2CppSystem.Array s_OneArr;

        public static void Clear()
        {
            s_Targets.Clear();
            s_Hits.Clear();
            s_Done.Clear();
        }

        public static string Prefix(string name)
        {
            int i = name.IndexOf("_Prefab", System.StringComparison.Ordinal);
            if (i > 0) return name.Substring(0, i);
            i = name.IndexOf(" (", System.StringComparison.Ordinal);
            return i > 0 ? name.Substring(0, i) : name;
        }

        static Target Get(string prefix, Vector3 pos)
        {
            foreach (var t in s_Targets) if (t.Prefix == prefix && (t.Pos - pos).sqrMagnitude < 0.01f) return t;
            return null;
        }

        public static void Add(Transform obj)
        {
            if (obj == null) return;
            Set(Prefix(obj.name), obj.position, false, Matrix4x4.identity);
        }

        public static void Set(string prefix, Vector3 origPos, bool moved, Matrix4x4 delta)
        {
            var t = Get(prefix, origPos);
            if (t == null) { t = new Target { Prefix = prefix, Pos = origPos }; s_Targets.Add(t); }
            t.Moved = moved;
            t.Delta = delta;
            if (t.Matched) WriteTarget(t);
        }

        public static void Remove(string prefix, Vector3 origPos)
        {
            var t = Get(prefix, origPos);
            if (t == null) return;
            s_Targets.Remove(t);
            foreach (var h in s_Hits)
            {
                if (h.T != t) continue;
                try { WriteOne(h, h.Orig); RestoreCulling(h); } catch { }
            }
            s_Hits.RemoveAll(h => h.T == t);
        }

        public static bool Has(string prefix, Vector3 origPos) => Get(prefix, origPos) != null;

        public static void Apply()
        {
            int found = 0;
            try
            {
                var list = RenderObjectInstance.s_RenderObjectInstanceList;
                if (list == null) return;
                bool any = false;
                foreach (var t in s_Targets) if (!t.Matched) { any = true; break; }
                foreach (var roi in list)
                {
                    if (roi == null) continue;
                    var b = roi.m_Batches;
                    if (b == null || !b.IsAllocated()) continue;
                    if (any) found += Find(b);
                    s_Done[roi.Pointer] = b.Pointer;
                }
                foreach (var t in s_Targets) if (!t.Matched) t.Matched = true;
                WriteAll();
            }
            catch (System.Exception e)
            {
                TerrainEditorMod.Log.Warning($"Instanced objects: {e.Message}");
            }
            if (found > 0) TerrainEditorMod.Debug($"Instanced objects: {found} copies of {s_Targets.Count} object(s) hidden or moved.");
        }

        static int Find(RenderObjectInstanceBatches b)
        {
            var names = b.m_Names;
            if (names == null) return 0;
            var mats = b.m_Objects.m_InstancedMatrices;
            var invs = b.m_Objects.m_InstancedInverses;
            if (mats == null) return 0;
            int batches = Mathf.Min(names.Count, b.GetBatchCount());
            int found = 0;
            for (int i = 0; i < batches; i++)
            {
                string name = names[i];
                if (name == null) continue;
                List<Target> mine = null;
                foreach (var t in s_Targets)
                    if (!t.Matched && name.StartsWith(t.Prefix + "_", System.StringComparison.Ordinal)) (mine ??= new List<Target>()).Add(t);
                if (mine == null) continue;
                int start = b.m_Batches.m_StartIndexes[i];
                int count = b.m_Batches.m_ObjectCounts[i];
                if (count <= 0 || start < 0 || start + count > mats.count) continue;
                Mesh batchMesh = null;
                try { var ri = b.m_Batches.m_RenderInfos[i]; if (ri != null) batchMesh = ri.m_Mesh; } catch { }
                var arr = new Il2CppStructArray<Matrix4x4>(count);
                mats.InternalGetData(new Il2CppSystem.Array(arr.Pointer), 0, start, count, 64);
                for (int k = 0; k < count; k++)
                {
                    var m = arr[k];
                    if (m.m33 == 0f) continue;
                    var pos = new Vector3(m.m03, m.m13, m.m23);
                    foreach (var t in mine)
                    {
                        var dd = t.Pos - pos;
                        if (dd.x * dd.x + dd.z * dd.z > MatchDist * MatchDist * 2.25f || Mathf.Abs(dd.y) > 3f) continue;
                        int idx = start + k;
                        s_Hits.Add(new Hit
                        {
                            T = t, B = b, Batches = b.Pointer, Mats = mats, Invs = invs, Index = idx, Orig = m,
                            OrigPos = b.m_Objects.m_Positions[idx], OrigBounds = b.m_Objects.m_Bounds[idx],
                            Name = name, Mesh = batchMesh,
                        });
                        found++;
                        break;
                    }
                }
            }
            return found;
        }

        static void WriteOne(Hit h, Matrix4x4 m)
        {
            if (s_One == null) { s_One = new Il2CppStructArray<Matrix4x4>(1); s_OneArr = new Il2CppSystem.Array(s_One.Pointer); }
            s_One[0] = m;
            h.Mats.InternalSetData(s_OneArr, 0, h.Index, 1, 64);
            if (h.Invs != null)
            {
                s_One[0] = m.m33 == 0f ? m : m.inverse;
                h.Invs.InternalSetData(s_OneArr, 0, h.Index, 1, 64);
            }
        }

        static void WriteHit(Hit h)
        {
            if (h.Mats == null) return;
            if (!h.T.Moved) { WriteOne(h, new Matrix4x4()); return; }
            WriteOne(h, h.T.Delta * h.Orig);
            var p = h.T.Delta.MultiplyPoint3x4(new Vector3(h.OrigPos.x, h.OrigPos.y, h.OrigPos.z));
            h.B.m_Objects.m_Positions[h.Index] = new Unity.Mathematics.float4(p.x, p.y, p.z, h.OrigPos.w);
            var bo = h.OrigBounds;
            bo.center = h.T.Delta.MultiplyPoint3x4(bo.center);
            h.B.m_Objects.m_Bounds[h.Index] = bo;
        }

        static void RestoreCulling(Hit h)
        {
            h.B.m_Objects.m_Positions[h.Index] = h.OrigPos;
            h.B.m_Objects.m_Bounds[h.Index] = h.OrigBounds;
        }

        static void WriteTarget(Target t)
        {
            foreach (var h in s_Hits) if (h.T == t) { try { WriteHit(h); } catch { } }
        }

        static Il2CppSystem.Array s_Zeros;
        static int s_ZeroLen;

        static void WriteAll()
        {
            var hidden = new List<Hit>();
            foreach (var h in s_Hits)
            {
                if (h.Mats == null) continue;
                if (h.T.Moved) WriteHit(h); else hidden.Add(h);
            }
            if (hidden.Count == 0) return;
            hidden.Sort((x, y) => x.Mats.Pointer != y.Mats.Pointer ? x.Mats.Pointer.ToInt64().CompareTo(y.Mats.Pointer.ToInt64()) : x.Index.CompareTo(y.Index));
            int i = 0;
            while (i < hidden.Count)
            {
                int j = i;
                while (j + 1 < hidden.Count && hidden[j + 1].Mats.Pointer == hidden[i].Mats.Pointer && hidden[j + 1].Index == hidden[j].Index + 1) j++;
                int n = j - i + 1;
                if (s_Zeros == null || s_ZeroLen < n)
                {
                    s_ZeroLen = Mathf.Max(n, 64);
                    s_Zeros = new Il2CppSystem.Array(new Il2CppStructArray<Matrix4x4>(s_ZeroLen).Pointer);
                }
                hidden[i].Mats.InternalSetData(s_Zeros, 0, hidden[i].Index, n, 64);
                if (hidden[i].Invs != null) hidden[i].Invs.InternalSetData(s_Zeros, 0, hidden[i].Index, n, 64);
                i = j + 1;
            }
        }

        internal sealed class InstPick
        {
            public string Prefix;
            public Vector3 OrigPos;
            public Quaternion OrigRot;
            public Vector3 Pos;
            public Quaternion Rot;
            public Bounds World;
            public float Dist;
            public string Key => Prefix + "@" + OrigPos.x.ToString("F2") + "," + OrigPos.z.ToString("F2");
        }

        struct Cand { public ComputeBuffer Mats; public int Index; public Bounds B; public string Name; public Mesh Mesh; }

        static readonly List<Cand> s_Cands = new List<Cand>();
        static readonly Dictionary<System.IntPtr, System.IntPtr> s_CandDone = new Dictionary<System.IntPtr, System.IntPtr>();
        static readonly Dictionary<(long, int), Matrix4x4> s_MatCache = new Dictionary<(long, int), Matrix4x4>();

        sealed class Tris { public Vector3[] V; public int[] T; }
        static readonly Dictionary<int, Tris> s_Tris = new Dictionary<int, Tris>();

        static void BuildCands()
        {
            var list = RenderObjectInstance.s_RenderObjectInstanceList;
            if (list == null) return;
            bool same = s_Cands.Count > 0;
            foreach (var roi in list)
            {
                if (roi == null || roi.m_Batches == null || !roi.m_Batches.IsAllocated()) continue;
                if (!s_CandDone.TryGetValue(roi.Pointer, out var bp) || bp != roi.m_Batches.Pointer) same = false;
            }
            if (same) return;
            s_Cands.Clear(); s_CandDone.Clear(); s_MatCache.Clear();
            foreach (var roi in list)
            {
                if (roi == null) continue;
                var b = roi.m_Batches;
                if (b == null || !b.IsAllocated() || b.m_Names == null) continue;
                var po = b.m_Objects;
                var mats = po.m_InstancedMatrices;
                if (mats == null) continue;
                int batches = Mathf.Min(b.m_Names.Count, b.GetBatchCount());
                for (int i = 0; i < batches; i++)
                {
                    string nm = b.m_Names[i];
                    if (nm == null || !IsLod0(nm)) continue;
                    Mesh mesh = null;
                    try { var ri = b.m_Batches.m_RenderInfos[i]; if (ri != null) mesh = ri.m_Mesh; } catch { }
                    if (mesh == null) continue;
                    int st = b.m_Batches.m_StartIndexes[i], n = b.m_Batches.m_ObjectCounts[i];
                    for (int k = 0; k < n; k++)
                        s_Cands.Add(new Cand { Mats = mats, Index = st + k, B = po.m_Bounds[st + k], Name = nm, Mesh = mesh });
                }
                s_CandDone[roi.Pointer] = b.Pointer;
            }
        }

        static Hit FindHit(ComputeBuffer mats, int index)
        {
            foreach (var h in s_Hits) if (h.Index == index && h.Mats != null && h.Mats.Pointer == mats.Pointer) return h;
            return null;
        }

        static Matrix4x4 ReadMatrix(ComputeBuffer mats, int index)
        {
            var key = (mats.Pointer.ToInt64(), index);
            if (s_MatCache.TryGetValue(key, out var m)) return m;
            if (s_One == null) { s_One = new Il2CppStructArray<Matrix4x4>(1); s_OneArr = new Il2CppSystem.Array(s_One.Pointer); }
            mats.InternalGetData(s_OneArr, 0, index, 1, 64);
            m = s_One[0];
            s_MatCache[key] = m;
            return m;
        }

        static Tris TrisOf(Mesh mesh)
        {
            int id = mesh.GetInstanceID();
            if (s_Tris.TryGetValue(id, out var t)) return t;
            if (mesh.isReadable) t = new Tris { V = mesh.vertices, T = mesh.triangles };
            s_Tris[id] = t;
            return t;
        }

        static bool HitMesh(Tris tr, Vector3 o, Vector3 d, out float best)
        {
            best = float.MaxValue;
            var v = tr.V; var t = tr.T;
            for (int i = 0; i + 2 < t.Length; i += 3)
            {
                var a = v[t[i]]; var e1 = v[t[i + 1]] - a; var e2 = v[t[i + 2]] - a;
                var pv = Vector3.Cross(d, e2);
                float det = Vector3.Dot(e1, pv);
                if (det > -1e-9f && det < 1e-9f) continue;
                float inv = 1f / det;
                var tv = o - a;
                float u = Vector3.Dot(tv, pv) * inv;
                if (u < 0f || u > 1f) continue;
                var qv = Vector3.Cross(tv, e1);
                float w = Vector3.Dot(d, qv) * inv;
                if (w < 0f || u + w > 1f) continue;
                float dist = Vector3.Dot(e2, qv) * inv;
                if (dist > 0f && dist < best) best = dist;
            }
            return best < float.MaxValue;
        }

        public static InstPick Pick(Ray ray, float maxDist)
        {
            BuildCands();
            InstPick best = null;
            float bestD = maxDist;
            ray.direction = ray.direction.normalized;

            void Test(Cand c, bool prefilter)
            {
                if (prefilter)
                {
                    if (!c.B.IntersectRay(ray, out float d0)) return;
                    if (d0 > bestD) return;
                }
                var hit = FindHit(c.Mats, c.Index);
                Matrix4x4 m;
                Vector3 oPos; Quaternion oRot;
                if (hit != null)
                {
                    if (!hit.T.Moved) return;
                    m = hit.T.Delta * hit.Orig;
                    oPos = hit.T.Pos; oRot = RotOf(hit.Orig);
                }
                else
                {
                    m = ReadMatrix(c.Mats, c.Index);
                    if (m.m33 == 0f) return;
                    oPos = new Vector3(m.m03, m.m13, m.m23); oRot = RotOf(m);
                }
                var tr = TrisOf(c.Mesh);
                var inv = m.inverse;
                var o = inv.MultiplyPoint3x4(ray.origin);
                var dir = inv.MultiplyVector(ray.direction);
                float dist;
                if (tr != null) { if (!HitMesh(tr, o, dir, out dist)) return; }
                else
                {
                    var lb = c.Mesh.bounds;
                    if (!lb.IntersectRay(new Ray(o, dir), out dist) || dist < 0f) return;
                }
                if (dist >= bestD) return;
                bestD = dist;
                var lbb = c.Mesh.bounds;
                var wb = new Bounds(m.MultiplyPoint3x4(lbb.center), Vector3.zero);
                for (int i = 0; i < 8; i++)
                    wb.Encapsulate(m.MultiplyPoint3x4(lbb.center + Vector3.Scale(lbb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))));
                string name = Base(c.Name);
                best = new InstPick { Prefix = name, OrigPos = oPos, OrigRot = oRot, Pos = new Vector3(m.m03, m.m13, m.m23), Rot = RotOf(m), World = wb, Dist = dist };
            }

            foreach (var c in s_Cands) Test(c, true);
            foreach (var h in s_Hits)
            {
                if (!h.T.Moved || h.Mesh == null || h.Name == null || !IsLod0(h.Name)) continue;
                Test(new Cand { Mats = h.Mats, Index = h.Index, Name = h.Name, Mesh = h.Mesh }, false);
            }
            return best;
        }

        static bool IsLod0(string n) => n.EndsWith("_LOD0", System.StringComparison.Ordinal) || n.EndsWith("_LOD00", System.StringComparison.Ordinal);

        static string Base(string n)
        {
            int i = n.LastIndexOf("_LOD", System.StringComparison.Ordinal);
            return i > 0 ? n.Substring(0, i) : n;
        }

        static Quaternion RotOf(Matrix4x4 m)
        {
            var f = new Vector3(m.m02, m.m12, m.m22);
            var u = new Vector3(m.m01, m.m11, m.m21);
            return f.sqrMagnitude > 1e-8f ? Quaternion.LookRotation(f, u) : Quaternion.identity;
        }

        public static void Update()
        {
            if (s_Targets.Count == 0) return;
            try
            {
                if (Time.time >= s_NextCheck)
                {
                    s_NextCheck = Time.time + 2f;
                    var list = RenderObjectInstance.s_RenderObjectInstanceList;
                    if (list != null)
                        foreach (var roi in list)
                        {
                            if (roi == null || roi.m_Batches == null || !roi.m_Batches.IsAllocated()) continue;
                            if (!s_Done.TryGetValue(roi.Pointer, out var bp) || bp != roi.m_Batches.Pointer)
                            {
                                s_Hits.Clear();
                                s_Done.Clear();
                                foreach (var t in s_Targets) t.Matched = false;
                                Apply();
                                return;
                            }
                        }
                    bool pending = false;
                    foreach (var t in s_Targets) if (!t.Matched) { pending = true; break; }
                    if (pending) Apply();
                }
                if (Time.time >= s_NextWrite)
                {
                    s_NextWrite = Time.time + 1f;
                    WriteAll();
                }
            }
            catch { }
        }
    }
}
