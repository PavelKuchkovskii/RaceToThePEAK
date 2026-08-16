using HarmonyLib;
using PeakRace.Core;

namespace PeakRace.Patch;

[HarmonyPatch]
internal static class RespawnPatch
{
    [HarmonyPatch(typeof(Character), nameof(Character.RPCA_Die))]
    [HarmonyPostfix]
    private static void ApplyRespawnStrategy(Character __instance)
    {
        RaceRespawnController.Instance?.HandleConfirmedDeath(__instance);
    }

    [HarmonyPatch(typeof(Character), "RPCA_ReviveAtPosition")]
    [HarmonyPostfix]
    private static void ResumeTimerAfterSelfRespawn(Character __instance)
    {
        if (RaceSettingsManager.Current.Mode == RespawnMode.NextCampfire)
        {
            return;
        }

        CharacterTeamInfo teamInfo = __instance.GetComponent<CharacterTeamInfo>();
        if (teamInfo != null)
        {
            teamInfo.timeOn = true;
        }
    }

    // Preserve PEAK's authoritative full-lobby wipe in every mode. Partial
    // deaths are handled after this check by the selected respawn strategy.
    [HarmonyPatch(typeof(Character), nameof(Character.CheckEndGame))]
    [HarmonyPrefix]
    private static bool PreserveFullLobbyWipe()
    {
        return true;
    }
}
