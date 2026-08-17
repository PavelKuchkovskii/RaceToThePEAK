using HarmonyLib;
using PeakRace.Core;
using Photon.Pun;
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
}
