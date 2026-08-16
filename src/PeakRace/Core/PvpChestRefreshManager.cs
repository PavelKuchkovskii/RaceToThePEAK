using ExitGames.Client.Photon;
using HarmonyLib;
using Peak;
using Photon.Pun;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PeakRace.Core;

/// <summary>
/// Owns synchronized, per-luggage refresh deadlines for PVP mode. Deadlines
/// and spawned view IDs live in room properties so late joiners and a migrated
/// host continue the same refresh without duplicating unclaimed loot.
/// </summary>
internal sealed class PvpChestRefreshManager : MonoBehaviourPunCallbacks
{
    private const string DeadlinePrefix = "RTP.PvpChest.At.";
    private const string LootPrefix = "RTP.PvpChest.Loot.";

    private static readonly AccessTools.FieldRef<Luggage, Luggage.LuggageState> LuggageState =
        AccessTools.FieldRefAccess<Luggage, Luggage.LuggageState>("state");
    private static readonly System.Reflection.FieldInfo TrackerHistoryField =
        AccessTools.Field(typeof(SpawnedItemTracker), "_historyFromSave");
    private static readonly System.Reflection.FieldInfo TrackerItemsField =
        AccessTools.Field(typeof(SpawnedItemTracker), "_spawnedItems");
    private static readonly System.Reflection.FieldInfo TrackerHasHistoryField =
        AccessTools.Field(typeof(SpawnedItemTracker), "<HasSpawnHistory>k__BackingField");

    private readonly Dictionary<int, ChestSchedule> schedules = new();
    private readonly Dictionary<int, double> processedDeadlines = new();

    internal static PvpChestRefreshManager Instance { get; private set; }

    private static bool RefreshActive
    {
        get
        {
            RaceSettingsSnapshot settings = RaceSettingsManager.Current;
            return settings.Mode == RespawnMode.Pvp && settings.PvpChestRefreshEnabled;
        }
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    internal void RegisterOpenedChest(Luggage luggage)
    {
        if (!RefreshActive
            || !PhotonNetwork.InRoom
            || !PhotonNetwork.IsMasterClient
            || !IsRefreshable(luggage)
            || !TryGetViewId(luggage, out int viewId))
        {
            return;
        }

        double deadline = PhotonNetwork.Time
            + RaceSettingsManager.Current.PvpChestRefreshSeconds;
        ChestSchedule schedule = GetOrCreate(viewId);
        schedule.Deadline = deadline;
        schedule.LootViewIds.Clear();
        processedDeadlines.Remove(viewId);

        Hashtable properties = new()
        {
            [DeadlineKey(viewId)] = deadline,
            [LootKey(viewId)] = Array.Empty<int>()
        };
        if (!PhotonNetwork.CurrentRoom.SetCustomProperties(properties))
        {
            Plugin.Log.LogWarning($"Photon rejected the refresh timer for luggage {viewId}.");
        }
    }

    internal void RecordSpawnedLoot(Luggage luggage, IEnumerable<PhotonView> spawnedViews)
    {
        if (!RefreshActive
            || !PhotonNetwork.InRoom
            || !PhotonNetwork.IsMasterClient
            || !IsRefreshable(luggage)
            || spawnedViews == null
            || !TryGetViewId(luggage, out int viewId)
            || !schedules.TryGetValue(viewId, out ChestSchedule schedule))
        {
            return;
        }

        foreach (PhotonView view in spawnedViews)
        {
            if (view != null && view.GetComponent<Item>() != null)
            {
                schedule.LootViewIds.Add(view.ViewID);
            }
        }

        Hashtable properties = new()
        {
            [LootKey(viewId)] = schedule.LootViewIds.ToArray()
        };
        if (!PhotonNetwork.CurrentRoom.SetCustomProperties(properties))
        {
            Plugin.Log.LogWarning($"Photon rejected the tracked loot for luggage {viewId}.");
        }
    }

    private void Update()
    {
        if (!RefreshActive || !PhotonNetwork.InRoom || schedules.Count == 0)
        {
            return;
        }

        double now = PhotonNetwork.Time;
        foreach ((int viewId, ChestSchedule schedule) in schedules.ToArray())
        {
            if (schedule.Deadline <= 0d
                || schedule.Deadline > now
                || (processedDeadlines.TryGetValue(viewId, out double processed)
                    && Math.Abs(processed - schedule.Deadline) < 0.001d))
            {
                continue;
            }

            PhotonView view = PhotonNetwork.GetPhotonView(viewId);
            Luggage luggage = view != null ? view.GetComponent<Luggage>() : null;
            if (!IsRefreshable(luggage))
            {
                // Scene objects may not exist yet for a late joiner. Keep the
                // deadline pending until its luggage PhotonView is available.
                continue;
            }

            if (PhotonNetwork.IsMasterClient)
            {
                DestroyUnclaimedLoot(luggage, schedule);
                ResetSpawnTracking(luggage);
                PhotonNetwork.CurrentRoom.SetCustomProperties(new Hashtable
                {
                    [LootKey(viewId)] = Array.Empty<int>()
                });
            }

            ResetLuggageLocally(luggage);
            processedDeadlines[viewId] = schedule.Deadline;
            Plugin.Log.LogInfo($"PVP luggage {viewId} is ready to be opened again.");
        }
    }

    private static void DestroyUnclaimedLoot(Luggage luggage, ChestSchedule schedule)
    {
        HashSet<int> viewIds = new(schedule.LootViewIds);
        if (luggage.HasSpawnTracking(out SpawnedItemTracker tracker))
        {
            foreach (Item item in tracker.SpawnedItems.ToArray())
            {
                PhotonView itemView = item != null ? item.GetComponent<PhotonView>() : null;
                if (itemView != null)
                {
                    viewIds.Add(itemView.ViewID);
                }
            }
        }

        foreach (int itemViewId in viewIds)
        {
            PhotonView itemView = PhotonNetwork.GetPhotonView(itemViewId);
            if (itemView != null && itemView.GetComponent<Item>() != null)
            {
                PhotonNetwork.Destroy(itemView);
            }
        }
    }

    private static void ResetSpawnTracking(Luggage luggage)
    {
        if (!luggage.HasSpawnTracking(out SpawnedItemTracker tracker))
        {
            return;
        }

        TrackerHistoryField?.SetValue(tracker, null);
        TrackerItemsField?.SetValue(tracker, new List<PhotonView>());
        TrackerHasHistoryField?.SetValue(tracker, false);
    }

    private static void ResetLuggageLocally(Luggage luggage)
    {
        LuggageState(luggage) = Luggage.LuggageState.Closed;

        Animator animator = luggage.GetComponent<Animator>();
        if (animator != null)
        {
            // Rebind returns every luggage variant to its authored default
            // closed pose, including ones without a transition from Open.
            animator.Rebind();
            animator.Update(0f);
        }

        if (luggage.activateOnOpen != null)
        {
            luggage.activateOnOpen.SetActive(false);
        }

        if (!Luggage.ALL_LUGGAGE.Contains(luggage))
        {
            // OpenLuggageRPC waits for this registry before it rolls loot.
            Luggage.ALL_LUGGAGE.Add(luggage);
        }

        luggage.HoverExit();
    }

    private void ApplyRoomProperties(Hashtable properties)
    {
        if (properties == null)
        {
            return;
        }

        foreach (object rawKey in properties.Keys)
        {
            if (rawKey is not string key || properties[rawKey] == null)
            {
                continue;
            }

            if (TryParseViewId(key, DeadlinePrefix, out int deadlineViewId))
            {
                try
                {
                    double deadline = Convert.ToDouble(properties[rawKey]);
                    ChestSchedule schedule = GetOrCreate(deadlineViewId);
                    if (Math.Abs(schedule.Deadline - deadline) >= 0.001d)
                    {
                        schedule.Deadline = deadline;
                        processedDeadlines.Remove(deadlineViewId);
                    }
                }
                catch (Exception)
                {
                    Plugin.Log.LogWarning($"Ignored malformed PVP luggage deadline '{key}'.");
                }
            }
            else if (TryParseViewId(key, LootPrefix, out int lootViewId))
            {
                ChestSchedule schedule = GetOrCreate(lootViewId);
                schedule.LootViewIds.Clear();
                foreach (int itemViewId in ReadViewIds(properties[rawKey]))
                {
                    schedule.LootViewIds.Add(itemViewId);
                }
            }
        }
    }

    private static IEnumerable<int> ReadViewIds(object value)
    {
        if (value is int[] integers)
        {
            return integers;
        }

        if (value is object[] objects)
        {
            List<int> result = new(objects.Length);
            foreach (object entry in objects)
            {
                try
                {
                    result.Add(Convert.ToInt32(entry));
                }
                catch (Exception)
                {
                    // Ignore only the malformed element; valid tracked views
                    // still need to be cleaned up by a migrated host.
                }
            }
            return result;
        }

        return Array.Empty<int>();
    }

    private void ClearSchedules(bool clearRoomProperties)
    {
        schedules.Clear();
        processedDeadlines.Clear();

        if (!clearRoomProperties
            || !PhotonNetwork.InRoom
            || !PhotonNetwork.IsMasterClient
            || PhotonNetwork.CurrentRoom == null)
        {
            return;
        }

        Hashtable removals = new();
        foreach (object rawKey in PhotonNetwork.CurrentRoom.CustomProperties.Keys)
        {
            if (rawKey is string key
                && (key.StartsWith(DeadlinePrefix, StringComparison.Ordinal)
                    || key.StartsWith(LootPrefix, StringComparison.Ordinal)))
            {
                removals[key] = null;
            }
        }

        if (removals.Count > 0)
        {
            PhotonNetwork.CurrentRoom.SetCustomProperties(removals);
        }
    }

    private ChestSchedule GetOrCreate(int viewId)
    {
        if (!schedules.TryGetValue(viewId, out ChestSchedule schedule))
        {
            schedule = new ChestSchedule();
            schedules.Add(viewId, schedule);
        }
        return schedule;
    }

    private static bool IsRefreshable(Luggage luggage)
    {
        return luggage != null && luggage is not RespawnChest;
    }

    private static bool TryGetViewId(Luggage luggage, out int viewId)
    {
        PhotonView view = luggage != null ? luggage.GetComponent<PhotonView>() : null;
        viewId = view != null ? view.ViewID : 0;
        return viewId != 0;
    }

    private static bool TryParseViewId(string key, string prefix, out int viewId)
    {
        viewId = 0;
        return key.StartsWith(prefix, StringComparison.Ordinal)
            && int.TryParse(key.Substring(prefix.Length), out viewId);
    }

    private static string DeadlineKey(int viewId) => DeadlinePrefix + viewId;

    private static string LootKey(int viewId) => LootPrefix + viewId;

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == "Airport" || scene.name == "Title")
        {
            ClearSchedules(clearRoomProperties: true);
        }
        else if (PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom != null)
        {
            ApplyRoomProperties(PhotonNetwork.CurrentRoom.CustomProperties);
        }
    }

    public override void OnJoinedRoom()
    {
        ClearSchedules(clearRoomProperties: false);
        ApplyRoomProperties(PhotonNetwork.CurrentRoom?.CustomProperties);
    }

    public override void OnRoomPropertiesUpdate(Hashtable propertiesThatChanged)
    {
        ApplyRoomProperties(propertiesThatChanged);
    }

    public override void OnLeftRoom()
    {
        ClearSchedules(clearRoomProperties: false);
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private sealed class ChestSchedule
    {
        internal double Deadline;
        internal readonly HashSet<int> LootViewIds = new();
    }
}
