using ExitGames.Client.Photon;
using PeakRace.Patch;
using Photon.Pun;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using Zorro.Core;

namespace PeakRace.Core;

/// <summary>
/// Validates campfire completion on the master client and persists monotonic
/// actor, team, and lobby checkpoints in Photon room properties.
/// </summary>
internal sealed class CampfireProgressionController : MonoBehaviourPunCallbacks
{
    private const string ProgressKeyPrefix = "RTP.Progress.";
    private const string LobbyProgressKey = ProgressKeyPrefix + "Lobby";
    private const float ReachPadding = 1f;

    private readonly Dictionary<int, int> actorProgress = new();
    private readonly Dictionary<int, int> teamProgress = new();
    private int lobbyProgress = -1;
    private bool clearedOutsideRun;

    internal static CampfireProgressionController Instance { get; private set; }

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
    }

    internal static bool TryGetCampfireIndex(Campfire campfire, out int campfireIndex)
    {
        campfireIndex = -1;
        if (campfire == null || !MapHandler.ExistsAndInitialized)
        {
            return false;
        }

        MapHandler map = Singleton<MapHandler>.Instance;
        for (int index = 0; index < map.segments.Length; index++)
        {
            GameObject root = map.segments[index].segmentCampfire;
            if (root == null)
            {
                continue;
            }

            if (campfire.gameObject == root
                || campfire.transform.IsChildOf(root.transform))
            {
                campfireIndex = index;
                return true;
            }
        }

        return false;
    }

    internal bool RequiresCompletion(Character character, Campfire campfire)
    {
        if (!TryGetCampfireIndex(campfire, out int campfireIndex))
        {
            return false;
        }

        RaceSettingsSnapshot settings = RaceSettingsManager.Current;
        if (settings.UsesPersonalCampfireClaims)
        {
            return GetActorProgress(GetActorNumber(character)) < campfireIndex;
        }

        // Outside PVP, Nobody keeps the original global first-racer rule.
        return settings.WaitMode == CampfireWaitMode.Nobody
            ? campfire.state == Campfire.FireState.Off
            : GetCompletedCampfireIndex(character) < campfireIndex;
    }

    internal bool CanCompleteCampfire(
        Character character,
        Campfire campfire,
        out string rejectionText)
    {
        rejectionText = string.Empty;
        if (!TryGetCampfireIndex(campfire, out int campfireIndex))
        {
            rejectionText = "CHECKPOINT UNAVAILABLE";
            return false;
        }

        return CanCompleteCampfire(character, campfire, campfireIndex, out rejectionText);
    }

    internal void RequestCompletion(Character character, Campfire campfire)
    {
        if (character == null
            || !TryGetCampfireIndex(campfire, out int campfireIndex))
        {
            return;
        }

        CharacterTeamInfo teamInfo = character.GetComponent<CharacterTeamInfo>();
        if (teamInfo == null)
        {
            Plugin.Log.LogWarning("Cannot request a campfire without synchronized team data.");
            return;
        }

        teamInfo.RequestCampfireCompletion(campfireIndex);
    }

    internal void HandleCompletionRequest(Character character, int campfireIndex)
    {
        string reason = string.Empty;
        if (!IsAuthority
            || character == null
            || !TryGetCampfire(campfireIndex, out Campfire campfire)
            || !CanCompleteCampfire(character, campfire, campfireIndex, out reason))
        {
            if (IsAuthority && !string.IsNullOrEmpty(reason))
            {
                Plugin.Log.LogWarning(
                    $"Rejected campfire {campfireIndex} completion for "
                    + $"{character?.characterName ?? "unknown player"}: {reason}.");
            }
            return;
        }

        RaceSettingsSnapshot settings = RaceSettingsManager.Current;
        int previousProgress = GetCompletedCampfireIndex(character);
        if ((settings.UsesPersonalCampfireClaims
                || settings.WaitMode != CampfireWaitMode.Nobody)
            && campfireIndex > previousProgress + 1)
        {
            Plugin.Log.LogWarning(
                $"Rejected non-sequential campfire progress from {previousProgress} "
                + $"to {campfireIndex} for {character.characterName}.");
            return;
        }

        CampfireWaitMode completionScope = settings.UsesPersonalCampfireClaims
            ? CampfireWaitMode.Nobody
            : settings.WaitMode;
        if (!PublishProgress(character, campfireIndex, completionScope))
        {
            return;
        }

        ResumeTimersForCompletedScope(character, completionScope);
        RaceRespawnController.Instance?.HandleCampfireCompleted(character, campfire, campfireIndex);
        CampfireAbilityManager.Instance?.HandleCampfireCompleted(character, campfireIndex);

        if (campfire.state == Campfire.FireState.Off)
        {
            campfire.DebugLight();
        }

        Plugin.Log.LogInfo(
            $"{character.characterName} completed campfire {campfireIndex} "
            + $"under {(settings.UsesPersonalCampfireClaims ? "personal PVP" : settings.WaitMode)} rules.");
    }

    internal int GetCompletedCampfireIndex(Character character)
    {
        if (character == null)
        {
            return -1;
        }

        RaceSettingsSnapshot settings = RaceSettingsManager.Current;
        if (settings.UsesPersonalCampfireClaims)
        {
            return GetActorProgress(GetActorNumber(character));
        }

        return settings.WaitMode switch
        {
            CampfireWaitMode.Team => GetTeamProgress(RaceTeamScope.ForCharacter(character)),
            CampfireWaitMode.Lobby => lobbyProgress,
            _ => GetActorProgress(GetActorNumber(character))
        };
    }

    internal Campfire GetCompletedCampfire(Character character)
    {
        int completed = GetCompletedCampfireIndex(character);
        if (!MapHandler.ExistsAndInitialized)
        {
            return null;
        }

        MapHandler map = Singleton<MapHandler>.Instance;
        for (int index = Mathf.Min(completed, map.segments.Length - 1); index >= 0; index--)
        {
            Campfire campfire = map.segments[index].segmentCampfire
                ?.GetComponentInChildren<Campfire>(true);
            if (campfire != null)
            {
                return campfire;
            }
        }

        return null;
    }

    internal IEnumerable<Character> GetActiveCharactersInScope(Character reference)
    {
        RaceSettingsSnapshot settings = RaceSettingsManager.Current;
        CampfireWaitMode waitMode = settings.UsesPersonalCampfireClaims
            ? CampfireWaitMode.Nobody
            : settings.WaitMode;
        RaceTeamScope teamScope = RaceTeamScope.ForCharacter(reference);
        foreach (Character character in GetActivePlayerCharacters())
        {
            if (waitMode == CampfireWaitMode.Lobby
                || waitMode == CampfireWaitMode.Nobody && character == reference
                || waitMode == CampfireWaitMode.Team && teamScope.Contains(character))
            {
                yield return character;
            }
        }
    }

    private bool CanCompleteCampfire(
        Character character,
        Campfire campfire,
        int campfireIndex,
        out string rejectionText)
    {
        rejectionText = string.Empty;
        if (!IsActivePlayerCharacter(character)
            || character.data == null
            || character.data.dead)
        {
            rejectionText = "ONLY A LIVING PLAYER CAN ACTIVATE THIS CHECKPOINT";
            return false;
        }

        float radius = Mathf.Max(1f, campfire.moraleBoostRadius) + ReachPadding;
        if (Vector3.Distance(character.Center, campfire.transform.position) > radius)
        {
            rejectionText = "MOVE CLOSER TO THE CAMPFIRE";
            return false;
        }

        RaceSettingsSnapshot settings = RaceSettingsManager.Current;
        if (settings.UsesPersonalCampfireClaims)
        {
            if (GetActorProgress(GetActorNumber(character)) >= campfireIndex)
            {
                rejectionText = "CHECKPOINT ALREADY COMPLETED";
                return false;
            }

            return true;
        }

        if (settings.WaitMode == CampfireWaitMode.Nobody)
        {
            return true;
        }

        if (GetCompletedCampfireIndex(character) >= campfireIndex)
        {
            rejectionText = "CHECKPOINT ALREADY COMPLETED";
            return false;
        }

        RaceTeamScope teamScope = RaceTeamScope.ForCharacter(character);
        List<string> missingPlayers = new();
        foreach (Character candidate in GetActivePlayerCharacters())
        {
            if (settings.WaitMode == CampfireWaitMode.Team
                && !teamScope.Contains(candidate))
            {
                continue;
            }

            // An unconscious player is still alive and must be carried into
            // range. Only a real skeleton/death satisfies the dead exception.
            if (candidate.data != null
                && !candidate.data.dead
                && Vector3.Distance(candidate.Center, campfire.transform.position) > radius)
            {
                missingPlayers.Add(GetDisplayName(candidate));
            }
        }

        if (missingPlayers.Count == 0)
        {
            return true;
        }

        string scope = settings.WaitMode == CampfireWaitMode.Team ? "TEAM" : "LOBBY";
        rejectionText = $"WAITING FOR {scope}: {string.Join(", ", missingPlayers)}";
        return false;
    }

    private bool PublishProgress(
        Character character,
        int campfireIndex,
        CampfireWaitMode waitMode)
    {
        string propertyKey;
        RaceTeamScope pendingTeamScope = default;
        int pendingActorNumber = 0;
        switch (waitMode)
        {
            case CampfireWaitMode.Team:
                RaceTeamScope teamScope = RaceTeamScope.ForCharacter(character);
                if (GetTeamProgress(teamScope) >= campfireIndex)
                {
                    return true;
                }
                pendingTeamScope = teamScope;
                propertyKey = ProgressKeyPrefix + teamScope.PropertySuffix;
                break;

            case CampfireWaitMode.Lobby:
                if (lobbyProgress >= campfireIndex)
                {
                    return true;
                }
                propertyKey = LobbyProgressKey;
                break;

            default:
                int actorNumber = GetActorNumber(character);
                if (GetActorProgress(actorNumber) >= campfireIndex)
                {
                    return true;
                }
                pendingActorNumber = actorNumber;
                propertyKey = ProgressKeyPrefix + $"Actor.{actorNumber}";
                break;
        }

        if (PhotonNetwork.InRoom
            && PhotonNetwork.CurrentRoom != null
            && !PhotonNetwork.CurrentRoom.SetCustomProperties(new Hashtable
            {
                [propertyKey] = campfireIndex
            }))
        {
            Plugin.Log.LogError($"Photon rejected campfire progress '{propertyKey}'.");
            return false;
        }

        switch (waitMode)
        {
            case CampfireWaitMode.Team:
                SetTeamProgress(pendingTeamScope, campfireIndex);
                break;
            case CampfireWaitMode.Lobby:
                lobbyProgress = campfireIndex;
                break;
            default:
                actorProgress[pendingActorNumber] = campfireIndex;
                break;
        }
        return true;
    }

    private static void ResumeTimersForCompletedScope(
        Character completingCharacter,
        CampfireWaitMode waitMode)
    {
        RaceTeamScope teamScope = RaceTeamScope.ForCharacter(completingCharacter);
        foreach (Character candidate in GetActivePlayerCharacters())
        {
            if (waitMode == CampfireWaitMode.Team && !teamScope.Contains(candidate))
            {
                continue;
            }
            CharacterTeamInfo teamInfo = candidate.GetComponent<CharacterTeamInfo>();
            if (teamInfo != null)
            {
                teamInfo.timeOn = true;
            }
        }
    }

    private int GetTeamProgress(RaceTeamScope scope)
    {
        return scope.IsExplicitTeam
            ? teamProgress.TryGetValue(scope.Value, out int progress) ? progress : -1
            : GetActorProgress(scope.Value);
    }

    private void SetTeamProgress(RaceTeamScope scope, int progress)
    {
        if (scope.IsExplicitTeam)
        {
            teamProgress[scope.Value] = progress;
        }
        else
        {
            actorProgress[scope.Value] = progress;
        }
    }

    private int GetActorProgress(int actorNumber)
    {
        return actorProgress.TryGetValue(actorNumber, out int progress) ? progress : -1;
    }

    private static int GetActorNumber(Character character)
    {
        return character?.photonView?.Owner?.ActorNumber
            ?? character?.photonView?.ViewID
            ?? character?.GetInstanceID()
            ?? 0;
    }

    private static bool TryGetCampfire(int campfireIndex, out Campfire campfire)
    {
        campfire = null;
        if (!MapHandler.ExistsAndInitialized)
        {
            return false;
        }

        MapHandler map = Singleton<MapHandler>.Instance;
        if (campfireIndex < 0 || campfireIndex >= map.segments.Length)
        {
            return false;
        }

        campfire = map.segments[campfireIndex].segmentCampfire
            ?.GetComponentInChildren<Campfire>(true);
        return campfire != null;
    }

    private static IEnumerable<Character> GetActivePlayerCharacters()
    {
        IEnumerable<Character> source = PhotonNetwork.InRoom
            ? PlayerHandler.GetAllPlayerCharacters()
            : Character.AllCharacters;
        return source.Where(IsActivePlayerCharacter);
    }

    private static bool IsActivePlayerCharacter(Character character)
    {
        return character != null
            && !character.isBot
            && character.photonView != null
            && (character.photonView.Owner == null
                || !character.photonView.Owner.IsInactive);
    }

    private static string GetDisplayName(Character character)
    {
        return character.photonView?.Owner?.NickName
            ?? character.characterName
            ?? "Player";
    }

    private void ApplyRoomProperties(Hashtable properties)
    {
        if (properties == null)
        {
            return;
        }

        foreach (object rawKey in properties.Keys)
        {
            if (rawKey is not string key
                || !key.StartsWith(ProgressKeyPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            object boxed = properties[rawKey];
            if (boxed == null)
            {
                ClearCachedProperty(key);
                continue;
            }

            if (!TryReadProgress(boxed, out int progress))
            {
                Plugin.Log.LogWarning($"Ignored malformed campfire progress '{key}'.");
                continue;
            }

            if (key == LobbyProgressKey)
            {
                lobbyProgress = Mathf.Max(lobbyProgress, progress);
            }
            else if (TryParseScopedKey(key, "Actor.", out int actorNumber))
            {
                actorProgress[actorNumber] = Mathf.Max(GetActorProgress(actorNumber), progress);
            }
            else if (TryParseScopedKey(key, "Team.", out int teamNumber))
            {
                int current = teamProgress.TryGetValue(teamNumber, out int value) ? value : -1;
                teamProgress[teamNumber] = Mathf.Max(current, progress);
            }
        }
    }

    private void ClearCachedProperty(string key)
    {
        if (key == LobbyProgressKey)
        {
            lobbyProgress = -1;
        }
        else if (TryParseScopedKey(key, "Actor.", out int actorNumber))
        {
            actorProgress.Remove(actorNumber);
        }
        else if (TryParseScopedKey(key, "Team.", out int teamNumber))
        {
            teamProgress.Remove(teamNumber);
        }
    }

    private static bool TryReadProgress(object boxed, out int progress)
    {
        progress = -1;
        if (boxed == null)
        {
            return false;
        }

        try
        {
            progress = Convert.ToInt32(boxed);
            return progress >= 0 && progress < 64;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool TryParseScopedKey(string key, string scope, out int value)
    {
        value = -1;
        string prefix = ProgressKeyPrefix + scope;
        return key.StartsWith(prefix, StringComparison.Ordinal)
            && int.TryParse(key.Substring(prefix.Length), out value)
            && value >= 0;
    }

    private void ClearRunState(bool clearRoomProperties)
    {
        actorProgress.Clear();
        teamProgress.Clear();
        lobbyProgress = -1;

        if (!clearRoomProperties
            || !PhotonNetwork.InRoom
            || !PhotonNetwork.IsMasterClient
            || PhotonNetwork.CurrentRoom == null)
        {
            return;
        }

        Hashtable cleared = new();
        foreach (object rawKey in PhotonNetwork.CurrentRoom.CustomProperties.Keys)
        {
            if (rawKey is string key
                && key.StartsWith(ProgressKeyPrefix, StringComparison.Ordinal))
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
        actorProgress.Clear();
        teamProgress.Clear();
        lobbyProgress = -1;
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
