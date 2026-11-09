using System.Collections.Generic;
using MelonLoader;
using UnityEngine;

namespace LongDarkTerrain
{
    internal static class Keys
    {
        internal static MelonPreferences_Entry<string> s_Toggle, s_NextBrush, s_LevelUp, s_LevelDown, s_Undo, s_Fast, s_Strength;
        internal static MelonPreferences_Entry<string> s_Remove, s_Move, s_Cancel, s_Copy, s_Original, s_Larger, s_Smaller, s_Fine;
        internal static MelonPreferences_Entry<int> s_PaintButton;
        internal static MelonPreferences_Entry<bool> s_UndoNeedsStrengthKey;
        static readonly Dictionary<string, KeyCode[]> s_Parsed = new Dictionary<string, KeyCode[]>();

        public static void Create(MelonPreferences_Category cat)
        {
            s_Remove = cat.CreateEntry("RemoveKey", "Delete", "Objects: removes (hides) the object in the crosshair");
            s_Move = cat.CreateEntry("MoveKey", "G", "Objects: picks the object up, puts it down");
            s_Cancel = cat.CreateEntry("CancelKey", "X", "Objects: puts a carried object back where it was");
            s_Copy = cat.CreateEntry("CopyKey", "C", "Objects: makes a copy to place");
            s_Original = cat.CreateEntry("OriginalKey", "Home", "Objects: puts the object back as the region made it");
            s_Larger = cat.CreateEntry("LargerPartKey", "LeftBracket", "Objects: selects the larger part (the whole object)");
            s_Smaller = cat.CreateEntry("SmallerPartKey", "RightBracket", "Objects: selects the smaller part");
            s_Fine = cat.CreateEntry("FineKey", "Ctrl", "Objects: held while turning for 1 degree steps");
            s_Toggle = cat.CreateEntry("ToggleKey", "F10", "Turns the brush on and off");
            s_NextBrush = cat.CreateEntry("NextBrushKey", "M", "Next brush type");
            s_LevelUp = cat.CreateEntry("LevelUpKey", "PageUp", "Raises the level used by flatten and fill");
            s_LevelDown = cat.CreateEntry("LevelDownKey", "PageDown", "Lowers the level used by flatten and fill");
            s_Undo = cat.CreateEntry("UndoKey", "Z", "Undoes the last stroke");
            s_UndoNeedsStrengthKey = cat.CreateEntry("UndoNeedsModifier", true, "Undo only works while the strength key is held (Ctrl+Z)");
            s_Fast = cat.CreateEntry("FastKey", "Shift", "Held for bigger steps (brush size, level)");
            s_Strength = cat.CreateEntry("StrengthKey", "Ctrl", "Held with the mouse wheel to change the brush strength");
            s_PaintButton = cat.CreateEntry("PaintMouseButton", 0, "Mouse button that paints (0 left, 1 right, 2 middle)");
        }

        public static bool Toggle => Down(s_Toggle);
        public static bool NextBrush => Down(s_NextBrush);
        public static bool LevelUp => Down(s_LevelUp);
        public static bool LevelDown => Down(s_LevelDown);
        public static bool Undo => Down(s_Undo) && (!s_UndoNeedsStrengthKey.Value || Held(s_Strength));
        public static bool Remove => Down(s_Remove);
        public static bool Move => Down(s_Move);
        public static bool Cancel => Down(s_Cancel);
        public static bool Copy => Down(s_Copy);
        public static bool Original => Down(s_Original);
        public static bool LargerPart => Down(s_Larger);
        public static bool SmallerPart => Down(s_Smaller);
        public static bool Fine => Held(s_Fine);
        public static bool Fast => Held(s_Fast);
        public static bool Strength => Held(s_Strength);
        public static bool Painting => Input.GetMouseButton(Mathf.Clamp(s_PaintButton.Value, 0, 4));

        public static string Name(string which) => which switch
        {
            "toggle" => Label(s_Toggle),
            "remove" => Label(s_Remove),
            "move" => Label(s_Move),
            "cancel" => Label(s_Cancel),
            "copy" => Label(s_Copy),
            "original" => Label(s_Original),
            "larger" => Label(s_Larger),
            "smaller" => Label(s_Smaller),
            "fine" => Label(s_Fine),
            "brush" => Label(s_NextBrush),
            "up" => Label(s_LevelUp),
            "down" => Label(s_LevelDown),
            "undo" => (s_UndoNeedsStrengthKey.Value ? Label(s_Strength) + "+" : "") + Label(s_Undo),
            "fast" => Label(s_Fast),
            "strength" => Label(s_Strength),
            "paint" => s_PaintButton.Value switch { 1 => L.T("Sağ tık", "Right mouse"), 2 => L.T("Orta tık", "Middle mouse"), _ => L.T("Sol tık", "Left mouse") },
            _ => "",
        };

        static string Label(MelonPreferences_Entry<string> e) => string.IsNullOrWhiteSpace(e.Value) ? "?" : e.Value.Trim();

        static KeyCode[] Codes(MelonPreferences_Entry<string> e)
        {
            string name = (e.Value ?? "").Trim();
            if (s_Parsed.TryGetValue(name, out var codes)) return codes;
            switch (name.ToLowerInvariant())
            {
                case "shift": case "leftshift": case "rightshift": codes = new[] { KeyCode.LeftShift, KeyCode.RightShift }; break;
                case "ctrl": case "control": case "leftcontrol": case "rightcontrol": codes = new[] { KeyCode.LeftControl, KeyCode.RightControl }; break;
                case "alt": case "leftalt": case "rightalt": codes = new[] { KeyCode.LeftAlt, KeyCode.RightAlt }; break;
                default:
                    codes = System.Enum.TryParse<KeyCode>(name, true, out var k) && k != KeyCode.None ? new[] { k } : new KeyCode[0];
                    if (codes.Length == 0) TerrainEditorMod.Log.Warning($"Unknown key \"{name}\" in the settings, the action is off.");
                    break;
            }
            s_Parsed[name] = codes;
            return codes;
        }

        static bool Down(MelonPreferences_Entry<string> e)
        {
            foreach (var k in Codes(e)) if (Input.GetKeyDown(k)) return true;
            return false;
        }

        static bool Held(MelonPreferences_Entry<string> e)
        {
            foreach (var k in Codes(e)) if (Input.GetKey(k)) return true;
            return false;
        }
    }
}
