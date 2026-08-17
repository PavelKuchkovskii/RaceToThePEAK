using Photon.Pun;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Zorro.Core;

namespace PeakRace.Core;

/// <summary>
/// Enforces earned checkpoint access without adding world-sized colliders.
/// PEAK's biome seals overlap retained lower routes because vanilla only loads
/// the next biome after everybody has arrived. The race keeps those seals open
/// for lagging clients and lets the master client correct an actual bypass only
/// after the player has clearly left the campfire interaction area.
/// </summary>
internal sealed class ScopedTransitionAccessController
{
    private const float CorrectionCooldownSeconds = 0.75f;
    private const float ForwardBoundaryPadding = 1f;

    private readonly Dictionary<int, TransitionBoundary> boundaries = new();
    private readonly Dictionary<int, TransitionSafePosition> safePositions = new();
    private readonly Dictionary<int, float> nextCorrectionTimes = new();

    internal void Reconcile(MapHandler map, int currentSegment)
    {
        if (map == null || map.segments == null)
        {
            Reset();
            return;
        }

        ReconcileBoundaries(map, currentSegment);

        RaceSettingsSnapshot settings = RaceSettingsManager.Current;
        CampfireProgressionController progression =
            CampfireProgressionController.Instance;
        bool usesScopedAccess = progression != null
            && (settings.UsesPersonalCampfireClaims
                || settings.WaitMode != CampfireWaitMode.Nobody);

        if (usesScopedAccess && HasProgressionAuthority)
        {
            ReconcileAuthoritativePositions(map, currentSegment, progression);
        }
        else
        {
            safePositions.Clear();
            nextCorrectionTimes.Clear();
        }
    }

    internal bool ShouldEnableUpperBiomeSeal(MapHandler map, int currentSegment)
    {
        if (currentSegment < 0)
        {
            return true;
        }

        RaceSettingsSnapshot settings = RaceSettingsManager.Current;
        if (!settings.UsesPersonalCampfireClaims
            && settings.WaitMode == CampfireWaitMode.Nobody)
        {
            return true;
        }

        Character localCharacter = Character.localCharacter;
        CampfireProgressionController progression =
            CampfireProgressionController.Instance;
        if (localCharacter == null || progression == null)
        {
            return true;
        }

        int completedCampfire =
            progression.GetCompletedCampfireIndex(localCharacter);

        // During a guest's transition RPC there can be a short interval where
        // the fire is lit but CurrentSegmentNumber still points at the source.
        if (IsCampfireLit(map, currentSegment)
            && completedCampfire < currentSegment)
        {
            return false;
        }

        // The upper seal belongs to the globally current biome. It is safe to
        // restore only after this client has earned entry into that biome.
        return completedCampfire >= currentSegment - 1;
    }

    internal void Reset()
    {
        boundaries.Clear();
        safePositions.Clear();
        nextCorrectionTimes.Clear();
    }

    private void ReconcileBoundaries(MapHandler map, int currentSegment)
    {
        for (int destinationSegment = 1;
            destinationSegment <= currentSegment;
            destinationSegment++)
        {
            if (TryCreateBoundary(
                map,
                destinationSegment,
                out TransitionBoundary boundary))
            {
                boundaries[destinationSegment] = boundary;
            }
        }

        foreach (int obsoleteSegment in boundaries.Keys
            .Where(segment => segment > currentSegment)
            .ToArray())
        {
            boundaries.Remove(obsoleteSegment);
        }
    }

    private void ReconcileAuthoritativePositions(
        MapHandler map,
        int currentSegment,
        CampfireProgressionController progression)
    {
        List<Character> characters = GetActivePlayerCharacters().ToList();
        HashSet<int> connectedIds = new();

        foreach (Character character in characters)
        {
            int characterId = GetStableCharacterId(character);
            connectedIds.Add(characterId);

            if (!TryResolveCheckpointSegment(
                character,
                currentSegment,
                out int physicalSegment))
            {
                continue;
            }

            int accessibleSegment = Mathf.Clamp(
                progression.GetCompletedCampfireIndex(character) + 1,
                0,
                currentSegment);
            if (physicalSegment <= accessibleSegment)
            {
                RememberSafePosition(characterId, character, physicalSegment);
                continue;
            }

            if (Time.unscaledTime < GetNextCorrectionTime(characterId))
            {
                continue;
            }

            Vector3 correctionPosition = ResolveCorrectionPosition(
                map,
                accessibleSegment,
                characterId,
                character);
            CorrectPosition(character, correctionPosition);
            nextCorrectionTimes[characterId] =
                Time.unscaledTime + CorrectionCooldownSeconds;

            character.GetComponent<Patch.CampfireAbilityState>()?.SendFeedback(
                "ACTIVATE THE CAMPFIRE TO CONTINUE",
                Plugin.Color);
            Plugin.Log.LogWarning(
                $"Corrected {GetDisplayName(character)} from locked segment "
                + $"{physicalSegment} to accessible segment {accessibleSegment}.");
        }

        PruneDisconnectedState(connectedIds);
    }

    private static bool TryCreateBoundary(
        MapHandler map,
        int destinationSegment,
        out TransitionBoundary boundary)
    {
        Campfire campfire = TryGetTransitionCampfire(map, destinationSegment);
        if (campfire == null)
        {
            boundary = default;
            return false;
        }

        Vector3 campfirePosition = campfire.transform.position;
        float forwardPosition = campfirePosition.z
            + Mathf.Max(3f, campfire.moraleBoostRadius + 1f);

        MountainProgressHandler progressHandler =
            Singleton<MountainProgressHandler>.Instance;
        MountainProgressHandler.ProgressPoint[] points =
            progressHandler?.progressPoints;
        if (points != null
            && destinationSegment >= 0
            && destinationSegment < points.Length
            && points[destinationSegment]?.transform != null)
        {
            // PEAK uses this forward plane to recognize entry into the
            // destination biome. Reusing it with a one-metre safety margin
            // keeps access, environment, scoring, and retention aligned.
            forwardPosition = points[destinationSegment].transform.position.z
                + ForwardBoundaryPadding;
        }

        boundary = new TransitionBoundary(forwardPosition);
        return true;
    }

    private static Campfire TryGetTransitionCampfire(
        MapHandler map,
        int destinationSegment)
    {
        int campfireIndex = destinationSegment - 1;
        return campfireIndex >= 0 && campfireIndex < map.segments.Length
            ? map.segments[campfireIndex].segmentCampfire
                ?.GetComponentInChildren<Campfire>(true)
            : null;
    }

    private bool TryResolveCheckpointSegment(
        Character character,
        int currentSegment,
        out int segment)
    {
        segment = 0;
        if (character == null)
        {
            return false;
        }

        Vector3 position = character.Center;
        for (int destinationSegment = 1;
            destinationSegment <= currentSegment;
            destinationSegment++)
        {
            if (!boundaries.TryGetValue(
                destinationSegment,
                out TransitionBoundary boundary))
            {
                return false;
            }

            bool clearlyPastCampfire = position.z > boundary.ForwardPosition;
            if (!clearlyPastCampfire)
            {
                break;
            }

            segment = destinationSegment;
        }

        return true;
    }

    private void RememberSafePosition(
        int characterId,
        Character character,
        int physicalSegment)
    {
        if (character.data == null
            || character.data.dead
            || !character.data.isGrounded)
        {
            return;
        }

        safePositions[characterId] = new TransitionSafePosition(
            physicalSegment,
            character.Center);
    }

    private Vector3 ResolveCorrectionPosition(
        MapHandler map,
        int accessibleSegment,
        int characterId,
        Character character)
    {
        if (safePositions.TryGetValue(
            characterId,
            out TransitionSafePosition safePosition)
            && safePosition.Segment <= accessibleSegment)
        {
            return safePosition.Position;
        }

        int campfireIndex = Mathf.Clamp(
            accessibleSegment,
            0,
            map.segments.Length - 1);
        Campfire campfire = map.segments[campfireIndex].segmentCampfire
            ?.GetComponentInChildren<Campfire>(true);
        if (campfire != null)
        {
            return campfire.transform.position
                + Vector3.back * 2f
                + Vector3.up * 1.5f;
        }

        return character.Center + Vector3.back * 3f + Vector3.up;
    }

    private static void CorrectPosition(Character character, Vector3 position)
    {
        if (PhotonNetwork.InRoom)
        {
            character.photonView.RPC(
                "WarpPlayerRPC",
                RpcTarget.All,
                position,
                false);
            return;
        }

        character.WarpPlayer(position, false);
    }

    private void PruneDisconnectedState(HashSet<int> connectedIds)
    {
        foreach (int characterId in safePositions.Keys
            .Where(id => !connectedIds.Contains(id))
            .ToArray())
        {
            safePositions.Remove(characterId);
        }

        foreach (int characterId in nextCorrectionTimes.Keys
            .Where(id => !connectedIds.Contains(id))
            .ToArray())
        {
            nextCorrectionTimes.Remove(characterId);
        }
    }

    private float GetNextCorrectionTime(int characterId)
    {
        return nextCorrectionTimes.TryGetValue(characterId, out float value)
            ? value
            : 0f;
    }

    private static bool IsCampfireLit(MapHandler map, int campfireIndex)
    {
        Campfire campfire = map.segments[campfireIndex].segmentCampfire
            ?.GetComponentInChildren<Campfire>(true);
        return campfire != null && campfire.state != Campfire.FireState.Off;
    }

    private static bool HasProgressionAuthority =>
        !PhotonNetwork.InRoom || PhotonNetwork.IsMasterClient;

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

    private static int GetStableCharacterId(Character character)
    {
        return character.photonView?.Owner?.ActorNumber
            ?? character.photonView?.ViewID
            ?? character.GetInstanceID();
    }

    private static string GetDisplayName(Character character)
    {
        return character.photonView?.Owner?.NickName
            ?? character.characterName
            ?? "Player";
    }

    private readonly struct TransitionBoundary
    {
        internal TransitionBoundary(float forwardPosition)
        {
            ForwardPosition = forwardPosition;
        }

        internal float ForwardPosition { get; }
    }

    private readonly struct TransitionSafePosition
    {
        internal TransitionSafePosition(int segment, Vector3 position)
        {
            Segment = segment;
            Position = position;
        }

        internal int Segment { get; }
        internal Vector3 Position { get; }
    }
}
