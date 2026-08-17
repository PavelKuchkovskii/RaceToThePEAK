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
            AccessTools.Method(typeof(Action_Consume), nameof(Action_Consume.RunAction)),
            prefix: new HarmonyMethod(
                typeof(PvpChestRefreshPatch),
                nameof(BeforeConsumeAction)));
        harmony.Patch(
            AccessTools.Method(typeof(Action_ReduceUses), nameof(Action_ReduceUses.RunAction)),
            prefix: new HarmonyMethod(
                typeof(PvpChestRefreshPatch),
                nameof(BeforeReduceUsesAction)));
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
        PhotonView consumerView = PhotonNetwork.GetPhotonView(consumerID);
        Character consumer = consumerView != null
            ? consumerView.GetComponent<Character>()
            : null;
        RequestHiddenMegaLaunchFoodConsumption(__instance, consumer);
    }

    // PEAK 2.0 does not reliably reach Item.Consume on the client that owns
    // every food action. RunAction is the authoritative point at which the
    // completed consume interaction commits, before the delayed item RPC can
    // deactivate or destroy the marked object.
    private static void BeforeConsumeAction(Action_Consume __instance)
    {
        RequestFromItemAction(__instance);
    }

    // Multi-use food runs Action_ReduceUses for every completed bite and only
    // reaches Action_Consume after its final portion. Hidden food must reveal
    // itself on the first completed bite, not when the empty wrapper vanishes.
    private static void BeforeReduceUsesAction(Action_ReduceUses __instance)
    {
        RequestFromItemAction(__instance);
    }

    private static void RequestFromItemAction(ItemActionBase action)
    {
        Item item = action != null ? action.GetComponent<Item>() : null;
        RequestHiddenMegaLaunchFoodConsumption(item, item?.holderCharacter);
    }

    private static void RequestHiddenMegaLaunchFoodConsumption(
        Item item,
        Character consumer)
    {
        if (item == null
            || consumer == null
            || consumer.photonView == null
            || !consumer.photonView.IsMine)
        {
            return;
        }

        PhotonView itemView = item.GetComponent<PhotonView>();
        if (itemView == null || itemView.ViewID <= 0)
        {
            return;
        }

        consumer.GetComponent<CampfireAbilityState>()
            ?.RequestHiddenMegaLaunchFoodConsumption(itemView.ViewID);
    }

    private static void BeforeItemDestroyed(Item __instance)
    {
        CampfireAbilityManager.Instance?.ForgetHiddenMegaLaunchFood(__instance);
    }
}
