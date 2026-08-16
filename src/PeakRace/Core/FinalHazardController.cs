using ExitGames.Client.Photon;
using Peak;
using Photon.Pun;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using Zorro.Core;

namespace PeakRace.Core;

/// <summary>
/// Selects the final-biome rising-field clock from the configured progression
/// scope. Nobody mode uses actor clocks, Team mode uses team clocks, and Lobby
/// mode leaves PEAK's vanilla global synchronization untouched.
/// </summary>
internal sealed class FinalHazardController : MonoBehaviourPunCallbacks
{
    private const string StartTimePrefix = "RTP.FinalHazard.At.";
    private static readonly int HeightFogAmountId = Shader.PropertyToID("HeightFogAmount");

    private sealed class HazardState
    {
        internal LavaRising Hazard;
        internal float StartHeight;
        internal bool EventReleased;
        internal bool ObservedInsideFinal;
        internal bool ObservedStarted;
        internal float ObservedTravelSeconds;
    }

    internal static FinalHazardController Instance { get; private set; }

    private readonly Dictionary<string, double> scopeStartTimes = new();
    private readonly Dictionary<int, HazardState> hazardStates = new();
    private readonly HashSet<string> riseMessageShownForScopes = new();

    private bool clearedOutsideRun;

    private static bool UsesScopedHazards =>
        RaceSettingsManager.Current.WaitMode != CampfireWaitMode.Lobby;

    internal static bool UsesVanillaGlobalHazard => !UsesScopedHazards;

    private static bool IsAuthority =>
        !PhotonNetwork.InRoom || PhotonNetwork.IsMasterClient;

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
        if (!UsesScopedHazards)
        {
            return;
        }

        foreach (LavaRising hazard in LavaRising.ALL_LAVA)
        {
            if (IsManagedFinalHazard(hazard) && IsHazardEnabled(hazard))
            {
                HazardState state = GetOrCreateState(hazard);
                CaptureAuthoredEventRelease(hazard, state);
                EnsureScopedStartsPublished(hazard, state);
            }
        }
    }

    private void LateUpdate()
    {
        // DayNightManager and the authored fog components also write this
        // global. LateUpdate makes the observed player's personal rising Gloom
        // the final value used for rendering this frame.
        foreach (HazardState state in hazardStates.Values)
        {
            if (state.Hazard == null || !state.Hazard.isGloom ||
                !state.ObservedInsideFinal)
            {
                continue;
            }

            float amount = state.ObservedStarted
                ? Mathf.Clamp01(state.ObservedTravelSeconds)
                : 0f;
            Shader.SetGlobalFloat(HeightFogAmountId, amount);
            break;
        }
    }

    internal static bool IsManagedFinalHazard(LavaRising hazard)
    {
        return hazard != null &&
            hazard.requiredSegment == Segment.TheKiln &&
            hazard.risingFieldType != LavaRising.RisingFieldType.VoidGhosts;
    }

    internal static bool TryDrive(LavaRising hazard)
    {
        FinalHazardController controller = Instance;
        if (controller == null || !IsManagedFinalHazard(hazard) || !UsesScopedHazards)
        {
            return false;
        }

        controller.Drive(hazard);
        return true;
    }

    internal static bool ShouldProcessLocalEffect(Component effect)
    {
        FinalHazardController controller = Instance;
        if (controller == null || effect == null ||
            !TryFindOwningFinalHazard(effect, out LavaRising hazard))
        {
            return true;
        }

        return !UsesScopedHazards || controller.LocalPlayerHasActiveField(hazard);
    }

    internal static bool TryShouldRunManagedVisual(Component visual, out bool shouldRun)
    {
        shouldRun = true;
        if (visual == null || !TryFindOwningFinalHazard(visual, out LavaRising hazard))
        {
            return false;
        }

        if (!UsesScopedHazards)
        {
            shouldRun = true;
            return true;
        }

        Character observed = GetObservedCharacter();
        shouldRun = observed != null && CharacterIsInHazardSegment(observed, hazard);
        return true;
    }

    private void Drive(LavaRising hazard)
    {
        HazardState state = GetOrCreateState(hazard);
        CaptureAuthoredEventRelease(hazard, state);
        if (!IsHazardEnabled(hazard))
        {
            hazard.enabled = false;
            ApplyProgress(hazard, state, hasStartTime: false, 0d, null);
            return;
        }

        EnsureScopedStartsPublished(hazard, state);

        Character observed = GetObservedCharacter();
        string observedScope = null;
        double startTime = 0d;
        bool hasStartTime = TryGetScopeKey(observed, out observedScope)
            && TryGetStartTime(observedScope, out startTime);

        ApplyProgress(hazard, state, hasStartTime, startTime, observed);

        if (state.ObservedStarted &&
            TryGetScopeKey(Character.localCharacter, out string localScope) &&
            observedScope == localScope &&
            riseMessageShownForScopes.Add(localScope))
        {
            ShowRiseMessage(hazard);
        }
    }

    private void ApplyProgress(
        LavaRising hazard,
        HazardState state,
        bool hasStartTime,
        double startTime,
        Character observed)
    {
        state.ObservedInsideFinal = observed != null &&
            CharacterIsInHazardSegment(observed, hazard);
        state.ObservedStarted = false;
        state.ObservedTravelSeconds = 0f;

        double elapsedSinceEntry = hasStartTime ? Math.Max(0d, NetworkTime - startTime) : 0d;
        double waitTime = Math.Max(0d, hazard.initialWaitTime);
        double travelSeconds = Math.Max(0d, elapsedSinceEntry - waitTime);
        bool started = hasStartTime && elapsedSinceEntry >= waitTime;
        float authoredTravelTime = Mathf.Max(0.01f, hazard.travelTime);
        float traveled = started
            ? Mathf.Min((float)travelSeconds, authoredTravelTime)
            : 0f;
        bool ended = started && travelSeconds >= authoredTravelTime;

        hazard.secondsWaitedToStart = hasStartTime
            ? Mathf.Min((float)elapsedSinceEntry, Mathf.Max(0.02f, hazard.initialWaitTime))
            : 0f;
        hazard.started = started;
        hazard.ended = ended;
        hazard.timeTraveled = traveled;

        state.ObservedStarted = started;
        state.ObservedTravelSeconds = traveled;

        if (hazard.lava == null)
        {
            return;
        }

        float targetHeight = state.StartHeight;
        if (started && hazard.topTransform != null)
        {
            targetHeight = Mathf.Lerp(
                state.StartHeight,
                hazard.topTransform.position.y,
                traveled / authoredTravelTime);
        }

        Vector3 position = hazard.lava.position;
        hazard.lava.MovePosition(new Vector3(position.x, targetHeight, position.z));
    }

    private void EnsureScopedStartsPublished(LavaRising hazard, HazardState state)
    {
        if (!IsAuthority
            || !IsHazardEnabled(hazard)
            || !state.EventReleased
            || !Ascents.fogEnabled)
        {
            return;
        }

        foreach (Character character in GetActivePlayerCharacters())
        {
            if (character.data == null
                || character.data.dead
                || !CharacterIsInHazardSegment(character, hazard)
                || !TryGetScopeKey(character, out string scopeKey))
            {
                continue;
            }

            EnsureScopeStartPublished(scopeKey);
        }
    }

    private void EnsureScopeStartPublished(string scopeKey)
    {
        if (TryGetStartTime(scopeKey, out _))
        {
            return;
        }

        double startTime = NetworkTime;
        if (!PhotonNetwork.InRoom)
        {
            scopeStartTimes[scopeKey] = startTime;
            Plugin.Log.LogInfo($"Final hazard timer started for {scopeKey} (offline).");
            return;
        }

        if (PhotonNetwork.CurrentRoom == null)
        {
            return;
        }

        string propertyKey = StartTimeKey(scopeKey);
        if (PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(propertyKey, out object existing))
        {
            try
            {
                double existingStart = Convert.ToDouble(existing);
                if (existingStart > 0d)
                {
                    scopeStartTimes[scopeKey] = existingStart;
                    return;
                }
            }
            catch (Exception)
            {
                // Replace only the malformed value with the valid timestamp below.
            }
        }

        Hashtable properties = new()
        {
            [propertyKey] = startTime
        };
        if (!PhotonNetwork.CurrentRoom.SetCustomProperties(properties))
        {
            Plugin.Log.LogWarning(
                $"Photon rejected final hazard start time for {scopeKey}.");
        }
        else
        {
            scopeStartTimes[scopeKey] = startTime;
            Plugin.Log.LogInfo($"Final hazard timer started for {scopeKey}.");
        }
    }

    private bool LocalPlayerHasActiveField(LavaRising hazard)
    {
        if (!IsHazardEnabled(hazard))
        {
            return false;
        }

        Character localCharacter = Character.localCharacter;
        if (localCharacter == null || !CharacterIsInHazardSegment(localCharacter, hazard))
        {
            return false;
        }

        double startTime;
        if (!TryGetScopeKey(localCharacter, out string scopeKey)
            || !TryGetStartTime(scopeKey, out startTime))
        {
            return false;
        }

        return NetworkTime >= startTime + Math.Max(0d, hazard.initialWaitTime);
    }

    private HazardState GetOrCreateState(LavaRising hazard)
    {
        int instanceId = hazard.GetInstanceID();
        if (!hazardStates.TryGetValue(instanceId, out HazardState state) ||
            state.Hazard != hazard)
        {
            state = new HazardState
            {
                Hazard = hazard,
                StartHeight = hazard.lava != null
                    ? hazard.lava.position.y
                    : hazard.transform.position.y,
                EventReleased = !hazard.waitForEvent || hazard.secondsWaitedToStart > 0f
            };
            hazardStates[instanceId] = state;
        }

        return state;
    }

    private static void CaptureAuthoredEventRelease(LavaRising hazard, HazardState state)
    {
        // Event-driven variants call LavaRising.StartWaiting(). Preserve that
        // authored gate, but start a separate entry clock for every racer once
        // the event has happened.
        if (!state.EventReleased && hazard.secondsWaitedToStart > 0f)
        {
            state.EventReleased = true;
        }
    }

    private static bool IsHazardEnabled(LavaRising hazard)
    {
        return hazard.risingFieldType switch
        {
            LavaRising.RisingFieldType.Gloom =>
                RunSettings.GetValue(RunSettings.SETTINGTYPE.Hazard_TheGloomRises) != 0,
            LavaRising.RisingFieldType.Lava =>
                RunSettings.GetValue(RunSettings.SETTINGTYPE.Hazard_TheLavaRises) != 0,
            _ => true
        };
    }

    private static bool CharacterIsInHazardSegment(Character character, LavaRising hazard)
    {
        if (character == null || !LocalBiomeEnvironmentController.TryResolveCharacterSegment(
            character,
            out int characterSegment))
        {
            return false;
        }

        int hazardSegment = (int)hazard.requiredSegment;
        if (MapHandler.Exists)
        {
            MapHandler map = Singleton<MapHandler>.Instance;
            hazardSegment = Mathf.Clamp(hazardSegment, 0, map.segments.Length - 1);
        }

        return characterSegment == hazardSegment;
    }

    private static Character GetObservedCharacter()
    {
        return Character.observedCharacter != null
            ? Character.observedCharacter
            : Character.localCharacter;
    }

    private static bool TryFindOwningFinalHazard(Component component, out LavaRising hazard)
    {
        FinalHazardController controller = Instance;
        if (controller != null)
        {
            foreach (HazardState state in controller.hazardStates.Values)
            {
                if (ComponentBelongsToField(component, state.Hazard))
                {
                    hazard = state.Hazard;
                    return true;
                }
            }
        }

        foreach (LavaRising candidate in LavaRising.ALL_LAVA)
        {
            if (ComponentBelongsToField(component, candidate))
            {
                hazard = candidate;
                return true;
            }
        }

        hazard = null;
        return false;
    }

    private static bool ComponentBelongsToField(Component component, LavaRising hazard)
    {
        if (component == null || !IsManagedFinalHazard(hazard) || hazard.lava == null)
        {
            return false;
        }

        Transform field = hazard.lava.transform;
        Transform target = component.transform;
        return target == field || target.IsChildOf(field) || field.IsChildOf(target);
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

    private static bool TryGetScopeKey(Character character, out string scopeKey)
    {
        scopeKey = null;
        if (character == null || !UsesScopedHazards)
        {
            return false;
        }

        if (RaceSettingsManager.Current.WaitMode == CampfireWaitMode.Team)
        {
            scopeKey = RaceTeamScope.ForCharacter(character).PropertySuffix;
            return true;
        }

        if (!TryGetActorNumber(character, out int actorNumber))
        {
            return false;
        }

        scopeKey = $"Actor.{actorNumber}";
        return true;
    }

    private bool TryGetStartTime(string scopeKey, out double startTime)
    {
        startTime = 0d;
        return scopeKey != null
            && scopeStartTimes.TryGetValue(scopeKey, out startTime)
            && startTime > 0d;
    }

    private static double NetworkTime => PhotonNetwork.InRoom
        ? PhotonNetwork.Time
        : Time.unscaledTime;

    private static string StartTimeKey(string scopeKey)
    {
        return StartTimePrefix + scopeKey;
    }

    private static IEnumerable<Character> GetActivePlayerCharacters()
    {
        IEnumerable<Character> source = PhotonNetwork.InRoom
            ? PlayerHandler.GetAllPlayerCharacters()
            : Character.AllCharacters;
        return source.Where(character =>
            character != null
            && !character.isBot
            && character.photonView != null
            && (character.photonView.Owner == null
                || !character.photonView.Owner.IsInactive));
    }

    private static void ShowRiseMessage(LavaRising hazard)
    {
        if (GUIManager.instance != null)
        {
            if (hazard.isGloom)
            {
                GUIManager.instance.TheGloomRises();
            }
            else
            {
                GUIManager.instance.TheLavaRises();
            }
        }

        if (GamefeelHandler.instance != null)
        {
            GamefeelHandler.instance.AddPerlinShake(5f, 3f);
        }

        hazard.shownLavaRisingMessage = true;
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
                || !key.StartsWith(StartTimePrefix, StringComparison.Ordinal)
                || !TryNormalizeScopeKey(
                    key.Substring(StartTimePrefix.Length),
                    out string scopeKey))
            {
                continue;
            }

            object boxed = properties[rawKey];
            if (boxed == null)
            {
                scopeStartTimes.Remove(scopeKey);
                continue;
            }

            try
            {
                double startTime = Convert.ToDouble(boxed);
                if (startTime > 0d && startTime <= NetworkTime + 30d)
                {
                    scopeStartTimes[scopeKey] = startTime;
                }
                else
                {
                    scopeStartTimes.Remove(scopeKey);
                }
            }
            catch (Exception)
            {
                scopeStartTimes.Remove(scopeKey);
                Plugin.Log.LogWarning($"Ignored malformed final hazard time '{key}'.");
            }
        }
    }

    private static bool TryNormalizeScopeKey(string rawScope, out string scopeKey)
    {
        scopeKey = null;
        if (int.TryParse(rawScope, out int legacyActor) && legacyActor > 0)
        {
            scopeKey = $"Actor.{legacyActor}";
            return true;
        }

        if (rawScope.StartsWith("Actor.", StringComparison.Ordinal)
            && int.TryParse(rawScope.Substring("Actor.".Length), out int actorNumber)
            && actorNumber > 0)
        {
            scopeKey = $"Actor.{actorNumber}";
            return true;
        }

        if (rawScope.StartsWith("Team.", StringComparison.Ordinal)
            && int.TryParse(rawScope.Substring("Team.".Length), out int teamNumber)
            && teamNumber >= 0
            && teamNumber < 64)
        {
            scopeKey = $"Team.{teamNumber}";
            return true;
        }

        return false;
    }

    private void ClearRunState(bool clearRoomProperties)
    {
        scopeStartTimes.Clear();
        hazardStates.Clear();
        riseMessageShownForScopes.Clear();

        if (!clearRoomProperties || !PhotonNetwork.InRoom ||
            !PhotonNetwork.IsMasterClient || PhotonNetwork.CurrentRoom == null)
        {
            return;
        }

        Hashtable cleared = new();
        foreach (object rawKey in PhotonNetwork.CurrentRoom.CustomProperties.Keys)
        {
            if (rawKey is string key &&
                key.StartsWith(StartTimePrefix, StringComparison.Ordinal))
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
        hazardStates.Clear();
        riseMessageShownForScopes.Clear();
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
        scopeStartTimes.Clear();
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
