using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace LongDarkTerrain
{
    internal sealed class EditRecord
    {
        public string Name { get; set; }
        public string Parent { get; set; }
        public float[] Pos { get; set; }
        public float[] Rot { get; set; }
        public bool Hidden { get; set; }
        public float[] NewPos { get; set; }
        public float[] NewRot { get; set; }
    }

    internal sealed class EditFile
    {
        public int Version { get; set; } = 1;
        public List<EditRecord> Edits { get; set; } = new List<EditRecord>();
    }

    internal static class ObjectEdits
    {
        const float MatchDist = 0.05f;
        static readonly JsonSerializerOptions s_Json = new JsonSerializerOptions { WriteIndented = true };

        static string s_Scene;
        static EditFile s_File = new EditFile();

        sealed class Live
        {
            public Transform T;
            public Vector3 OrigPos;
            public Quaternion OrigRot;
            public EditRecord R;
            public readonly List<(Renderer r, bool was)> OffR = new List<(Renderer, bool)>();
            public readonly List<(Collider c, bool was)> OffC = new List<(Collider, bool)>();
            public readonly List<(LODGroup l, bool was)> OffL = new List<(LODGroup, bool)>();
            public readonly List<GameObject> Clones = new List<GameObject>();
            public readonly List<(string prefix, Vector3 pos)> Inst = new List<(string, Vector3)>();
            public Bounds OrigBounds;
            public bool HasBounds;
            public readonly List<Loot> Loot = new List<Loot>();
        }

        sealed class Loot
        {
            public Transform T;
            public Vector3 Pos;
            public Quaternion Rot;
            public readonly List<(Renderer r, bool was)> R = new List<(Renderer, bool)>();
            public readonly List<(Collider c, bool was)> C = new List<(Collider, bool)>();
        }

        static readonly HashSet<System.IntPtr> s_Claimed = new HashSet<System.IntPtr>();

        static readonly Dictionary<System.IntPtr, Live> s_Live = new Dictionary<System.IntPtr, Live>();

        static string FilePath(string scene) => Path.Combine(StrokeStore.SlotFolder, "Objects", scene + ".json");
        public static int Count => s_File.Edits.Count;

        static float[] F(Vector3 v) => new[] { R3(v.x), R3(v.y), R3(v.z) };
        static float[] F(Quaternion q) => new[] { R5(q.x), R5(q.y), R5(q.z), R5(q.w) };
        static float R3(float f) => (float)System.Math.Round(f, 3);
        static float R5(float f) => (float)System.Math.Round(f, 5);
        static Vector3 V(float[] a) => a != null && a.Length >= 3 ? new Vector3(a[0], a[1], a[2]) : Vector3.zero;
        static Quaternion Q(float[] a) => a != null && a.Length >= 4 ? new Quaternion(a[0], a[1], a[2], a[3]) : Quaternion.identity;

        public static void ApplyScene(string scene)
        {
            foreach (var c in s_Copies) if (c.Go != null) Object.Destroy(c.Go);
            s_Copies.Clear();
            if (scene != s_Scene) { s_Live.Clear(); s_Claimed.Clear(); s_Scene = scene; }
            s_SceneStart = Time.time;
            s_File = new EditFile();
            var path = FilePath(scene);
            if (StrokeStore.SlotKnown && File.Exists(path))
            {
                try { s_File = JsonSerializer.Deserialize<EditFile>(File.ReadAllText(path), s_Json) ?? new EditFile(); }
                catch (System.Exception e) { TerrainEditorMod.Log.Warning($"{path} could not be read: {e.Message}"); s_File = new EditFile(); }
            }
            if (s_File.Edits == null) s_File.Edits = new List<EditRecord>();
            if (s_File.Edits.Count == 0) return;

            var byName = new Dictionary<string, List<Transform>>();
            foreach (var t in Resources.FindObjectsOfTypeAll<Transform>())
            {
                if (t.gameObject.scene.name == null) continue;
                if (!byName.TryGetValue(t.name, out var l)) byName[t.name] = l = new List<Transform>();
                l.Add(t);
            }
            int applied = 0, missing = 0;
            foreach (var r in s_File.Edits)
            {
                if (r.Parent == InstTag) { ApplyInstance(r); applied++; continue; }
                if (r.Parent == CopyTag) { if (SpawnCopy(r) != null) applied++; else missing++; continue; }
                var t = Find(byName, r);
                if (t == null) { missing++; continue; }
                try { Put(t, r); applied++; }
                catch (System.Exception e) { TerrainEditorMod.Log.Warning($"Edit of {r.Name}: {e.Message}"); }
            }
            TerrainEditorMod.Log.Msg($"{scene}: {applied} object edit(s) applied" + (missing > 0 ? $", {missing} object(s) not found." : "."));
        }

        static Live NewLive(Transform t)
        {
            var live = new Live { T = t, OrigPos = t.position, OrigRot = t.rotation };
            live.OrigBounds = BoundsOf(t, out live.HasBounds);
            live.Inst.Add((InstancedHider.Prefix(t.name), t.position));
            int n = 0;
            foreach (var d in t.GetComponentsInChildren<Transform>(true))
            {
                if (d == t || !d.name.Contains("_Prefab") || n++ > 300) continue;
                live.Inst.Add((InstancedHider.Prefix(d.name), d.position));
            }
            s_Live[t.Pointer] = live;
            return live;
        }

        public static Bounds BoundsOf(Transform t, out bool any)
        {
            any = false;
            var b = new Bounds(t.position, Vector3.zero);
            foreach (var r in t.GetComponentsInChildren<Renderer>(false))
            {
                if (!r.enabled || r is ParticleSystemRenderer) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            if (!any)
                foreach (var c in t.GetComponentsInChildren<Collider>(false))
                {
                    if (!c.enabled) continue;
                    if (!any) { b = c.bounds; any = true; } else b.Encapsulate(c.bounds);
                }
            return b;
        }

        const string CopyTag = "@copy";

        sealed class CopyLive { public GameObject Go; public EditRecord R; }
        static readonly List<CopyLive> s_Copies = new List<CopyLive>();

        public static bool IsCopy(Transform t) => t != null && t.name.StartsWith("TE_Copy_");

        static Transform CopySource(EditRecord r)
        {
            var p = V(r.Pos); var q = Q(r.Rot);
            Transform best = null; float bd = float.MaxValue;
            foreach (var t in Resources.FindObjectsOfTypeAll<Transform>())
            {
                if (t.gameObject.scene.name == null || t.name != r.Name) continue;
                float d = (t.position - p).sqrMagnitude * 100f + Quaternion.Angle(t.rotation, q);
                if (d < bd) { bd = d; best = t; }
            }
            return best;
        }

        static GameObject SpawnCopy(EditRecord r)
        {
            var src = CopySource(r);
            if (src == null) return null;
            var go = Object.Instantiate(src.gameObject);
            go.name = "TE_Copy_" + r.Name;
            go.transform.SetPositionAndRotation(V(r.NewPos), Q(r.NewRot));
            if (!go.activeSelf) go.SetActive(true);
            foreach (var x in go.GetComponentsInChildren<Renderer>(true)) x.enabled = true;
            foreach (var x in go.GetComponentsInChildren<LODGroup>(true)) x.enabled = true;
            foreach (var x in go.GetComponentsInChildren<Collider>(true)) x.enabled = true;
            s_Copies.Add(new CopyLive { Go = go, R = r });
            return go;
        }

        public static Transform AddCopy(Transform src)
        {
            var r = new EditRecord
            {
                Name = src.name, Parent = CopyTag, Pos = F(OriginalPosition(src)), Rot = F(OriginalRotation(src)),
                NewPos = F(src.position), NewRot = F(src.rotation),
            };
            var go = SpawnCopy(r);
            if (go == null) return null;
            s_File.Edits.Add(r);
            Save();
            return go.transform;
        }

        public static void MoveCopy(Transform copy)
        {
            var c = s_Copies.Find(x => x.Go != null && x.Go.transform == copy);
            if (c == null) return;
            c.R.NewPos = F(copy.position);
            c.R.NewRot = F(copy.rotation);
            Save();
        }

        public static void ResetCopy(Transform copy)
        {
            var c = s_Copies.Find(x => x.Go != null && x.Go.transform == copy);
            if (c != null) copy.SetPositionAndRotation(V(c.R.NewPos), Q(c.R.NewRot));
        }

        public static bool RemoveCopy(Transform copy)
        {
            var c = s_Copies.Find(x => x.Go != null && x.Go.transform == copy);
            if (c == null) return false;
            s_File.Edits.Remove(c.R);
            s_Copies.Remove(c);
            Object.Destroy(c.Go);
            Save();
            return true;
        }

        const string InstTag = "@instance";

        static void ApplyInstance(EditRecord r)
        {
            var pos = V(r.Pos);
            if (r.Hidden) { InstancedHider.Set(r.Name, pos, false, Matrix4x4.identity); return; }
            var delta = Matrix4x4.TRS(V(r.NewPos), Q(r.NewRot), Vector3.one) * Matrix4x4.TRS(pos, Q(r.Rot), Vector3.one).inverse;
            InstancedHider.Set(r.Name, pos, true, delta);
        }

        public static EditRecord InstRecord(InstancedHider.InstPick p)
        {
            foreach (var x in s_File.Edits)
                if (x.Parent == InstTag && x.Name == p.Prefix && (V(x.Pos) - p.OrigPos).sqrMagnitude <= MatchDist * MatchDist) return x;
            return null;
        }

        public static EditRecord InstHidden(InstancedHider.InstPick p) => new EditRecord
        { Name = p.Prefix, Parent = InstTag, Pos = F(p.OrigPos), Rot = F(p.OrigRot), Hidden = true };

        public static EditRecord InstMoved(InstancedHider.InstPick p, Vector3 pos, Quaternion rot) => new EditRecord
        { Name = p.Prefix, Parent = InstTag, Pos = F(p.OrigPos), Rot = F(p.OrigRot), NewPos = F(pos), NewRot = F(rot) };

        public static void PreviewInstance(InstancedHider.InstPick p, Vector3 pos, Quaternion rot)
        {
            var delta = Matrix4x4.TRS(pos, rot, Vector3.one) * Matrix4x4.TRS(p.OrigPos, p.OrigRot, Vector3.one).inverse;
            InstancedHider.Set(p.Prefix, p.OrigPos, true, delta);
        }

        public static void SetInstance(InstancedHider.InstPick p, EditRecord r)
        {
            s_File.Edits.RemoveAll(x => x.Parent == InstTag && x.Name == p.Prefix && (V(x.Pos) - p.OrigPos).sqrMagnitude <= MatchDist * MatchDist);
            if (r == null) InstancedHider.Remove(p.Prefix, p.OrigPos);
            else { s_File.Edits.Add(r); ApplyInstance(r); }
            Save();
        }

        static Transform Find(Dictionary<string, List<Transform>> byName, EditRecord r)
        {
            if (r.Name == null || !byName.TryGetValue(r.Name, out var cands)) return null;
            var p = V(r.Pos);
            Transform best = null; float bd = MatchDist * MatchDist;
            foreach (var c in cands)
            {
                var live = s_Live.TryGetValue(c.Pointer, out var lv) ? lv : null;
                var o = live != null ? live.OrigPos : c.position;
                float d = (o - p).sqrMagnitude;
                if (d <= bd) { bd = d; best = c; }
            }
            return best;
        }

        public static EditRecord RecordOf(Transform t)
        {
            if (t == null) return null;
            return s_Live.TryGetValue(t.Pointer, out var l) ? l.R : null;
        }

        public static Vector3 OriginalPosition(Transform t) => s_Live.TryGetValue(t.Pointer, out var l) ? l.OrigPos : t.position;
        public static Quaternion OriginalRotation(Transform t) => s_Live.TryGetValue(t.Pointer, out var l) ? l.OrigRot : t.rotation;

        public static EditRecord Hidden(Transform t) => new EditRecord
        {
            Name = t.name, Parent = t.parent != null ? t.parent.name : null,
            Pos = F(OriginalPosition(t)), Rot = F(OriginalRotation(t)), Hidden = true,
        };

        public static EditRecord Moved(Transform t, Vector3 pos, Quaternion rot) => new EditRecord
        {
            Name = t.name, Parent = t.parent != null ? t.parent.name : null,
            Pos = F(OriginalPosition(t)), Rot = F(OriginalRotation(t)), NewPos = F(pos), NewRot = F(rot),
        };

        public static void Set(Transform t, EditRecord r)
        {
            if (t == null) return;
            var orig = OriginalPosition(t);
            Put(t, r);
            s_File.Edits.RemoveAll(x => x.Name == t.name && (V(x.Pos) - orig).sqrMagnitude <= MatchDist * MatchDist);
            if (r != null) s_File.Edits.Add(r);
            Save();
        }

        static void Save()
        {
            if (s_Scene == null) return;
            try
            {
                var path = FilePath(s_Scene);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, JsonSerializer.Serialize(s_File, s_Json));
            }
            catch (System.Exception e) { TerrainEditorMod.Log.Warning($"Writing the object edits failed: {e.Message}"); }
        }

        static void Put(Transform t, EditRecord r)
        {
            if (!s_Live.TryGetValue(t.Pointer, out var live))
            {
                if (r == null) return;
                live = NewLive(t);
            }
            Restore(live);
            live.R = r;
            if (r == null) { s_Live.Remove(t.Pointer); return; }

            if (r.Hidden)
            {
                HideAll(live);
                foreach (var (pre, op) in live.Inst) InstancedHider.Set(pre, op, false, Matrix4x4.identity);
                Claim(live);
                HideLoot(live, true);
                return;
            }

            Detach(live);
            var pos = V(r.NewPos);
            var rot = Q(r.NewRot);
            t.SetPositionAndRotation(pos, rot);
            SetInstanced(live);
            Claim(live);
            PoseLoot(live);
        }

        static void HideAll(Live live)
        {
            var t = live.T;
            foreach (var x in t.GetComponentsInChildren<Renderer>(true)) { live.OffR.Add((x, x.enabled)); x.enabled = false; }
            foreach (var x in t.GetComponentsInChildren<LODGroup>(true)) { live.OffL.Add((x, x.enabled)); x.enabled = false; }
            foreach (var x in t.GetComponentsInChildren<Collider>(true)) { live.OffC.Add((x, x.enabled)); x.enabled = false; }
        }

        static void SetInstanced(Live live)
        {
            var t = live.T;
            var delta = Matrix4x4.TRS(t.position, t.rotation, Vector3.one) * Matrix4x4.TRS(live.OrigPos, live.OrigRot, Vector3.one).inverse;
            foreach (var (pre, op) in live.Inst) InstancedHider.Set(pre, op, true, delta);
        }

        static Bounds ReachOf(Live live)
        {
            var b = live.OrigBounds;
            var lo = b.min - new Vector3(0.6f, 0.3f, 0.6f);
            var hi = b.max + new Vector3(0.6f, 1.2f, 0.6f);
            b.SetMinMax(lo, hi);
            return b;
        }

        static void Claim(Live live)
        {
            if (!live.HasBounds || live.T == null) return;
            var reach = ReachOf(live);
            bool moved = live.R != null && !live.R.Hidden;
            bool now = false;
            var cur = moved ? BoundsOf(live.T, out now) : default(Bounds);
            var hits = Physics.OverlapBox(reach.center, reach.extents, Quaternion.identity, ~0, QueryTriggerInteraction.Collide);
            foreach (var h in hits)
            {
                if (h == null) continue;
                var gi = h.GetComponentInParent<Il2Cpp.GearItem>();
                if (gi != null)
                {
                    if (gi.m_InPlayerInventory || gi.m_BeenInPlayerInventory || gi.m_InsideContainer || gi.m_IsInSatchel) continue;
                    TryClaim(live, gi.transform, reach, moved && now, cur);
                    continue;
                }
                var ct = h.GetComponentInParent<Il2Cpp.Container>();
                if (ct != null) TryClaim(live, ct.transform, reach, moved && now, cur);
            }
        }

        static void TryClaim(Live live, Transform it, Bounds reach, bool checkNow, Bounds cur)
        {
            if (it == null || it == live.T || it.IsChildOf(live.T) || live.T.IsChildOf(it) || s_Claimed.Contains(it.Pointer)) return;
            var p = it.position;
            if (!reach.Contains(p)) return;
            if (checkNow && cur.Contains(p)) return;
            var l = new Loot { T = it, Pos = p, Rot = it.rotation };
            foreach (var r in it.GetComponentsInChildren<Renderer>(true)) l.R.Add((r, r.enabled));
            foreach (var c in it.GetComponentsInChildren<Collider>(true)) l.C.Add((c, c.enabled));
            live.Loot.Add(l);
            s_Claimed.Add(it.Pointer);
        }

        static void PoseLoot(Live live)
        {
            if (live.T == null || live.Loot.Count == 0) return;
            var m = Matrix4x4.TRS(live.T.position, live.T.rotation, Vector3.one) * Matrix4x4.TRS(live.OrigPos, live.OrigRot, Vector3.one).inverse;
            var dq = live.T.rotation * Quaternion.Inverse(live.OrigRot);
            foreach (var l in live.Loot)
            {
                if (l.T == null) continue;
                l.T.SetPositionAndRotation(m.MultiplyPoint3x4(l.Pos), dq * l.Rot);
                var rb = l.T.GetComponent<Rigidbody>();
                if (rb != null && !rb.isKinematic) { rb.velocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
            }
        }

        static void HideLoot(Live live, bool hide)
        {
            foreach (var l in live.Loot)
            {
                if (l.T == null) continue;
                foreach (var (r, was) in l.R) if (r != null) r.enabled = hide ? false : was;
                foreach (var (c, was) in l.C) if (c != null) c.enabled = hide ? false : was;
            }
        }

        static float s_NextTick, s_NextScan, s_SceneStart;
        static bool s_Carrying;

        public static void Tick()
        {
            if (s_Live.Count == 0 || Time.time < s_NextTick) return;
            s_NextTick = Time.time + 1.5f;
            foreach (var live in s_Live.Values)
            {
                if (live.T == null || live.R == null || !live.R.Hidden) continue;
                foreach (var (r, _) in live.OffR) if (r != null && r.enabled) r.enabled = false;
                foreach (var (c, _) in live.OffC) if (c != null && c.enabled) c.enabled = false;
                foreach (var (g, _) in live.OffL) if (g != null && g.enabled) g.enabled = false;
            }
            if (s_Carrying || Time.time < s_NextScan) return;
            s_NextScan = Time.time + (Time.time - s_SceneStart < 60f ? 3f : 12f);
            var all = new List<Live>(s_Live.Values);
            var cam = Il2Cpp.GameManager.GetVpFPSCamera();
            var from = cam != null ? cam.transform.position : Vector3.zero;
            foreach (var live in all)
            {
                if (live.T == null || live.R == null) continue;
                if (cam != null && live.HasBounds && live.OrigBounds.SqrDistance(from) > 150f * 150f) continue;
                int before = live.Loot.Count;
                Claim(live);
                if (live.Loot.Count != before && !live.R.Hidden) PoseLoot(live);
                if (live.R.Hidden) HideLoot(live, true);
            }
        }

        public static void BeginMove(Transform t)
        {
            if (!s_Live.TryGetValue(t.Pointer, out var live)) live = NewLive(t);
            s_Carrying = true;
            Detach(live);
            Claim(live);
        }

        public static void EndMove() => s_Carrying = false;

        public static void Preview(Transform t, Vector3 pos, Quaternion rot)
        {
            if (!s_Live.TryGetValue(t.Pointer, out var live)) return;
            t.SetPositionAndRotation(pos, rot);
            SetInstanced(live);
            PoseLoot(live);
        }

        static void Restore(Live live)
        {
            var t = live.T;
            foreach (var c in live.Clones) if (c != null) Object.Destroy(c);
            live.Clones.Clear();
            foreach (var (x, was) in live.OffR) if (x != null) x.enabled = was;
            foreach (var (x, was) in live.OffL) if (x != null) x.enabled = was;
            foreach (var (x, was) in live.OffC) if (x != null) x.enabled = was;
            live.OffR.Clear(); live.OffL.Clear(); live.OffC.Clear();
            if (t != null) t.SetPositionAndRotation(live.OrigPos, live.OrigRot);
            if (live.Loot.Count > 0)
            {
                HideLoot(live, false);
                PoseLoot(live);
                foreach (var l in live.Loot) if (l.T != null) s_Claimed.Remove(l.T.Pointer);
                live.Loot.Clear();
            }
            foreach (var (pre, op) in live.Inst) InstancedHider.Remove(pre, op);
        }

        static bool HasRenderers(Transform t) => t.GetComponentsInChildren<Renderer>(true).Length > 0;

        public static bool IsStatic(Transform t)
        {
            foreach (var r in t.GetComponentsInChildren<MeshRenderer>(true)) if (r.isPartOfStaticBatch) return true;
            return false;
        }

        static void Detach(Live live)
        {
            var t = live.T;
            if (!IsStatic(t)) return;
            var lod0 = new HashSet<System.IntPtr>();
            bool hasLods = false;
            foreach (var lg in t.GetComponentsInChildren<LODGroup>(true))
            {
                var lods = lg.GetLODs();
                if (lods == null || lods.Length == 0) continue;
                hasLods = true;
                foreach (var r in lods[0].renderers) if (r != null) lod0.Add(r.Pointer);
                if (lg.enabled) { lg.enabled = false; live.OffL.Add((lg, true)); }
            }
            int made = 0, failed = 0;
            foreach (var r in t.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!r.enabled) continue;
                bool keep = !hasLods || lod0.Contains(r.Pointer);
                if (!keep) { r.enabled = false; live.OffR.Add((r, true)); continue; }
                if (!r.isPartOfStaticBatch) continue;
                var mesh = CopyMesh(r);
                if (mesh == null) { failed++; continue; }
                var go = new GameObject("TE_Moved_" + r.gameObject.name);
                go.layer = r.gameObject.layer;
                go.transform.SetParent(r.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterials = r.sharedMaterials;
                mr.shadowCastingMode = r.shadowCastingMode;
                mr.receiveShadows = r.receiveShadows;
                live.Clones.Add(go);
                r.enabled = false;
                live.OffR.Add((r, true));
                made++;
            }
            if (failed > 0)
                TerrainEditorMod.Log.Warning($"{t.name}: {failed} part(s) are fixed in the region's combined mesh and could not be copied; they stay where they were.");
            else if (made > 0) TerrainEditorMod.Debug($"{t.name}: {made} static part(s) copied so they can move.");
        }

        static Mesh CopyMesh(MeshRenderer r)
        {
            var mf = r.GetComponent<MeshFilter>();
            var combined = mf != null ? mf.sharedMesh : null;
            if (combined != null && combined.isReadable)
            {
                try { var m = Cut(r, combined); if (m != null) return m; }
                catch (System.Exception e) { TerrainEditorMod.Debug($"Cutting {r.name} out of the combined mesh: {e.Message}"); }
            }
            return ByName(r);
        }

        static Mesh Cut(MeshRenderer r, Mesh combined)
        {
            int start = r.subMeshStartIndex;
            int count = r.sharedMaterials.Length;
            if (start < 0 || start + count > combined.subMeshCount) return null;
            var root = r.staticBatchRootTransform;
            var toWorld = root != null ? root.localToWorldMatrix : Matrix4x4.identity;
            var toLocal = r.transform.worldToLocalMatrix * toWorld;
            var sv = combined.vertices; var sn = combined.normals; var st = combined.tangents; var su = combined.uv;
            bool hasN = sn.Length == sv.Length, hasT = st.Length == sv.Length, hasU = su.Length == sv.Length;
            var map = new Dictionary<int, int>();
            var v = new List<Vector3>(); var n = new List<Vector3>(); var tg = new List<Vector4>(); var uv = new List<Vector2>();
            var subs = new List<int[]>();
            for (int s = start; s < start + count; s++)
            {
                var tris = combined.GetTriangles(s);
                var outT = new int[tris.Length];
                for (int i = 0; i < tris.Length; i++)
                {
                    int k = tris[i];
                    if (!map.TryGetValue(k, out int ni))
                    {
                        ni = v.Count; map[k] = ni;
                        v.Add(toLocal.MultiplyPoint3x4(sv[k]));
                        if (hasN) n.Add(toLocal.MultiplyVector(sn[k]).normalized);
                        if (hasT) { var d = toLocal.MultiplyVector(new Vector3(st[k].x, st[k].y, st[k].z)).normalized; tg.Add(new Vector4(d.x, d.y, d.z, st[k].w)); }
                        if (hasU) uv.Add(su[k]);
                    }
                    outT[i] = ni;
                }
                subs.Add(outT);
            }
            if (v.Count == 0) return null;
            var m = new Mesh { name = r.name + "_Moved" };
            if (v.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(new Il2CppStructArray<Vector3>(v.ToArray()));
            if (hasN) m.SetNormals(new Il2CppStructArray<Vector3>(n.ToArray()));
            if (hasT) m.SetTangents(new Il2CppStructArray<Vector4>(tg.ToArray()));
            if (hasU) m.SetUVs(0, new Il2CppStructArray<Vector2>(uv.ToArray()));
            m.subMeshCount = subs.Count;
            for (int s = 0; s < subs.Count; s++) m.SetTriangles(new Il2CppStructArray<int>(subs[s]), s);
            m.RecalculateBounds();
            return m;
        }

        static Mesh ByName(MeshRenderer r)
        {
            var want = r.bounds;
            foreach (var m in Resources.FindObjectsOfTypeAll<Mesh>())
            {
                if (m == null || m.name != r.gameObject.name || m.vertexCount == 0) continue;
                var b = m.bounds;
                var w = new Bounds(r.transform.TransformPoint(b.center), Vector3.zero);
                for (int i = 0; i < 8; i++)
                {
                    var c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    w.Encapsulate(r.transform.TransformPoint(c));
                }
                if ((w.center - want.center).magnitude < 0.3f && (w.size - want.size).magnitude < 0.1f * want.size.magnitude + 0.2f) return m;
            }
            return null;
        }
    }
}
