using HarmonyLib;
using Peak.Afflictions;
using PeakRace.Core;
using Photon.Pun;
using System;
using System.Collections;
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

    internal void SendMegaLaunchImpulse(Vector3 direction, float force)
    {
        if (!PhotonNetwork.InRoom)
        {
            ApplyMegaLaunchImpulse(direction, force);
            return;
        }

        photonView.RPC(
            nameof(RPCA_ApplyMegaLaunchImpulse),
            RpcTarget.All,
            direction,
            force);
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
        float force,
        PhotonMessageInfo messageInfo)
    {
        if (AbilityRpcValidation.IsAuthorityMessage(messageInfo))
        {
            ApplyMegaLaunchImpulse(direction, force);
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

        Vector3 direction = character?.data?.lookDirection ?? Vector3.forward;
        RequestMegaLaunchImpulse(direction);
    }

    private void ApplyMegaLaunchImpulse(Vector3 direction, float force)
    {
        if (character == null || character.data == null || character.data.dead)
        {
            return;
        }

        StartCoroutine(MaintainMegaLaunchProtection());
        if (!photonView.IsMine)
        {
            return;
        }

        character.refs.movement.CapFallDamage(0f, 15f);
        character.data.sinceGrounded = 0f;
        character.AddForce(direction.normalized * force);
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

        // Remove only enough curable status to make ordinary unconscious
        // recovery valid. Persistent equipment weight and petrification are
        // deliberately not bypassed.
        float excess = Mathf.Max(0f, character.refs.afflictions.statusSum - 0.95f);
        foreach (CharacterAfflictions.STATUSTYPE status in
            Enum.GetValues(typeof(CharacterAfflictions.STATUSTYPE)))
        {
            if (excess <= 0f)
            {
                break;
            }
            if (status is CharacterAfflictions.STATUSTYPE.Weight
                or CharacterAfflictions.STATUSTYPE.Thorns
                or CharacterAfflictions.STATUSTYPE.Arrow
                or CharacterAfflictions.STATUSTYPE.Petrify)
            {
                continue;
            }

            float current = character.refs.afflictions.GetCurrentStatus(status);
            float reduction = Mathf.Min(current, excess);
            if (reduction > 0f)
            {
                character.refs.afflictions.SubtractStatus(status, reduction);
                excess -= reduction;
            }
        }

        if (character.refs.afflictions.statusSum >= 1f)
        {
            yield break;
        }

        character.data.passOutValue = 0f;
        character.data.deathTimer = 0f;
        photonView.RPC("RPCA_UnPassOut", RpcTarget.All);
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
