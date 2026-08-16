using ExitGames.Client.Photon;
using Photon.Pun;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PeakRace.Core;

/// <summary>
/// Executes room-authoritative respawns. Timed deaths are tracked on every
/// client against Photon time so a host migration does not lose the schedule.
/// </summary>
internal sealed class RaceRespawnController : MonoBehaviourPunCallbacks
{
    private const string CorpseDueKeyPrefix = "RTP.CorpseDue.";

    internal readonly struct Countdown
    {
        internal Countdown(Character character, double dueAt, int totalDelaySeconds)
        {
            Character = character;
            DueAt = dueAt;
            TotalDelaySeconds = totalDelaySeconds;
        }

        internal Character Character { get; }
        internal double DueAt { get; }
        internal int TotalDelaySeconds { get; }
    }

    private sealed class PendingRespawn
    {
        internal int ViewId;
        internal Vector3 Position;
        internal double DueAt;
        internal int TotalDelaySeconds;
        internal bool ReviveSent;
    }

    private readonly Dictionary<int, PendingRespawn> pendingRespawns = new();
    private readonly HashSet<int> immediateRevivesSent = new();
    private readonly HashSet<RaceTeamScope> teamWipeRevivesSent = new();

    internal static RaceRespawnController Instance { get; private set; }

    private static bool IsAuthority => !PhotonNetwork.InRoom || PhotonNetwork.IsMasterClient;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    internal void HandleConfirmedDeath(Character character)
    {
        if (character == null || character.isBot || character.photonView == null)
        {
            return;
        }

        // Scout flags call Character.TryCheckpoint before RPCA_Die. Reaching
        // this method therefore means the flag did not revive the player.
        if (!GetPlayerCharacters().Any(candidate => !IsActuallyDead(candidate)))
        {
            // The final real death belongs to PEAK's ordinary full-lobby wipe.
            return;
        }

        RaceSettingsSnapshot settings = RaceSettingsManager.Current;
        if (settings.UsesCorpseTimerRespawn)
        {
            ScheduleCorpseRespawn(character);
            if (IsAuthority)
            {
                PublishCorpseDeadline(character.photonView.ViewID);
            }
        }
        else if (settings.UsesPreviousCampfireRespawn && IsAuthority)
        {
            SendPreviousCampfireRespawn(character);
        }
        // Next-campfire deaths remain as bones until MapPatch observes the
        // next real campfire activation. If every racer dies, PEAK may end the run.
    }

    internal void HandleCampfireCompleted(
        Character completingCharacter,
        Campfire campfire,
        int completedCampfireIndex)
    {
        RaceSettingsSnapshot settings = RaceSettingsManager.Current;
        if (!IsAuthority
            || !settings.UsesNextCampfireRespawn
            || completingCharacter == null
            || campfire == null)
        {
            return;
        }

        RaceTeamScope completingTeam = RaceTeamScope.ForCharacter(completingCharacter);
        List<Character> waitingCharacters = GetPlayerCharacters()
            .Where(character => IsActuallyDead(character)
                && (settings.WaitMode == CampfireWaitMode.Lobby
                    || settings.WaitMode == CampfireWaitMode.Team
                        && completingTeam.Contains(character)))
            .ToList();

        for (int index = 0; index < waitingCharacters.Count; index++)
        {
            Character character = waitingCharacters[index];
            SendRevive(
                character,
                AddSpiralOffset(campfire.transform.position, index, verticalOffset: 5f),
                completedCampfireIndex);
        }

        if (waitingCharacters.Count > 0)
        {
            Plugin.Log.LogInfo(
                $"Respawning {waitingCharacters.Count} player(s) at completed "
                + $"campfire {completedCampfireIndex}.");
        }
    }

    private void Update()
    {
        string scene = SceneManager.GetActiveScene().name;
        bool runEnded = Character.AllCharacters.Any(character =>
            character != null && character.gameOver);
        if (scene == "Airport" || scene == "Title" || runEnded)
        {
            ClearState();
            return;
        }

        RaceSettingsSnapshot settings = RaceSettingsManager.Current;
        if (settings.UsesCorpseTimerRespawn)
        {
            immediateRevivesSent.Clear();
            ReconcileCorpseRespawns();
            ProcessCorpseRespawns();
        }
        else if (settings.UsesPreviousCampfireRespawn)
        {
            pendingRespawns.Clear();
            ReconcileImmediateRespawns();
        }
        else if (settings.UsesNextCampfireRespawn)
        {
            pendingRespawns.Clear();
            immediateRevivesSent.Clear();
            if (IsAuthority)
            {
                ReconcileTeamWipeRespawns();
            }
        }
        else
        {
            ClearState();
        }
    }

    private void ScheduleCorpseRespawn(Character character, double? synchronizedDueAt = null)
    {
        int viewId = character.photonView.ViewID;
        int delaySeconds = RaceSettingsManager.Current.CorpseRespawnDelaySeconds;
        Vector3 deathPosition = character.LastLivingPosition;
        if (deathPosition == Vector3.zero)
        {
            deathPosition = character.Center;
        }

        pendingRespawns[viewId] = new PendingRespawn
        {
            ViewId = viewId,
            Position = deathPosition + Vector3.up * 1.5f,
            DueAt = synchronizedDueAt ?? PhotonNetwork.Time + delaySeconds,
            TotalDelaySeconds = delaySeconds,
            ReviveSent = false
        };
    }

    private void ReconcileCorpseRespawns()
    {
        foreach (Character character in GetPlayerCharacters())
        {
            int viewId = character.photonView.ViewID;
            if (IsActuallyDead(character))
            {
                if (!pendingRespawns.ContainsKey(viewId))
                {
                    // Covers late joins and a client promoted to host after the death.
                    if (TryGetPublishedDeadline(viewId, out double dueAt))
                    {
                        ScheduleCorpseRespawn(character, dueAt);
                    }
                    else
                    {
                        ScheduleCorpseRespawn(character);
                        if (IsAuthority)
                        {
                            PublishCorpseDeadline(viewId);
                        }
                    }
                }
            }
            else
            {
                if (pendingRespawns.Remove(viewId) && IsAuthority)
                {
                    ClearPublishedDeadline(viewId);
                }
            }
        }
    }

    private void ProcessCorpseRespawns()
    {
        foreach (PendingRespawn pending in pendingRespawns.Values.ToList())
        {
            if (!TryGetCharacter(pending.ViewId, out Character character))
            {
                pendingRespawns.Remove(pending.ViewId);
                if (IsAuthority)
                {
                    ClearPublishedDeadline(pending.ViewId);
                }
                continue;
            }

            if (!IsActuallyDead(character))
            {
                pendingRespawns.Remove(pending.ViewId);
                if (IsAuthority)
                {
                    ClearPublishedDeadline(pending.ViewId);
                }
                continue;
            }

            if (IsAuthority && !pending.ReviveSent && PhotonNetwork.Time >= pending.DueAt)
            {
                pending.ReviveSent = true;
                SendRevive(character, pending.Position);
                Plugin.Log.LogInfo($"Respawning {character.characterName} at their corpse.");
            }
        }
    }

    private void ReconcileImmediateRespawns()
    {
        foreach (Character character in GetPlayerCharacters())
        {
            int viewId = character.photonView.ViewID;
            if (!IsActuallyDead(character))
            {
                immediateRevivesSent.Remove(viewId);
                continue;
            }

            if (IsAuthority && !immediateRevivesSent.Contains(viewId))
            {
                SendPreviousCampfireRespawn(character);
            }
        }
    }

    private void ReconcileTeamWipeRespawns()
    {
        List<Character> characters = GetPlayerCharacters().ToList();
        if (!characters.Any(character => !IsActuallyDead(character)))
        {
            // A full lobby wipe always ends the run.
            return;
        }

        foreach (IGrouping<RaceTeamScope, Character> team in characters.GroupBy(
            RaceTeamScope.ForCharacter))
        {
            if (team.Any(character => !IsActuallyDead(character)))
            {
                teamWipeRevivesSent.Remove(team.Key);
                continue;
            }

            if (!teamWipeRevivesSent.Add(team.Key))
            {
                continue;
            }

            int index = 0;
            foreach (Character character in team)
            {
                Vector3 position = AddSpiralOffset(
                    GetPreviousCampfirePosition(character, includeActorOffset: false),
                    index++,
                    verticalOffset: 2f);
                SendRevive(character, position);
            }

            Plugin.Log.LogInfo(
                $"Reviving fully wiped {team.Key} at its last completed checkpoint.");
        }
    }

    private void SendPreviousCampfireRespawn(Character character)
    {
        int viewId = character.photonView.ViewID;
        if (!immediateRevivesSent.Add(viewId))
        {
            return;
        }

        Vector3 position = GetPreviousCampfirePosition(character);
        SendRevive(character, position);
        Plugin.Log.LogInfo($"Respawning {character.characterName} at the previous campfire.");
    }

    internal void HandlePvpKnockout(Character character)
    {
        if (RaceSettingsManager.Current.Mode != RespawnMode.Pvp
            || !IsAuthority
            || character == null
            || character.isBot
            || character.photonView == null)
        {
            return;
        }

        // This path runs before PEAK turns the scout into bones. It deliberately
        // uses the same revive RPC as the ordinary previous-campfire mode so all
        // clients clear the pending pass-out state in the same network event.
        Vector3 position = GetPreviousCampfirePosition(character);
        SendRevive(character, position);
        Plugin.Log.LogInfo(
            $"PVP blowgun knockout moved {character.characterName} to the previous campfire.");
    }

    internal static Vector3 GetPreviousCampfirePosition(
        Character character,
        bool includeActorOffset = true)
    {
        Vector3 position = character.LastLivingPosition;
        if (MapHandler.Exists)
        {
            Campfire previousCampfire =
                PlayerCampfireProgressTracker.GetPersonalPreviousCampfire(character);
            if (previousCampfire != null)
            {
                position = previousCampfire.transform.position;
            }
            else
            {
                SpawnPoint spawnPoint = SpawnPoint.GetSpawnPoint(character.photonView.Owner.ActorNumber);
                if (spawnPoint != null)
                {
                    position = spawnPoint.transform.position;
                }
            }
        }

        if (!includeActorOffset)
        {
            return position;
        }

        int actorNumber = character.photonView.Owner?.ActorNumber ?? character.photonView.ViewID;
        float angle = actorNumber * 2.39996323f;
        Vector3 offset = new(Mathf.Cos(angle) * 2f, 2f, Mathf.Sin(angle) * 2f);
        return position + offset;
    }

    private static Vector3 AddSpiralOffset(
        Vector3 origin,
        int index,
        float verticalOffset)
    {
        float angle = index * 2.39996323f;
        float radius = 2f + Mathf.Sqrt(index) * 0.65f;
        return origin + new Vector3(
            Mathf.Cos(angle) * radius,
            verticalOffset,
            Mathf.Sin(angle) * radius);
    }

    private static void SendRevive(
        Character character,
        Vector3 position,
        int completedCampfireIndex = -1)
    {
        character.photonView.RPC(
            "RPCA_ReviveAtPosition",
            RpcTarget.All,
            position,
            true,
            completedCampfireIndex);
    }

    private static bool TryGetCharacter(int viewId, out Character character)
    {
        return Character.GetCharacterWithPhotonID(viewId, out character) && character != null;
    }

    private static bool IsActuallyDead(Character character)
    {
        return character != null
            && character.data != null
            // Passing out is part of normal PEAK rescue gameplay. Custom
            // respawns begin only after RPCA_Die has created the skeleton.
            && character.data.dead;
    }

    private static IEnumerable<Character> GetPlayerCharacters()
    {
        return Character.AllCharacters.Where(character =>
            character != null
            && !character.isBot
            && character.photonView != null);
    }

    internal void CollectCountdowns(List<Countdown> output)
    {
        output.Clear();
        if (!RaceSettingsManager.Current.UsesCorpseTimerRespawn)
        {
            return;
        }

        foreach (PendingRespawn pending in pendingRespawns.Values)
        {
            if (TryGetCharacter(pending.ViewId, out Character character)
                && IsActuallyDead(character))
            {
                output.Add(new Countdown(character, pending.DueAt, pending.TotalDelaySeconds));
            }
        }
    }

    internal int GetLowestPendingRespawnSegment()
    {
        int lowestSegment = int.MaxValue;
        foreach (PendingRespawn pending in pendingRespawns.Values)
        {
            if (!LocalBiomeEnvironmentController.TryResolveWorldPositionSegment(
                pending.Position,
                out int segment))
            {
                // Unknown map layouts are retained rather than risking removal
                // of a live corpse-timer destination.
                return 0;
            }

            lowestSegment = Mathf.Min(lowestSegment, segment);
        }

        return lowestSegment == int.MaxValue ? -1 : lowestSegment;
    }

    private void PublishCorpseDeadline(int viewId)
    {
        if (!PhotonNetwork.InRoom
            || PhotonNetwork.CurrentRoom == null
            || !pendingRespawns.TryGetValue(viewId, out PendingRespawn pending))
        {
            return;
        }

        PhotonNetwork.CurrentRoom.SetCustomProperties(new Hashtable
        {
            [GetDeadlineKey(viewId)] = pending.DueAt
        });
    }

    private static bool TryGetPublishedDeadline(int viewId, out double dueAt)
    {
        dueAt = 0d;
        if (!PhotonNetwork.InRoom
            || PhotonNetwork.CurrentRoom == null
            || !PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(
                GetDeadlineKey(viewId),
                out object boxed)
            || boxed == null)
        {
            return false;
        }

        try
        {
            dueAt = Convert.ToDouble(boxed);
            return dueAt > 0d;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static void ClearPublishedDeadline(int viewId)
    {
        if (!PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null)
        {
            return;
        }

        string key = GetDeadlineKey(viewId);
        if (PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(key, out object boxed))
        {
            try
            {
                if (boxed == null || Convert.ToDouble(boxed) <= 0d)
                {
                    return;
                }
            }
            catch (Exception)
            {
                // Replace malformed values with the inactive sentinel below.
            }
        }

        PhotonNetwork.CurrentRoom.SetCustomProperties(new Hashtable
        {
            [key] = -1d
        });
    }

    private static string GetDeadlineKey(int viewId)
    {
        return CorpseDueKeyPrefix + viewId;
    }

    public override void OnRoomPropertiesUpdate(Hashtable propertiesThatChanged)
    {
        foreach (object rawKey in propertiesThatChanged.Keys)
        {
            if (rawKey is not string key
                || !key.StartsWith(CorpseDueKeyPrefix, StringComparison.Ordinal)
                || !int.TryParse(key.Substring(CorpseDueKeyPrefix.Length), out int viewId))
            {
                continue;
            }

            object value = propertiesThatChanged[rawKey];
            if (value == null)
            {
                pendingRespawns.Remove(viewId);
                continue;
            }

            try
            {
                double dueAt = Convert.ToDouble(value);
                if (dueAt <= 0d)
                {
                    pendingRespawns.Remove(viewId);
                    continue;
                }

                if (TryGetCharacter(viewId, out Character character) && IsActuallyDead(character))
                {
                    ScheduleCorpseRespawn(character, dueAt);
                }
            }
            catch (Exception)
            {
                Plugin.Log.LogWarning($"Ignoring invalid corpse deadline for view {viewId}.");
            }
        }
    }

    private void ClearState()
    {
        if (IsAuthority && PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom != null)
        {
            Hashtable clearedProperties = new();
            foreach (object rawKey in PhotonNetwork.CurrentRoom.CustomProperties.Keys)
            {
                if (rawKey is string key
                    && key.StartsWith(CorpseDueKeyPrefix, StringComparison.Ordinal)
                    && PhotonNetwork.CurrentRoom.CustomProperties[rawKey] is object value)
                {
                    try
                    {
                        if (Convert.ToDouble(value) > 0d)
                        {
                            clearedProperties[key] = -1d;
                        }
                    }
                    catch (Exception)
                    {
                        clearedProperties[key] = -1d;
                    }
                }
            }

            if (clearedProperties.Count > 0)
            {
                PhotonNetwork.CurrentRoom.SetCustomProperties(clearedProperties);
            }
        }

        pendingRespawns.Clear();
        immediateRevivesSent.Clear();
        teamWipeRevivesSent.Clear();
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        ClearState();
    }

    public override void OnLeftRoom()
    {
        ClearState();
    }

    public override void OnMasterClientSwitched(Photon.Realtime.Player newMasterClient)
    {
        // Schedules are intentionally kept. Every client tracks the same Photon deadline.
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (Instance == this)
        {
            Instance = null;
        }
    }
}
