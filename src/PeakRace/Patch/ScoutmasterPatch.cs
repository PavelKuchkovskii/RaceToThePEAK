using HarmonyLib;

namespace PeakRace.Patch;

[HarmonyPatch(typeof(ScoutmasterSpawner), "SpawnScoutmaster")]
internal static class ScoutmasterPatch
{
    [HarmonyPrefix]
    private static bool DisableScoutmasterSpawn()
    {
        Plugin.Log.LogDebug("Scoutmaster spawn blocked.");
        return false;
    }
}
