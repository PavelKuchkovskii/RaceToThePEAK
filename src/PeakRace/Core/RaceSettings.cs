using BepInEx.Configuration;
using ExitGames.Client.Photon;
using Photon.Pun;
using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PeakRace.Core;

internal enum RespawnMode
{
    NextCampfire = 0,
    CorpseTimer = 1,
    PreviousCampfire = 2,
    Pvp = 3
}

internal enum CampfireWaitMode
{
    Nobody = 0,
    Team = 1,
    Lobby = 2
}

internal enum PvpDeathRespawnMode
{
    PreviousCampfire = 0,
    CorpseTimer = 1,
    NextCampfire = 2
}

internal readonly struct RaceSettingsSnapshot : IEquatable<RaceSettingsSnapshot>
{
    internal static readonly RaceSettingsSnapshot Defaults = new(
        CampfireWaitMode.Nobody,
        RespawnMode.PreviousCampfire,
        nextCampfirePenaltyMinutes: 5,
        corpsePenaltyMinutes: 5,
        previousCampfirePenaltyMinutes: 0,
        pvpPenaltyMinutes: 0,
        corpseRespawnDelaySeconds: 30,
        pvpDeathRespawn: PvpDeathRespawnMode.PreviousCampfire,
        pvpChestRefreshEnabled: false,
        pvpChestRefreshSeconds: 300);

    internal RaceSettingsSnapshot(
        CampfireWaitMode waitMode,
        RespawnMode mode,
        int nextCampfirePenaltyMinutes,
        int corpsePenaltyMinutes,
        int previousCampfirePenaltyMinutes,
        int pvpPenaltyMinutes,
        int corpseRespawnDelaySeconds,
        PvpDeathRespawnMode pvpDeathRespawn,
        bool pvpChestRefreshEnabled,
        int pvpChestRefreshSeconds)
    {
        WaitMode = Enum.IsDefined(typeof(CampfireWaitMode), waitMode)
            ? waitMode
            : CampfireWaitMode.Nobody;
        Mode = Enum.IsDefined(typeof(RespawnMode), mode)
            ? mode
            : RespawnMode.PreviousCampfire;
        NextCampfirePenaltyMinutes = Mathf.Clamp(nextCampfirePenaltyMinutes, 0, 60);
        CorpsePenaltyMinutes = Mathf.Clamp(corpsePenaltyMinutes, 0, 60);
        PreviousCampfirePenaltyMinutes = Mathf.Clamp(previousCampfirePenaltyMinutes, 0, 60);
        PvpPenaltyMinutes = Mathf.Clamp(pvpPenaltyMinutes, 0, 60);
        CorpseRespawnDelaySeconds = Mathf.Clamp(corpseRespawnDelaySeconds, 0, 600);
        PvpDeathRespawn = Enum.IsDefined(typeof(PvpDeathRespawnMode), pvpDeathRespawn)
            ? pvpDeathRespawn
            : PvpDeathRespawnMode.PreviousCampfire;

        // A room snapshot is a security boundary: never allow a malformed or
        // legacy network value to move a player forward through an unearned fire.
        if (WaitMode == CampfireWaitMode.Nobody && Mode == RespawnMode.NextCampfire)
        {
            Mode = RespawnMode.PreviousCampfire;
        }
        if (PvpDeathRespawn == PvpDeathRespawnMode.NextCampfire)
        {
            PvpDeathRespawn = PvpDeathRespawnMode.PreviousCampfire;
        }
        PvpChestRefreshEnabled = pvpChestRefreshEnabled;
        PvpChestRefreshSeconds = Mathf.Clamp(pvpChestRefreshSeconds, 30, 1800);
    }

    internal CampfireWaitMode WaitMode { get; }
    internal RespawnMode Mode { get; }
    internal int NextCampfirePenaltyMinutes { get; }
    internal int CorpsePenaltyMinutes { get; }
    internal int PreviousCampfirePenaltyMinutes { get; }
    internal int PvpPenaltyMinutes { get; }
    internal int CorpseRespawnDelaySeconds { get; }
    internal PvpDeathRespawnMode PvpDeathRespawn { get; }
    internal bool PvpChestRefreshEnabled { get; }
    internal int PvpChestRefreshSeconds { get; }

    internal int ActivePenaltyMinutes => GetPenaltyMinutes(Mode);

    internal int ActivePenaltySeconds => ActivePenaltyMinutes * 60;

    internal bool UsesNextCampfireRespawn => Mode == RespawnMode.NextCampfire
        || (Mode == RespawnMode.Pvp
            && PvpDeathRespawn == PvpDeathRespawnMode.NextCampfire);

    internal bool UsesCorpseTimerRespawn => Mode == RespawnMode.CorpseTimer
        || (Mode == RespawnMode.Pvp
            && PvpDeathRespawn == PvpDeathRespawnMode.CorpseTimer);

    internal bool UsesPreviousCampfireRespawn => Mode == RespawnMode.PreviousCampfire
        || (Mode == RespawnMode.Pvp
            && PvpDeathRespawn == PvpDeathRespawnMode.PreviousCampfire);

    internal bool UsesIndividualCampfireProgress => WaitMode == CampfireWaitMode.Nobody;

    internal bool UsesPersonalCampfireClaims => Mode == RespawnMode.Pvp;

    internal bool UsesTeamCampfireProgress => WaitMode == CampfireWaitMode.Team;

    internal bool UsesLobbyCampfireProgress => WaitMode == CampfireWaitMode.Lobby;

    internal static bool IsCombinationAllowed(
        CampfireWaitMode waitMode,
        RespawnMode mode,
        PvpDeathRespawnMode pvpDeathRespawn)
    {
        if (pvpDeathRespawn == PvpDeathRespawnMode.NextCampfire)
        {
            return false;
        }

        return waitMode != CampfireWaitMode.Nobody
            || mode != RespawnMode.NextCampfire;
    }

    internal int GetPenaltyMinutes(RespawnMode mode)
    {
        return mode switch
        {
            RespawnMode.CorpseTimer => CorpsePenaltyMinutes,
            RespawnMode.PreviousCampfire => PreviousCampfirePenaltyMinutes,
            RespawnMode.Pvp => PvpPenaltyMinutes,
            _ => NextCampfirePenaltyMinutes
        };
    }

    public bool Equals(RaceSettingsSnapshot other)
    {
        return WaitMode == other.WaitMode
            && Mode == other.Mode
            && NextCampfirePenaltyMinutes == other.NextCampfirePenaltyMinutes
            && CorpsePenaltyMinutes == other.CorpsePenaltyMinutes
            && PreviousCampfirePenaltyMinutes == other.PreviousCampfirePenaltyMinutes
            && PvpPenaltyMinutes == other.PvpPenaltyMinutes
            && CorpseRespawnDelaySeconds == other.CorpseRespawnDelaySeconds
            && PvpDeathRespawn == other.PvpDeathRespawn
            && PvpChestRefreshEnabled == other.PvpChestRefreshEnabled
            && PvpChestRefreshSeconds == other.PvpChestRefreshSeconds;
    }

    public override bool Equals(object obj)
    {
        return obj is RaceSettingsSnapshot other && Equals(other);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = (int)WaitMode;
            hash = (hash * 397) ^ (int)Mode;
            hash = (hash * 397) ^ NextCampfirePenaltyMinutes;
            hash = (hash * 397) ^ CorpsePenaltyMinutes;
            hash = (hash * 397) ^ PreviousCampfirePenaltyMinutes;
            hash = (hash * 397) ^ PvpPenaltyMinutes;
            hash = (hash * 397) ^ CorpseRespawnDelaySeconds;
            hash = (hash * 397) ^ (int)PvpDeathRespawn;
            hash = (hash * 397) ^ PvpChestRefreshEnabled.GetHashCode();
            hash = (hash * 397) ^ PvpChestRefreshSeconds;
            return hash;
        }
    }
}

/// <summary>
/// Owns the host's persistent configuration and the room-authoritative snapshot.
/// Photon room properties make settings deterministic for all clients and late joiners.
/// </summary>
internal sealed class RaceSettingsManager : MonoBehaviourPunCallbacks
{
    private const int NetworkSchemaVersion = 6;
    private const string VersionKey = "RTP.SettingsVersion";
    private const string WaitModeKey = "RTP.CampfireWaitMode";
    private const string ModeKey = "RTP.RespawnMode";
    private const string NextPenaltyKey = "RTP.NextPenaltyMinutes";
    private const string CorpsePenaltyKey = "RTP.CorpsePenaltyMinutes";
    private const string PreviousPenaltyKey = "RTP.PreviousPenaltyMinutes";
    private const string PvpPenaltyKey = "RTP.PvpPenaltyMinutes";
    private const string CorpseDelayKey = "RTP.CorpseDelaySeconds";
    private const string PvpDeathRespawnKey = "RTP.PvpDeathRespawnMode";
    private const string PvpChestRefreshEnabledKey = "RTP.PvpChestRefreshEnabled";
    private const string PvpChestRefreshSecondsKey = "RTP.PvpChestRefreshSeconds";

    private ConfigEntry<CampfireWaitMode> waitModeConfig;
    private ConfigEntry<RespawnMode> modeConfig;
    private ConfigEntry<int> nextPenaltyConfig;
    private ConfigEntry<int> corpsePenaltyConfig;
    private ConfigEntry<int> previousPenaltyConfig;
    private ConfigEntry<int> pvpPenaltyConfig;
    private ConfigEntry<int> corpseDelayConfig;
    private ConfigEntry<PvpDeathRespawnMode> pvpDeathRespawnConfig;
    private ConfigEntry<bool> pvpChestRefreshEnabledConfig;
    private ConfigEntry<int> pvpChestRefreshSecondsConfig;
    private bool initialized;
    private bool normalizingLocalConfig;
    private RaceSettingsSnapshot current = RaceSettingsSnapshot.Defaults;

    internal static RaceSettingsManager Instance { get; private set; }

    internal static RaceSettingsSnapshot Current => Instance != null
        ? Instance.current
        : RaceSettingsSnapshot.Defaults;

    internal bool CanEditLobbySettings => IsLobbyScene
        && (!PhotonNetwork.InRoom || PhotonNetwork.IsMasterClient);

    private static bool IsLobbyScene => SceneManager.GetActiveScene().name == "Airport";

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    internal void Initialize(ConfigFile config)
    {
        waitModeConfig = config.Bind(
            "Progression",
            "CampfireWaitMode",
            CampfireWaitMode.Nobody,
            "Who must reach a campfire before the next biome can be entered: nobody, the player's team, or the whole lobby.");

        modeConfig = config.Bind(
            "Respawn",
            "Mode",
            RespawnMode.PreviousCampfire,
            "Respawn strategy selected by the lobby host.");

        nextPenaltyConfig = BindMinutes(
            config,
            "NextCampfirePenaltyMinutes",
            5,
            "Death penalty for respawning when the next campfire is lit.");

        corpsePenaltyConfig = BindMinutes(
            config,
            "CorpseTimerPenaltyMinutes",
            5,
            "Death penalty for timed respawning at the corpse.");

        previousPenaltyConfig = BindMinutes(
            config,
            "PreviousCampfirePenaltyMinutes",
            0,
            "Death penalty for immediate respawning at the previous campfire.");

        pvpPenaltyConfig = BindMinutes(
            config,
            "PvpPenaltyMinutes",
            0,
            "Death penalty in PVP mode. PVP blowgun knockouts do not add this penalty.");

        corpseDelayConfig = config.Bind(
            "Respawn",
            "CorpseRespawnDelaySeconds",
            30,
            new ConfigDescription(
                "Delay before a dead player respawns at their own corpse.",
                new AcceptableValueRange<int>(0, 600)));

        pvpDeathRespawnConfig = config.Bind(
            "PVP",
            "RealDeathRespawnMode",
            PvpDeathRespawnMode.PreviousCampfire,
            "Respawn strategy after becoming a skeleton in PVP mode. PVP blowgun knockouts always use the previous campfire.");

        pvpChestRefreshEnabledConfig = config.Bind(
            "PVP",
            "RefreshOpenedChests",
            false,
            "Close and refill opened luggage after a delay while PVP mode is active.");

        pvpChestRefreshSecondsConfig = config.Bind(
            "PVP",
            "ChestRefreshSeconds",
            300,
            new ConfigDescription(
                "Delay before an opened PVP luggage chest can be opened for new loot.",
                new AcceptableValueRange<int>(30, 1800)));

        NormalizeLocalConfig(logWarning: true);

        waitModeConfig.SettingChanged += OnLocalConfigChanged;
        modeConfig.SettingChanged += OnLocalConfigChanged;
        nextPenaltyConfig.SettingChanged += OnLocalConfigChanged;
        corpsePenaltyConfig.SettingChanged += OnLocalConfigChanged;
        previousPenaltyConfig.SettingChanged += OnLocalConfigChanged;
        pvpPenaltyConfig.SettingChanged += OnLocalConfigChanged;
        corpseDelayConfig.SettingChanged += OnLocalConfigChanged;
        pvpDeathRespawnConfig.SettingChanged += OnLocalConfigChanged;
        pvpChestRefreshEnabledConfig.SettingChanged += OnLocalConfigChanged;
        pvpChestRefreshSecondsConfig.SettingChanged += OnLocalConfigChanged;
        initialized = true;

        if (PhotonNetwork.InRoom)
        {
            if (PhotonNetwork.IsMasterClient && IsLobbyScene)
            {
                Publish(ReadLocalConfig());
            }
            else
            {
                ApplyRoomSettings();
            }
        }
        else
        {
            SetCurrent(ReadLocalConfig());
        }
    }

    private static ConfigEntry<int> BindMinutes(
        ConfigFile config,
        string key,
        int defaultValue,
        string description)
    {
        return config.Bind(
            "Respawn",
            key,
            defaultValue,
            new ConfigDescription(description, new AcceptableValueRange<int>(0, 60)));
    }

    internal bool SetWaitMode(CampfireWaitMode waitMode)
    {
        if (!CanEditLobbySettings
            || !Enum.IsDefined(typeof(CampfireWaitMode), waitMode)
            || !RaceSettingsSnapshot.IsCombinationAllowed(
                waitMode,
                modeConfig.Value,
                pvpDeathRespawnConfig.Value))
        {
            return false;
        }

        waitModeConfig.Value = waitMode;
        return true;
    }

    internal bool SetMode(RespawnMode mode)
    {
        if (!CanEditLobbySettings
            || !Enum.IsDefined(typeof(RespawnMode), mode)
            || !RaceSettingsSnapshot.IsCombinationAllowed(
                waitModeConfig.Value,
                mode,
                pvpDeathRespawnConfig.Value))
        {
            return false;
        }

        modeConfig.Value = mode;
        return true;
    }

    internal bool CanSelectWaitMode(CampfireWaitMode waitMode)
    {
        return Enum.IsDefined(typeof(CampfireWaitMode), waitMode)
            && RaceSettingsSnapshot.IsCombinationAllowed(
                waitMode,
                current.Mode,
                current.PvpDeathRespawn);
    }

    internal bool CanSelectRespawnMode(RespawnMode mode)
    {
        return Enum.IsDefined(typeof(RespawnMode), mode)
            && RaceSettingsSnapshot.IsCombinationAllowed(
                current.WaitMode,
                mode,
                current.PvpDeathRespawn);
    }

    internal bool CanSelectPvpDeathRespawn(PvpDeathRespawnMode mode)
    {
        return Enum.IsDefined(typeof(PvpDeathRespawnMode), mode)
            && RaceSettingsSnapshot.IsCombinationAllowed(
                current.WaitMode,
                current.Mode,
                mode);
    }

    internal void AdjustPenalty(RespawnMode mode, int deltaMinutes)
    {
        if (!CanEditLobbySettings)
        {
            return;
        }

        ConfigEntry<int> entry = mode switch
        {
            RespawnMode.CorpseTimer => corpsePenaltyConfig,
            RespawnMode.PreviousCampfire => previousPenaltyConfig,
            RespawnMode.Pvp => pvpPenaltyConfig,
            _ => nextPenaltyConfig
        };
        entry.Value = Mathf.Clamp(entry.Value + deltaMinutes, 0, 60);
    }

    internal void AdjustCorpseDelay(int deltaSeconds)
    {
        if (CanEditLobbySettings)
        {
            corpseDelayConfig.Value = Mathf.Clamp(corpseDelayConfig.Value + deltaSeconds, 0, 600);
        }
    }

    internal void CyclePvpDeathRespawn()
    {
        if (!CanEditLobbySettings)
        {
            return;
        }

        int optionCount = Enum.GetValues(typeof(PvpDeathRespawnMode)).Length;
        for (int offset = 1; offset <= optionCount; offset++)
        {
            PvpDeathRespawnMode candidate = (PvpDeathRespawnMode)(
                ((int)pvpDeathRespawnConfig.Value + offset) % optionCount);
            if (RaceSettingsSnapshot.IsCombinationAllowed(
                waitModeConfig.Value,
                modeConfig.Value,
                candidate))
            {
                pvpDeathRespawnConfig.Value = candidate;
                return;
            }
        }
    }

    internal bool SetPvpDeathRespawn(PvpDeathRespawnMode mode)
    {
        if (!CanEditLobbySettings
            || !Enum.IsDefined(typeof(PvpDeathRespawnMode), mode)
            || !RaceSettingsSnapshot.IsCombinationAllowed(
                waitModeConfig.Value,
                modeConfig.Value,
                mode))
        {
            return false;
        }

        pvpDeathRespawnConfig.Value = mode;
        return true;
    }

    internal void TogglePvpChestRefresh()
    {
        if (CanEditLobbySettings)
        {
            pvpChestRefreshEnabledConfig.Value = !pvpChestRefreshEnabledConfig.Value;
        }
    }

    internal void AdjustPvpChestRefreshSeconds(int deltaSeconds)
    {
        if (CanEditLobbySettings)
        {
            pvpChestRefreshSecondsConfig.Value = Mathf.Clamp(
                pvpChestRefreshSecondsConfig.Value + deltaSeconds,
                30,
                1800);
        }
    }

    private void OnLocalConfigChanged(object sender, EventArgs eventArgs)
    {
        if (!initialized || normalizingLocalConfig)
        {
            return;
        }

        NormalizeLocalConfig(logWarning: true);
        RaceSettingsSnapshot local = ReadLocalConfig();
        if (!PhotonNetwork.InRoom)
        {
            SetCurrent(local);
        }
        else if (PhotonNetwork.IsMasterClient && IsLobbyScene)
        {
            Publish(local);
        }
    }

    private RaceSettingsSnapshot ReadLocalConfig()
    {
        return new RaceSettingsSnapshot(
            waitModeConfig.Value,
            modeConfig.Value,
            nextPenaltyConfig.Value,
            corpsePenaltyConfig.Value,
            previousPenaltyConfig.Value,
            pvpPenaltyConfig.Value,
            corpseDelayConfig.Value,
            pvpDeathRespawnConfig.Value,
            pvpChestRefreshEnabledConfig.Value,
            pvpChestRefreshSecondsConfig.Value);
    }

    private void NormalizeLocalConfig(bool logWarning)
    {
        CampfireWaitMode waitMode = Enum.IsDefined(
            typeof(CampfireWaitMode),
            waitModeConfig.Value)
            ? waitModeConfig.Value
            : CampfireWaitMode.Nobody;
        RespawnMode mode = Enum.IsDefined(typeof(RespawnMode), modeConfig.Value)
            ? modeConfig.Value
            : RespawnMode.PreviousCampfire;
        PvpDeathRespawnMode pvpDeathRespawn = Enum.IsDefined(
            typeof(PvpDeathRespawnMode),
            pvpDeathRespawnConfig.Value)
            ? pvpDeathRespawnConfig.Value
            : PvpDeathRespawnMode.PreviousCampfire;

        bool changed = waitMode != waitModeConfig.Value
            || mode != modeConfig.Value
            || pvpDeathRespawn != pvpDeathRespawnConfig.Value;
        if (!RaceSettingsSnapshot.IsCombinationAllowed(waitMode, mode, pvpDeathRespawn))
        {
            if (mode == RespawnMode.NextCampfire)
            {
                mode = RespawnMode.PreviousCampfire;
            }
            if (mode == RespawnMode.Pvp
                && pvpDeathRespawn == PvpDeathRespawnMode.NextCampfire)
            {
                pvpDeathRespawn = PvpDeathRespawnMode.PreviousCampfire;
            }
            changed = true;
        }

        if (!changed)
        {
            return;
        }

        if (logWarning)
        {
            Plugin.Log.LogWarning(
                "Normalized incompatible or invalid local race settings to a safe combination: "
                + $"wait {waitMode}, respawn {mode}, PVP real death {pvpDeathRespawn}.");
        }

        normalizingLocalConfig = true;
        try
        {
            waitModeConfig.Value = waitMode;
            modeConfig.Value = mode;
            pvpDeathRespawnConfig.Value = pvpDeathRespawn;
        }
        finally
        {
            normalizingLocalConfig = false;
        }
    }

    private void Publish(RaceSettingsSnapshot snapshot)
    {
        SetCurrent(snapshot);
        if (!PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null)
        {
            return;
        }

        Hashtable properties = new()
        {
            [VersionKey] = NetworkSchemaVersion,
            [WaitModeKey] = (int)snapshot.WaitMode,
            [ModeKey] = (int)snapshot.Mode,
            [NextPenaltyKey] = snapshot.NextCampfirePenaltyMinutes,
            [CorpsePenaltyKey] = snapshot.CorpsePenaltyMinutes,
            [PreviousPenaltyKey] = snapshot.PreviousCampfirePenaltyMinutes,
            [PvpPenaltyKey] = snapshot.PvpPenaltyMinutes,
            [CorpseDelayKey] = snapshot.CorpseRespawnDelaySeconds,
            [PvpDeathRespawnKey] = (int)snapshot.PvpDeathRespawn,
            [PvpChestRefreshEnabledKey] = snapshot.PvpChestRefreshEnabled,
            [PvpChestRefreshSecondsKey] = snapshot.PvpChestRefreshSeconds
        };
        if (!PhotonNetwork.CurrentRoom.SetCustomProperties(properties))
        {
            Plugin.Log.LogError("Photon rejected the lobby respawn settings update.");
            return;
        }
        Plugin.Log.LogInfo(
            $"Published lobby race settings: wait {snapshot.WaitMode}, respawn {snapshot.Mode}, "
            + $"penalty {snapshot.ActivePenaltyMinutes}m, corpse delay {snapshot.CorpseRespawnDelaySeconds}s, "
            + $"PVP real death {snapshot.PvpDeathRespawn}, "
            + $"PVP chest refresh {(snapshot.PvpChestRefreshEnabled ? $"{snapshot.PvpChestRefreshSeconds}s" : "off")}.");
    }

    private bool ApplyRoomSettings()
    {
        Hashtable properties = PhotonNetwork.CurrentRoom?.CustomProperties;
        if (properties == null
            || !TryReadInt(properties, VersionKey, out int version)
            || (version < 4 || version > NetworkSchemaVersion)
            || !TryReadInt(properties, ModeKey, out int mode)
            || !TryReadInt(properties, NextPenaltyKey, out int nextPenalty)
            || !TryReadInt(properties, CorpsePenaltyKey, out int corpsePenalty)
            || !TryReadInt(properties, PreviousPenaltyKey, out int previousPenalty)
            || !TryReadInt(properties, PvpPenaltyKey, out int pvpPenalty)
            || !TryReadInt(properties, CorpseDelayKey, out int corpseDelay)
            || !TryReadInt(properties, PvpDeathRespawnKey, out int pvpDeathRespawn)
            || !TryReadBool(properties, PvpChestRefreshEnabledKey, out bool chestRefreshEnabled)
            || !TryReadInt(properties, PvpChestRefreshSecondsKey, out int chestRefreshSeconds))
        {
            return false;
        }

        int waitMode = (int)CampfireWaitMode.Nobody;
        if (version >= 5
            && !TryReadInt(properties, WaitModeKey, out waitMode))
        {
            return false;
        }

        RespawnMode parsedMode = Enum.IsDefined(typeof(RespawnMode), mode)
            ? (RespawnMode)mode
            : RespawnMode.PreviousCampfire;
        CampfireWaitMode parsedWaitMode = Enum.IsDefined(typeof(CampfireWaitMode), waitMode)
            ? (CampfireWaitMode)waitMode
            : CampfireWaitMode.Nobody;
        PvpDeathRespawnMode parsedPvpDeathRespawn = Enum.IsDefined(
            typeof(PvpDeathRespawnMode),
            pvpDeathRespawn)
            ? (PvpDeathRespawnMode)pvpDeathRespawn
            : PvpDeathRespawnMode.PreviousCampfire;
        RaceSettingsSnapshot snapshot = new(
            parsedWaitMode,
            parsedMode,
            nextPenalty,
            corpsePenalty,
            previousPenalty,
            pvpPenalty,
            corpseDelay,
            parsedPvpDeathRespawn,
            chestRefreshEnabled,
            chestRefreshSeconds);
        if (version != NetworkSchemaVersion
            || parsedWaitMode != (CampfireWaitMode)waitMode
            || parsedMode != (RespawnMode)mode
            || parsedPvpDeathRespawn != (PvpDeathRespawnMode)pvpDeathRespawn
            || snapshot.WaitMode != parsedWaitMode
            || snapshot.Mode != parsedMode
            || snapshot.PvpDeathRespawn != parsedPvpDeathRespawn)
        {
            Plugin.Log.LogWarning(
                $"Migrated room race settings schema {version} to safe local rules: "
                + $"wait {snapshot.WaitMode}, respawn {snapshot.Mode}, "
                + $"PVP real death {snapshot.PvpDeathRespawn}.");
        }
        SetCurrent(snapshot);
        return true;
    }

    private static bool TryReadInt(Hashtable properties, string key, out int value)
    {
        value = 0;
        if (!properties.TryGetValue(key, out object boxed) || boxed == null)
        {
            return false;
        }

        try
        {
            value = Convert.ToInt32(boxed);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool TryReadBool(Hashtable properties, string key, out bool value)
    {
        value = false;
        if (!properties.TryGetValue(key, out object boxed) || boxed == null)
        {
            return false;
        }

        try
        {
            value = Convert.ToBoolean(boxed);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void SetCurrent(RaceSettingsSnapshot snapshot)
    {
        if (current.Equals(snapshot))
        {
            return;
        }

        current = snapshot;
        Plugin.Log.LogInfo(
            $"Using lobby race settings: wait {snapshot.WaitMode}, respawn {snapshot.Mode}, "
            + $"penalty {snapshot.ActivePenaltyMinutes}m, corpse delay {snapshot.CorpseRespawnDelaySeconds}s, "
            + $"PVP real death {snapshot.PvpDeathRespawn}, "
            + $"PVP chest refresh {(snapshot.PvpChestRefreshEnabled ? $"{snapshot.PvpChestRefreshSeconds}s" : "off")}.");
    }

    public override void OnJoinedRoom()
    {
        if (!initialized)
        {
            return;
        }

        if (PhotonNetwork.IsMasterClient && IsLobbyScene)
        {
            Publish(ReadLocalConfig());
        }
        else
        {
            ApplyRoomSettings();
        }
    }

    public override void OnRoomPropertiesUpdate(Hashtable propertiesThatChanged)
    {
        if (initialized
            && (propertiesThatChanged.ContainsKey(VersionKey)
                || propertiesThatChanged.ContainsKey(WaitModeKey)
                || propertiesThatChanged.ContainsKey(ModeKey)
                || propertiesThatChanged.ContainsKey(NextPenaltyKey)
                || propertiesThatChanged.ContainsKey(CorpsePenaltyKey)
                || propertiesThatChanged.ContainsKey(PreviousPenaltyKey)
                || propertiesThatChanged.ContainsKey(PvpPenaltyKey)
                || propertiesThatChanged.ContainsKey(CorpseDelayKey)
                || propertiesThatChanged.ContainsKey(PvpDeathRespawnKey)
                || propertiesThatChanged.ContainsKey(PvpChestRefreshEnabledKey)
                || propertiesThatChanged.ContainsKey(PvpChestRefreshSecondsKey)))
        {
            ApplyRoomSettings();
        }
    }

    public override void OnMasterClientSwitched(Photon.Realtime.Player newMasterClient)
    {
        if (initialized && PhotonNetwork.IsMasterClient)
        {
            // Keep the room's active rules stable during host migration.
            Publish(current);
        }
    }

    public override void OnLeftRoom()
    {
        if (initialized)
        {
            SetCurrent(ReadLocalConfig());
        }
    }

    private void OnDestroy()
    {
        if (initialized)
        {
            waitModeConfig.SettingChanged -= OnLocalConfigChanged;
            modeConfig.SettingChanged -= OnLocalConfigChanged;
            nextPenaltyConfig.SettingChanged -= OnLocalConfigChanged;
            corpsePenaltyConfig.SettingChanged -= OnLocalConfigChanged;
            previousPenaltyConfig.SettingChanged -= OnLocalConfigChanged;
            pvpPenaltyConfig.SettingChanged -= OnLocalConfigChanged;
            corpseDelayConfig.SettingChanged -= OnLocalConfigChanged;
            pvpDeathRespawnConfig.SettingChanged -= OnLocalConfigChanged;
            pvpChestRefreshEnabledConfig.SettingChanged -= OnLocalConfigChanged;
            pvpChestRefreshSecondsConfig.SettingChanged -= OnLocalConfigChanged;
        }

        if (Instance == this)
        {
            Instance = null;
        }
    }
}
