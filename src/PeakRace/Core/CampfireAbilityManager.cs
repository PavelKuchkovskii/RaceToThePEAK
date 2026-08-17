using BepInEx.Configuration;
using ExitGames.Client.Photon;
using PeakRace.Patch;
using Photon.Pun;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace PeakRace.Core;

/// <summary>
/// Master-authoritative campfire ability inventory. Room properties are the
/// durable source of truth; character RPCs carry authenticated use requests.
/// </summary>
internal sealed class CampfireAbilityManager : MonoBehaviourPunCallbacks
{
    private const string AbilityKeyPrefix = "RTP.Ability.";
    private const string ChaosKeyPrefix = "RTP.Chaos.";
    private const string ChaosFireKeyPrefix = "RTP.ChaosFire.";
    private const float FeedbackSeconds = 3.5f;

    private readonly Dictionary<int, CampfireAbility> abilities = new();
    private readonly HashSet<int> chaosActors = new();
    private readonly HashSet<int> chaosAwardedCampfires = new();
    private readonly Dictionary<CampfireAbility, ConfigEntry<float>> abilityWeights = new();

    private ConfigEntry<Key> abilityKeyConfig;
    private ConfigEntry<Key> chaosKeyConfig;
    private string feedbackText;
    private Color feedbackColor = Color.white;
    private float feedbackUntil;
    private bool initialized;
    private bool clearedOutsideRun;

    internal static CampfireAbilityManager Instance { get; private set; }

    private static bool IsAuthority => !PhotonNetwork.InRoom || PhotonNetwork.IsMasterClient;

    internal string AbilityKeyDisplayName => (abilityKeyConfig?.Value ?? Key.F4).ToString();

    internal string ChaosKeyDisplayName => (chaosKeyConfig?.Value ?? Key.F5).ToString();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    internal void Initialize(ConfigFile config)
    {
        abilityKeyConfig = config.Bind(
            "PVP Abilities",
            "AbilityKey",
            Key.F4,
            "Key used to activate the current non-passive Campfire Ability.");
        chaosKeyConfig = config.Bind(
            "PVP Abilities",
            "ChaosKey",
            Key.F5,
            "Key used to activate the separately stored Chaos charge.");

        BindWeight(config, CampfireAbility.Adrenaline, 12f);
        BindWeight(config, CampfireAbility.Shield, 12f);
        BindWeight(config, CampfireAbility.Exhaust, 12f);
        BindWeight(config, CampfireAbility.SecondWind, 12f);
        BindWeight(config, CampfireAbility.CatchUp, 12f);
        BindWeight(config, CampfireAbility.Recall, 10f);
        BindWeight(config, CampfireAbility.ChaosHorn, 10f);
        BindWeight(config, CampfireAbility.GhostRunner, 8f);
        BindWeight(config, CampfireAbility.MegaLaunch, 12f);
        initialized = true;
    }

    private void BindWeight(
        ConfigFile config,
        CampfireAbility ability,
        float defaultWeight)
    {
        abilityWeights[ability] = config.Bind(
            "PVP Ability Weights",
            CampfireAbilityInfo.GetName(ability).Replace(" ", string.Empty) + "Weight",
            defaultWeight,
            new ConfigDescription(
                $"Relative campfire roll weight for {CampfireAbilityInfo.GetName(ability)}. Zero disables it.",
                new AcceptableValueRange<float>(0f, 1000f)));
    }

    private void Update()
    {
        string scene = SceneManager.GetActiveScene().name;
        if (scene is "Airport" or "Title")
        {
            if (!clearedOutsideRun)
            {
                ClearRunState(clearRoomProperties: true);
                clearedOutsideRun = true;
            }
            return;
        }

        clearedOutsideRun = false;
        if (!initialized
            || RaceSettingsManager.Current.Mode != RespawnMode.Pvp
            || Character.localCharacter == null
            || Keyboard.current == null
            || GUIManager.InPauseMenu
            || GUIManager.instance?.windowBlockingInput == true)
        {
            return;
        }

        Character localCharacter = Character.localCharacter;
        CampfireAbilityState state = localCharacter.GetComponent<CampfireAbilityState>();
        if (state == null)
        {
            return;
        }

        Key abilityKey = abilityKeyConfig.Value;
        if (abilityKey != Key.None && Keyboard.current[abilityKey].wasPressedThisFrame)
        {
            CampfireAbility ability = GetAbility(localCharacter);
            if (CampfireAbilityInfo.IsPassive(ability))
            {
                ShowFeedback("PASSIVE ABILITY", Plugin.Color);
            }
            else
            {
                state.RequestUse(chaosSlot: false);
            }
        }

        Key chaosKey = chaosKeyConfig.Value;
        if (chaosKey != Key.None && Keyboard.current[chaosKey].wasPressedThisFrame)
        {
            state.RequestUse(chaosSlot: true);
        }
    }

    internal void HandleCampfireCompleted(
        Character character,
        int campfireIndex)
    {
        if (!IsAuthority
            || RaceSettingsManager.Current.Mode != RespawnMode.Pvp
            || character == null
            || campfireIndex < 0)
        {
            return;
        }

        CampfireAbility awarded = RollAbility();
        SetAbility(character, awarded);
        NotifyAward(character, CampfireAbilityInfo.GetName(awarded), isChaos: false);

        if (!chaosAwardedCampfires.Contains(campfireIndex)
            && IsLastToComplete(character, campfireIndex))
        {
            SetChaos(character, hasChaos: true);
            MarkChaosAwarded(campfireIndex);
            NotifyAward(character, "CHAOS CHARGE", isChaos: true);
            Plugin.Log.LogInfo(
                $"{character.characterName} was last to complete campfire {campfireIndex} and received Chaos.");
        }
    }

    internal void HandleUseRequest(Character character, bool chaosSlot)
    {
        if (!IsAuthority
            || RaceSettingsManager.Current.Mode != RespawnMode.Pvp
            || !IsActivePlayerCharacter(character))
        {
            return;
        }

        if (chaosSlot)
        {
            if (!HasChaos(character))
            {
                NotifyMessage(character, "NO CHAOS CHARGE", Color.gray);
                return;
            }

            // Effect selection is added by the effect layer. Never consume a
            // charge until that layer confirms an effect was started.
            NotifyMessage(character, "CHAOS READY", new Color(1f, 0.34f, 0.23f, 1f));
            return;
        }

        CampfireAbility ability = GetAbility(character);
        if (ability == CampfireAbility.None)
        {
            NotifyMessage(character, "NO CAMPFIRE ABILITY", Color.gray);
        }
        else if (CampfireAbilityInfo.IsPassive(ability))
        {
            NotifyMessage(character, "PASSIVE ABILITY", Plugin.Color);
        }
        else
        {
            // The inventory and authentication boundary are deliberately
            // complete before individual effects are attached.
            NotifyMessage(character, $"{CampfireAbilityInfo.GetName(ability)} READY", Plugin.Color);
        }
    }

    internal CampfireAbility GetAbility(Character character)
    {
        int actorNumber = GetActorNumber(character);
        return abilities.TryGetValue(actorNumber, out CampfireAbility ability)
            ? ability
            : CampfireAbility.None;
    }

    internal bool HasChaos(Character character)
    {
        return chaosActors.Contains(GetActorNumber(character));
    }

    internal bool TryGetFeedback(out string text, out Color color)
    {
        text = feedbackText;
        color = feedbackColor;
        return !string.IsNullOrEmpty(text) && Time.unscaledTime < feedbackUntil;
    }

    internal void ShowFeedback(string text, Color color)
    {
        feedbackText = text;
        feedbackColor = color;
        feedbackUntil = Time.unscaledTime + FeedbackSeconds;
    }

    private CampfireAbility RollAbility()
    {
        float total = 0f;
        foreach ((CampfireAbility ability, ConfigEntry<float> entry) in abilityWeights)
        {
            if (ability != CampfireAbility.None)
            {
                total += Mathf.Max(0f, entry.Value);
            }
        }

        if (total <= 0f)
        {
            Plugin.Log.LogWarning(
                "Every PVP campfire ability weight is zero; falling back to Adrenaline.");
            return CampfireAbility.Adrenaline;
        }

        float roll = UnityEngine.Random.Range(0f, total);
        foreach ((CampfireAbility ability, ConfigEntry<float> entry) in abilityWeights
            .OrderBy(pair => (int)pair.Key))
        {
            roll -= Mathf.Max(0f, entry.Value);
            if (roll <= 0f)
            {
                return ability;
            }
        }

        return CampfireAbility.MegaLaunch;
    }

    private bool IsLastToComplete(Character completingCharacter, int campfireIndex)
    {
        CampfireProgressionController progression = CampfireProgressionController.Instance;
        if (progression == null)
        {
            return false;
        }

        bool foundCompletingCharacter = false;
        foreach (Character candidate in GetActivePlayerCharacters())
        {
            foundCompletingCharacter |= candidate == completingCharacter;
            if (progression.GetCompletedCampfireIndex(candidate) < campfireIndex)
            {
                return false;
            }
        }

        return foundCompletingCharacter;
    }

    private void SetAbility(Character character, CampfireAbility ability)
    {
        int actorNumber = GetActorNumber(character);
        if (actorNumber == 0)
        {
            return;
        }

        if (!SetRoomProperty(AbilityKey(actorNumber), (int)ability))
        {
            return;
        }

        abilities[actorNumber] = ability;
        Plugin.Log.LogInfo(
            $"{character.characterName} received {CampfireAbilityInfo.GetName(ability)}.");
    }

    private void SetChaos(Character character, bool hasChaos)
    {
        int actorNumber = GetActorNumber(character);
        if (actorNumber == 0
            || !SetRoomProperty(ChaosKey(actorNumber), hasChaos))
        {
            return;
        }

        if (hasChaos)
        {
            chaosActors.Add(actorNumber);
        }
        else
        {
            chaosActors.Remove(actorNumber);
        }
    }

    private void MarkChaosAwarded(int campfireIndex)
    {
        if (SetRoomProperty(ChaosFireKey(campfireIndex), true))
        {
            chaosAwardedCampfires.Add(campfireIndex);
        }
    }

    private static bool SetRoomProperty(string key, object value)
    {
        if (!PhotonNetwork.InRoom)
        {
            return true;
        }

        return PhotonNetwork.CurrentRoom != null
            && PhotonNetwork.CurrentRoom.SetCustomProperties(new Hashtable
            {
                [key] = value
            });
    }

    private void NotifyAward(Character character, string award, bool isChaos)
    {
        string text = isChaos ? $"RECEIVED {award}" : $"NEW ABILITY: {award}";
        Color color = isChaos ? new Color(1f, 0.34f, 0.23f, 1f) : Plugin.Color;
        NotifyMessage(character, text, color);
    }

    private void NotifyMessage(Character character, string text, Color color)
    {
        CampfireAbilityState state = character?.GetComponent<CampfireAbilityState>();
        if (state != null)
        {
            state.SendFeedback(text, color);
        }
        else if (character == Character.localCharacter)
        {
            ShowFeedback(text, color);
        }
    }

    private void ApplyRoomProperties(Hashtable properties)
    {
        if (properties == null)
        {
            return;
        }

        foreach (object rawKey in properties.Keys)
        {
            if (rawKey is not string key)
            {
                continue;
            }

            object boxed = properties[rawKey];
            if (TryParseSuffix(key, AbilityKeyPrefix, out int abilityActor))
            {
                if (boxed == null)
                {
                    abilities.Remove(abilityActor);
                    continue;
                }

                try
                {
                    int rawAbility = Convert.ToInt32(boxed);
                    abilities[abilityActor] = Enum.IsDefined(
                        typeof(CampfireAbility),
                        rawAbility)
                        ? (CampfireAbility)rawAbility
                        : CampfireAbility.None;
                }
                catch (Exception)
                {
                    Plugin.Log.LogWarning($"Ignored malformed campfire ability '{key}'.");
                }
            }
            else if (TryParseSuffix(key, ChaosKeyPrefix, out int chaosActor))
            {
                bool enabled = false;
                try
                {
                    enabled = boxed != null && Convert.ToBoolean(boxed);
                }
                catch (Exception)
                {
                    Plugin.Log.LogWarning($"Ignored malformed Chaos charge '{key}'.");
                }

                if (enabled)
                {
                    chaosActors.Add(chaosActor);
                }
                else
                {
                    chaosActors.Remove(chaosActor);
                }
            }
            else if (TryParseSuffix(key, ChaosFireKeyPrefix, out int campfireIndex))
            {
                bool awarded = false;
                try
                {
                    awarded = boxed != null && Convert.ToBoolean(boxed);
                }
                catch (Exception)
                {
                    Plugin.Log.LogWarning($"Ignored malformed Chaos campfire '{key}'.");
                }

                if (awarded)
                {
                    chaosAwardedCampfires.Add(campfireIndex);
                }
                else
                {
                    chaosAwardedCampfires.Remove(campfireIndex);
                }
            }
        }
    }

    private static bool TryParseSuffix(string key, string prefix, out int value)
    {
        value = 0;
        return key.StartsWith(prefix, StringComparison.Ordinal)
            && int.TryParse(key.Substring(prefix.Length), out value)
            && value >= 0;
    }

    private void ClearRunState(bool clearRoomProperties)
    {
        abilities.Clear();
        chaosActors.Clear();
        chaosAwardedCampfires.Clear();
        feedbackText = null;

        if (!clearRoomProperties
            || !PhotonNetwork.InRoom
            || !PhotonNetwork.IsMasterClient
            || PhotonNetwork.CurrentRoom == null)
        {
            return;
        }

        Hashtable removals = new();
        foreach (object rawKey in PhotonNetwork.CurrentRoom.CustomProperties.Keys)
        {
            if (rawKey is string key
                && (key.StartsWith(AbilityKeyPrefix, StringComparison.Ordinal)
                    || key.StartsWith(ChaosKeyPrefix, StringComparison.Ordinal)
                    || key.StartsWith(ChaosFireKeyPrefix, StringComparison.Ordinal)))
            {
                removals[key] = null;
            }
        }

        if (removals.Count > 0)
        {
            PhotonNetwork.CurrentRoom.SetCustomProperties(removals);
        }
    }

    private static int GetActorNumber(Character character)
    {
        return character?.photonView?.Owner?.ActorNumber
            ?? character?.photonView?.ViewID
            ?? character?.GetInstanceID()
            ?? 0;
    }

    private static IEnumerable<Character> GetActivePlayerCharacters()
    {
        IEnumerable<Character> source = PhotonNetwork.InRoom
            ? PlayerHandler.GetAllPlayerCharacters()
            : Character.AllCharacters;
        return source.Where(IsActivePlayerCharacter);
    }

    private static bool IsActivePlayerCharacter(Character character)
    {
        return character != null
            && !character.isBot
            && character.photonView != null
            && (character.photonView.Owner == null
                || !character.photonView.Owner.IsInactive);
    }

    private static string AbilityKey(int actorNumber) => AbilityKeyPrefix + actorNumber;

    private static string ChaosKey(int actorNumber) => ChaosKeyPrefix + actorNumber;

    private static string ChaosFireKey(int campfireIndex) => ChaosFireKeyPrefix + campfireIndex;

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name is "Airport" or "Title")
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
        ClearRunState(clearRoomProperties: false);
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
