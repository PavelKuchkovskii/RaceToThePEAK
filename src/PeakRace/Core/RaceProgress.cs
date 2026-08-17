using UnityEngine;
using Zorro.Core;

namespace PeakRace.Core;

/// <summary>
/// Comparable race position based on earned checkpoints plus normalized
/// progress through the current segment. World distance never crosses a
/// checkpoint boundary on its own.
/// </summary>
internal readonly struct RaceProgress
{
    internal RaceProgress(int checkpointIndex, int segmentIndex, float segmentProgress)
    {
        CheckpointIndex = checkpointIndex;
        SegmentIndex = segmentIndex;
        SegmentProgress = Mathf.Clamp01(segmentProgress);
    }

    internal int CheckpointIndex { get; }

    internal int SegmentIndex { get; }

    internal float SegmentProgress { get; }

    internal float Score => SegmentIndex + SegmentProgress;

    internal static RaceProgress ForCharacter(Character character)
    {
        int checkpoint = CampfireProgressionController.Instance
            ?.GetCompletedCampfireIndex(character) ?? -1;
        int segment = Mathf.Max(0, checkpoint + 1);
        float progress = 0f;

        if (character == null
            || !MapHandler.ExistsAndInitialized
            || !LocalBiomeEnvironmentController.TryResolveCharacterSegment(
                character,
                out int physicalSegment))
        {
            return new RaceProgress(checkpoint, segment, progress);
        }

        MapHandler map = Singleton<MapHandler>.Instance;
        segment = Mathf.Clamp(physicalSegment, 0, map.segments.Length - 1);
        float start = GetBoundaryZ(map, segment - 1, character.Center.z);
        float end = GetBoundaryZ(map, segment, start + 1f);
        if (Mathf.Abs(end - start) > 0.01f)
        {
            progress = Mathf.Clamp01((character.Center.z - start) / (end - start));
        }

        // A retained biome can be revisited after a checkpoint was earned.
        // Never let falling backwards erase the logical race score.
        int earnedSegment = checkpoint + 1;
        if (earnedSegment > segment)
        {
            segment = earnedSegment;
            progress = 0f;
        }

        return new RaceProgress(checkpoint, segment, progress);
    }

    private static float GetBoundaryZ(MapHandler map, int campfireIndex, float fallback)
    {
        if (campfireIndex < 0 || campfireIndex >= map.segments.Length)
        {
            return fallback;
        }

        GameObject root = map.segments[campfireIndex].segmentCampfire;
        Campfire campfire = root?.GetComponentInChildren<Campfire>(true);
        return campfire != null ? campfire.transform.position.z : fallback;
    }
}
