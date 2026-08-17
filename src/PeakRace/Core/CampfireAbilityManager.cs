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
    private const string InitialAbilityGrantedKeyPrefix = "RTP.InitialAbilityGranted.";
    private const string ChaosKeyPrefix = "RTP.Chaos.";
    private const string ChaosFireKeyPrefix = "RTP.ChaosFire.";
    private const string ExhaustUntilKeyPrefix = "RTP.ExhaustUntil.";
    private const string GhostRunnerUntilKeyPrefix = "RTP.GhostRunnerUntil.";
    private const string MegaCooldownUntilKeyPrefix = "RTP.MegaCooldownUntil.";
    private const string MegaLaunchFoodKeyPrefix = "RTP.MegaFood.";
    private const string RuleAbilityWeightPrefix = "RTP.PvpRule.AbilityWeight.";
    private const string RuleChaosWeightPrefix = "RTP.PvpRule.ChaosWeight.";
    private const string RuleMegaCooldownKey = "RTP.PvpRule.MegaCooldown";
    // Keep the legacy room-property key so existing room/config values retain
    // their numeric selection. The value now represents metres.
    private const string RuleMegaDistanceKey = "RTP.PvpRule.MegaForce";
    private const string RuleMegaFoodLeaderKey = "RTP.PvpRule.FoodLeader";
    private const string RuleMegaFoodMiddleKey = "RTP.PvpRule.FoodMiddle";
    private const string RuleMegaFoodNearLastKey = "RTP.PvpRule.FoodNearLast";
    private const string RuleMegaFoodLastKey = "RTP.PvpRule.FoodLast";
    private const string RuleMegaFoodFarBehindKey = "RTP.PvpRule.FoodFarBehind";
    private const float FeedbackSeconds = 3.5f;
    private const double ExhaustDurationSeconds = 8d;
    private const double SecondWindImmunitySeconds = 2d;

    private readonly Dictionary<int, CampfireAbility> abilities = new();
    private readonly HashSet<int> initialAbilityActors = new();
    private readonly HashSet<int> chaosActors = new();
    private readonly HashSet<int> chaosAwardedCampfires = new();
    private readonly Dictionary<int, double> exhaustedUntil = new();
    private readonly Dictionary<int, double> ghostRunnerUntil = new();
    private readonly Dictionary<int, double> megaCooldownUntil = new();
    private readonly Dictionary<int, double> secondWindImmunityUntil = new();
    private readonly Dictionary<int, double> megaLaunchImmunityUntil = new();
    private readonly Dictionary<int, PendingMegaLaunch> pendingMegaLaunches = new();
    private readonly HashSet<int> megaLaunchFoodViewIds = new();
    private readonly Dictionary<int, float> catchUpMultipliers = new();
    private readonly Dictionary<int, Vector3> lastSafePositions = new();
    private readonly Dictionary<CampfireAbility, ConfigEntry<float>> abilityWeights = new();
    private readonly Dictionary<ChaosEffect, ConfigEntry<float>> chaosWeights = new();
    private readonly Dictionary<CampfireAbility, float> activeAbilityWeights = new();
    private readonly Dictionary<ChaosEffect, float> activeChaosWeights = new();

    private ConfigEntry<Key> abilityKeyConfig;
    private ConfigEntry<Key> chaosKeyConfig;
    private ConfigEntry<float> megaLaunchCooldownConfig;
    private ConfigEntry<float> megaLaunchDistanceConfig;
    private ConfigEntry<float> megaFoodLeaderChanceConfig;
    private ConfigEntry<float> megaFoodMiddleChanceConfig;
    private ConfigEntry<float> megaFoodNearLastChanceConfig;
    private ConfigEntry<float> megaFoodLastChanceConfig;
    private ConfigEntry<float> megaFoodFarBehindChanceConfig;
    private float activeMegaLaunchCooldown;
    private float activeMegaLaunchDistance;
    private float activeMegaFoodLeaderChance;
    private float activeMegaFoodMiddleChance;
    private float activeMegaFoodNearLastChance;
    private float activeMegaFoodLastChance;
    private float activeMegaFoodFarBehindChance;
    private string feedbackText;
    private Color feedbackColor = Color.white;
    private float feedbackUntil;
    private bool initialized;
    private bool suppressPvpConfigEvents;
    private bool clearedOutsideRun;
    private float nextCatchUpRefreshTime;
    private float nextSafePositionRefreshTime;

    internal static CampfireAbilityManager Instance { get; private set; }

    private static bool IsAuthority => !PhotonNetwork.InRoom || PhotonNetwork.IsMasterClient;

    internal string AbilityKeyDisplayName => (abilityKeyConfig?.Value ?? Key.F).ToString();

    internal string ChaosKeyDisplayName => (chaosKeyConfig?.Value ?? Key.C).ToString();

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
            Key.F,
            "Key used to activate the current non-passive Campfire Ability.");
        chaosKeyConfig = config.Bind(
            "PVP Abilities",
            "ChaosKey",
            Key.C,
            "Key used to activate the separately stored Chaos charge.");
        ConfigEntry<int> keyBindingSchemaConfig = config.Bind(
            "Internal",
            "PvpAbilityKeyBindingSchema",
            0,
            "Internal one-time migration marker for PVP ability key defaults.");
        if (keyBindingSchemaConfig.Value < 1)
        {
            if (abilityKeyConfig.Value == Key.F4)
            {
                abilityKeyConfig.Value = Key.F;
            }
            if (chaosKeyConfig.Value == Key.F5)
            {
                chaosKeyConfig.Value = Key.C;
            }
            keyBindingSchemaConfig.Value = 1;
        }
        megaLaunchCooldownConfig = config.Bind(
            "PVP Abilities",
            "MegaLaunchCooldownSeconds",
            45f,
            new ConfigDescription(
                "Cooldown between uses of the reusable Mega Launch ability.",
                new AcceptableValueRange<float>(5f, 300f)));
        // Keep the legacy config key to preserve existing host selections.
        megaLaunchDistanceConfig = config.Bind(
            "PVP Abilities",
            "MegaLaunchForce",
            75f,
            new ConfigDescription(
                "Target Mega Launch travel distance in metres before collisions and terrain.",
                new AcceptableValueRange<float>(10f, 250f)));
        megaFoodLeaderChanceConfig = BindMegaFoodChance(
            config,
            "LeaderChancePercent",
            1.5f,
            "Hidden Mega Launch food chance for the current leader.");
        megaFoodMiddleChanceConfig = BindMegaFoodChance(
            config,
            "MiddleChancePercent",
            4f,
            "Hidden Mega Launch food chance for a middle-position racer.");
        megaFoodNearLastChanceConfig = BindMegaFoodChance(
            config,
            "NearLastChancePercent",
            7f,
            "Hidden Mega Launch food chance for a racer in the final quarter of the field.");
        megaFoodLastChanceConfig = BindMegaFoodChance(
            config,
            "LastChancePercent",
            12.5f,
            "Hidden Mega Launch food chance for the last-place racer.");
        megaFoodFarBehindChanceConfig = BindMegaFoodChance(
            config,
            "FarBehindChancePercent",
            18f,
            "Hidden Mega Launch food chance when the opener trails the leader by at least 1.5 segments.");

        BindWeight(config, CampfireAbility.Adrenaline, 12f);
        BindWeight(config, CampfireAbility.Shield, 12f);
        BindWeight(config, CampfireAbility.Exhaust, 12f);
        BindWeight(config, CampfireAbility.SecondWind, 12f);
        BindWeight(config, CampfireAbility.CatchUp, 12f);
        BindWeight(config, CampfireAbility.Recall, 10f);
        BindWeight(config, CampfireAbility.ChaosHorn, 10f);
        BindWeight(config, CampfireAbility.GhostRunner, 8f);
        BindWeight(config, CampfireAbility.MegaLaunch, 12f);
        BindChaosWeight(config, ChaosEffect.FullStamina, 22f);
        BindChaosWeight(config, ChaosEffect.InfiniteStamina, 18f);
        BindChaosWeight(config, ChaosEffect.GlobalAdrenaline, 18f);
        BindChaosWeight(config, ChaosEffect.GlobalUnconscious, 16f);
        BindChaosWeight(config, ChaosEffect.PlayerSwap, 12f);
        BindChaosWeight(config, ChaosEffect.PreviousCampfire, 10f);
        BindChaosWeight(config, ChaosEffect.MiddleCampfire, 4f);
        LoadActivePvpRulesFromLocalConfig();
        SetPvpConfigSubscriptions(subscribe: true);
        initialized = true;

        if (PhotonNetwork.InRoom)
        {
            if (PhotonNetwork.IsMasterClient
                && SceneManager.GetActiveScene().name == "Airport")
            {
                PublishLocalPvpRules();
            }
            else
            {
                ApplyRoomProperties(PhotonNetwork.CurrentRoom?.CustomProperties);
            }
        }
    }

    internal float GetAbilityWeight(CampfireAbility ability)
    {
        return activeAbilityWeights.TryGetValue(ability, out float weight)
            ? weight
            : 0f;
    }

    internal float GetChaosWeight(ChaosEffect effect)
    {
        return activeChaosWeights.TryGetValue(effect, out float weight)
            ? weight
            : 0f;
    }

    internal float MegaLaunchCooldownSeconds => activeMegaLaunchCooldown;

    internal float MegaLaunchDistanceMeters => activeMegaLaunchDistance;

    internal float GetMegaLaunchFoodChance(MegaLaunchFoodChanceTier tier)
    {
        return tier switch
        {
            MegaLaunchFoodChanceTier.Leader => activeMegaFoodLeaderChance,
            MegaLaunchFoodChanceTier.Middle => activeMegaFoodMiddleChance,
            MegaLaunchFoodChanceTier.NearLast => activeMegaFoodNearLastChance,
            MegaLaunchFoodChanceTier.Last => activeMegaFoodLastChance,
            MegaLaunchFoodChanceTier.FarBehind => activeMegaFoodFarBehindChance,
            _ => 0f
        };
    }

    internal void AdjustAbilityWeight(CampfireAbility ability, float delta)
    {
        if (CanEditPvpRules && abilityWeights.TryGetValue(ability, out ConfigEntry<float> entry))
        {
            entry.Value = Mathf.Clamp(entry.Value + delta, 0f, 1000f);
        }
    }

    internal void AdjustChaosWeight(ChaosEffect effect, float delta)
    {
        if (CanEditPvpRules && chaosWeights.TryGetValue(effect, out ConfigEntry<float> entry))
        {
            entry.Value = Mathf.Clamp(entry.Value + delta, 0f, 1000f);
        }
    }

    internal void AdjustMegaLaunchCooldown(float delta)
    {
        if (CanEditPvpRules)
        {
            megaLaunchCooldownConfig.Value = Mathf.Clamp(
                megaLaunchCooldownConfig.Value + delta,
                5f,
                300f);
        }
    }

    internal void AdjustMegaLaunchDistance(float delta)
    {
        if (CanEditPvpRules)
        {
            megaLaunchDistanceConfig.Value = Mathf.Clamp(
                megaLaunchDistanceConfig.Value + delta,
                10f,
                250f);
        }
    }

    internal void AdjustMegaLaunchFoodChance(
        MegaLaunchFoodChanceTier tier,
        float delta)
    {
        if (!CanEditPvpRules)
        {
            return;
        }

        ConfigEntry<float> entry = tier switch
        {
            MegaLaunchFoodChanceTier.Leader => megaFoodLeaderChanceConfig,
            MegaLaunchFoodChanceTier.Middle => megaFoodMiddleChanceConfig,
            MegaLaunchFoodChanceTier.NearLast => megaFoodNearLastChanceConfig,
            MegaLaunchFoodChanceTier.Last => megaFoodLastChanceConfig,
            MegaLaunchFoodChanceTier.FarBehind => megaFoodFarBehindChanceConfig,
            _ => null
        };
        if (entry != null)
        {
            entry.Value = Mathf.Clamp(entry.Value + delta, 0f, 100f);
        }
    }

    private bool CanEditPvpRules => RaceSettingsManager.Instance?.CanEditLobbySettings == true
        && RaceSettingsManager.Current.Mode == RespawnMode.Pvp;

    private static ConfigEntry<float> BindMegaFoodChance(
        ConfigFile config,
        string key,
        float defaultValue,
        string description)
    {
        return config.Bind(
            "PVP Hidden Mega Launch Food",
            key,
            defaultValue,
            new ConfigDescription(
                description,
                new AcceptableValueRange<float>(0f, 100f)));
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

    private void BindChaosWeight(
        ConfigFile config,
        ChaosEffect effect,
        float defaultWeight)
    {
        chaosWeights[effect] = config.Bind(
            "PVP Chaos Weights",
            effect + "Weight",
            defaultWeight,
            new ConfigDescription(
                $"Relative roll weight for the {effect} Chaos effect. Zero disables it.",
                new AcceptableValueRange<float>(0f, 1000f)));
    }

    private void LoadActivePvpRulesFromLocalConfig()
    {
        activeAbilityWeights.Clear();
        foreach ((CampfireAbility ability, ConfigEntry<float> entry) in abilityWeights)
        {
            activeAbilityWeights[ability] = Mathf.Clamp(entry.Value, 0f, 1000f);
        }

        activeChaosWeights.Clear();
        foreach ((ChaosEffect effect, ConfigEntry<float> entry) in chaosWeights)
        {
            activeChaosWeights[effect] = Mathf.Clamp(entry.Value, 0f, 1000f);
        }

        activeMegaLaunchCooldown = Mathf.Clamp(
            megaLaunchCooldownConfig.Value,
            5f,
            300f);
        activeMegaLaunchDistance = Mathf.Clamp(
            megaLaunchDistanceConfig.Value,
            10f,
            250f);
        activeMegaFoodLeaderChance = Mathf.Clamp(
            megaFoodLeaderChanceConfig.Value,
            0f,
            100f);
        activeMegaFoodMiddleChance = Mathf.Clamp(
            megaFoodMiddleChanceConfig.Value,
            0f,
            100f);
        activeMegaFoodNearLastChance = Mathf.Clamp(
            megaFoodNearLastChanceConfig.Value,
            0f,
            100f);
        activeMegaFoodLastChance = Mathf.Clamp(
            megaFoodLastChanceConfig.Value,
            0f,
            100f);
        activeMegaFoodFarBehindChance = Mathf.Clamp(
            megaFoodFarBehindChanceConfig.Value,
            0f,
            100f);
    }

    private void SetPvpConfigSubscriptions(bool subscribe)
    {
        foreach (ConfigEntry<float> entry in abilityWeights.Values)
        {
            if (subscribe)
            {
                entry.SettingChanged += OnPvpRuleConfigChanged;
            }
            else
            {
                entry.SettingChanged -= OnPvpRuleConfigChanged;
            }
        }

        foreach (ConfigEntry<float> entry in chaosWeights.Values)
        {
            if (subscribe)
            {
                entry.SettingChanged += OnPvpRuleConfigChanged;
            }
            else
            {
                entry.SettingChanged -= OnPvpRuleConfigChanged;
            }
        }

        ConfigEntry<float>[] scalarEntries =
        {
            megaLaunchCooldownConfig,
            megaLaunchDistanceConfig,
            megaFoodLeaderChanceConfig,
            megaFoodMiddleChanceConfig,
            megaFoodNearLastChanceConfig,
            megaFoodLastChanceConfig,
            megaFoodFarBehindChanceConfig
        };
        foreach (ConfigEntry<float> entry in scalarEntries)
        {
            if (subscribe)
            {
                entry.SettingChanged += OnPvpRuleConfigChanged;
            }
            else
            {
                entry.SettingChanged -= OnPvpRuleConfigChanged;
            }
        }
    }

    private void StoreActivePvpRulesInLocalConfig()
    {
        suppressPvpConfigEvents = true;
        try
        {
            foreach ((CampfireAbility ability, float weight) in activeAbilityWeights)
            {
                if (abilityWeights.TryGetValue(ability, out ConfigEntry<float> entry))
                {
                    entry.Value = weight;
                }
            }
            foreach ((ChaosEffect effect, float weight) in activeChaosWeights)
            {
                if (chaosWeights.TryGetValue(effect, out ConfigEntry<float> entry))
                {
                    entry.Value = weight;
                }
            }

            megaLaunchCooldownConfig.Value = activeMegaLaunchCooldown;
            megaLaunchDistanceConfig.Value = activeMegaLaunchDistance;
            megaFoodLeaderChanceConfig.Value = activeMegaFoodLeaderChance;
            megaFoodMiddleChanceConfig.Value = activeMegaFoodMiddleChance;
            megaFoodNearLastChanceConfig.Value = activeMegaFoodNearLastChance;
            megaFoodLastChanceConfig.Value = activeMegaFoodLastChance;
            megaFoodFarBehindChanceConfig.Value = activeMegaFoodFarBehindChance;
        }
        finally
        {
            suppressPvpConfigEvents = false;
        }
    }

    private void OnPvpRuleConfigChanged(object sender, EventArgs eventArgs)
    {
        if (!initialized || suppressPvpConfigEvents)
        {
            return;
        }

        if (!PhotonNetwork.InRoom)
        {
            LoadActivePvpRulesFromLocalConfig();
        }
        else if (PhotonNetwork.IsMasterClient
            && SceneManager.GetActiveScene().name == "Airport")
        {
            PublishLocalPvpRules();
        }
    }

    private void PublishLocalPvpRules()
    {
        LoadActivePvpRulesFromLocalConfig();
        if (!PhotonNetwork.InRoom
            || !PhotonNetwork.IsMasterClient
            || PhotonNetwork.CurrentRoom == null)
        {
            return;
        }

        Hashtable properties = new()
        {
            [RuleMegaCooldownKey] = activeMegaLaunchCooldown,
            [RuleMegaDistanceKey] = activeMegaLaunchDistance,
            [RuleMegaFoodLeaderKey] = activeMegaFoodLeaderChance,
            [RuleMegaFoodMiddleKey] = activeMegaFoodMiddleChance,
            [RuleMegaFoodNearLastKey] = activeMegaFoodNearLastChance,
            [RuleMegaFoodLastKey] = activeMegaFoodLastChance,
            [RuleMegaFoodFarBehindKey] = activeMegaFoodFarBehindChance
        };
        foreach ((CampfireAbility ability, float weight) in activeAbilityWeights)
        {
            properties[RuleAbilityWeightKey(ability)] = weight;
        }
        foreach ((ChaosEffect effect, float weight) in activeChaosWeights)
        {
            properties[RuleChaosWeightKey(effect)] = weight;
        }

        if (!PhotonNetwork.CurrentRoom.SetCustomProperties(properties))
        {
            Plugin.Log.LogWarning("Photon rejected the PVP ability rule update.");
        }
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
        GrantInitialAbilities();
        RefreshCatchUpMultipliers();
        RefreshSafePositions();
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

    private void GrantInitialAbilities()
    {
        if (!initialized
            || !IsAuthority
            || RaceSettingsManager.Current.Mode != RespawnMode.Pvp)
        {
            return;
        }

        foreach (Character character in GetActivePlayerCharacters())
        {
            int actorNumber = GetActorNumber(character);
            if (actorNumber == 0 || initialAbilityActors.Contains(actorNumber))
            {
                continue;
            }

            CampfireAbility awarded = RollAbility();
            if (!SetInitialAbility(actorNumber, awarded))
            {
                continue;
            }

            NotifyAward(character, CampfireAbilityInfo.GetName(awarded), isChaos: false);
            Plugin.Log.LogInfo(
                $"{character.characterName} received starting ability "
                + $"{CampfireAbilityInfo.GetName(awarded)}.");
        }
    }

    internal int RerollAbilitiesForTesting()
    {
        if (!CanUsePvpTestMode())
        {
            return 0;
        }

        List<(Character Character, int ActorNumber, CampfireAbility Ability)>
            assignments = new();
        HashSet<int> assignedActors = new();
        foreach (Character character in GetActivePlayerCharacters())
        {
            int actorNumber = GetActorNumber(character);
            if (actorNumber <= 0 || !assignedActors.Add(actorNumber))
            {
                continue;
            }

            assignments.Add((character, actorNumber, RollAbility()));
        }

        if (assignments.Count == 0)
        {
            return 0;
        }

        if (PhotonNetwork.InRoom)
        {
            Hashtable properties = new();
            foreach (var assignment in assignments)
            {
                properties[AbilityKey(assignment.ActorNumber)] =
                    (int)assignment.Ability;
                properties[MegaCooldownUntilKey(assignment.ActorNumber)] = 0d;
            }

            if (PhotonNetwork.CurrentRoom == null
                || !PhotonNetwork.CurrentRoom.SetCustomProperties(properties))
            {
                Plugin.Log.LogWarning(
                    "Photon rejected the PVP test ability reroll.");
                return 0;
            }
        }

        foreach (var assignment in assignments)
        {
            abilities[assignment.ActorNumber] = assignment.Ability;
            megaCooldownUntil.Remove(assignment.ActorNumber);
            pendingMegaLaunches.Remove(assignment.ActorNumber);
            NotifyAward(
                assignment.Character,
                CampfireAbilityInfo.GetName(assignment.Ability),
                isChaos: false);
        }

        Plugin.Log.LogInfo(
            $"PVP test mode rerolled abilities for {assignments.Count} player(s).");
        return assignments.Count;
    }

    internal int GrantChaosForTesting()
    {
        if (!CanUsePvpTestMode())
        {
            return 0;
        }

        List<(Character Character, int ActorNumber)> recipients = new();
        HashSet<int> assignedActors = new();
        foreach (Character character in GetActivePlayerCharacters())
        {
            int actorNumber = GetActorNumber(character);
            if (actorNumber <= 0 || !assignedActors.Add(actorNumber))
            {
                continue;
            }

            recipients.Add((character, actorNumber));
        }

        if (recipients.Count == 0)
        {
            return 0;
        }

        if (PhotonNetwork.InRoom)
        {
            Hashtable properties = new();
            foreach (var recipient in recipients)
            {
                properties[ChaosKey(recipient.ActorNumber)] = true;
            }

            if (PhotonNetwork.CurrentRoom == null
                || !PhotonNetwork.CurrentRoom.SetCustomProperties(properties))
            {
                Plugin.Log.LogWarning(
                    "Photon rejected the PVP test Chaos grant.");
                return 0;
            }
        }

        foreach (var recipient in recipients)
        {
            chaosActors.Add(recipient.ActorNumber);
            NotifyAward(recipient.Character, "CHAOS CHARGE", isChaos: true);
        }

        Plugin.Log.LogInfo(
            $"PVP test mode granted Chaos to {recipients.Count} player(s).");
        return recipients.Count;
    }

    private bool CanUsePvpTestMode()
    {
        string scene = SceneManager.GetActiveScene().name;
        RaceSettingsSnapshot settings = RaceSettingsManager.Current;
        return initialized
            && IsAuthority
            && scene is not "Airport" and not "Title"
            && settings.Mode == RespawnMode.Pvp
            && settings.PvpTestModeEnabled;
    }

    private bool SetInitialAbility(int actorNumber, CampfireAbility ability)
    {
        if (PhotonNetwork.InRoom
            && (PhotonNetwork.CurrentRoom == null
                || !PhotonNetwork.CurrentRoom.SetCustomProperties(new Hashtable
                {
                    [AbilityKey(actorNumber)] = (int)ability,
                    [InitialAbilityGrantedKey(actorNumber)] = true,
                    [MegaCooldownUntilKey(actorNumber)] = 0d
                })))
        {
            return false;
        }

        abilities[actorNumber] = ability;
        initialAbilityActors.Add(actorNumber);
        megaCooldownUntil.Remove(actorNumber);
        pendingMegaLaunches.Remove(actorNumber);
        return true;
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
        SetMegaCooldownUntil(character, 0d);
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

            ActivateChaos(character);
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
        else if (ability != CampfireAbility.GhostRunner
            && (character.data == null
            || character.data.dead
            || character.data.fullyPassedOut))
        {
            NotifyMessage(character, "ABILITY REQUIRES A LIVING SCOUT", Color.gray);
        }
        else
        {
            ActivateMainAbility(character, ability);
        }
    }

    internal void HandleSecondWindRequest(Character character)
    {
        if (!IsAuthority
            || RaceSettingsManager.Current.Mode != RespawnMode.Pvp
            || GetAbility(character) != CampfireAbility.SecondWind
            || character?.data == null
            || character.data.dead
            || character.data.shouldPetrify
            || (!character.data.passedOut && !character.data.fullyPassedOut))
        {
            return;
        }

        CampfireAbilityState state = character.GetComponent<CampfireAbilityState>();
        if (state == null)
        {
            return;
        }

        double immunityUntil = NetworkTime + SecondWindImmunitySeconds;
        SetAbility(character, CampfireAbility.None);
        state.SendSecondWindRecovery(immunityUntil);
        NotifyMessage(character, "SECOND WIND", Plugin.Color);
    }

    internal bool IsExhausted(Character character)
    {
        return exhaustedUntil.TryGetValue(
                GetActorNumber(character),
                out double deadline)
            && NetworkTime < deadline;
    }

    internal bool HasSecondWindImmunity(Character character)
    {
        return secondWindImmunityUntil.TryGetValue(
                GetActorNumber(character),
                out double deadline)
            && NetworkTime < deadline;
    }

    internal void MarkSecondWindImmunity(Character character, double deadline)
    {
        int actorNumber = GetActorNumber(character);
        if (actorNumber > 0 && deadline > NetworkTime)
        {
            secondWindImmunityUntil[actorNumber] = deadline;
        }
    }

    private void ActivateMainAbility(Character user, CampfireAbility ability)
    {
        switch (ability)
        {
            case CampfireAbility.Adrenaline:
                user.GetComponent<CampfireAbilityState>()?.SendAdrenaline();
                ConsumeMainAbility(user, ability);
                NotifyMessage(user, "ADRENALINE ACTIVATED", Plugin.Color);
                break;

            case CampfireAbility.Exhaust:
                ActivateExhaust(user);
                break;

            case CampfireAbility.ChaosHorn:
                ActivateChaosHorn(user);
                break;

            case CampfireAbility.Recall:
                ActivateRecall(user);
                break;

            case CampfireAbility.GhostRunner:
                ActivateGhostRunner(user);
                break;

            case CampfireAbility.MegaLaunch:
                ActivateMegaLaunch(user);
                break;

            default:
                NotifyMessage(
                    user,
                    $"{CampfireAbilityInfo.GetName(ability)} IS NOT READY",
                    Color.gray);
                break;
        }
    }

    private void ActivateExhaust(Character user)
    {
        float userProgress = RaceProgress.ForCharacter(user).Score;
        List<Character> candidates = GetActivePlayerCharacters()
            .Where(candidate => candidate != user
                && candidate.data != null
                && !candidate.data.dead
                && RaceProgress.ForCharacter(candidate).Score > userProgress + 0.01f)
            .ToList();
        if (candidates.Count == 0)
        {
            NotifyMessage(user, "NO VALID TARGET", Color.gray);
            return;
        }

        Character target = candidates[UnityEngine.Random.Range(0, candidates.Count)];
        bool blocked = TryBlockDirectedAttack(user, target);
        ConsumeMainAbility(user, CampfireAbility.Exhaust);
        if (blocked)
        {
            return;
        }

        double deadline = Math.Max(
            exhaustedUntil.TryGetValue(GetActorNumber(target), out double current)
                ? current
                : 0d,
            NetworkTime + ExhaustDurationSeconds);
        SetExhaustedUntil(target, deadline);
        NotifyMessage(user, $"EXHAUST HIT {target.characterName}", Plugin.Color);
        NotifyMessage(target, "EXHAUSTED  •  8 SECONDS", new Color(1f, 0.55f, 0.2f, 1f));
    }

    private void ActivateChaosHorn(Character user)
    {
        int affected = 0;
        int blocked = 0;
        foreach (Character target in GetActivePlayerCharacters())
        {
            if (target == user
                || target.data == null
                || target.data.dead
                || target.IsGhost)
            {
                continue;
            }

            if (TryBlockDirectedAttack(user, target))
            {
                blocked++;
                continue;
            }

            target.GetComponent<CampfireAbilityState>()?.SendRemoveExtraStamina();
            NotifyMessage(target, "CHAOS HORN  •  BONUS STAMINA LOST", new Color(1f, 0.4f, 0.2f, 1f));
            affected++;
        }

        ConsumeMainAbility(user, CampfireAbility.ChaosHorn);
        string result = blocked > 0
            ? $"CHAOS HORN HIT {affected}  •  BLOCKED {blocked}"
            : $"CHAOS HORN HIT {affected} PLAYER(S)";
        NotifyMessage(user, result, Plugin.Color);
    }

    private void ActivateChaos(Character user)
    {
        ChaosEffect effect = RollChaosEffect();
        SetChaos(user, hasChaos: false);
        NotifyAll(
            $"⚠ CHAOS ACTIVATED  •  {FormatChaosEffect(effect)}",
            new Color(1f, 0.3f, 0.2f, 1f));

        switch (effect)
        {
            case ChaosEffect.FullStamina:
                foreach (Character character in GetLivingCharacters())
                {
                    character.GetComponent<CampfireAbilityState>()?.SendFullStamina();
                }
                break;

            case ChaosEffect.InfiniteStamina:
                foreach (Character character in GetLivingCharacters())
                {
                    character.GetComponent<CampfireAbilityState>()
                        ?.SendInfiniteStamina(5f);
                }
                break;

            case ChaosEffect.GlobalAdrenaline:
                foreach (Character character in GetLivingCharacters())
                {
                    character.GetComponent<CampfireAbilityState>()?.SendAdrenaline();
                }
                break;

            case ChaosEffect.GlobalUnconscious:
                NotifyAll(
                    "EVERYONE WILL PASS OUT IN 10 SECONDS",
                    new Color(1f, 0.35f, 0.2f, 1f));
                StartCoroutine(GlobalPassOutAfterWarning());
                break;

            case ChaosEffect.PlayerSwap:
                SwapPlayersAtSafePositions();
                break;

            case ChaosEffect.PreviousCampfire:
                WarpAllToPreviousCampfires();
                break;

            case ChaosEffect.MiddleCampfire:
                WarpAllToMiddleCampfire();
                break;
        }

        Plugin.Log.LogInfo(
            $"{user.characterName} activated Chaos effect {effect}.");
    }

    private ChaosEffect RollChaosEffect()
    {
        float total = activeChaosWeights.Values.Sum(weight => Mathf.Max(0f, weight));
        if (total <= 0f)
        {
            Plugin.Log.LogWarning(
                "Every Chaos weight is zero; falling back to Full Stamina.");
            return ChaosEffect.FullStamina;
        }

        float roll = UnityEngine.Random.Range(0f, total);
        foreach ((ChaosEffect effect, float weight) in activeChaosWeights
            .OrderBy(pair => (int)pair.Key))
        {
            roll -= Mathf.Max(0f, weight);
            if (roll <= 0f)
            {
                return effect;
            }
        }

        return ChaosEffect.FullStamina;
    }

    private System.Collections.IEnumerator GlobalPassOutAfterWarning()
    {
        yield return new WaitForSeconds(10f);
        if (!IsAuthority || RaceSettingsManager.Current.Mode != RespawnMode.Pvp)
        {
            yield break;
        }

        foreach (Character character in GetLivingCharacters())
        {
            character.photonView.RPC("RPCA_PassOut", RpcTarget.All);
        }
    }

    private void SwapPlayersAtSafePositions()
    {
        List<Character> characters = GetLivingCharacters().ToList();
        for (int index = characters.Count - 1; index > 0; index--)
        {
            int swapIndex = UnityEngine.Random.Range(0, index + 1);
            (characters[index], characters[swapIndex]) =
                (characters[swapIndex], characters[index]);
        }

        Dictionary<Character, Vector3> positions = characters.ToDictionary(
            character => character,
            GetLastSafePosition);
        int pairedCount = characters.Count - characters.Count % 2;
        for (int index = 0; index < pairedCount; index += 2)
        {
            Character first = characters[index];
            Character second = characters[index + 1];
            first.photonView.RPC(
                "WarpPlayerRPC",
                RpcTarget.All,
                positions[second],
                true);
            second.photonView.RPC(
                "WarpPlayerRPC",
                RpcTarget.All,
                positions[first],
                true);
        }
    }

    private void WarpAllToPreviousCampfires()
    {
        foreach (Character character in GetLivingCharacters())
        {
            Vector3 position = RaceRespawnController.GetPreviousCampfirePosition(character);
            character.photonView.RPC("WarpPlayerRPC", RpcTarget.All, position, true);
        }
    }

    private void WarpAllToMiddleCampfire()
    {
        List<Character> characters = GetLivingCharacters().ToList();
        if (characters.Count == 0 || !MapHandler.ExistsAndInitialized)
        {
            return;
        }

        int leadingCheckpoint = characters.Max(character =>
            RaceProgress.ForCharacter(character).CheckpointIndex);
        int trailingCheckpoint = characters.Min(character =>
            RaceProgress.ForCharacter(character).CheckpointIndex);
        int middleCheckpoint = Mathf.Max(
            0,
            Mathf.RoundToInt((leadingCheckpoint + trailingCheckpoint) * 0.5f));
        if (!TryGetCampfire(middleCheckpoint, out Campfire campfire))
        {
            Plugin.Log.LogWarning(
                $"Chaos could not resolve middle campfire {middleCheckpoint}.");
            return;
        }

        for (int index = 0; index < characters.Count; index++)
        {
            Character character = characters[index];
            // Chaos may skip intermediate fires, but the destination fire
            // itself remains unclaimed and must still be activated personally.
            CampfireProgressionController.Instance
                ?.AdvancePersonalProgressForChaos(character, middleCheckpoint - 1);
            float angle = index * 2.39996323f;
            Vector3 offset = new(
                Mathf.Cos(angle) * 2f,
                2f,
                Mathf.Sin(angle) * 2f);
            character.photonView.RPC(
                "WarpPlayerRPC",
                RpcTarget.All,
                campfire.transform.position + offset,
                true);
        }
    }

    private void RefreshSafePositions()
    {
        if (Time.unscaledTime < nextSafePositionRefreshTime)
        {
            return;
        }

        nextSafePositionRefreshTime = Time.unscaledTime + 0.2f;
        foreach (Character character in GetLivingCharacters())
        {
            if (character.data.isGrounded
                && !character.warping
                && character.data.avarageVelocity.sqrMagnitude < 100f)
            {
                lastSafePositions[GetActorNumber(character)] = character.Center;
            }
        }
    }

    private Vector3 GetLastSafePosition(Character character)
    {
        return lastSafePositions.TryGetValue(
            GetActorNumber(character),
            out Vector3 position)
            ? position
            : RaceRespawnController.GetPreviousCampfirePosition(character);
    }

    private void NotifyAll(string text, Color color)
    {
        foreach (Character character in GetActivePlayerCharacters())
        {
            NotifyMessage(character, text, color);
        }
    }

    private static IEnumerable<Character> GetLivingCharacters()
    {
        return GetActivePlayerCharacters().Where(character =>
            character.data != null && !character.data.dead);
    }

    private static bool TryGetCampfire(int index, out Campfire campfire)
    {
        campfire = null;
        if (!MapHandler.ExistsAndInitialized)
        {
            return false;
        }

        MapHandler map = Zorro.Core.Singleton<MapHandler>.Instance;
        if (index < 0 || index >= map.segments.Length)
        {
            return false;
        }

        campfire = map.segments[index].segmentCampfire
            ?.GetComponentInChildren<Campfire>(true);
        return campfire != null;
    }

    private static string FormatChaosEffect(ChaosEffect effect)
    {
        return effect switch
        {
            ChaosEffect.FullStamina => "FULL STAMINA",
            ChaosEffect.InfiniteStamina => "INFINITE STAMINA",
            ChaosEffect.GlobalAdrenaline => "GLOBAL ADRENALINE",
            ChaosEffect.GlobalUnconscious => "GLOBAL UNCONSCIOUS",
            ChaosEffect.PlayerSwap => "PLAYER SWAP",
            ChaosEffect.PreviousCampfire => "PREVIOUS CAMPFIRE",
            ChaosEffect.MiddleCampfire => "MIDDLE CAMPFIRE",
            _ => effect.ToString().ToUpperInvariant()
        };
    }

    private void ActivateRecall(Character user)
    {
        Character leader = GetActivePlayerCharacters()
            .Where(candidate => candidate.data != null
                && !candidate.data.dead)
            .OrderByDescending(candidate => RaceProgress.ForCharacter(candidate).Score)
            .ThenBy(candidate => GetActorNumber(candidate))
            .FirstOrDefault();
        if (leader == null || leader == user)
        {
            NotifyMessage(user, "NO VALID TARGET", Color.gray);
            return;
        }

        RaceProgress userProgress = RaceProgress.ForCharacter(user);
        RaceProgress leaderProgress = RaceProgress.ForCharacter(leader);
        bool largeEnoughGap = leaderProgress.CheckpointIndex
                >= userProgress.CheckpointIndex + 1
            || leaderProgress.Score - userProgress.Score >= 0.5f;
        if (!largeEnoughGap)
        {
            NotifyMessage(user, "LEADER GAP TOO SMALL", Color.gray);
            return;
        }

        bool blocked = TryBlockDirectedAttack(user, leader);
        ConsumeMainAbility(user, CampfireAbility.Recall);
        if (blocked)
        {
            return;
        }

        NotifyMessage(user, $"RECALL TARGET: {leader.characterName}", Plugin.Color);
        NotifyMessage(leader, "⚠ RECALL INCOMING", new Color(1f, 0.32f, 0.2f, 1f));
        StartCoroutine(RecallAfterWarning(leader));
    }

    private System.Collections.IEnumerator RecallAfterWarning(Character target)
    {
        yield return new WaitForSeconds(2f);
        if (!IsAuthority
            || target == null
            || target.data == null
            || target.data.dead
            || target.photonView == null)
        {
            yield break;
        }

        Vector3 position = RaceRespawnController.GetPreviousCampfirePosition(target);
        target.photonView.RPC("WarpPlayerRPC", RpcTarget.All, position, true);
        NotifyMessage(target, "RECALLED", new Color(1f, 0.32f, 0.2f, 1f));
    }

    private void ActivateGhostRunner(Character user)
    {
        if (user == null || !user.IsGhost || user.Ghost == null)
        {
            NotifyMessage(user, "GHOST ONLY", Color.gray);
            return;
        }

        Character target = user.Ghost.m_target;
        if (target == null || target.data == null || target.data.dead || target == user)
        {
            NotifyMessage(user, "NO VALID SPECTATED TARGET", Color.gray);
            return;
        }

        bool blocked = TryBlockDirectedAttack(user, target);
        ConsumeMainAbility(user, CampfireAbility.GhostRunner);
        if (blocked)
        {
            return;
        }

        SetGhostRunnerUntil(target, NetworkTime + 15d);
        NotifyMessage(user, $"GHOST RUNNER HIT {target.characterName}", Plugin.Color);
        NotifyMessage(target, "GHOST RUNNER  •  15 SECONDS", new Color(0.55f, 0.75f, 1f, 1f));
    }

    private void ActivateMegaLaunch(Character user)
    {
        double remaining = GetMegaLaunchCooldownRemaining(user);
        if (remaining > 0d)
        {
            NotifyMessage(user, $"MEGA LAUNCH COOLDOWN {Math.Ceiling(remaining):0}s", Color.gray);
            return;
        }

        int actorNumber = GetActorNumber(user);
        double launchAt = NetworkTime + 5d;
        float distanceMeters = activeMegaLaunchDistance;
        pendingMegaLaunches[actorNumber] = new PendingMegaLaunch(
            launchAt,
            distanceMeters,
            requiresAbility: true);
        SetMegaCooldownUntil(user, NetworkTime + activeMegaLaunchCooldown);
        user.GetComponent<CampfireAbilityState>()?.SendMegaLaunchCountdown(launchAt);
    }

    internal void RecordHiddenMegaLaunchFood(
        Luggage luggage,
        IEnumerable<PhotonView> spawnedViews)
    {
        if (!IsAuthority
            || RaceSettingsManager.Current.Mode != RespawnMode.Pvp
            || luggage == null
            || luggage is RespawnChest
            || spawnedViews == null)
        {
            return;
        }

        Vector3 chestCenter = luggage.Center();
        Character opener = GetLivingCharacters()
            .OrderBy(character => (character.Center - chestCenter).sqrMagnitude)
            .FirstOrDefault();
        if (opener == null)
        {
            return;
        }

        float chancePercent = GetHiddenMegaLaunchFoodChance(opener);
        foreach (PhotonView view in spawnedViews)
        {
            Item item = view != null ? view.GetComponent<Item>() : null;
            if (!IsOrdinaryFood(item)
                || UnityEngine.Random.Range(0f, 100f) >= chancePercent)
            {
                continue;
            }

            if (SetRoomProperty(MegaLaunchFoodKey(view.ViewID), true))
            {
                megaLaunchFoodViewIds.Add(view.ViewID);
                Plugin.Log.LogInfo(
                    $"Marked hidden Mega Launch food {view.ViewID} "
                    + $"({item.GetName()}) for "
                    + $"{opener.characterName} at {chancePercent:0.#}% odds.");
            }
        }
    }

    internal void HandleHiddenMegaLaunchFoodConsumed(
        int itemViewId,
        Character consumer)
    {
        if (!IsAuthority
            || itemViewId <= 0
            || !megaLaunchFoodViewIds.Contains(itemViewId))
        {
            return;
        }

        PhotonView itemView = PhotonNetwork.GetPhotonView(itemViewId);
        Item item = itemView != null ? itemView.GetComponent<Item>() : null;
        if (!IsActivePlayerCharacter(consumer)
            || consumer.data == null
            || consumer.data.dead
            || item == null
            || (item.holderCharacter != consumer
                && item.trueHolderCharacter != consumer))
        {
            Plugin.Log.LogWarning(
                $"Rejected hidden Mega Launch food {itemViewId}: "
                + "the requesting player is not its current holder.");
            return;
        }

        megaLaunchFoodViewIds.Remove(itemViewId);
        SetRoomProperty(MegaLaunchFoodKey(itemViewId), null);
        Plugin.Log.LogInfo(
            $"Consumed hidden Mega Launch food {itemViewId} "
            + $"({item.GetName()}) by {consumer.characterName}.");
        StartMegaLaunchCountdown(consumer, requiresAbility: false);
    }

    internal void ForgetHiddenMegaLaunchFood(Item item)
    {
        PhotonView itemView = item != null ? item.GetComponent<PhotonView>() : null;
        if (IsAuthority
            && itemView != null
            && megaLaunchFoodViewIds.Remove(itemView.ViewID))
        {
            SetRoomProperty(MegaLaunchFoodKey(itemView.ViewID), null);
        }
    }

    private void StartMegaLaunchCountdown(Character character, bool requiresAbility)
    {
        int actorNumber = GetActorNumber(character);
        if (actorNumber == 0)
        {
            return;
        }

        double launchAt = NetworkTime + 5d;
        pendingMegaLaunches[actorNumber] = new PendingMegaLaunch(
            launchAt,
            activeMegaLaunchDistance,
            requiresAbility);
        character.GetComponent<CampfireAbilityState>()?.SendMegaLaunchCountdown(launchAt);
    }

    private float GetHiddenMegaLaunchFoodChance(Character opener)
    {
        List<Character> racers = GetLivingCharacters()
            .OrderByDescending(character => RaceProgress.ForCharacter(character).Score)
            .ThenBy(character => GetActorNumber(character))
            .ToList();
        int position = racers.IndexOf(opener);
        if (position <= 0 || racers.Count <= 1)
        {
            return activeMegaFoodLeaderChance;
        }

        float leaderScore = RaceProgress.ForCharacter(racers[0]).Score;
        float openerScore = RaceProgress.ForCharacter(opener).Score;
        if (leaderScore - openerScore >= 1.5f)
        {
            return activeMegaFoodFarBehindChance;
        }

        if (position == racers.Count - 1)
        {
            return activeMegaFoodLastChance;
        }

        float fieldPosition = position / (float)(racers.Count - 1);
        return fieldPosition >= 0.75f
            ? activeMegaFoodNearLastChance
            : activeMegaFoodMiddleChance;
    }

    private static bool IsOrdinaryFood(Item item)
    {
        if (item == null || item.itemTags.HasFlag(Item.ItemTags.Mystical))
        {
            return false;
        }

        Action_ModifyStatus[] statusActions =
            item.GetComponents<Action_ModifyStatus>();
        bool restoresHunger = item.GetComponent<Action_RestoreHunger>() != null
            || statusActions.Any(effect =>
                effect.statusType == CharacterAfflictions.STATUSTYPE.Hunger
                && effect.changeAmount < 0f);
        if (!restoresHunger)
        {
            return false;
        }

        // PEAK 2.0 does not tag every ordinary food consistently (Scout
        // Cookies are one example), so eligibility follows the actual item
        // actions instead of a hard-coded PackagedFood/Berry/Mushroom list.
        // Keep rare emergency healing items deterministic. Hunger restoration
        // and harmful side effects still qualify as ordinary food behavior.
        return !statusActions.Any(effect =>
            effect.changeAmount < 0f
            && effect.statusType != CharacterAfflictions.STATUSTYPE.Hunger);
    }

    internal void HandleMegaLaunchImpulseRequest(Character character, Vector3 direction)
    {
        int actorNumber = GetActorNumber(character);
        if (!IsAuthority
            || !pendingMegaLaunches.TryGetValue(actorNumber, out PendingMegaLaunch pending)
            || (pending.RequiresAbility
                && GetAbility(character) != CampfireAbility.MegaLaunch)
            || NetworkTime < pending.LaunchAt - 0.5d
            || NetworkTime > pending.LaunchAt + 3d
            || character?.data == null
            || character.data.dead)
        {
            return;
        }

        pendingMegaLaunches.Remove(actorNumber);
        Vector3 launchDirection = direction.sqrMagnitude > 0.01f
            ? direction.normalized
            : character.data.lookDirection.normalized;
        character.GetComponent<CampfireAbilityState>()
            ?.SendMegaLaunchImpulse(
                launchDirection,
                pending.DistanceMeters);
        NotifyMessage(character, "MEGA LAUNCH", Plugin.Color);
    }

    internal bool IsGhostRunnerAffected(Character character)
    {
        return ghostRunnerUntil.TryGetValue(
                GetActorNumber(character),
                out double deadline)
            && NetworkTime < deadline;
    }

    internal double GetMegaLaunchCooldownRemaining(Character character)
    {
        return megaCooldownUntil.TryGetValue(
                GetActorNumber(character),
                out double deadline)
            ? Math.Max(0d, deadline - NetworkTime)
            : 0d;
    }

    internal bool HasMegaLaunchImmunity(Character character)
    {
        return megaLaunchImmunityUntil.TryGetValue(
                GetActorNumber(character),
                out double deadline)
            && NetworkTime < deadline;
    }

    internal void MarkMegaLaunchImmunity(Character character, double deadline)
    {
        int actorNumber = GetActorNumber(character);
        if (actorNumber > 0 && deadline > NetworkTime)
        {
            megaLaunchImmunityUntil[actorNumber] = deadline;
        }
    }

    internal float GetCatchUpMultiplier(Character character)
    {
        RefreshCatchUpMultipliers();
        return catchUpMultipliers.TryGetValue(
            GetActorNumber(character),
            out float multiplier)
            ? multiplier
            : 1f;
    }

    internal bool HasSystemCatchUp(Character character)
    {
        return GetCatchUpMultiplier(character) > 1f
            && GetAbility(character) != CampfireAbility.CatchUp;
    }

    private void RefreshCatchUpMultipliers()
    {
        if (Time.unscaledTime < nextCatchUpRefreshTime)
        {
            return;
        }

        nextCatchUpRefreshTime = Time.unscaledTime + 0.25f;
        catchUpMultipliers.Clear();
        if (RaceSettingsManager.Current.Mode != RespawnMode.Pvp)
        {
            return;
        }

        List<Character> racers = GetActivePlayerCharacters()
            .Where(character => character.data != null && !character.data.dead)
            .ToList();
        if (racers.Count < 2)
        {
            return;
        }

        Dictionary<Character, RaceProgress> progress = racers.ToDictionary(
            character => character,
            RaceProgress.ForCharacter);
        Character leader = racers
            .OrderByDescending(character => progress[character].Score)
            .ThenBy(character => GetActorNumber(character))
            .First();
        Character last = racers
            .OrderBy(character => progress[character].Score)
            .ThenByDescending(character => GetActorNumber(character))
            .First();
        RaceProgress leaderProgress = progress[leader];

        foreach (Character racer in racers)
        {
            if (racer != last && GetAbility(racer) != CampfireAbility.CatchUp)
            {
                continue;
            }

            RaceProgress racerProgress = progress[racer];
            int checkpointGap = leaderProgress.CheckpointIndex - racerProgress.CheckpointIndex;
            float scoreGap = leaderProgress.Score - racerProgress.Score;
            float multiplier = checkpointGap >= 2
                ? 1.25f
                : checkpointGap >= 1
                    ? 1.15f
                    : scoreGap >= 0.66f
                        ? 1.10f
                        : scoreGap >= 0.10f
                            ? 1.05f
                            : 1f;
            if (multiplier > 1f)
            {
                catchUpMultipliers[GetActorNumber(racer)] = multiplier;
            }
        }
    }

    private void SetGhostRunnerUntil(Character character, double deadline)
    {
        int actorNumber = GetActorNumber(character);
        if (actorNumber > 0
            && SetRoomProperty(GhostRunnerUntilKey(actorNumber), deadline))
        {
            ghostRunnerUntil[actorNumber] = deadline;
        }
    }

    private void SetMegaCooldownUntil(Character character, double deadline)
    {
        int actorNumber = GetActorNumber(character);
        if (actorNumber == 0
            || !SetRoomProperty(MegaCooldownUntilKey(actorNumber), deadline))
        {
            return;
        }

        if (deadline > NetworkTime)
        {
            megaCooldownUntil[actorNumber] = deadline;
        }
        else
        {
            megaCooldownUntil.Remove(actorNumber);
            pendingMegaLaunches.Remove(actorNumber);
        }
    }

    internal bool TryBlockDirectedAttack(Character attacker, Character target)
    {
        if (GetAbility(target) != CampfireAbility.Shield)
        {
            return false;
        }

        SetAbility(target, CampfireAbility.None);
        NotifyMessage(attacker, "ATTACK BLOCKED", new Color(1f, 0.45f, 0.25f, 1f));
        NotifyMessage(target, "SHIELD USED", Plugin.Color);
        return true;
    }

    private void ConsumeMainAbility(Character character, CampfireAbility expected)
    {
        if (!CampfireAbilityInfo.IsReusable(expected)
            && GetAbility(character) == expected)
        {
            SetAbility(character, CampfireAbility.None);
        }
    }

    private void SetExhaustedUntil(Character character, double deadline)
    {
        int actorNumber = GetActorNumber(character);
        if (actorNumber == 0
            || !SetRoomProperty(ExhaustUntilKey(actorNumber), deadline))
        {
            return;
        }

        exhaustedUntil[actorNumber] = deadline;
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
        foreach ((CampfireAbility ability, float weight) in activeAbilityWeights)
        {
            if (ability != CampfireAbility.None)
            {
                total += Mathf.Max(0f, weight);
            }
        }

        if (total <= 0f)
        {
            Plugin.Log.LogWarning(
                "Every PVP campfire ability weight is zero; falling back to Adrenaline.");
            return CampfireAbility.Adrenaline;
        }

        float roll = UnityEngine.Random.Range(0f, total);
        foreach ((CampfireAbility ability, float weight) in activeAbilityWeights
            .OrderBy(pair => (int)pair.Key))
        {
            roll -= Mathf.Max(0f, weight);
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
            if (TryApplyPvpRuleProperty(key, boxed))
            {
                continue;
            }

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
            else if (TryParseSuffix(
                key,
                InitialAbilityGrantedKeyPrefix,
                out int initialAbilityActor))
            {
                bool granted = false;
                try
                {
                    granted = boxed != null && Convert.ToBoolean(boxed);
                }
                catch (Exception)
                {
                    Plugin.Log.LogWarning(
                        $"Ignored malformed initial ability marker '{key}'.");
                }

                if (granted)
                {
                    initialAbilityActors.Add(initialAbilityActor);
                }
                else
                {
                    initialAbilityActors.Remove(initialAbilityActor);
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
            else if (TryParseSuffix(key, ExhaustUntilKeyPrefix, out int exhaustedActor))
            {
                try
                {
                    double deadline = boxed != null ? Convert.ToDouble(boxed) : 0d;
                    if (deadline > NetworkTime)
                    {
                        exhaustedUntil[exhaustedActor] = deadline;
                    }
                    else
                    {
                        exhaustedUntil.Remove(exhaustedActor);
                    }
                }
                catch (Exception)
                {
                    exhaustedUntil.Remove(exhaustedActor);
                    Plugin.Log.LogWarning($"Ignored malformed Exhaust deadline '{key}'.");
                }
            }
            else if (TryParseSuffix(key, GhostRunnerUntilKeyPrefix, out int ghostActor))
            {
                ApplyDeadlineProperty(
                    key,
                    boxed,
                    ghostActor,
                    ghostRunnerUntil,
                    "Ghost Runner");
            }
            else if (TryParseSuffix(key, MegaCooldownUntilKeyPrefix, out int megaActor))
            {
                ApplyDeadlineProperty(
                    key,
                    boxed,
                    megaActor,
                    megaCooldownUntil,
                    "Mega Launch cooldown");
            }
            else if (TryParseSuffix(key, MegaLaunchFoodKeyPrefix, out int foodViewId))
            {
                bool enabled = false;
                try
                {
                    enabled = boxed != null && Convert.ToBoolean(boxed);
                }
                catch (Exception)
                {
                    Plugin.Log.LogWarning(
                        $"Ignored malformed hidden Mega Launch food '{key}'.");
                }

                if (enabled)
                {
                    megaLaunchFoodViewIds.Add(foodViewId);
                }
                else
                {
                    megaLaunchFoodViewIds.Remove(foodViewId);
                }
            }
        }
    }

    private bool TryApplyPvpRuleProperty(string key, object boxed)
    {
        if (TryParseSuffix(key, RuleAbilityWeightPrefix, out int rawAbility))
        {
            if (Enum.IsDefined(typeof(CampfireAbility), rawAbility)
                && (CampfireAbility)rawAbility != CampfireAbility.None
                && TryConvertRuleFloat(key, boxed, 0f, 1000f, out float weight))
            {
                activeAbilityWeights[(CampfireAbility)rawAbility] = weight;
            }
            return true;
        }

        if (TryParseSuffix(key, RuleChaosWeightPrefix, out int rawEffect))
        {
            if (Enum.IsDefined(typeof(ChaosEffect), rawEffect)
                && TryConvertRuleFloat(key, boxed, 0f, 1000f, out float weight))
            {
                activeChaosWeights[(ChaosEffect)rawEffect] = weight;
            }
            return true;
        }

        float scalar;
        if (key == RuleMegaCooldownKey)
        {
            if (TryConvertRuleFloat(key, boxed, 5f, 300f, out scalar))
            {
                activeMegaLaunchCooldown = scalar;
            }
            return true;
        }
        if (key == RuleMegaDistanceKey)
        {
            if (TryConvertRuleFloat(key, boxed, 10f, 250f, out scalar))
            {
                activeMegaLaunchDistance = scalar;
            }
            return true;
        }
        if (key == RuleMegaFoodLeaderKey)
        {
            if (TryConvertRuleFloat(key, boxed, 0f, 100f, out scalar))
            {
                activeMegaFoodLeaderChance = scalar;
            }
            return true;
        }
        if (key == RuleMegaFoodMiddleKey)
        {
            if (TryConvertRuleFloat(key, boxed, 0f, 100f, out scalar))
            {
                activeMegaFoodMiddleChance = scalar;
            }
            return true;
        }
        if (key == RuleMegaFoodNearLastKey)
        {
            if (TryConvertRuleFloat(key, boxed, 0f, 100f, out scalar))
            {
                activeMegaFoodNearLastChance = scalar;
            }
            return true;
        }
        if (key == RuleMegaFoodLastKey)
        {
            if (TryConvertRuleFloat(key, boxed, 0f, 100f, out scalar))
            {
                activeMegaFoodLastChance = scalar;
            }
            return true;
        }
        if (key == RuleMegaFoodFarBehindKey)
        {
            if (TryConvertRuleFloat(key, boxed, 0f, 100f, out scalar))
            {
                activeMegaFoodFarBehindChance = scalar;
            }
            return true;
        }

        return false;
    }

    private static bool TryConvertRuleFloat(
        string key,
        object boxed,
        float minimum,
        float maximum,
        out float value)
    {
        value = 0f;
        if (boxed == null)
        {
            return false;
        }

        try
        {
            value = Mathf.Clamp(Convert.ToSingle(boxed), minimum, maximum);
            return true;
        }
        catch (Exception)
        {
            Plugin.Log.LogWarning($"Ignored malformed PVP ability rule '{key}'.");
            return false;
        }
    }

    private static void ApplyDeadlineProperty(
        string key,
        object boxed,
        int actorNumber,
        Dictionary<int, double> destination,
        string label)
    {
        try
        {
            double deadline = boxed != null ? Convert.ToDouble(boxed) : 0d;
            if (deadline > NetworkTime)
            {
                destination[actorNumber] = deadline;
            }
            else
            {
                destination.Remove(actorNumber);
            }
        }
        catch (Exception)
        {
            destination.Remove(actorNumber);
            Plugin.Log.LogWarning($"Ignored malformed {label} deadline '{key}'.");
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
        initialAbilityActors.Clear();
        chaosActors.Clear();
        chaosAwardedCampfires.Clear();
        exhaustedUntil.Clear();
        ghostRunnerUntil.Clear();
        megaCooldownUntil.Clear();
        secondWindImmunityUntil.Clear();
        megaLaunchImmunityUntil.Clear();
        pendingMegaLaunches.Clear();
        megaLaunchFoodViewIds.Clear();
        catchUpMultipliers.Clear();
        lastSafePositions.Clear();
        nextCatchUpRefreshTime = 0f;
        nextSafePositionRefreshTime = 0f;
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
                    || key.StartsWith(
                        InitialAbilityGrantedKeyPrefix,
                        StringComparison.Ordinal)
                    || key.StartsWith(ChaosKeyPrefix, StringComparison.Ordinal)
                    || key.StartsWith(ChaosFireKeyPrefix, StringComparison.Ordinal)
                    || key.StartsWith(ExhaustUntilKeyPrefix, StringComparison.Ordinal)
                    || key.StartsWith(GhostRunnerUntilKeyPrefix, StringComparison.Ordinal)
                    || key.StartsWith(MegaCooldownUntilKeyPrefix, StringComparison.Ordinal)
                    || key.StartsWith(MegaLaunchFoodKeyPrefix, StringComparison.Ordinal)))
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

    private static string InitialAbilityGrantedKey(int actorNumber) =>
        InitialAbilityGrantedKeyPrefix + actorNumber;

    private static string ChaosKey(int actorNumber) => ChaosKeyPrefix + actorNumber;

    private static string ChaosFireKey(int campfireIndex) => ChaosFireKeyPrefix + campfireIndex;

    private static string ExhaustUntilKey(int actorNumber) =>
        ExhaustUntilKeyPrefix + actorNumber;

    private static string GhostRunnerUntilKey(int actorNumber) =>
        GhostRunnerUntilKeyPrefix + actorNumber;

    private static string MegaCooldownUntilKey(int actorNumber) =>
        MegaCooldownUntilKeyPrefix + actorNumber;

    private static string MegaLaunchFoodKey(int viewId) =>
        MegaLaunchFoodKeyPrefix + viewId;

    private static string RuleAbilityWeightKey(CampfireAbility ability) =>
        RuleAbilityWeightPrefix + (int)ability;

    private static string RuleChaosWeightKey(ChaosEffect effect) =>
        RuleChaosWeightPrefix + (int)effect;

    private static double NetworkTime => PhotonNetwork.InRoom
        ? PhotonNetwork.Time
        : Time.unscaledTime;

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
        if (PhotonNetwork.IsMasterClient
            && SceneManager.GetActiveScene().name == "Airport")
        {
            PublishLocalPvpRules();
        }
        else
        {
            ApplyRoomProperties(PhotonNetwork.CurrentRoom?.CustomProperties);
        }
    }

    public override void OnRoomPropertiesUpdate(Hashtable propertiesThatChanged)
    {
        ApplyRoomProperties(propertiesThatChanged);
    }

    public override void OnLeftRoom()
    {
        ClearRunState(clearRoomProperties: false);
        LoadActivePvpRulesFromLocalConfig();
    }

    public override void OnMasterClientSwitched(Photon.Realtime.Player newMasterClient)
    {
        if (!initialized || !PhotonNetwork.IsMasterClient)
        {
            return;
        }

        ApplyRoomProperties(PhotonNetwork.CurrentRoom?.CustomProperties);
        StoreActivePvpRulesInLocalConfig();
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (initialized)
        {
            SetPvpConfigSubscriptions(subscribe: false);
        }
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private readonly struct PendingMegaLaunch
    {
        internal PendingMegaLaunch(
            double launchAt,
            float distanceMeters,
            bool requiresAbility)
        {
            LaunchAt = launchAt;
            DistanceMeters = distanceMeters;
            RequiresAbility = requiresAbility;
        }

        internal double LaunchAt { get; }

        internal float DistanceMeters { get; }

        internal bool RequiresAbility { get; }
    }
}
