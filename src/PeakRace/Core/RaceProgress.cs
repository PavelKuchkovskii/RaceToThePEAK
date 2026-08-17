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
        float start = GetSegmentStartZ(map, segment);
        float end = GetCampfireZ(map, segment, start + 1f);
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

    private static float GetSegmentStartZ(MapHandler map, int segment)
    {
        if (segment > 0)
        {
            return GetCampfireZ(
                map,
                segment - 1,
                GetSegmentRootZ(map, segment));
        }

        MountainProgressHandler progressHandler =
            Singleton<MountainProgressHandler>.Instance;
        MountainProgressHandler.ProgressPoint[] points =
            progressHandler?.progressPoints;
        if (points != null
            && points.Length > 0
            && points[0]?.transform != null)
        {
            return points[0].transform.position.z;
        }

        // This fallback must be shared by every racer. Using the character's
        // own position here makes everybody in the first biome score zero.
        return GetSegmentRootZ(map, segment);
    }

    private static float GetSegmentRootZ(MapHandler map, int segment)
    {
        if (segment >= 0 && segment < map.segments.Length)
        {
            GameObject root = map.segments[segment].segmentParent;
            if (root != null)
            {
                return root.transform.position.z;
            }
        }

        return map.transform.position.z;
    }

    private static float GetCampfireZ(
        MapHandler map,
        int campfireIndex,
        float fallback)
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
