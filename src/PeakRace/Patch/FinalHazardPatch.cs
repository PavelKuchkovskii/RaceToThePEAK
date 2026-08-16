using HarmonyLib;
using Peak;
using PeakRace.Core;

namespace PeakRace.Patch;

internal static class FinalHazardPatch
{
    [HarmonyPatch(typeof(LavaRising), nameof(LavaRising.LavaActive))]
    [HarmonyPostfix]
    private static void DoNotUsePersonalFinalHazardsAsGlobalSpawnLocks(
        Segment segment,
        ref bool __result)
    {
        if (segment != Segment.TheKiln)
        {
            return;
        }

        foreach (LavaRising hazard in LavaRising.ALL_LAVA)
        {
            if (!FinalHazardController.IsManagedFinalHazard(hazard))
            {
                continue;
            }

            // CharacterSpawner uses this global query to reject reconnect and
            // late-join revives. A personal field must never let the host's
            // timer decide whether another actor may spawn at base camp.
            __result = false;
            return;
        }
    }

    [HarmonyPatch(typeof(LavaRising), "Update")]
    [HarmonyPrefix]
    private static bool DrivePersonalFinalHazard(LavaRising __instance)
    {
        return !FinalHazardController.TryDrive(__instance);
    }

    [HarmonyPatch(typeof(LavaRising), nameof(LavaRising.RPC_SyncLava))]
    [HarmonyPrefix]
    private static bool IgnoreVanillaFinalHazardRpc(LavaRising __instance)
    {
        return !FinalHazardController.IsManagedFinalHazard(__instance);
    }

    [HarmonyPatch(typeof(LavaRising), nameof(LavaRising.RecieveLavaData))]
    [HarmonyPrefix]
    private static bool IgnoreVanillaFinalHazardPackage(LavaRising __instance)
    {
        return !FinalHazardController.IsManagedFinalHazard(__instance);
    }

    [HarmonyPatch(typeof(Lava), "Update")]
    [HarmonyPrefix]
    private static bool LimitFinalLavaUpdateToLocalPlayer(Lava __instance)
    {
        return FinalHazardController.ShouldProcessLocalEffect(__instance);
    }

    [HarmonyPatch(typeof(Lava), "FixedUpdate")]
    [HarmonyPrefix]
    private static bool LimitFinalLavaPhysicsToLocalPlayer(Lava __instance)
    {
        return FinalHazardController.ShouldProcessLocalEffect(__instance);
    }

    [HarmonyPatch(typeof(StatusFieldBase), "Update")]
    [HarmonyPrefix]
    private static bool LimitFinalStatusFieldToLocalPlayer(StatusFieldBase __instance)
    {
        return FinalHazardController.ShouldProcessLocalEffect(__instance);
    }

    [HarmonyPatch(typeof(StatusFieldGloom), "Update")]
    [HarmonyPrefix]
    private static bool LimitFinalGloomStatusToLocalPlayer(StatusFieldGloom __instance)
    {
        return FinalHazardController.ShouldProcessLocalEffect(__instance);
    }
}
