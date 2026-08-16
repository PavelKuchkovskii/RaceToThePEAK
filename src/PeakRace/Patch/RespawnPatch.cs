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

    // Modes with guaranteed self-respawn must not lose the run when the last
    // living racer dies. A next-campfire strategy intentionally keeps vanilla
    // game-over because no living racer would remain to activate that fire.
    [HarmonyPatch(typeof(Character), nameof(Character.CheckEndGame))]
    [HarmonyPrefix]
    private static bool KeepRunAliveForSelfRespawn()
    {
        return RaceSettingsManager.Current.UsesNextCampfireRespawn;
    }
}
