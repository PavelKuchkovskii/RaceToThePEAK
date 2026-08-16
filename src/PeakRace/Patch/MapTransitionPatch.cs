using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Peak;
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

        return codes;
    }

}
