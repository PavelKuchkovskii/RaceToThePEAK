using ExitGames.Client.Photon;
using Peak;
using Photon.Pun;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Zorro.Core;

namespace PeakRace.Core;

/// <summary>
/// Replaces PEAK's single synchronized final-biome rising field with a stable
/// Photon start timestamp per player. Rendering follows the observed player;
/// damage/status checks follow the local player.
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

    private readonly Dictionary<int, double> actorStartTimes = new();
    private readonly Dictionary<int, HazardState> hazardStates = new();
    private readonly HashSet<int> riseMessageShownForActors = new();

    private bool clearedOutsideRun;
    private double offlineStartTime = -1d;

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
        foreach (LavaRising hazard in LavaRising.ALL_LAVA)
        {
            if (IsManagedFinalHazard(hazard) && IsHazardEnabled(hazard))
            {
                HazardState state = GetOrCreateState(hazard);
                CaptureAuthoredEventRelease(hazard, state);
                EnsureLocalStartPublished(hazard, state);
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
        if (controller == null || !IsManagedFinalHazard(hazard))
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

        return controller.LocalPlayerHasActiveField(hazard);
    }

    internal static bool TryShouldRunManagedVisual(Component visual, out bool shouldRun)
    {
        shouldRun = true;
        if (visual == null || !TryFindOwningFinalHazard(visual, out LavaRising hazard))
        {
            return false;
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

        EnsureLocalStartPublished(hazard, state);

        Character observed = GetObservedCharacter();
        int observedActor = 0;
        double startTime = 0d;
        bool hasStartTime = TryGetActorNumber(observed, out observedActor) &&
            TryGetStartTime(observedActor, out startTime);
        if (!hasStartTime && !PhotonNetwork.InRoom && observed == Character.localCharacter &&
            offlineStartTime > 0d)
        {
            hasStartTime = true;
            startTime = offlineStartTime;
        }

        ApplyProgress(hazard, state, hasStartTime, startTime, observed);

        if (state.ObservedStarted &&
            TryGetActorNumber(Character.localCharacter, out int localActor) &&
            observedActor == localActor &&
            riseMessageShownForActors.Add(localActor))
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

    private void EnsureLocalStartPublished(LavaRising hazard, HazardState state)
    {
        if (!IsHazardEnabled(hazard) || !state.EventReleased || !Ascents.fogEnabled)
        {
            return;
        }

        Character localCharacter = Character.localCharacter;
        if (localCharacter == null || localCharacter.data == null || localCharacter.data.dead ||
            !CharacterIsInHazardSegment(localCharacter, hazard))
        {
            return;
        }

        if (!PhotonNetwork.InRoom)
        {
            if (offlineStartTime <= 0d)
            {
                offlineStartTime = NetworkTime;
                Plugin.Log.LogInfo("Local final hazard timer started (offline).");
            }
            return;
        }

        if (!TryGetActorNumber(localCharacter, out int actorNumber) ||
            actorStartTimes.ContainsKey(actorNumber))
        {
            return;
        }

        if (PhotonNetwork.CurrentRoom == null)
        {
            return;
        }

        string propertyKey = StartTimeKey(actorNumber);
        if (PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(propertyKey, out object existing))
        {
            try
            {
                double existingStart = Convert.ToDouble(existing);
                if (existingStart > 0d)
                {
                    actorStartTimes[actorNumber] = existingStart;
                    return;
                }
            }
            catch (Exception)
            {
                // Replace only the malformed value with the valid timestamp below.
            }
        }

        double startTime = NetworkTime;
        actorStartTimes[actorNumber] = startTime;
        Hashtable properties = new()
        {
            [propertyKey] = startTime
        };
        if (!PhotonNetwork.CurrentRoom.SetCustomProperties(properties))
        {
            Plugin.Log.LogWarning(
                $"Photon rejected final hazard start time for actor {actorNumber}; " +
                "the local cached timer will still be used.");
        }
        else
        {
            Plugin.Log.LogInfo($"Final hazard timer started for actor {actorNumber}.");
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
        if (PhotonNetwork.InRoom)
        {
            if (!TryGetActorNumber(localCharacter, out int actorNumber) ||
                !TryGetStartTime(actorNumber, out startTime))
            {
                return false;
            }
        }
        else
        {
            startTime = offlineStartTime;
            if (startTime <= 0d)
            {
                return false;
            }
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

    private bool TryGetStartTime(int actorNumber, out double startTime)
    {
        return actorStartTimes.TryGetValue(actorNumber, out startTime) && startTime > 0d;
    }

    private static double NetworkTime => PhotonNetwork.InRoom
        ? PhotonNetwork.Time
        : Time.unscaledTime;

    private static string StartTimeKey(int actorNumber)
    {
        return StartTimePrefix + actorNumber;
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
            if (rawKey is not string key ||
                !key.StartsWith(StartTimePrefix, StringComparison.Ordinal) ||
                !int.TryParse(key.Substring(StartTimePrefix.Length), out int actorNumber))
            {
                continue;
            }

            try
            {
                double startTime = Convert.ToDouble(properties[rawKey]);
                if (startTime > 0d)
                {
                    actorStartTimes[actorNumber] = startTime;
                }
                else
                {
                    actorStartTimes.Remove(actorNumber);
                }
            }
            catch (Exception)
            {
                actorStartTimes.Remove(actorNumber);
                Plugin.Log.LogWarning($"Ignored malformed final hazard time '{key}'.");
            }
        }
    }

    private void ClearRunState(bool clearRoomProperties)
    {
        actorStartTimes.Clear();
        hazardStates.Clear();
        riseMessageShownForActors.Clear();
        offlineStartTime = -1d;

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
        riseMessageShownForActors.Clear();
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
        actorStartTimes.Clear();
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
