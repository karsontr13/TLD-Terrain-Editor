using System.Collections.Generic;
using Il2Cpp;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace LongDarkTerrain
{
    internal static class L
    {
        static string s_Language;
        static bool s_Turkish;

        public static string T(string tr, string en)
        {
            string lang = null;
            try { lang = Localization.Language; } catch { }
            if (lang != s_Language) { s_Language = lang; s_Turkish = lang == "Turkish"; }
            return s_Turkish ? tr : en;
        }
    }

    internal static class Hud
    {
        public static void Message(string text) => HUDMessage.AddMessage(text, false, false);
    }

    internal static class Overlay
    {
        static GUIStyle s_Big, s_Small, s_Note;
        static Material s_LineMat;

        public static bool Aim(out Vector3 point, float reach = 400f)
        {
            point = Vector3.zero;
            var cam = GameManager.GetVpFPSCamera();
            if (cam == null) return false;
            var from = cam.transform;
            float best = float.MaxValue;
            bool any = false;
            foreach (var h in Physics.RaycastAll(from.position, from.forward, reach, ~0, QueryTriggerInteraction.Ignore))
            {
                if (h.distance >= best || IsOwn(h.collider.transform)) continue;
                best = h.distance;
                point = h.point;
                any = true;
            }
            return any;
        }

        static bool IsOwn(Transform t)
        {
            for (var x = t; x != null; x = x.parent)
                if (x.name.StartsWith("CHARACTER_FPSPlayer") || x.name.StartsWith("TE_")) return true;
            return false;
        }

        public static float GroundHeight(Vector3 p, float fallback)
        {
            foreach (var t in Terrain.activeTerrains)
            {
                var tp = t.transform.position;
                var size = t.terrainData.size;
                if (p.x >= tp.x && p.x <= tp.x + size.x && p.z >= tp.z && p.z <= tp.z + size.z)
                    return t.SampleHeight(p) + tp.y;
            }
            return fallback;
        }

        public static List<Vector3> Ring(Vector3 c, float r, int n)
        {
            var pts = new List<Vector3>(n);
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2f / n;
                var p = c + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                p.y = GroundHeight(p, c.y) + 0.15f;
                pts.Add(p);
            }
            return pts;
        }

        static void Styles()
        {
            if (s_Big != null) return;
            s_Big = new GUIStyle(GUI.skin.label) { fontSize = 18, alignment = TextAnchor.UpperCenter };
            s_Big.normal.textColor = Color.white;
            s_Small = new GUIStyle(GUI.skin.label) { fontSize = 14, alignment = TextAnchor.UpperCenter };
            s_Small.normal.textColor = new Color(0.85f, 0.9f, 1f);
            s_Note = new GUIStyle(GUI.skin.label) { fontSize = 14, alignment = TextAnchor.UpperCenter };
            s_Note.normal.textColor = new Color(1f, 0.75f, 0.3f);
        }

        public static void Panel(string head, string info, string note, string keys)
        {
            Styles();
            float w = Screen.width;
            GUI.Label(new Rect(w * 0.5f - 12f, Screen.height * 0.5f - 14f, 24f, 28f), "+", s_Big);
            GUI.Label(new Rect(0f, 30f, w, 28f), head + "  " + info, s_Big);
            if (!string.IsNullOrEmpty(note)) GUI.Label(new Rect(0f, 58f, w, 24f), note, s_Note);
            var lines = keys.Split('\n');
            for (int i = 0; i < lines.Length; i++)
                GUI.Label(new Rect(0f, Screen.height - 60f - (lines.Length - 1 - i) * 20f, w, 22f), lines[i], s_Small);
        }

        static Material LineMaterial()
        {
            if (s_LineMat != null) return s_LineMat;
            Shader sh = null;
            foreach (var n in new[] { "Hidden/Internal-Colored", "Sprites/Default", "Unlit/Color", "Legacy Shaders/Particles/Alpha Blended Premultiply" })
            {
                sh = Shader.Find(n);
                if (sh != null) break;
            }
            if (sh == null) return null;
            s_LineMat = new Material(sh) { hideFlags = HideFlags.DontUnloadUnusedAsset };
            try { s_LineMat.SetInt("_ZWrite", 0); } catch { }
            return s_LineMat;
        }

        internal sealed class Line
        {
            readonly string m_Name;
            readonly float m_Width;
            GameObject m_Go;
            LineRenderer m_Lr;
            Material m_Mat;

            public Line(string name, float width) { m_Name = name; m_Width = width; }

            void Make()
            {
                if (m_Go != null) return;
                m_Go = new GameObject("TE_" + m_Name);
                Object.DontDestroyOnLoad(m_Go);
                m_Lr = m_Go.AddComponent<LineRenderer>();
                m_Lr.useWorldSpace = true;
                m_Lr.widthMultiplier = m_Width;
                m_Lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                m_Lr.receiveShadows = false;
                var baseMat = LineMaterial();
                if (baseMat != null) { m_Mat = new Material(baseMat); m_Lr.sharedMaterial = m_Mat; }
            }

            public void Set(IList<Vector3> pts, Color c)
            {
                if (pts == null || pts.Count < 2) { Hide(); return; }
                Make();
                m_Go.SetActive(true);
                m_Lr.loop = true;
                var arr = new Vector3[pts.Count];
                for (int i = 0; i < arr.Length; i++) arr[i] = pts[i];
                m_Lr.positionCount = arr.Length;
                m_Lr.SetPositions(new Il2CppStructArray<Vector3>(arr));
                m_Lr.startColor = c;
                m_Lr.endColor = c;
                if (m_Mat != null) m_Mat.color = c;
            }

            public void Hide()
            {
                if (m_Go != null) m_Go.SetActive(false);
            }
        }
    }
}
