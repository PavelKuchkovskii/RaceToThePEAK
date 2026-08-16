using HarmonyLib;
using Photon.Pun;
using PeakRace.Core;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using UnityEngine;

namespace PeakRace.Patch;

[HarmonyPatch]
internal static class MapPatch
{
    // PEAK 2.x added a third (segment) argument to RPCA_ReviveAtPosition.
    // Only the master client sends the revive RPC so each player is revived once.
    [HarmonyPatch(typeof(Campfire), "Light_Rpc")]
    [HarmonyPrefix]
    private static void ReviveAtLitCampfire(Campfire __instance, bool updateSegment)
    {
        if (!updateSegment
            || !PhotonNetwork.IsMasterClient
            || !RaceSettingsManager.Current.UsesNextCampfireRespawn)
        {
            return;
        }

        // PlayerHandler is used instead of Character.AllCharacters so bots and
        // modded NPCs are ignored while PEAK Unlimited players are all included.
        List<Character> deadCharacters = PlayerHandler.GetAllPlayerCharacters()
            .Where(character => character != null
                && character.data != null
                && character.data.dead)
            .ToList();

        if (deadCharacters.Count == 0)
        {
            return;
        }

        int completedSegment = MapHandler.Exists
            ? (int)MapHandler.CurrentSegmentNumber
            : Mathf.Max(0, (int)__instance.advanceToSegment - 1);

        for (int index = 0; index < deadCharacters.Count; index++)
        {
            // A golden-angle spiral avoids stacking players in large Unlimited lobbies.
            float angle = index * 2.39996323f;
            float radius = 2f + Mathf.Sqrt(index) * 0.65f;
            Vector3 offset = new Vector3(Mathf.Cos(angle) * radius, 5f, Mathf.Sin(angle) * radius);
            Character character = deadCharacters[index];

            character.photonView.RPC(
                "RPCA_ReviveAtPosition",
                RpcTarget.All,
                __instance.transform.position + offset,
                true,
                completedSegment);
        }

        Plugin.Log.LogInfo($"Reviving {deadCharacters.Count} player(s) at the lit campfire.");
    }

    // Restart timers only for a real segment transition. Light_Rpc is also used
    // to synchronize an already-lit fire to players who join late.
    [HarmonyPatch(typeof(Campfire), "Light_Rpc")]
    [HarmonyPostfix]
    private static void RestartTimers(bool updateSegment)
    {
        if (!updateSegment)
        {
            return;
        }

        foreach (Character character in Character.AllCharacters)
        {
            if (character == null || character.isBot)
            {
                continue;
            }

            CharacterTeamInfo teamInfo = character.GetComponent<CharacterTeamInfo>();
            if (teamInfo != null)
            {
                teamInfo.timeOn = true;
            }
        }
    }

    // A racer can light a fire without waiting for every other living player.
    // This bypass is scoped to lighting; the original range checks still control
    // morale, resting and whether a lit fire can burn out.
    [HarmonyPatch(typeof(Campfire), nameof(Campfire.GetInteractionText))]
    [HarmonyPostfix]
    private static void AllowSoloCampfirePrompt(Campfire __instance, ref string __result)
    {
        if (__instance.state == Campfire.FireState.Off)
        {
            __result = LocalizedText.GetText("LIGHT");
        }
    }

    [HarmonyPatch(typeof(Campfire), nameof(Campfire.Interact_CastFinished))]
    [HarmonyPrefix]
    private static bool AllowSoloCampfireActivation(Campfire __instance)
    {
        if (__instance.state != Campfire.FireState.Off)
        {
            return true;
        }

        __instance.DebugLight();
        return false;
    }

    [HarmonyPatch(typeof(RespawnChest), nameof(RespawnChest.GetInteractionText))]
    [HarmonyPostfix]
    private static void DisableRespawnStatueText(ref string __result)
    {
        __result = LocalizedText.GetText("TOUCH");
    }

    // Force the statue down its normal item-spawn branch. The old mod removed
    // fixed IL indices, which no longer correspond to this check in PEAK 2.x.
    [HarmonyPatch(typeof(RespawnChest), nameof(RespawnChest.SpawnItems))]
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> MakeRespawnStatuesItemOnly(
        IEnumerable<CodeInstruction> instructions)
    {
        List<CodeInstruction> codes = instructions.ToList();
        var deadCheck = AccessTools.Method(typeof(Character), nameof(Character.PlayerIsDeadOrDown));
        int replacements = 0;

        foreach (CodeInstruction code in codes)
        {
            if (!code.Calls(deadCheck))
            {
                continue;
            }

            code.opcode = OpCodes.Ldc_I4_0;
            code.operand = null;
            replacements++;
        }

        if (replacements == 0)
        {
            Plugin.Log.LogError("Could not locate the respawn check in RespawnChest.SpawnItems.");
        }
        else
        {
            Plugin.Log.LogInfo("Respawn statues now always spawn items.");
        }

        return codes;
    }

    // Safety net for any other game path that invokes the private revive helper.
    [HarmonyPatch(typeof(RespawnChest), "RespawnAllPlayersHere")]
    [HarmonyPrefix]
    private static bool BlockStatueRevive()
    {
        return false;
    }
}
