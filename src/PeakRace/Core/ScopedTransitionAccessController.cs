using Photon.Pun;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Zorro.Core;

namespace PeakRace.Core;

/// <summary>
/// Keeps globally loaded biome boundaries open while enforcing progression at
/// the much narrower, local checkpoint boundary. The local collider provides
/// immediate physical feedback; the master-client correction is the authority
/// fallback for tunnelling, physics desync, and modified clients.
/// </summary>
internal sealed class ScopedTransitionAccessController
{
    private const float GateWidth = 4096f;
    private const float GateHeight = 4096f;
    private const float GateDepth = 0.75f;
    private const float CorrectionCooldownSeconds = 0.75f;

    private readonly Dictionary<int, TransitionGate> gates = new();
    private readonly Dictionary<int, TransitionSafePosition> safePositions = new();
    private readonly Dictionary<int, float> nextCorrectionTimes = new();

    internal void Reconcile(MapHandler map, int currentSegment)
    {
        if (map == null || map.segments == null)
        {
            Reset();
            return;
        }

        RaceSettingsSnapshot settings = RaceSettingsManager.Current;
        CampfireProgressionController progression =
            CampfireProgressionController.Instance;
        bool usesScopedAccess = progression != null
            && (settings.UsesPersonalCampfireClaims
                || settings.WaitMode != CampfireWaitMode.Nobody);

        ReconcileLocalGates(map, currentSegment, progression, usesScopedAccess);

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

    internal void Reset()
    {
        foreach (TransitionGate gate in gates.Values)
        {
            gate.Destroy();
        }

        gates.Clear();
        safePositions.Clear();
        nextCorrectionTimes.Clear();
    }

    private void ReconcileLocalGates(
        MapHandler map,
        int currentSegment,
        CampfireProgressionController progression,
        bool usesScopedAccess)
    {
        Character localCharacter = Character.localCharacter;
        int completedCampfire = usesScopedAccess && localCharacter != null
            ? progression.GetCompletedCampfireIndex(localCharacter)
            : currentSegment;

        for (int destinationSegment = 1;
            destinationSegment <= currentSegment;
            destinationSegment++)
        {
            if (!gates.TryGetValue(destinationSegment, out TransitionGate gate))
            {
                gate = CreateGate(map, destinationSegment);
                if (gate == null)
                {
                    continue;
                }

                gates.Add(destinationSegment, gate);
            }

            bool shouldBlockLocalPlayer = usesScopedAccess
                && localCharacter != null
                && completedCampfire < destinationSegment - 1;
            gate.SetBlocking(shouldBlockLocalPlayer, localCharacter);
        }

        foreach (int obsoleteSegment in gates.Keys
            .Where(segment => segment > currentSegment)
            .ToArray())
        {
            gates[obsoleteSegment].Destroy();
            gates.Remove(obsoleteSegment);
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

    private static TransitionGate CreateGate(MapHandler map, int destinationSegment)
    {
        if (!TryGetBoundaryPosition(map, destinationSegment, out Vector3 position))
        {
            Plugin.Log.LogWarning(
                $"Could not resolve checkpoint boundary for segment {destinationSegment}; "
                + "the host progression correction remains active.");
            return null;
        }

        GameObject gateObject = new(
            $"RaceToThePeak_TransitionGate_{destinationSegment}");
        gateObject.transform.SetParent(map.transform, worldPositionStays: true);
        gateObject.transform.position = position;

        BoxCollider collider = gateObject.AddComponent<BoxCollider>();
        collider.isTrigger = false;
        collider.size = new Vector3(GateWidth, GateHeight, GateDepth);

        ScopedTransitionGateFeedback feedback =
            gateObject.AddComponent<ScopedTransitionGateFeedback>();
        feedback.Initialize(collider);
        gateObject.SetActive(false);
        return new TransitionGate(gateObject, collider, feedback);
    }

    private static bool TryGetBoundaryPosition(
        MapHandler map,
        int destinationSegment,
        out Vector3 position)
    {
        Campfire campfire = TryGetTransitionCampfire(map, destinationSegment);
        float minimumForwardPosition = campfire != null
            ? campfire.transform.position.z
                + Mathf.Max(5f, campfire.moraleBoostRadius + 2f)
            : float.NegativeInfinity;

        MountainProgressHandler progressHandler =
            Singleton<MountainProgressHandler>.Instance;
        MountainProgressHandler.ProgressPoint[] points =
            progressHandler?.progressPoints;
        if (points != null
            && destinationSegment >= 0
            && destinationSegment < points.Length
            && points[destinationSegment]?.transform != null)
        {
            position = points[destinationSegment].transform.position;
            position.z = Mathf.Max(position.z, minimumForwardPosition);
            return true;
        }

        if (campfire != null)
        {
            position = campfire.transform.position;
            position.z = minimumForwardPosition;
            return true;
        }

        position = default;
        return false;
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

        for (int destinationSegment = 1;
            destinationSegment <= currentSegment;
            destinationSegment++)
        {
            if (!gates.TryGetValue(destinationSegment, out TransitionGate gate))
            {
                return false;
            }

            if (character.Center.z <= gate.ForwardPosition + GateDepth * 0.5f)
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

    private sealed class TransitionGate
    {
        private readonly GameObject gameObject;
        private readonly BoxCollider collider;
        private readonly ScopedTransitionGateFeedback feedback;

        internal TransitionGate(
            GameObject gameObject,
            BoxCollider collider,
            ScopedTransitionGateFeedback feedback)
        {
            this.gameObject = gameObject;
            this.collider = collider;
            this.feedback = feedback;
        }

        internal float ForwardPosition => gameObject != null
            ? gameObject.transform.position.z
            : float.PositiveInfinity;

        internal void SetBlocking(bool blocking, Character localCharacter)
        {
            if (gameObject == null)
            {
                return;
            }

            if (gameObject.activeSelf != blocking)
            {
                gameObject.SetActive(blocking);
            }

            if (!blocking)
            {
                return;
            }

            feedback.SetLocalCharacter(localCharacter);
            foreach (Character character in GetActivePlayerCharacters())
            {
                bool ignore = character != localCharacter;
                foreach (Collider characterCollider in
                    character.GetComponentsInChildren<Collider>(true))
                {
                    if (characterCollider != null && characterCollider != collider)
                    {
                        Physics.IgnoreCollision(collider, characterCollider, ignore);
                    }
                }
            }
        }

        internal void Destroy()
        {
            if (gameObject != null)
            {
                Object.Destroy(gameObject);
            }
        }
    }
}

/// <summary>
/// Shows a throttled local explanation when the scoped transition gate is hit.
/// </summary>
internal sealed class ScopedTransitionGateFeedback : MonoBehaviour
{
    private const float FeedbackCooldownSeconds = 1f;

    private Collider gateCollider;
    private Character localCharacter;
    private float nextFeedbackTime;

    internal void Initialize(Collider collider)
    {
        gateCollider = collider;
    }

    internal void SetLocalCharacter(Character character)
    {
        localCharacter = character;
    }

    private void OnCollisionEnter(Collision collision)
    {
        ShowFeedbackIfLocal(collision.collider);
    }

    private void OnCollisionStay(Collision collision)
    {
        ShowFeedbackIfLocal(collision.collider);
    }

    private void ShowFeedbackIfLocal(Collider other)
    {
        if (gateCollider == null
            || localCharacter == null
            || Time.unscaledTime < nextFeedbackTime
            || other.GetComponentInParent<Character>() != localCharacter)
        {
            return;
        }

        nextFeedbackTime = Time.unscaledTime + FeedbackCooldownSeconds;
        CampfireAbilityManager.Instance?.ShowFeedback(
            "ACTIVATE THE CAMPFIRE TO CONTINUE",
            Plugin.Color);
    }
}
