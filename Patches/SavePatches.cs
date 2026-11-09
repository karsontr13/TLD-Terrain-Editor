using HarmonyLib;
using Il2Cpp;

namespace LongDarkTerrain
{
    [HarmonyPatch(typeof(SaveGameSystem), nameof(SaveGameSystem.DeleteSaveFiles))]
    internal static class SaveGameSystem_DeleteSaveFiles
    {
        private static void Postfix(string name) => StrokeStore.Discard(name);
    }

    [HarmonyPatch(typeof(SaveGameSystem), nameof(SaveGameSystem.GenerateNewSaveName))]
    internal static class SaveGameSystem_GenerateNewSaveName
    {
        private static void Postfix() => StrokeStore.Discard(StrokeStore.CurrentSaveName());
    }
}
