using HarmonyLib;
using PeakRace.Core;
using Photon.Pun;
using System.Collections.Generic;

namespace PeakRace.Patch;

/// <summary>Feeds authoritative luggage opens and their spawned loot to the refresh manager.</summary>
internal static class PvpChestRefreshPatch
{
    internal static void Apply(Harmony harmony)
    {
        harmony.Patch(
            AccessTools.Method(typeof(Luggage), "OpenLuggageRPC"),
            postfix: new HarmonyMethod(
                typeof(PvpChestRefreshPatch),
                nameof(AfterLuggageOpened)));
        harmony.Patch(
            AccessTools.Method(typeof(Spawner), nameof(Spawner.SpawnItems)),
            postfix: new HarmonyMethod(
                typeof(PvpChestRefreshPatch),
                nameof(AfterItemsSpawned)));
        harmony.Patch(
            AccessTools.Method(typeof(Item), nameof(Item.Consume)),
            prefix: new HarmonyMethod(
                typeof(PvpChestRefreshPatch),
                nameof(BeforeItemConsumed)));
        harmony.Patch(
            AccessTools.Method(typeof(Item), "OnDestroy"),
            prefix: new HarmonyMethod(
                typeof(PvpChestRefreshPatch),
                nameof(BeforeItemDestroyed)));
    }

    private static void AfterLuggageOpened(Luggage __instance, bool spawnItems)
    {
        if (spawnItems)
        {
            PvpChestRefreshManager.Instance?.RegisterOpenedChest(__instance);
        }
    }

    private static void AfterItemsSpawned(
        Spawner __instance,
        List<PhotonView> __result)
    {
        if (__instance is Luggage luggage && luggage is not RespawnChest)
        {
            PvpChestRefreshManager.Instance?.RecordSpawnedLoot(luggage, __result);
            CampfireAbilityManager.Instance?.RecordHiddenMegaLaunchFood(
                luggage,
                __result);
        }
    }

    private static void BeforeItemConsumed(Item __instance, int consumerID)
    {
        CampfireAbilityManager.Instance?.HandleHiddenMegaLaunchFoodConsumed(
            __instance,
            consumerID);
    }

    private static void BeforeItemDestroyed(Item __instance)
    {
        CampfireAbilityManager.Instance?.ForgetHiddenMegaLaunchFood(__instance);
    }
}
