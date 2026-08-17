using HarmonyLib;
using PeakRace.Core;

namespace PeakRace.Patch;

/// <summary>Hooks stamina spending and pass-out transitions for timed/passive effects.</summary>
[HarmonyPatch]
internal static class CampfireAbilityPatch
{
    [HarmonyPatch(typeof(Character), "UseStamina")]
    [HarmonyPrefix]
    private static void ApplyExhaustionMultiplier(Character __instance, ref float usage)
    {
        if (usage > 0f
            && CampfireAbilityManager.Instance?.IsExhausted(__instance) == true)
        {
            usage *= 1.4f;
        }
    }

    [HarmonyPatch(typeof(Character), "RPCA_PassOut")]
    [HarmonyPrefix]
    private static bool BlockPassOutDuringSecondWindImmunity(Character __instance)
    {
        return CampfireAbilityManager.Instance?.HasSecondWindImmunity(__instance) != true;
    }

    [HarmonyPatch(typeof(Character), "RPCA_PassOut")]
    [HarmonyPostfix]
    private static void TriggerSecondWind(Character __instance)
    {
        if (__instance.photonView.IsMine
            && RaceSettingsManager.Current.Mode == RespawnMode.Pvp
            && CampfireAbilityManager.Instance?.GetAbility(__instance)
                == CampfireAbility.SecondWind)
        {
            __instance.GetComponent<CampfireAbilityState>()?.RequestSecondWind();
        }
    }
}
