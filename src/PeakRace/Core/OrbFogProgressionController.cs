using ExitGames.Client.Photon;
using HarmonyLib;
using Peak;
using Photon.Pun;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using Zorro.Core;

namespace PeakRace.Core;

/// <summary>
/// Decouples PEAK's single OrbFog origin from the globally loaded map segment.
/// In scoped progression modes the fog follows the slowest connected racer,
/// so loading a biome for a leader cannot engulf somebody still climbing below.
/// </summary>
internal sealed class OrbFogProgressionController : MonoBehaviourPunCallbacks
{
    private const string OriginKey = "RTP.OrbFogOrigin";
    private const float ReconcileIntervalSeconds = 0.2f;
    private const float AdvanceStabilitySeconds = 1f;

    private static readonly FieldInfo OriginsField =
        AccessTools.Field(typeof(OrbFogHandler), "origins");

    internal static OrbFogProgressionController Instance { get; private set; }

    private MapHandler cachedMap;
    private OrbFogHandler cachedFog;
    private int synchronizedOrigin = -1;
    private int appliedOrigin = -1;
    private int pendingOrigin = -1;
    private float pendingOriginSince;
    private float nextReconcileTime;
    private bool clearedOutsideRun;

    internal static bool ManagesCurrentTransition
    {
        get
        {
            RaceSettingsSnapshot settings = RaceSettingsManager.Current;
            return Instance != null
                && (settings.UsesPersonalCampfireClaims
                    || settings.WaitMode != CampfireWaitMode.Lobby);
        }
    }

    private static bool IsAuthority =>
        !PhotonNetwork.InRoom || PhotonNetwork.IsMasterClient;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void Update()
    {
        string scene = SceneManager.GetActiveScene().name;
        if (scene == "Airport" || scene == "Title")
        {
            if (!clearedOutsideRun)
            {
                ClearRunState(clearRoomProperty: true);
                clearedOutsideRun = true;
            }
            return;
        }

        clearedOutsideRun = false;
        if (!ManagesCurrentTransition
            || Time.unscaledTime < nextReconcileTime
            || !TryGetContext(out MapHandler map, out OrbFogHandler fog))
        {
            return;
        }

        nextReconcileTime = Time.unscaledTime + ReconcileIntervalSeconds;
        if (cachedMap != map || cachedFog != fog)
        {
            cachedMap = map;
            cachedFog = fog;
            appliedOrigin = -1;
            pendingOrigin = -1;
        }

        if (IsAuthority)
        {
            ReconcileAuthority(map, fog);
        }

        ApplySynchronizedOrigin(map, fog);
    }

    private void ReconcileAuthority(MapHandler map, OrbFogHandler fog)
    {
        if (!TryGetSlowestPhysicalSegment(map, out int slowestSegment))
        {
            return;
        }

        int unlockedSegment = Mathf.Clamp(
            (int)MapHandler.CurrentSegmentNumber,
            0,
            map.segments.Length - 1);
        int safeOrigin = Mathf.Clamp(slowestSegment, 0, unlockedSegment);
        int currentOrigin = synchronizedOrigin >= 0
            ? synchronizedOrigin
            : Mathf.Clamp(fog.currentID, 0, unlockedSegment);

        // Fog progression is deliberately monotonic. Forced backward movement
        // (for example a PVP recall) must not let players repeatedly reset it.
        if (safeOrigin <= currentOrigin)
        {
            pendingOrigin = -1;
            if (synchronizedOrigin < 0)
            {
                PublishOrigin(currentOrigin);
            }
            return;
        }

        if (pendingOrigin != safeOrigin)
        {
            pendingOrigin = safeOrigin;
            pendingOriginSince = Time.unscaledTime;
            return;
        }

        if (Time.unscaledTime - pendingOriginSince < AdvanceStabilitySeconds)
        {
            return;
        }

        pendingOrigin = -1;
        PublishOrigin(safeOrigin);
    }

    private void PublishOrigin(int origin)
    {
        if (origin < 0 || origin <= synchronizedOrigin)
        {
            return;
        }

        if (PhotonNetwork.InRoom
            && (PhotonNetwork.CurrentRoom == null
                || !PhotonNetwork.CurrentRoom.SetCustomProperties(new Hashtable
                {
                    [OriginKey] = origin
                })))
        {
            Plugin.Log.LogWarning($"Photon rejected OrbFog origin {origin}.");
            return;
        }

        synchronizedOrigin = origin;
        Plugin.Log.LogInfo($"Scoped OrbFog advanced to segment {origin}.");
    }

    private void ApplySynchronizedOrigin(MapHandler map, OrbFogHandler fog)
    {
        if (synchronizedOrigin < 0 || appliedOrigin == synchronizedOrigin)
        {
            return;
        }

        int finalSegment = Mathf.Clamp(
            (int)Segment.TheKiln,
            0,
            map.segments.Length - 1);
        int originId = synchronizedOrigin;
        if (synchronizedOrigin >= finalSegment)
        {
            FogSphereOrigin[] origins = OriginsField?.GetValue(fog) as FogSphereOrigin[];
            originId = origins?.Length ?? synchronizedOrigin;
        }

        // SetFogOrigin is intentionally called directly here. Only its call in
        // MapHandler's normal transition coroutine is intercepted by the mod.
        fog.isMoving = false;
        fog.currentWaitTime = 0f;
        fog.SetFogOrigin(originId);
        fog.isMoving = false;
        appliedOrigin = synchronizedOrigin;

        Plugin.Log.LogInfo(
            synchronizedOrigin >= finalSegment
                ? "Scoped OrbFog disabled after the last racer entered The Kiln."
                : $"Applied scoped OrbFog origin {synchronizedOrigin} locally.");
    }

    private static bool TryGetSlowestPhysicalSegment(
        MapHandler map,
        out int slowestSegment)
    {
        slowestSegment = int.MaxValue;
        bool foundCharacter = false;
        foreach (Character character in GetActivePlayerCharacters())
        {
            foundCharacter = true;
            if (!LocalBiomeEnvironmentController.TryResolveCharacterSegment(
                character,
                out int segment))
            {
                // Uncertain player position must never advance a lethal field.
                return false;
            }

            slowestSegment = Mathf.Min(slowestSegment, segment);
        }

        if (!foundCharacter)
        {
            return false;
        }

        slowestSegment = Mathf.Clamp(slowestSegment, 0, map.segments.Length - 1);
        return true;
    }

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

    private static bool TryGetContext(
        out MapHandler map,
        out OrbFogHandler fog)
    {
        map = null;
        fog = null;
        if (!MapHandler.ExistsAndInitialized
            || VoidBiome.VoidBiomeActive
            || Singleton<OrbFogHandler>.Instance == null)
        {
            return false;
        }

        map = Singleton<MapHandler>.Instance;
        fog = Singleton<OrbFogHandler>.Instance;
        return map != null
            && fog != null
            && map.segments != null
            && map.segments.Length > 0;
    }

    private void ApplyRoomProperties(Hashtable properties)
    {
        if (properties == null || !properties.ContainsKey(OriginKey))
        {
            return;
        }

        object boxed = properties[OriginKey];
        if (boxed == null)
        {
            synchronizedOrigin = -1;
            appliedOrigin = -1;
            return;
        }

        try
        {
            int origin = Convert.ToInt32(boxed);
            if (origin < 0 || origin >= 64)
            {
                throw new ArgumentOutOfRangeException(nameof(origin));
            }

            synchronizedOrigin = Mathf.Max(synchronizedOrigin, origin);
        }
        catch (Exception)
        {
            Plugin.Log.LogWarning("Ignored malformed scoped OrbFog origin.");
        }
    }

    private void ClearRunState(bool clearRoomProperty)
    {
        cachedMap = null;
        cachedFog = null;
        synchronizedOrigin = -1;
        appliedOrigin = -1;
        pendingOrigin = -1;
        pendingOriginSince = 0f;
        nextReconcileTime = 0f;

        if (clearRoomProperty
            && PhotonNetwork.InRoom
            && PhotonNetwork.IsMasterClient
            && PhotonNetwork.CurrentRoom != null
            && PhotonNetwork.CurrentRoom.CustomProperties.ContainsKey(OriginKey))
        {
            PhotonNetwork.CurrentRoom.SetCustomProperties(new Hashtable
            {
                [OriginKey] = null
            });
        }
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        ClearRunState(clearRoomProperty: scene.name == "Airport" || scene.name == "Title");
        clearedOutsideRun = scene.name == "Airport" || scene.name == "Title";
        if (!clearedOutsideRun)
        {
            ApplyRoomProperties(PhotonNetwork.CurrentRoom?.CustomProperties);
        }
    }

    public override void OnJoinedRoom()
    {
        ClearRunState(clearRoomProperty: false);
        ApplyRoomProperties(PhotonNetwork.CurrentRoom?.CustomProperties);
    }

    public override void OnRoomPropertiesUpdate(Hashtable propertiesThatChanged)
    {
        ApplyRoomProperties(propertiesThatChanged);
    }

    public override void OnLeftRoom()
    {
        ClearRunState(clearRoomProperty: false);
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
