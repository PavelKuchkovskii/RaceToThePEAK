using Peak;
using Photon.Pun;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using Zorro.Core;

namespace PeakRace.Core;

/// <summary>
/// Separates globally loaded map geometry from team-local transition access and
/// retains the smallest contiguous segment range required by players, respawns,
/// and unfinished teams.
/// </summary>
internal sealed class BiomeLifecycleController : MonoBehaviour
{
    private const float ReconcileIntervalSeconds = 0.25f;

    internal static BiomeLifecycleController Instance { get; private set; }

    private readonly ScopedTransitionAccessController transitionAccess = new();
    private float nextReconcileTime;
    private MapHandler cachedMap;
    private int lastRetainedFloor = -1;

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
        if (Time.unscaledTime < nextReconcileTime)
        {
            return;
        }

        nextReconcileTime = Time.unscaledTime + ReconcileIntervalSeconds;
        Reconcile();
    }

    internal void Reconcile()
    {
        if (!MapHandler.ExistsAndInitialized || VoidBiome.VoidBiomeActive)
        {
            ResetState();
            return;
        }

        MapHandler map = Singleton<MapHandler>.Instance;
        if (map == null || map.segments == null || map.segments.Length == 0)
        {
            ResetState();
            return;
        }

        if (cachedMap != map)
        {
            transitionAccess.Reset();
            cachedMap = map;
            lastRetainedFloor = -1;
        }

        int currentSegment = Mathf.Clamp(
            (int)MapHandler.CurrentSegmentNumber,
            0,
            map.segments.Length - 1);
        int retainedFloor = DetermineRetainedFloor(map, currentSegment);
        ApplySegmentRetention(map, currentSegment, retainedFloor);
        transitionAccess.Reconcile(map, currentSegment);
        ApplyBoundaryPolicy(map, currentSegment);

        if (retainedFloor != lastRetainedFloor)
        {
            lastRetainedFloor = retainedFloor;
            Plugin.Log.LogInfo(
                $"Retaining contiguous map segments {retainedFloor}..{currentSegment} "
                + $"for {(RaceSettingsManager.Current.UsesPersonalCampfireClaims ? "personal PVP" : RaceSettingsManager.Current.WaitMode)} rules.");
        }
    }

    private static int DetermineRetainedFloor(MapHandler map, int currentSegment)
    {
        int retainedFloor = currentSegment;
        List<Character> characters = GetActivePlayerCharacters().ToList();

        // Never remove geometry containing (or below) a connected player.
        foreach (Character character in characters)
        {
            if (!LocalBiomeEnvironmentController.TryResolveCharacterSegment(
                character,
                out int physicalSegment))
            {
                return 0;
            }

            retainedFloor = Mathf.Min(retainedFloor, physicalSegment);
        }

        RaceSettingsSnapshot settings = RaceSettingsManager.Current;
        CampfireProgressionController progression = CampfireProgressionController.Instance;

        if (!settings.UsesPersonalCampfireClaims
            && settings.WaitMode == CampfireWaitMode.Team
            && progression != null)
        {
            // A team that has completed fire N is still working in segment N+1.
            // This remains true even if all of its members are temporarily dead.
            foreach (RaceTeamScope team in characters
                .Select(RaceTeamScope.ForCharacter)
                .Distinct())
            {
                Character representative = characters.First(team.Contains);
                int teamSegment = Mathf.Clamp(
                    progression.GetCompletedCampfireIndex(representative) + 1,
                    0,
                    currentSegment);
                retainedFloor = Mathf.Min(retainedFloor, teamSegment);
            }
        }

        bool completedCampfireIsRespawnTarget = settings.UsesPreviousCampfireRespawn
            || settings.UsesNextCampfireRespawn
            || settings.Mode == RespawnMode.Pvp;
        if (completedCampfireIsRespawnTarget)
        {
            foreach (Character character in characters)
            {
                Campfire checkpoint =
                    PlayerCampfireProgressTracker.GetPersonalPreviousCampfire(character);
                if (checkpoint == null)
                {
                    // Beach/shore remains the fallback target until a real
                    // checkpoint is earned.
                    retainedFloor = 0;
                    continue;
                }

                if (CampfireProgressionController.TryGetCampfireIndex(
                    checkpoint,
                    out int checkpointSegment))
                {
                    retainedFloor = Mathf.Min(retainedFloor, checkpointSegment);
                }
                else
                {
                    retainedFloor = 0;
                }
            }
        }

        int pendingRespawnSegment =
            RaceRespawnController.Instance?.GetLowestPendingRespawnSegment() ?? -1;
        if (pendingRespawnSegment >= 0)
        {
            retainedFloor = Mathf.Min(retainedFloor, pendingRespawnSegment);
        }

        return Mathf.Clamp(retainedFloor, 0, currentSegment);
    }

    private static void ApplySegmentRetention(
        MapHandler map,
        int currentSegment,
        int retainedFloor)
    {
        // The active segment is owned by MapHandler. We only reconcile older
        // segments that vanilla would otherwise discard during progression.
        for (int index = 0; index < currentSegment; index++)
        {
            bool shouldRetain = index >= retainedFloor;
            SetActiveIfDifferent(map.segments[index].segmentParent, shouldRetain);
            SetActiveIfDifferent(map.segments[index].segmentCampfire, shouldRetain);
        }
    }

    private void ApplyBoundaryPolicy(MapHandler map, int currentSegment)
    {
        for (int destinationSegment = 1;
            destinationSegment <= currentSegment;
            destinationSegment++)
        {
            // Both objects are broad vanilla biome seals, not precise checkpoint
            // gates. Either one can overlap the campfire approach and strand a
            // racer below an already-lit fire. Once MapHandler has loaded the
            // destination, keep the passed transition physically open and let
            // the host-authoritative progression guard below enforce access.
            SetActiveIfDifferent(
                map.segments[destinationSegment - 1].wallNext,
                false);
            SetActiveIfDifferent(
                map.segments[destinationSegment].wallPrevious,
                false);
        }

        // PEAK's future-biome seal is a very large volume. It is safe for a
        // racer already admitted into the current biome, but on a lagging
        // client it can overlap the retained route below the previous fire.
        // Keep it open until that local racer earns entry; the master-client
        // progression guard prevents skipping the fire in the meantime.
        bool shouldEnableUpperSeal =
            transitionAccess.ShouldEnableUpperBiomeSeal(map, currentSegment);
        GameObject upperSeal = map.segments[currentSegment].wallNext;
        if (upperSeal != null && upperSeal.activeSelf != shouldEnableUpperSeal)
        {
            upperSeal.SetActive(shouldEnableUpperSeal);
            Plugin.Log.LogInfo(
                $"{(shouldEnableUpperSeal ? "Enabled" : "Opened")} upper biome "
                + $"seal {currentSegment} for the local checkpoint scope.");
        }
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

    private static void SetActiveIfDifferent(GameObject gameObject, bool active)
    {
        if (gameObject != null && gameObject.activeSelf != active)
        {
            gameObject.SetActive(active);
        }
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        ResetState();
        nextReconcileTime = 0f;
    }

    private void ResetState()
    {
        cachedMap = null;
        lastRetainedFloor = -1;
        transitionAccess.Reset();
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
