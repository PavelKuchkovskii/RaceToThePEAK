using HarmonyLib;
using Peak.Afflictions;
using PeakRace.Core;
using Photon.Pun;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PeakRace.Patch;

/// <summary>
/// Provides an owner-authenticated Photon route from a character to the
/// master-client ability authority. Persistent state itself stays in room
/// properties so host migration and late joins do not lose a slot.
/// </summary>
[HarmonyPatch]
internal sealed class CampfireAbilityState : MonoBehaviourPunCallbacks
{
    private const float MegaLaunchTargetSpeed = 30f;
    private const float MegaLaunchMinimumFlightSeconds = 1.25f;
    private const float MegaLaunchMaximumFlightSeconds = 4f;
    private const float MegaLaunchLandingBufferSeconds = 1f;

    private static readonly CharacterAfflictions.STATUSTYPE[]
        SecondWindTemporaryStatuses =
        {
            CharacterAfflictions.STATUSTYPE.Cold,
            CharacterAfflictions.STATUSTYPE.Hot,
            CharacterAfflictions.STATUSTYPE.Poison,
            CharacterAfflictions.STATUSTYPE.Spores,
            CharacterAfflictions.STATUSTYPE.Drowsy
        };

    private Character character;

    [HarmonyPatch(typeof(Character), nameof(Character.Awake))]
    [HarmonyPostfix]
    private static void Attach(Character __instance)
    {
        if (__instance.GetComponent<CampfireAbilityState>() == null)
        {
            __instance.gameObject.AddComponent<CampfireAbilityState>();
        }
    }

    private void Awake()
    {
        character = GetComponent<Character>();
    }

    internal void RequestUse(bool chaosSlot)
    {
        if (character == null || !photonView.IsMine)
        {
            return;
        }

        if (!PhotonNetwork.InRoom || PhotonNetwork.IsMasterClient)
        {
            CampfireAbilityManager.Instance?.HandleUseRequest(character, chaosSlot);
            return;
        }

        photonView.RPC(
            nameof(RPCA_RequestUseCampfireAbility),
            RpcTarget.MasterClient,
            chaosSlot);
    }

    internal void SendFeedback(string text, Color color)
    {
        if (!PhotonNetwork.InRoom)
        {
            if (character == Character.localCharacter)
            {
                CampfireAbilityManager.Instance?.ShowFeedback(text, color);
            }
            return;
        }

        photonView.RPC(
            nameof(RPCA_ShowCampfireAbilityFeedback),
            RpcTarget.All,
            text,
            color.r,
            color.g,
            color.b);
    }

    internal void RequestSecondWind()
    {
        if (character == null || !photonView.IsMine)
        {
            return;
        }

        if (!PhotonNetwork.InRoom || PhotonNetwork.IsMasterClient)
        {
            CampfireAbilityManager.Instance?.HandleSecondWindRequest(character);
            return;
        }

        photonView.RPC(nameof(RPCA_RequestSecondWind), RpcTarget.MasterClient);
    }

    internal void RequestHiddenMegaLaunchFoodConsumption(Guid instanceId)
    {
        if (character == null
            || !photonView.IsMine
            || instanceId == Guid.Empty)
        {
            return;
        }

        if (!PhotonNetwork.InRoom || PhotonNetwork.IsMasterClient)
        {
            CampfireAbilityManager.Instance
                ?.HandleHiddenMegaLaunchFoodConsumed(instanceId, character);
            return;
        }

        photonView.RPC(
            nameof(RPCA_RequestHiddenMegaLaunchFoodConsumption),
            RpcTarget.MasterClient,
            instanceId.ToString("N"));
    }

    internal void SendAdrenaline()
    {
        if (!PhotonNetwork.InRoom)
        {
            CampfireItemEffectApplicator.ApplyAdrenaline(character);
            return;
        }

        photonView.RPC(nameof(RPCA_ApplyAdrenaline), RpcTarget.All);
    }

    internal void SendRemoveExtraStamina()
    {
        if (!PhotonNetwork.InRoom)
        {
            character?.SetExtraStamina(0f);
            return;
        }

        photonView.RPC(nameof(RPCA_RemoveExtraStamina), RpcTarget.All);
    }

    internal void SendFullStamina()
    {
        if (!PhotonNetwork.InRoom)
        {
            character?.AddStamina(1f);
            return;
        }

        photonView.RPC(nameof(RPCA_GainFullStamina), RpcTarget.All);
    }

    internal void SendInfiniteStamina(float seconds)
    {
        if (!PhotonNetwork.InRoom)
        {
            character?.refs.afflictions.AddAffliction(
                new Affliction_InfiniteStamina(seconds));
            return;
        }

        photonView.RPC(
            nameof(RPCA_ApplyInfiniteStamina),
            RpcTarget.All,
            seconds);
    }

    internal void SendSecondWindRecovery(double immunityUntil)
    {
        if (!PhotonNetwork.InRoom)
        {
            ApplySecondWindRecovery(immunityUntil);
            return;
        }

        photonView.RPC(
            nameof(RPCA_ApplySecondWindRecovery),
            RpcTarget.All,
            immunityUntil);
    }

    internal void SendMegaLaunchCountdown(double launchAt)
    {
        if (!PhotonNetwork.InRoom)
        {
            if (photonView.IsMine)
            {
                StartCoroutine(MegaLaunchCountdown(launchAt));
            }
            return;
        }

        photonView.RPC(
            nameof(RPCA_StartMegaLaunchCountdown),
            RpcTarget.All,
            launchAt);
    }

    internal void SendMegaLaunchImpulse(
        Vector3 direction,
        float distanceMeters)
    {
        if (!PhotonNetwork.InRoom)
        {
            ApplyMegaLaunchImpulse(direction, distanceMeters);
            return;
        }

        photonView.RPC(
            nameof(RPCA_ApplyMegaLaunchImpulse),
            RpcTarget.All,
            direction,
            distanceMeters);
    }

    private void RequestMegaLaunchImpulse(Vector3 direction)
    {
        if (!PhotonNetwork.InRoom || PhotonNetwork.IsMasterClient)
        {
            CampfireAbilityManager.Instance?.HandleMegaLaunchImpulseRequest(
                character,
                direction);
            return;
        }

        photonView.RPC(
            nameof(RPCA_RequestMegaLaunchImpulse),
            RpcTarget.MasterClient,
            direction);
    }

    [PunRPC]
    private void RPCA_StartMegaLaunchCountdown(
        double launchAt,
        PhotonMessageInfo messageInfo)
    {
        if (AbilityRpcValidation.IsAuthorityMessage(messageInfo)
            && photonView.IsMine)
        {
            StartCoroutine(MegaLaunchCountdown(launchAt));
        }
    }

    [PunRPC]
    private void RPCA_RequestMegaLaunchImpulse(
        Vector3 direction,
        PhotonMessageInfo messageInfo)
    {
        if (!IsOwnerRequest(messageInfo))
        {
            Plugin.Log.LogWarning("Rejected an unauthorized Mega Launch impulse.");
            return;
        }

        CampfireAbilityManager.Instance?.HandleMegaLaunchImpulseRequest(
            character,
            direction);
    }

    [PunRPC]
    private void RPCA_ApplyMegaLaunchImpulse(
        Vector3 direction,
        float distanceMeters,
        PhotonMessageInfo messageInfo)
    {
        if (AbilityRpcValidation.IsAuthorityMessage(messageInfo))
        {
            ApplyMegaLaunchImpulse(direction, distanceMeters);
        }
    }

    private IEnumerator MegaLaunchCountdown(double launchAt)
    {
        int lastSecond = -1;
        while (NetworkTime < launchAt)
        {
            int remaining = Mathf.Max(
                1,
                Mathf.CeilToInt((float)(launchAt - NetworkTime)));
            if (remaining != lastSecond)
            {
                lastSecond = remaining;
                CampfireAbilityManager.Instance?.ShowFeedback(
                    $"MEGA LAUNCH IN {remaining}...",
                    Plugin.Color);
            }
            yield return null;
        }

        Vector3 direction = MainCamera.instance != null
            ? MainCamera.instance.transform.forward
            : character?.data?.lookDirection ?? Vector3.forward;
        RequestMegaLaunchImpulse(direction);
    }

    private void ApplyMegaLaunchImpulse(
        Vector3 direction,
        float distanceMeters)
    {
        if (character == null || character.data == null || character.data.dead)
        {
            return;
        }

        float safeDistance = float.IsNaN(distanceMeters)
                || float.IsInfinity(distanceMeters)
            ? 75f
            : Mathf.Clamp(distanceMeters, 10f, 250f);
        float flightSeconds = Mathf.Clamp(
            safeDistance / MegaLaunchTargetSpeed,
            MegaLaunchMinimumFlightSeconds,
            MegaLaunchMaximumFlightSeconds);
        StartCoroutine(MaintainMegaLaunchProtection());

        // PEAK's own scout cannon disables active ragdoll control before
        // launching. Without this transition, standing and movement forces can
        // absorb an impulse while the character is still touching the ground.
        character.data.launchedByCannon = true;
        character.RPCA_Fall(
            flightSeconds + MegaLaunchLandingBufferSeconds,
            0f);
        if (!photonView.IsMine)
        {
            return;
        }

        character.refs.movement.CapFallDamage(0f, 15f);
        StartCoroutine(ApplyMegaLaunchVelocity(
            direction,
            safeDistance,
            flightSeconds));
    }

    private IEnumerator ApplyMegaLaunchVelocity(
        Vector3 direction,
        float distanceMeters,
        float flightSeconds)
    {
        // Let Character.FixedUpdate observe fallSeconds and release active
        // ragdoll control before changing velocity.
        yield return new WaitForFixedUpdate();

        if (character == null || character.data == null || character.data.dead)
        {
            yield break;
        }

        Vector3 safeDirection = direction;
        if (!IsFinite(safeDirection) || safeDirection.sqrMagnitude <= 0.01f)
        {
            safeDirection = character.data.lookDirection;
        }
        if (!IsFinite(safeDirection) || safeDirection.sqrMagnitude <= 0.01f)
        {
            safeDirection = Vector3.forward;
        }
        safeDirection.Normalize();
        Vector3 launchVelocity = safeDirection
                * (distanceMeters / flightSeconds)
            - Physics.gravity * (0.5f * flightSeconds);
        int launchedBodies = 0;

        // Bodypart.AddForce does not preserve its ForceMode argument in PEAK
        // 2.0: it buffers the vector and later always applies ForceMode.Force.
        // Assign every owned rigidbody the same ballistic velocity directly.
        // Ignoring collisions, gravity then places the character the configured
        // number of metres along the camera direction after flightSeconds.
        foreach (Bodypart bodypart in character.refs.ragdoll.partList)
        {
            Rigidbody body = bodypart?.Rig;
            if (body == null || body.isKinematic)
            {
                continue;
            }

            body.WakeUp();
            body.linearVelocity = launchVelocity;
            launchedBodies++;
        }

        if (launchedBodies == 0)
        {
            Plugin.Log.LogError(
                $"Mega Launch could not find a dynamic body for {character.characterName}.");
            CampfireAbilityManager.Instance?.ShowFeedback(
                "MEGA LAUNCH FAILED  •  NO DYNAMIC BODY",
                Color.red);
            yield break;
        }

        Plugin.Log.LogInfo(
            $"Applied Mega Launch to {character.characterName}: "
            + $"target {distanceMeters:0.#} m over {flightSeconds:0.##} s, "
            + $"velocity {launchVelocity.magnitude:0.##} m/s across "
            + $"{launchedBodies} bodies.");
    }

    private bool IsFinite(Vector3 value)
    {
        return !float.IsNaN(value.x)
            && !float.IsInfinity(value.x)
            && !float.IsNaN(value.y)
            && !float.IsInfinity(value.y)
            && !float.IsNaN(value.z)
            && !float.IsInfinity(value.z);
    }

    private IEnumerator MaintainMegaLaunchProtection()
    {
        float started = Time.unscaledTime;
        do
        {
            CampfireAbilityManager.Instance?.MarkMegaLaunchImmunity(
                character,
                NetworkTime + 0.5d);
            yield return null;
        }
        while (Time.unscaledTime - started < 15f
            && (Time.unscaledTime - started < 0.5f || !character.data.isGrounded));

        CampfireAbilityManager.Instance?.MarkMegaLaunchImmunity(
            character,
            NetworkTime + 2d);
    }

    [PunRPC]
    private void RPCA_RequestSecondWind(PhotonMessageInfo messageInfo)
    {
        if (!IsOwnerRequest(messageInfo))
        {
            Plugin.Log.LogWarning("Rejected an unauthorized Second Wind request.");
            return;
        }

        CampfireAbilityManager.Instance?.HandleSecondWindRequest(character);
    }

    [PunRPC]
    private void RPCA_ApplyAdrenaline(PhotonMessageInfo messageInfo)
    {
        if (AbilityRpcValidation.IsAuthorityMessage(messageInfo) && photonView.IsMine)
        {
            CampfireItemEffectApplicator.ApplyAdrenaline(character);
        }
    }

    [PunRPC]
    private void RPCA_RemoveExtraStamina(PhotonMessageInfo messageInfo)
    {
        if (AbilityRpcValidation.IsAuthorityMessage(messageInfo) && photonView.IsMine)
        {
            character.SetExtraStamina(0f);
        }
    }

    [PunRPC]
    private void RPCA_GainFullStamina(PhotonMessageInfo messageInfo)
    {
        if (AbilityRpcValidation.IsAuthorityMessage(messageInfo) && photonView.IsMine)
        {
            character.AddStamina(1f);
        }
    }

    [PunRPC]
    private void RPCA_ApplyInfiniteStamina(
        float seconds,
        PhotonMessageInfo messageInfo)
    {
        if (AbilityRpcValidation.IsAuthorityMessage(messageInfo) && photonView.IsMine)
        {
            character.refs.afflictions.AddAffliction(
                new Affliction_InfiniteStamina(Mathf.Clamp(seconds, 0f, 30f)));
        }
    }

    [PunRPC]
    private void RPCA_ApplySecondWindRecovery(
        double immunityUntil,
        PhotonMessageInfo messageInfo)
    {
        if (AbilityRpcValidation.IsAuthorityMessage(messageInfo))
        {
            ApplySecondWindRecovery(immunityUntil);
        }
    }

    private void ApplySecondWindRecovery(double immunityUntil)
    {
        CampfireAbilityManager.Instance?.MarkSecondWindImmunity(
            character,
            immunityUntil);
        if (photonView.IsMine)
        {
            StartCoroutine(RecoverWithSecondWind());
        }
    }

    private IEnumerator RecoverWithSecondWind()
    {
        yield return new WaitForSeconds(0.35f);
        if (character == null
            || character.data == null
            || character.data.dead
            || character.data.shouldPetrify)
        {
            yield break;
        }

        CharacterAfflictions afflictions = character.refs.afflictions;
        ClearSecondWindTemporaryEffects(afflictions);

        // Second Wind remains a reliable ordinary recovery even when a small
        // amount of injury or hunger also contributed to the knockout. Reduce
        // only the remaining amount required to wake up, using PEAK's own
        // curability policy so persistent conditions are never removed.
        float excess = Mathf.Max(0f, afflictions.statusSum - 0.95f);
        foreach (CharacterAfflictions.STATUSTYPE status in
            Enum.GetValues(typeof(CharacterAfflictions.STATUSTYPE)))
        {
            if (excess <= 0f)
            {
                break;
            }
            if (!afflictions.StatusIsCurable(
                status,
                isCurseCurable: false,
                isPetrifyCurable: false))
            {
                continue;
            }

            float current = afflictions.GetCurrentStatus(status);
            float reduction = Mathf.Min(current, excess);
            if (reduction > 0f)
            {
                afflictions.SubtractStatus(status, reduction);
                excess -= reduction;
            }
        }

        if (afflictions.statusSum >= 1f)
        {
            yield break;
        }

        character.data.passOutValue = 0f;
        character.data.deathTimer = 0f;
        photonView.RPC("RPCA_UnPassOut", RpcTarget.All);
    }

    private static void ClearSecondWindTemporaryEffects(
        CharacterAfflictions afflictions)
    {
        // Remove harmful over-time sources before clearing their accumulated
        // status. Beneficial warming/sedation-recovery effects and the persistent
        // Zombie Bite mechanic are deliberately retained.
        bool removedAffliction = false;
        foreach (Affliction affliction in
            new List<Affliction>(afflictions.afflictionList))
        {
            bool isHarmfulTemporarySource = affliction switch
            {
                Affliction_PoisonOverTime poison => poison.statusPerSecond > 0f,
                Affliction_AdjustColdOverTime cold => cold.statusPerSecond > 0f,
                Affliction_AdjustDrowsyOverTime drowsy => drowsy.statusPerSecond > 0f,
                _ => false
            };
            if (!isHarmfulTemporarySource)
            {
                continue;
            }

            afflictions.RemoveAffliction(
                affliction,
                fromRPC: false,
                pushAfflictions: false);
            removedAffliction = true;
        }

        if (removedAffliction)
        {
            afflictions.PushAfflictions(null, -1);
        }

        bool clearedStatus = false;
        foreach (CharacterAfflictions.STATUSTYPE status in
            SecondWindTemporaryStatuses)
        {
            if (afflictions.GetCurrentStatus(status) <= 0f)
            {
                continue;
            }

            afflictions.SetStatus(status, 0f, pushStatus: false);
            clearedStatus = true;
        }

        if (clearedStatus)
        {
            afflictions.PushStatuses(null);
        }
    }

    [PunRPC]
    private void RPCA_ShowCampfireAbilityFeedback(
        string text,
        float red,
        float green,
        float blue,
        PhotonMessageInfo messageInfo)
    {
        if ((messageInfo.Sender == null || messageInfo.Sender.IsMasterClient)
            && photonView.IsMine)
        {
            CampfireAbilityManager.Instance?.ShowFeedback(
                text,
                new Color(red, green, blue, 1f));
        }
    }

    [PunRPC]
    private void RPCA_RequestUseCampfireAbility(
        bool chaosSlot,
        PhotonMessageInfo messageInfo)
    {
        if (!PhotonNetwork.IsMasterClient
            || messageInfo.Sender == null
            || photonView.Owner == null
            || messageInfo.Sender.ActorNumber != photonView.Owner.ActorNumber)
        {
            Plugin.Log.LogWarning("Rejected an unauthorized campfire ability request.");
            return;
        }

        CampfireAbilityManager.Instance?.HandleUseRequest(character, chaosSlot);
    }

    [PunRPC]
    private void RPCA_RequestHiddenMegaLaunchFoodConsumption(
        string instanceIdText,
        PhotonMessageInfo messageInfo)
    {
        if (!IsOwnerRequest(messageInfo))
        {
            Plugin.Log.LogWarning(
                "Rejected an unauthorized hidden Mega Launch food request.");
            return;
        }

        if (!Guid.TryParseExact(instanceIdText, "N", out Guid instanceId))
        {
            Plugin.Log.LogWarning(
                "Rejected a malformed hidden Mega Launch food identifier.");
            return;
        }

        CampfireAbilityManager.Instance
            ?.HandleHiddenMegaLaunchFoodConsumed(instanceId, character);
    }

    private bool IsOwnerRequest(PhotonMessageInfo messageInfo)
    {
        return PhotonNetwork.IsMasterClient
            && messageInfo.Sender != null
            && photonView.Owner != null
            && messageInfo.Sender.ActorNumber == photonView.Owner.ActorNumber;
    }

    private static double NetworkTime => PhotonNetwork.InRoom
        ? PhotonNetwork.Time
        : Time.unscaledTime;

}

internal static class AbilityRpcValidation
{
    internal static bool IsAuthorityMessage(PhotonMessageInfo messageInfo)
    {
        return messageInfo.Sender == null || messageInfo.Sender.IsMasterClient;
    }
}
