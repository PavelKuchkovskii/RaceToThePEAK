using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Peak;
using PeakRace.Core;
using UnityEngine;

namespace PeakRace.Patch;

internal static class MapTransitionPatch
{
    public static void Apply(Harmony harmony)
    {
        MethodInfo moveNext = FindSegmentTransitionCoroutine();
        if (moveNext == null)
        {
            Plugin.Log.LogError("Could not find MapHandler's segment transition coroutine; previous biomes will not be preserved.");
            return;
        }

        HarmonyMethod transpiler = new HarmonyMethod(
            AccessTools.Method(typeof(MapTransitionPatch), nameof(KeepPreviousBiomeActive)));
        harmony.Patch(moveNext, transpiler: transpiler);
    }

    private static MethodInfo FindSegmentTransitionCoroutine()
    {
        foreach (Type nestedType in GetNestedTypes(typeof(MapHandler)))
        {
            if (!nestedType.Name.Contains("ShowNextSegmentCoroutine"))
            {
                continue;
            }

            MethodInfo moveNext = AccessTools.Method(nestedType, "MoveNext");
            if (moveNext != null)
            {
                return moveNext;
            }
        }

        return null;
    }

    private static IEnumerable<Type> GetNestedTypes(Type parent)
    {
        foreach (Type nestedType in parent.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
        {
            yield return nestedType;

            foreach (Type descendant in GetNestedTypes(nestedType))
            {
                yield return descendant;
            }
        }
    }

    private static IEnumerable<CodeInstruction> KeepPreviousBiomeActive(
        IEnumerable<CodeInstruction> instructions)
    {
        List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
        MethodInfo setActive = AccessTools.Method(
            typeof(GameObject),
            nameof(GameObject.SetActive),
            new[] { typeof(bool) });
        MethodInfo getSegmentParent = AccessTools.PropertyGetter(
            typeof(MapHandler.MapSegment),
            nameof(MapHandler.MapSegment.segmentParent));

        MethodInfo waitForFogCatchUp = AccessTools.Method(
            typeof(OrbFogHandler),
            nameof(OrbFogHandler.WaitForFogCatchUp));
        MethodInfo waitForReveal = AccessTools.Method(
            typeof(OrbFogHandler),
            nameof(OrbFogHandler.WaitForReveal));
        MethodInfo setFogOrigin = AccessTools.Method(
            typeof(OrbFogHandler),
            nameof(OrbFogHandler.SetFogOrigin));
        MethodInfo waitForFogCatchUpByPolicy = AccessTools.Method(
            typeof(MapTransitionPatch),
            nameof(WaitForFogCatchUpByPolicy));
        MethodInfo waitForRevealByPolicy = AccessTools.Method(
            typeof(MapTransitionPatch),
            nameof(WaitForRevealByPolicy));
        MethodInfo setFogOriginByPolicy = AccessTools.Method(
            typeof(MapTransitionPatch),
            nameof(SetFogOriginByPolicy));

        int catchUpReplacements = 0;
        int revealReplacements = 0;
        int originReplacements = 0;
        foreach (CodeInstruction code in codes)
        {
            if (code.Calls(waitForFogCatchUp))
            {
                code.opcode = OpCodes.Call;
                code.operand = waitForFogCatchUpByPolicy;
                catchUpReplacements++;
            }
            else if (code.Calls(waitForReveal))
            {
                code.opcode = OpCodes.Call;
                code.operand = waitForRevealByPolicy;
                revealReplacements++;
            }
            else if (code.Calls(setFogOrigin))
            {
                code.opcode = OpCodes.Call;
                code.operand = setFogOriginByPolicy;
                originReplacements++;
            }
        }

        bool patched = false;
        for (int index = 1; index < codes.Count; index++)
        {
            if (!codes[index].Calls(setActive) || codes[index - 1].opcode != OpCodes.Ldc_I4_0)
            {
                continue;
            }

            int searchStart = Math.Max(0, index - 16);
            for (int candidate = index - 2; candidate >= searchStart; candidate--)
            {
                if (!codes[candidate].Calls(getSegmentParent))
                {
                    continue;
                }

                // Defer old-segment removal to BiomeLifecycleController, which
                // can account for lagging teams and valid respawn destinations.
                codes[index - 1].opcode = OpCodes.Ldc_I4_1;
                codes[index - 1].operand = null;
                patched = true;
                break;
            }

            if (patched)
            {
                break;
            }
        }

        if (patched)
        {
            Plugin.Log.LogInfo("Previous-biome removal is managed by the race lifecycle policy.");
        }
        else
        {
            Plugin.Log.LogError("Could not patch the previous-biome deactivation call.");
        }

        if (catchUpReplacements == 1
            && revealReplacements == 1
            && originReplacements == 1)
        {
            Plugin.Log.LogInfo(
                "Map transitions now defer OrbFog to the scoped progression policy.");
        }
        else
        {
            Plugin.Log.LogError(
                "Could not fully patch map-transition OrbFog calls: "
                + $"catch-up={catchUpReplacements}, reveal={revealReplacements}, "
                + $"origin={originReplacements}.");
        }

        return codes;
    }

    private static IEnumerator WaitForFogCatchUpByPolicy(OrbFogHandler fog)
    {
        return OrbFogProgressionController.ManagesCurrentTransition
            ? EmptyRoutine()
            : fog.WaitForFogCatchUp();
    }

    private static IEnumerator WaitForRevealByPolicy(OrbFogHandler fog)
    {
        return OrbFogProgressionController.ManagesCurrentTransition
            ? EmptyRoutine()
            : fog.WaitForReveal();
    }

    private static void SetFogOriginByPolicy(OrbFogHandler fog, int originId)
    {
        if (!OrbFogProgressionController.ManagesCurrentTransition)
        {
            fog.SetFogOrigin(originId);
        }
    }

    private static IEnumerator EmptyRoutine()
    {
        yield break;
    }

}
