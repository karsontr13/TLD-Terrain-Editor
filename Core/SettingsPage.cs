using System.Runtime.CompilerServices;
using MelonLoader;
using UnityEngine;

namespace LongDarkTerrain
{
    internal static class SettingsRegistration
    {
        public static void TryRegister()
        {
            bool installed = false;
            foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
                if (asm.GetName().Name == "ModSettings") { installed = true; break; }
            if (!installed)
            {
                TerrainEditorMod.Log.Msg("ModSettings is not installed: the settings stay in MelonPreferences.cfg.");
                return;
            }
            try { Register(); }
            catch (System.Exception e) { TerrainEditorMod.Log.Warning($"The settings page could not be added: {e.Message}"); }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Register()
        {
            var page = new SettingsPage();
            page.CopyFromPreferences();
            page.AddToModSettings("Terrain Editor");
            TerrainEditorMod.Log.Msg("Settings page added (Options > Mod Settings).");
        }
    }

    internal enum PaintButton { Left = 0, Right = 1, Middle = 2 }

    internal class SettingsPage : ModSettings.ModSettingsBase
    {
        [ModSettings.Section("General")]
        [ModSettings.Name("Enabled")]
        [ModSettings.Description("Turns the whole mod off: no editors, and the saved terrain and object edits are not played back.")]
        public bool Enabled = true;

        [ModSettings.Name("Editor key")]
        [ModSettings.Description("Goes round: terrain brush, object editor, off.")]
        public KeyCode ToggleKey = KeyCode.F10;

        [ModSettings.Name("Undo key")]
        [ModSettings.Description("Undoes the last brush stroke or object edit.")]
        public KeyCode UndoKey = KeyCode.Z;

        [ModSettings.Name("Undo needs the strength key")]
        [ModSettings.Description("On: undo is Ctrl+Z (the strength key plus the undo key). Off: the undo key alone.")]
        public bool UndoNeedsModifier = true;

        [ModSettings.Name("Bigger steps key")]
        [ModSettings.Description("Held for bigger steps: brush size, level and turning.")]
        public KeyCode FastKey = KeyCode.LeftShift;

        [ModSettings.Name("Fine key")]
        [ModSettings.Description("Held while turning an object for 1 degree steps.")]
        public KeyCode FineKey = KeyCode.LeftControl;

        [ModSettings.Name("Level up key")]
        [ModSettings.Description("Terrain: raises the level for flatten and fill. Objects: raises the carried object.")]
        public KeyCode LevelUpKey = KeyCode.PageUp;

        [ModSettings.Name("Level down key")]
        [ModSettings.Description("Terrain: lowers the level for flatten and fill. Objects: lowers the carried object.")]
        public KeyCode LevelDownKey = KeyCode.PageDown;

        [ModSettings.Section("Terrain brush")]
        [ModSettings.Name("Paint mouse button")]
        [ModSettings.Description("The mouse button that paints while it is held.")]
        [ModSettings.Choice(new string[] { "Left", "Right", "Middle" })]
        public PaintButton PaintMouseButton = PaintButton.Left;

        [ModSettings.Name("Next brush key")]
        [ModSettings.Description("Raise, lower, flatten, fill, smooth.")]
        public KeyCode NextBrushKey = KeyCode.M;

        [ModSettings.Name("Strength key")]
        [ModSettings.Description("Held with the mouse wheel to change the brush strength.")]
        public KeyCode StrengthKey = KeyCode.LeftControl;

        [ModSettings.Section("Objects")]
        [ModSettings.Name("Remove key")]
        [ModSettings.Description("Removes (hides) the object in the crosshair.")]
        public KeyCode RemoveKey = KeyCode.Delete;

        [ModSettings.Name("Move key")]
        [ModSettings.Description("Picks the object up; press again to put it down.")]
        public KeyCode MoveKey = KeyCode.G;

        [ModSettings.Name("Cancel key")]
        [ModSettings.Description("Puts a carried object back where it was.")]
        public KeyCode CancelKey = KeyCode.X;

        [ModSettings.Name("Copy key")]
        [ModSettings.Description("Makes a copy of the object to place.")]
        public KeyCode CopyKey = KeyCode.C;

        [ModSettings.Name("Original key")]
        [ModSettings.Description("Puts the object back as the region made it.")]
        public KeyCode OriginalKey = KeyCode.Home;

        [ModSettings.Name("Larger part key")]
        [ModSettings.Description("Selects the larger part of what the crosshair touches (the whole object).")]
        public KeyCode LargerPartKey = KeyCode.LeftBracket;

        [ModSettings.Name("Smaller part key")]
        [ModSettings.Description("Selects the smaller part of what the crosshair touches.")]
        public KeyCode SmallerPartKey = KeyCode.RightBracket;

        [ModSettings.Section("Troubleshooting")]
        [ModSettings.Name("Verbose logging")]
        [ModSettings.Description("Writes extra lines to the log. Turn on only when reporting a problem.")]
        public bool VerboseLogging = false;

        static KeyCode Parse(string name, KeyCode fallback)
        {
            switch ((name ?? "").Trim().ToLowerInvariant())
            {
                case "shift": return KeyCode.LeftShift;
                case "ctrl": case "control": return KeyCode.LeftControl;
                case "alt": return KeyCode.LeftAlt;
            }
            return System.Enum.TryParse<KeyCode>(name, true, out var k) && k != KeyCode.None ? k : fallback;
        }

        internal void CopyFromPreferences()
        {
            Enabled = TerrainEditorMod.s_Enabled?.Value ?? true;
            ToggleKey = Parse(Keys.s_Toggle?.Value, KeyCode.F10);
            UndoKey = Parse(Keys.s_Undo?.Value, KeyCode.Z);
            UndoNeedsModifier = Keys.s_UndoNeedsStrengthKey?.Value ?? true;
            FastKey = Parse(Keys.s_Fast?.Value, KeyCode.LeftShift);
            FineKey = Parse(Keys.s_Fine?.Value, KeyCode.LeftControl);
            LevelUpKey = Parse(Keys.s_LevelUp?.Value, KeyCode.PageUp);
            LevelDownKey = Parse(Keys.s_LevelDown?.Value, KeyCode.PageDown);
            PaintMouseButton = (PaintButton)Mathf.Clamp(Keys.s_PaintButton?.Value ?? 0, 0, 2);
            NextBrushKey = Parse(Keys.s_NextBrush?.Value, KeyCode.M);
            StrengthKey = Parse(Keys.s_Strength?.Value, KeyCode.LeftControl);
            RemoveKey = Parse(Keys.s_Remove?.Value, KeyCode.Delete);
            MoveKey = Parse(Keys.s_Move?.Value, KeyCode.G);
            CancelKey = Parse(Keys.s_Cancel?.Value, KeyCode.X);
            CopyKey = Parse(Keys.s_Copy?.Value, KeyCode.C);
            OriginalKey = Parse(Keys.s_Original?.Value, KeyCode.Home);
            LargerPartKey = Parse(Keys.s_Larger?.Value, KeyCode.LeftBracket);
            SmallerPartKey = Parse(Keys.s_Smaller?.Value, KeyCode.RightBracket);
            VerboseLogging = TerrainEditorMod.s_Verbose?.Value ?? false;
            RefreshGUI();
        }

        protected override void OnConfirm()
        {
            Set(TerrainEditorMod.s_Enabled, Enabled);
            Set(Keys.s_Toggle, ToggleKey.ToString());
            Set(Keys.s_Undo, UndoKey.ToString());
            Set(Keys.s_UndoNeedsStrengthKey, UndoNeedsModifier);
            Set(Keys.s_Fast, FastKey.ToString());
            Set(Keys.s_Fine, FineKey.ToString());
            Set(Keys.s_LevelUp, LevelUpKey.ToString());
            Set(Keys.s_LevelDown, LevelDownKey.ToString());
            Set(Keys.s_PaintButton, (int)PaintMouseButton);
            Set(Keys.s_NextBrush, NextBrushKey.ToString());
            Set(Keys.s_Strength, StrengthKey.ToString());
            Set(Keys.s_Remove, RemoveKey.ToString());
            Set(Keys.s_Move, MoveKey.ToString());
            Set(Keys.s_Cancel, CancelKey.ToString());
            Set(Keys.s_Copy, CopyKey.ToString());
            Set(Keys.s_Original, OriginalKey.ToString());
            Set(Keys.s_Larger, LargerPartKey.ToString());
            Set(Keys.s_Smaller, SmallerPartKey.ToString());
            Set(TerrainEditorMod.s_Verbose, VerboseLogging);
            MelonPreferences.Save();
            TerrainEditorMod.Log.Msg("Saved from the settings page.");
        }

        static void Set<T>(MelonPreferences_Entry<T> entry, T value)
        {
            if (entry != null && !Equals(entry.Value, value)) entry.Value = value;
        }
    }
}
