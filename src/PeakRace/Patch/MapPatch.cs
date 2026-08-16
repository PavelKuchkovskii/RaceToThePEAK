using HarmonyLib;
using PeakRace.Core;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;

namespace PeakRace.Patch;

[HarmonyPatch]
internal static class MapPatch
{
    // The prompt is derived from the synchronized waiting policy. A physically
    // lit fire remains claimable by a team that has not completed it yet.
    [HarmonyPatch(typeof(Campfire), nameof(Campfire.GetInteractionText))]
    [HarmonyPostfix]
    private static void AllowSoloCampfirePrompt(Campfire __instance, ref string __result)
    {
        CampfireProgressionController progression = CampfireProgressionController.Instance;
        Character character = Character.localCharacter;
        if (progression == null
            || character == null
            || (__instance.state != Campfire.FireState.Off
                && !progression.RequiresCompletion(character, __instance)))
        {
            return;
        }

        if (progression.CanCompleteCampfire(character, __instance, out string rejectionText))
        {
            __result = __instance.state == Campfire.FireState.Off
                ? LocalizedText.GetText("LIGHT")
                : "COMPLETE CHECKPOINT";
        }
        else
        {
            __result = rejectionText;
        }
    }

    [HarmonyPatch(typeof(Campfire), nameof(Campfire.Interact_CastFinished))]
    [HarmonyPrefix]
    private static bool ApplyCampfireWaitingPolicy(Campfire __instance, Character interactor)
    {
        CampfireProgressionController progression = CampfireProgressionController.Instance;
        if (progression == null
            || (__instance.state != Campfire.FireState.Off
                && !progression.RequiresCompletion(interactor, __instance)))
        {
            return true;
        }

        progression.RequestCompletion(interactor, __instance);
        return false;
    }

    [HarmonyPatch(typeof(Campfire), nameof(Campfire.IsInteractible))]
    [HarmonyPostfix]
    private static void AllowUncompletedCheckpointInteraction(
        Campfire __instance,
        Character interactor,
        ref bool __result)
    {
        if (CampfireProgressionController.Instance?.RequiresCompletion(
            interactor,
            __instance) == true)
        {
            __result = true;
        }
    }

    [HarmonyPatch(typeof(Campfire), nameof(Campfire.IsConstantlyInteractable))]
    [HarmonyPostfix]
    private static void AllowUncompletedCheckpointCast(
        Campfire __instance,
        Character interactor,
        ref bool __result)
    {
        if (CampfireProgressionController.Instance?.RequiresCompletion(
            interactor,
            __instance) == true)
        {
            __result = true;
        }
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
