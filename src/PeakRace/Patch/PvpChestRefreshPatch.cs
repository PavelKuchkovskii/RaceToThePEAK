using HarmonyLib;
using PeakRace.Core;
using Photon.Pun;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace PeakRace.Patch;

/// <summary>Feeds authoritative luggage opens and their spawned loot to the refresh manager.</summary>
internal static class PvpChestRefreshPatch
{
    private static readonly FieldInfo ItemActionItemField =
        AccessTools.Field(typeof(ItemActionBase), "item");

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
            AccessTools.Method(typeof(Player), nameof(Player.AddItem)),
            postfix: new HarmonyMethod(
                typeof(PvpChestRefreshPatch),
                nameof(AfterItemAdded)));
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
            AccessTools.Method(typeof(Action_RestoreHunger), nameof(Action_RestoreHunger.RunAction)),
            prefix: new HarmonyMethod(
                typeof(PvpChestRefreshPatch),
                nameof(BeforeRestoreHungerAction)));
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

    private static void AfterItemAdded(
        Player __instance,
        ItemInstanceData instanceData,
        bool __result)
    {
        if (__result)
        {
            CampfireAbilityManager.Instance?.RecordHiddenMegaLaunchFoodHolder(
                instanceData,
                __instance?.character);
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

    // PEAK commits a completed food use through Action_ReduceUses, including
    // food exhausted in a single use. Action_Consume is a later removal path
    // and is not a reliable gameplay trigger for the hidden effect.
    private static void BeforeReduceUsesAction(Action_ReduceUses __instance)
    {
        RequestFromItemAction(__instance);
    }

    // This is the semantic food-effect path and remains a fallback if a PEAK
    // prefab does not use the usual ReduceUses action.
    private static void BeforeRestoreHungerAction(Action_RestoreHunger __instance)
    {
        RequestFromItemAction(__instance);
    }

    private static void RequestFromItemAction(ItemActionBase action)
    {
        if (action == null)
        {
            return;
        }

        // PEAK initializes this protected reference from the owning Item even
        // when action components live on child GameObjects. GetComponent on
        // the action object silently missed those prefabs.
        Item item = ItemActionItemField?.GetValue(action) as Item
            ?? action.GetComponentInParent<Item>();
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

        Guid instanceId = item.data?.guid ?? Guid.Empty;
        if (instanceId == Guid.Empty)
        {
            return;
        }

        if (CampfireAbilityManager.Instance?.IsHiddenMegaLaunchFood(item) == true)
        {
            Plugin.Log.LogInfo(
                $"Detected hidden Mega Launch food use {instanceId:N} "
                + $"({item.GetName()}).");
        }

        consumer.GetComponent<CampfireAbilityState>()
            ?.RequestHiddenMegaLaunchFoodConsumption(instanceId);
    }
}
