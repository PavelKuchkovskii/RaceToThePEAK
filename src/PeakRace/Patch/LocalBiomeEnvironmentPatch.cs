using HarmonyLib;
using Peak;
using PeakRace.Core;
using UnityEngine;

namespace PeakRace.Patch;

/// <summary>
/// Prevents the globally unlocked segment from changing the visual environment
/// for racers who have not physically entered it yet.
/// </summary>
internal static class LocalBiomeEnvironmentPatch
{
    private static readonly int FogSafeZoneId = Shader.PropertyToID("FogSafeZone");
    private static readonly int GloomVisibilityOffsetId = Shader.PropertyToID("GloomVisibiltyOffset");

    [HarmonyPatch(typeof(DayNightManager), nameof(DayNightManager.BlendProfiles))]
    [HarmonyPrefix]
    private static void SelectObservedCharactersProfile(ref DayNightProfile profile)
    {
        if (LocalBiomeEnvironmentController.TrySelectLocalProfile(profile, out DayNightProfile selectedProfile))
        {
            profile = selectedProfile;
        }
    }

    [HarmonyPatch(typeof(ForceFogShaderHeight), "Update")]
    [HarmonyPrefix]
    private static bool IsHeightFogOwnedByObservedBiome(ForceFogShaderHeight __instance)
    {
        return LocalBiomeEnvironmentController.ShouldRunSegmentVisual(__instance);
    }

    [HarmonyPatch(typeof(FogSafeZoneManager), "Update")]
    [HarmonyPrefix]
    private static bool IsGloomSafeZoneOwnedByObservedBiome(FogSafeZoneManager __instance)
    {
        if (LocalBiomeEnvironmentController.ShouldRunSegmentVisual(__instance))
        {
            return true;
        }

        // FogSafeZoneManager normally disappears with its segment. Explicitly
        // clear its last global values while the retained Gloom is out of view.
        Shader.SetGlobalFloat(FogSafeZoneId, 0f);
        Shader.SetGlobalFloat(GloomVisibilityOffsetId, 0f);
        return false;
    }
}
