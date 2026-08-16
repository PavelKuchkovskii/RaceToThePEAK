using ExitGames.Client.Photon;
using Photon.Pun;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Zorro.Core;

namespace PeakRace.Core;

/// <summary>
/// Tracks the furthest lit campfire each actor has personally reached. Global
/// MapHandler progression belongs to the leading racer and cannot be used as a
/// respawn checkpoint for players who are still in older retained segments.
/// </summary>
internal sealed class PlayerCampfireProgressTracker : MonoBehaviourPunCallbacks
{
    private const string CampfireKeyPrefix = "RTP.Campfire.";
    private const float CampfireReachPadding = 1f;

    internal static PlayerCampfireProgressTracker Instance { get; private set; }

    private readonly Dictionary<int, int> reachedCampfires = new();
    private int offlineReachedCampfire = -1;
    private bool clearedOutsideRun;

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

    private void Update()
    {
        string scene = SceneManager.GetActiveScene().name;
        if (scene == "Airport" || scene == "Title")
        {
            if (!clearedOutsideRun)
            {
                ClearRunState(clearRoomProperties: true);
                clearedOutsideRun = true;
            }
            return;
        }

        clearedOutsideRun = false;
        if (!MapHandler.ExistsAndInitialized)
        {
            return;
        }

        // Every client keeps an in-memory maximum for visible characters. The
        // owner additionally publishes their value, so the host has both an
        // immediate positional fallback and a migration-safe Photon value.
        foreach (Character character in PlayerHandler.GetAllPlayerCharacters())
        {
            TrackVisibleCharacter(character);
        }

        PublishLocalProgress();
    }

    internal static Campfire GetPersonalPreviousCampfire(Character character)
    {
        if (character == null || !MapHandler.ExistsAndInitialized)
        {
            return null;
        }

        int campfireIndex = Instance != null
            ? Instance.GetFurthestReachedCampfire(character)
            : ResolveReachedCampfireFromPosition(character);

        MapHandler map = Singleton<MapHandler>.Instance;
        campfireIndex = Mathf.Min(campfireIndex, map.segments.Length - 1);
        for (int index = campfireIndex; index >= 0; index--)
        {
            GameObject root = MapHandler.GetCampfireRoot(index);
            Campfire campfire = root?.GetComponentInChildren<Campfire>(true);
            if (campfire != null)
            {
                return campfire;
            }
        }

        return null;
    }

    private void TrackVisibleCharacter(Character character)
    {
        if (character == null || character.isBot)
        {
            return;
        }

        int resolved = ResolveReachedCampfireFromPosition(character);
        if (TryGetActorNumber(character, out int actorNumber))
        {
            if (!reachedCampfires.TryGetValue(actorNumber, out int current) || resolved > current)
            {
                reachedCampfires[actorNumber] = resolved;
            }
        }
        else if (character == Character.localCharacter)
        {
            offlineReachedCampfire = Mathf.Max(offlineReachedCampfire, resolved);
        }
    }

    private void PublishLocalProgress()
    {
        Character localCharacter = Character.localCharacter;
        if (localCharacter == null || localCharacter.isBot)
        {
            return;
        }

        int resolved = ResolveReachedCampfireFromPosition(localCharacter);
        if (!PhotonNetwork.InRoom)
        {
            offlineReachedCampfire = Mathf.Max(offlineReachedCampfire, resolved);
            return;
        }

        if (PhotonNetwork.CurrentRoom == null ||
            !TryGetActorNumber(localCharacter, out int actorNumber))
        {
            return;
        }

        int current = reachedCampfires.TryGetValue(actorNumber, out int cached)
            ? cached
            : -1;
        int published = ReadPublishedCampfire(actorNumber);
        current = Mathf.Max(current, published);
        int next = Mathf.Max(current, resolved);
        reachedCampfires[actorNumber] = next;
        if (next <= published)
        {
            return;
        }

        Hashtable properties = new()
        {
            [CampfireKey(actorNumber)] = next
        };
        if (!PhotonNetwork.CurrentRoom.SetCustomProperties(properties))
        {
            Plugin.Log.LogWarning(
                $"Photon rejected personal campfire progress for actor {actorNumber}; " +
                "the local cached checkpoint will still be used.");
        }
        else
        {
            Plugin.Log.LogInfo(
                $"Actor {actorNumber} personally reached campfire {next}.");
        }
    }

    private int GetFurthestReachedCampfire(Character character)
    {
        int resolved = ResolveReachedCampfireFromPosition(character);
        if (!PhotonNetwork.InRoom)
        {
            return character == Character.localCharacter
                ? Mathf.Max(offlineReachedCampfire, resolved)
                : resolved;
        }

        if (!TryGetActorNumber(character, out int actorNumber))
        {
            return resolved;
        }

        int cached = reachedCampfires.TryGetValue(actorNumber, out int value)
            ? value
            : -1;
        int published = ReadPublishedCampfire(actorNumber);
        int result = Mathf.Max(resolved, Mathf.Max(cached, published));
        reachedCampfires[actorNumber] = result;
        return result;
    }

    private static int ResolveReachedCampfireFromPosition(Character character)
    {
        if (character == null || !MapHandler.ExistsAndInitialized)
        {
            return -1;
        }

        MapHandler map = Singleton<MapHandler>.Instance;
        int reached = -1;

        // Entering segment N proves that campfire N-1 was passed even if the
        // radius sample happened between frames.
        if (LocalBiomeEnvironmentController.TryResolveCharacterSegment(
            character,
            out int physicalSegment) && physicalSegment > 0)
        {
            reached = physicalSegment - 1;
        }

        int globallyUnlocked = Mathf.Clamp(
            (int)MapHandler.CurrentSegmentNumber,
            0,
            map.segments.Length - 1);
        int highestLitCandidate = Mathf.Min(globallyUnlocked - 1, map.segments.Length - 1);
        for (int index = 0; index <= highestLitCandidate; index++)
        {
            GameObject root = map.segments[index].segmentCampfire;
            Campfire campfire = root?.GetComponentInChildren<Campfire>(true);
            if (campfire == null || !campfire.Lit)
            {
                continue;
            }

            float reachRadius = Mathf.Max(1f, campfire.moraleBoostRadius) + CampfireReachPadding;
            if (Vector3.Distance(character.Center, campfire.transform.position) <= reachRadius)
            {
                reached = Mathf.Max(reached, index);
            }
        }

        return reached;
    }

    private int ReadPublishedCampfire(int actorNumber)
    {
        if (!PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null ||
            !PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(
                CampfireKey(actorNumber),
                out object boxed) || boxed == null)
        {
            return -1;
        }

        try
        {
            return Convert.ToInt32(boxed);
        }
        catch (Exception)
        {
            return -1;
        }
    }

    private void ApplyRoomProperties(Hashtable properties)
    {
        if (properties == null)
        {
            return;
        }

        foreach (object rawKey in properties.Keys)
        {
            if (rawKey is not string key ||
                !key.StartsWith(CampfireKeyPrefix, StringComparison.Ordinal) ||
                !int.TryParse(key.Substring(CampfireKeyPrefix.Length), out int actorNumber))
            {
                continue;
            }

            object value = properties[rawKey];
            if (value == null)
            {
                reachedCampfires.Remove(actorNumber);
                continue;
            }

            try
            {
                int campfire = Convert.ToInt32(value);
                if (campfire >= 0)
                {
                    if (!reachedCampfires.TryGetValue(actorNumber, out int current) ||
                        campfire > current)
                    {
                        reachedCampfires[actorNumber] = campfire;
                    }
                }
                else
                {
                    reachedCampfires.Remove(actorNumber);
                }
            }
            catch (Exception)
            {
                Plugin.Log.LogWarning($"Ignored malformed campfire progress '{key}'.");
            }
        }
    }

    private static bool TryGetActorNumber(Character character, out int actorNumber)
    {
        actorNumber = 0;
        if (character == null || character.photonView == null ||
            character.photonView.Owner == null)
        {
            return false;
        }

        actorNumber = character.photonView.Owner.ActorNumber;
        return actorNumber > 0;
    }

    private static string CampfireKey(int actorNumber)
    {
        return CampfireKeyPrefix + actorNumber;
    }

    private void ClearRunState(bool clearRoomProperties)
    {
        reachedCampfires.Clear();
        offlineReachedCampfire = -1;

        if (!clearRoomProperties || !PhotonNetwork.InRoom ||
            !PhotonNetwork.IsMasterClient || PhotonNetwork.CurrentRoom == null)
        {
            return;
        }

        Hashtable cleared = new();
        foreach (object rawKey in PhotonNetwork.CurrentRoom.CustomProperties.Keys)
        {
            if (rawKey is string key &&
                key.StartsWith(CampfireKeyPrefix, StringComparison.Ordinal))
            {
                cleared[key] = null;
            }
        }

        if (cleared.Count > 0)
        {
            PhotonNetwork.CurrentRoom.SetCustomProperties(cleared);
        }
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == "Airport" || scene.name == "Title")
        {
            ClearRunState(clearRoomProperties: true);
            clearedOutsideRun = true;
        }
        else
        {
            clearedOutsideRun = false;
            ApplyRoomProperties(PhotonNetwork.CurrentRoom?.CustomProperties);
        }
    }

    public override void OnJoinedRoom()
    {
        reachedCampfires.Clear();
        ApplyRoomProperties(PhotonNetwork.CurrentRoom?.CustomProperties);
    }

    public override void OnRoomPropertiesUpdate(Hashtable propertiesThatChanged)
    {
        ApplyRoomProperties(propertiesThatChanged);
    }

    public override void OnLeftRoom()
    {
        ClearRunState(clearRoomProperties: false);
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
