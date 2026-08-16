using HarmonyLib;
using PeakRace.Core;
using Photon.Pun;
using System.Collections.Generic;
using UnityEngine;

namespace PeakRace.Patch;

/// <summary>
/// Keeps PVP attribution at the dart RPC boundary. Only the owner of the hit
/// character confirms that this dart crossed the pass-out threshold; the
/// manager then mirrors that confirmation to the other clients.
/// </summary>
[HarmonyPatch]
internal static class PvpBlowgunPatch
{
    [HarmonyPatch(typeof(Action_RaycastDart), "RPC_DartImpact")]
    [HarmonyPrefix]
    private static void BeforeDartImpact(
        Action_RaycastDart __instance,
        int characterID,
        out Character __state)
    {
        __state = default;
        if (characterID < 0 || PvpBlowgunManager.Instance == null)
        {
            return;
        }

        PhotonView targetView = PhotonNetwork.GetPhotonView(characterID);
        Character target = targetView != null ? targetView.GetComponent<Character>() : null;
        if (target == null || target.data == null || target.refs?.afflictions == null)
        {
            return;
        }

        bool isPvpBlowgun = __instance.GetComponentInParent<PvpBlowgunMarker>() != null;
        if (!isPvpBlowgun)
        {
            // A later hit by PEAK's normal blowgun becomes the new source of
            // drowsiness and must never inherit PVP attribution.
            PvpBlowgunManager.Instance.ClearAttribution(target);
            return;
        }

        bool shouldConfirm = RaceSettingsManager.Current.Mode == RespawnMode.Pvp
            && target.photonView.IsMine
            && !target.isBot
            && !target.data.dead
            && !target.data.passedOut
            && !target.data.fullyPassedOut
            && !target.data.isCarried
            && !target.refs.afflictions.shouldPassOut;
        if (shouldConfirm)
        {
            __state = target;
        }
    }

    [HarmonyPatch(typeof(Action_RaycastDart), "RPC_DartImpact")]
    [HarmonyPostfix]
    private static void AfterDartImpact(Character __state)
    {
        Character target = __state;
        if (target == null
            || target.data == null
            || target.refs?.afflictions == null
            || target.data.dead
            || target.data.passedOut
            || target.data.fullyPassedOut
            || !target.refs.afflictions.shouldPassOut)
        {
            return;
        }

        PvpBlowgunManager.Instance?.ConfirmLocalPvpHit(target);
    }

    [HarmonyPatch(typeof(Character), "RPCA_PassOut")]
    [HarmonyPrefix]
    private static bool RedirectConfirmedPvpKnockout(Character __instance)
    {
        if (RaceSettingsManager.Current.Mode != RespawnMode.Pvp
            || PvpBlowgunManager.Instance == null
            || !PvpBlowgunManager.Instance.ConsumeConfirmedHit(__instance))
        {
            return true;
        }

        // PEAK's blowgun applies a full Drowsy bar. Requiring that signature
        // here prevents a cured dart from claiming a later hunger/fall pass-out
        // that merely happened before the short attribution token expired.
        if (__instance.refs.afflictions.GetCurrentStatus(
                CharacterAfflictions.STATUSTYPE.Drowsy) < 0.99f)
        {
            return true;
        }

        // The scout flag remains the highest-priority recovery mechanic. Let
        // PEAK complete its normal pass-out/death flow so TryCheckpoint uses it.
        if (__instance.WillDoCheckpoint(out _))
        {
            Plugin.Log.LogInfo(
                $"PVP knockout did not redirect {__instance.characterName}: a scout flag has priority.");
            return true;
        }

        // Preserve vanilla pass-out inventory loss, but skip the fade/corpse
        // flow and reset the transition state before the networked revive.
        __instance.refs.items.DropAllItems(includeBackpack: false);
        __instance.data.passOutValue = 0f;
        __instance.data.passedOut = false;
        __instance.data.fullyPassedOut = false;
        __instance.data.deathTimer = 0f;
        RaceRespawnController.Instance?.HandlePvpKnockout(__instance);
        return false;
    }

    [HarmonyPatch(typeof(Character), "RPCA_UnPassOut")]
    [HarmonyPostfix]
    private static void ClearAttributionAfterRecovery(Character __instance)
    {
        PvpBlowgunManager.Instance?.ClearAttribution(__instance);
    }

    [HarmonyPatch(typeof(Character), "RPCA_ReviveAtPosition")]
    [HarmonyPostfix]
    private static void ClearAttributionAfterRevive(Character __instance)
    {
        PvpBlowgunManager.Instance?.ClearAttribution(__instance);
    }
}

/// <summary>Replaces one ordinary luggage reward with the PVP blowgun at 50% odds.</summary>
[HarmonyPatch(typeof(Spawner), "GetObjectsToSpawn")]
internal static class PvpLuggageLootPatch
{
    [HarmonyPostfix]
    private static void AddPvpBlowgunToLuggagePool(
        Spawner __instance,
        ref List<GameObject> __result)
    {
        if (RaceSettingsManager.Current.Mode != RespawnMode.Pvp
            || !PhotonNetwork.IsMasterClient
            || __instance is not Luggage
            || __instance is RespawnChest
            || __result == null
            || __result.Count == 0
            || Random.value >= 0.5f
            || PvpBlowgunManager.Instance == null
            || !PvpBlowgunManager.Instance.IsReady)
        {
            return;
        }

        int replacementIndex = Random.Range(0, __result.Count);
        __result[replacementIndex] = PvpBlowgunManager.Instance.PrefabTemplate;
        Plugin.Log.LogInfo($"PVP blowgun selected for luggage {__instance.gameObject.name}.");
    }
}
