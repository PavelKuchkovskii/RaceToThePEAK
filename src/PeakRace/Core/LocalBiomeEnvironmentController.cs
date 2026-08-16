using Peak;
using System;
using UnityEngine;
using Zorro.Core;

namespace PeakRace.Core;

/// <summary>
/// Keeps global visual state local to the character being observed. PEAK normally
/// unloads the previous segment before changing its single day/night profile and
/// global fog shader values. RaceToThePeak deliberately keeps those segments, so
/// every client must select the environment that belongs to its own camera.
/// </summary>
internal sealed class LocalBiomeEnvironmentController : MonoBehaviour
{
    private const float ForwardBoundaryHysteresis = 1f;
    private const float BackwardBoundaryHysteresis = 2f;
    private const float ProfileRepairCooldown = 0.5f;

    internal static LocalBiomeEnvironmentController Instance { get; private set; }

    private MapHandler cachedMap;
    private DayNightManager cachedDayNightManager;
    private int visualSegment = -1;
    private float nextProfileRepairTime;

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void Update()
    {
        if (!RefreshVisualSegment(out MapHandler map, out DayNightManager dayNightManager))
        {
            ResetContext();
            return;
        }

        DayNightProfile desiredProfile = GetProfile(map, visualSegment);
        if (desiredProfile == null ||
            dayNightManager.currentProfile == desiredProfile ||
            dayNightManager.newProfile == desiredProfile ||
            Time.unscaledTime < nextProfileRepairTime)
        {
            return;
        }

        // This also repairs late-join/reconnect frames where vanilla selected
        // the globally unlocked segment before the observed character existed.
        nextProfileRepairTime = Time.unscaledTime + ProfileRepairCooldown;
        dayNightManager.BlendProfiles(desiredProfile);
    }

    internal static bool TrySelectLocalProfile(
        DayNightProfile requestedProfile,
        out DayNightProfile selectedProfile)
    {
        selectedProfile = requestedProfile;
        LocalBiomeEnvironmentController controller = Instance;
        if (controller == null || requestedProfile == null ||
            !controller.RefreshVisualSegment(out MapHandler map, out _))
        {
            return false;
        }

        int unlockedSegment = GetUnlockedSegment(map);
        DayNightProfile unlockedProfile = GetProfile(map, unlockedSegment);

        // Only replace PEAK's map-transition request. Other systems are still
        // free to blend a deliberate special profile.
        if (unlockedProfile == null || requestedProfile != unlockedProfile)
        {
            return false;
        }

        DayNightProfile localProfile = GetProfile(map, controller.visualSegment);
        if (localProfile == null)
        {
            return false;
        }

        selectedProfile = localProfile;
        return selectedProfile != requestedProfile;
    }

    internal static bool ShouldRunSegmentVisual(Component visualComponent)
    {
        LocalBiomeEnvironmentController controller = Instance;
        if (controller == null || visualComponent == null ||
            !controller.RefreshVisualSegment(out MapHandler map, out _))
        {
            return true;
        }

        int ownerSegment = FindOwningSegment(map, visualComponent.transform);
        if (ownerSegment >= 0)
        {
            return ownerSegment == controller.visualSegment;
        }

        Biome ownerBiome = visualComponent.GetComponentInParent<Biome>();
        if (ownerBiome != null)
        {
            return map.segments[controller.visualSegment].biome == ownerBiome.biomeType;
        }

        if (FinalHazardController.TryShouldRunManagedVisual(
            visualComponent,
            out bool shouldRunFinalVisual))
        {
            return shouldRunFinalVisual;
        }

        // The Gloom managers in some map variants sit beside the selected
        // segment root rather than beneath it. They still exclusively belong
        // to the Swamp/Gloom biome.
        if (visualComponent is FogSafeZoneManager ||
            visualComponent is ForceFogShaderHeight forceFog &&
            forceFog.fogType == ForceFogShaderHeight.FogTypes.Gloom)
        {
            return map.segments[controller.visualSegment].biome == Biome.BiomeType.Swamp;
        }

        // Unknown/global components retain vanilla behaviour.
        return true;
    }

    internal static bool TryGetObservedSegment(out int segment)
    {
        segment = -1;
        LocalBiomeEnvironmentController controller = Instance;
        if (controller == null || !controller.RefreshVisualSegment(out _, out _))
        {
            return false;
        }

        segment = controller.visualSegment;
        return segment >= 0;
    }

    internal static bool TryResolveCharacterSegment(Character character, out int segment)
    {
        segment = -1;
        if (character == null || !MapHandler.ExistsAndInitialized || VoidBiome.VoidBiomeActive)
        {
            return false;
        }

        MapHandler map = Singleton<MapHandler>.Instance;
        if (map == null || map.segments == null || map.segments.Length == 0)
        {
            return false;
        }

        segment = ResolveSegmentFromProgress(map, character.Center.z, -1);
        return segment >= 0;
    }

    private bool RefreshVisualSegment(
        out MapHandler map,
        out DayNightManager dayNightManager)
    {
        map = null;
        dayNightManager = null;

        if (!MapHandler.ExistsAndInitialized || VoidBiome.VoidBiomeActive ||
            DayNightManager.instance == null)
        {
            return false;
        }

        Character observedCharacter = Character.observedCharacter;
        if (observedCharacter == null)
        {
            observedCharacter = Character.localCharacter;
        }

        if (observedCharacter == null)
        {
            return false;
        }

        map = Singleton<MapHandler>.Instance;
        dayNightManager = DayNightManager.instance;
        if (map == null || map.segments == null || map.segments.Length == 0)
        {
            return false;
        }

        bool contextChanged = cachedMap != map || cachedDayNightManager != dayNightManager;
        if (contextChanged)
        {
            cachedMap = map;
            cachedDayNightManager = dayNightManager;
            visualSegment = -1;
            nextProfileRepairTime = 0f;
        }

        int resolvedSegment = ResolveSegmentFromProgress(
            map,
            observedCharacter.Center.z,
            visualSegment);
        if (resolvedSegment != visualSegment)
        {
            visualSegment = resolvedSegment;
            Plugin.Log.LogInfo(
                $"Local visual environment: segment {visualSegment} " +
                $"({map.segments[visualSegment].biome}).");
        }

        return visualSegment >= 0;
    }

    private static int ResolveSegmentFromProgress(
        MapHandler map,
        float observedForwardPosition,
        int previousSegment)
    {
        int unlockedSegment = GetUnlockedSegment(map);
        int rawSegment = 0;

        MountainProgressHandler progressHandler = Singleton<MountainProgressHandler>.Instance;
        MountainProgressHandler.ProgressPoint[] progressPoints = progressHandler?.progressPoints;

        for (int segment = 1; segment <= unlockedSegment; segment++)
        {
            if (!TryGetBoundaryForwardPosition(map, progressPoints, segment, out float boundary))
            {
                // If PEAK changes its map layout and exposes no trustworthy
                // boundary, prefer the unlocked profile over guessing by height.
                return unlockedSegment;
            }

            if (observedForwardPosition > boundary)
            {
                rawSegment = segment;
            }
            else
            {
                break;
            }
        }

        if (previousSegment < 0 || previousSegment > unlockedSegment ||
            rawSegment == previousSegment)
        {
            return rawSegment;
        }

        // Avoid repeatedly starting two-second profile blends when somebody
        // circles around a biome's title trigger.
        if (rawSegment > previousSegment &&
            TryGetBoundaryForwardPosition(map, progressPoints, previousSegment + 1, out float nextBoundary) &&
            observedForwardPosition <= nextBoundary + ForwardBoundaryHysteresis)
        {
            return previousSegment;
        }

        if (rawSegment < previousSegment &&
            TryGetBoundaryForwardPosition(map, progressPoints, previousSegment, out float currentBoundary) &&
            observedForwardPosition >= currentBoundary - BackwardBoundaryHysteresis)
        {
            return previousSegment;
        }

        return rawSegment;
    }

    private static bool TryGetBoundaryForwardPosition(
        MapHandler map,
        MountainProgressHandler.ProgressPoint[] progressPoints,
        int segment,
        out float boundary)
    {
        if (progressPoints != null && segment >= 0 && segment < progressPoints.Length &&
            progressPoints[segment]?.transform != null)
        {
            boundary = progressPoints[segment].transform.position.z;
            return true;
        }

        int previousCampfireIndex = segment - 1;
        if (previousCampfireIndex >= 0 && previousCampfireIndex < map.segments.Length)
        {
            GameObject campfireRoot = map.segments[previousCampfireIndex].segmentCampfire;
            Campfire campfire = campfireRoot?.GetComponentInChildren<Campfire>(true);
            if (campfire != null)
            {
                boundary = campfire.transform.position.z;
                return true;
            }
        }

        boundary = 0f;
        return false;
    }

    private static int GetUnlockedSegment(MapHandler map)
    {
        return Mathf.Clamp((int)MapHandler.CurrentSegmentNumber, 0, map.segments.Length - 1);
    }

    private static DayNightProfile GetProfile(MapHandler map, int segment)
    {
        if (segment < 0 || segment >= map.segments.Length)
        {
            return null;
        }

        return map.segments[segment].dayNightProfile;
    }

    private static int FindOwningSegment(MapHandler map, Transform componentTransform)
    {
        for (int index = 0; index < map.segments.Length; index++)
        {
            GameObject segmentParent = map.segments[index].segmentParent;
            if (segmentParent != null &&
                (componentTransform == segmentParent.transform ||
                 componentTransform.IsChildOf(segmentParent.transform)))
            {
                return index;
            }
        }

        return -1;
    }

    private void ResetContext()
    {
        cachedMap = null;
        cachedDayNightManager = null;
        visualSegment = -1;
        nextProfileRepairTime = 0f;
    }
}
