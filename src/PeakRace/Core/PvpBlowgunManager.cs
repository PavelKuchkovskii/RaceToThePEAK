using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using Zorro.Core;

namespace PeakRace.Core;

/// <summary>
/// Creates and registers a network-safe copy of PEAK's blowgun. A distinct item
/// ID and prefab name make its inventory and hit source unambiguous on every
/// client, while a delegating prefab pool remains compatible with other pools.
/// </summary>
internal sealed class PvpBlowgunManager : MonoBehaviour
{
    private const byte KnockoutEventCode = 198;
    private const string KnockoutEventSignature = "RaceToThePeak.PvpKnockout.v1";
    private const string NetworkPrefabPath = "0_Items/" + PrefabName;
    private const int PreferredItemId = 64817;
    private const double AttributionLifetimeSeconds = 10d;
    private static readonly Color PvpColor = new(0.95f, 0.06f, 0.12f, 1f);

    internal const string PrefabName = "RaceToThePeak_PVPBlowgun";

    private readonly Dictionary<int, double> confirmedPvpHits = new();
    private readonly List<Material> ownedMaterials = new();
    private readonly List<Texture2D> ownedTextures = new();
    private ItemDatabase itemDatabase;
    private Item registeredItem;
    private GameObject prefabTemplate;
    private IPunPrefabPool previousPrefabPool;
    private PvpPrefabPool pvpPrefabPool;
    private Coroutine initializationRoutine;

    internal static PvpBlowgunManager Instance { get; private set; }

    internal bool IsReady => prefabTemplate != null && registeredItem != null;
    internal GameObject PrefabTemplate => prefabTemplate;

    private static double NetworkTime => PhotonNetwork.InRoom
        ? PhotonNetwork.Time
        : Time.realtimeSinceStartupAsDouble;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        PhotonNetwork.NetworkingClient.EventReceived += OnPhotonEvent;
        SceneManager.sceneLoaded += OnSceneLoaded;
        initializationRoutine = StartCoroutine(InitializeWhenAssetsAreReady());
    }

    private IEnumerator InitializeWhenAssetsAreReady()
    {
        while (!TryRegisterPrefab())
        {
            yield return null;
        }

        initializationRoutine = null;
    }

    private bool TryRegisterPrefab()
    {
        if (IsReady)
        {
            return true;
        }

        itemDatabase = SingletonAsset<ItemDatabase>.Instance;
        if (itemDatabase == null)
        {
            return false;
        }

        Item sourceItem = itemDatabase.Objects.FirstOrDefault(item =>
            item != null
            && item.gameObject.name != PrefabName
            && item.GetComponentInChildren<Action_RaycastDart>(true) != null);
        if (sourceItem == null)
        {
            return false;
        }

        int itemId = FindAvailableItemId(itemDatabase);
        if (itemId < 0)
        {
            Plugin.Log.LogError("No free item ID is available for the PVP blowgun.");
            enabled = false;
            return true;
        }

        GameObject sourceObject = sourceItem.gameObject;
        bool sourceWasActive = sourceObject.activeSelf;
        try
        {
            sourceObject.SetActive(false);
            prefabTemplate = Instantiate(sourceObject);
        }
        finally
        {
            sourceObject.SetActive(sourceWasActive);
        }

        prefabTemplate.name = PrefabName;
        prefabTemplate.transform.SetParent(null);
        prefabTemplate.SetActive(false);
        DontDestroyOnLoad(prefabTemplate);

        registeredItem = prefabTemplate.GetComponent<Item>();
        registeredItem.itemID = (ushort)itemId;
        registeredItem.UIData = CloneUiData(sourceItem.UIData);
        prefabTemplate.AddComponent<PvpBlowgunMarker>();

        LootData lootData = prefabTemplate.GetComponent<LootData>();
        if (lootData != null)
        {
            // PVP loot is injected explicitly into luggage. Keeping it out of
            // the normal pools prevents it from leaking into the other modes.
            lootData.spawnLocations = SpawnPool.None;
            lootData.excludeFromCustomRunSelection = true;
        }

        RecolorPrefab(prefabTemplate);
        RecolorInventoryIcons(registeredItem.UIData);
        itemDatabase.itemLookup[(ushort)itemId] = registeredItem;
        itemDatabase.AddRuntimeEntry(registeredItem);
        InstallPrefabPool();

        Plugin.Log.LogInfo(
            $"Registered crimson PVP blowgun as item {itemId} and prefab {NetworkPrefabPath}.");
        return true;
    }

    private static int FindAvailableItemId(ItemDatabase database)
    {
        if (!database.itemLookup.ContainsKey((ushort)PreferredItemId))
        {
            return PreferredItemId;
        }

        Item existing = database.itemLookup[(ushort)PreferredItemId];
        if (existing != null && existing.gameObject.name == PrefabName)
        {
            return PreferredItemId;
        }

        for (int candidate = PreferredItemId - 1; candidate >= 64000; candidate--)
        {
            if (!database.itemLookup.ContainsKey((ushort)candidate))
            {
                return candidate;
            }
        }

        return -1;
    }

    private void InstallPrefabPool()
    {
        if (pvpPrefabPool != null && ReferenceEquals(PhotonNetwork.PrefabPool, pvpPrefabPool))
        {
            return;
        }

        previousPrefabPool = PhotonNetwork.PrefabPool;
        pvpPrefabPool = new PvpPrefabPool(previousPrefabPool, prefabTemplate);
        PhotonNetwork.PrefabPool = pvpPrefabPool;
    }

    private void RecolorPrefab(GameObject prefab)
    {
        foreach (Renderer renderer in prefab.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer is not MeshRenderer && renderer is not SkinnedMeshRenderer)
            {
                continue;
            }

            Material[] sourceMaterials = renderer.sharedMaterials;
            Material[] coloredMaterials = new Material[sourceMaterials.Length];
            for (int index = 0; index < sourceMaterials.Length; index++)
            {
                Material source = sourceMaterials[index];
                if (source == null)
                {
                    continue;
                }

                Material material = new(source)
                {
                    name = source.name + " (RaceToThePeak PVP)"
                };
                SetTint(material, PvpColor);
                coloredMaterials[index] = material;
                ownedMaterials.Add(material);
            }

            renderer.sharedMaterials = coloredMaterials;
        }
    }

    private void RecolorInventoryIcons(Item.ItemUIData uiData)
    {
        if (uiData?.icon == null)
        {
            Plugin.Log.LogWarning("The source blowgun has no inventory icon to recolor.");
            return;
        }

        Texture2D sourceIcon = uiData.icon;
        uiData.icon = CreateCrimsonIcon(sourceIcon);

        if (uiData.altIcon != null)
        {
            uiData.altIcon = uiData.altIcon == sourceIcon
                ? uiData.icon
                : CreateCrimsonIcon(uiData.altIcon);
        }
    }

    private Texture2D CreateCrimsonIcon(Texture2D source)
    {
        RenderTexture previousTarget = RenderTexture.active;
        RenderTexture temporaryTarget = null;
        Texture2D icon = null;
        try
        {
            // PEAK's imported icon textures are not guaranteed to be readable.
            // Copying through a render target works for both readable and
            // compressed textures without altering the original blowgun asset.
            temporaryTarget = RenderTexture.GetTemporary(
                source.width,
                source.height,
                0,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.Default);
            Graphics.Blit(source, temporaryTarget);
            RenderTexture.active = temporaryTarget;

            icon = new Texture2D(
                source.width,
                source.height,
                TextureFormat.RGBA32,
                false,
                false)
            {
                name = source.name + " (RaceToThePeak PVP)",
                filterMode = source.filterMode,
                wrapMode = source.wrapMode,
                anisoLevel = source.anisoLevel
            };
            icon.ReadPixels(new Rect(0f, 0f, source.width, source.height), 0, 0, false);

            Color.RGBToHSV(PvpColor, out float pvpHue, out _, out _);
            Color[] pixels = icon.GetPixels();
            for (int index = 0; index < pixels.Length; index++)
            {
                Color pixel = pixels[index];
                if (pixel.a <= 0.001f)
                {
                    continue;
                }

                Color.RGBToHSV(pixel, out _, out float saturation, out float value);
                float crimsonSaturation = Mathf.Lerp(0.72f, 0.95f, saturation);

                // Keep bright low-saturation pixels as highlights so the icon
                // remains legible instead of becoming a flat red silhouette.
                float highlight = Mathf.InverseLerp(0.72f, 1f, value)
                    * (1f - saturation);
                crimsonSaturation = Mathf.Lerp(crimsonSaturation, 0.22f, highlight * 0.7f);

                Color tinted = Color.HSVToRGB(pvpHue, crimsonSaturation, value);
                tinted.a = pixel.a;
                pixels[index] = tinted;
            }

            icon.SetPixels(pixels);
            icon.Apply(false, false);
            ownedTextures.Add(icon);
            return icon;
        }
        catch (Exception exception)
        {
            if (icon != null)
            {
                Destroy(icon);
            }

            Plugin.Log.LogWarning(
                $"Could not create the crimson PVP blowgun inventory icon: {exception.Message}");
            return source;
        }
        finally
        {
            RenderTexture.active = previousTarget;
            if (temporaryTarget != null)
            {
                RenderTexture.ReleaseTemporary(temporaryTarget);
            }
        }
    }

    private static Item.ItemUIData CloneUiData(Item.ItemUIData source)
    {
        if (source == null)
        {
            return new Item.ItemUIData();
        }

        // ItemUIData is an embedded serializable class rather than a Unity
        // object. Copy it explicitly so changing the PVP icon can never mutate
        // the ordinary blowgun's shared UI data.
        return new Item.ItemUIData
        {
            itemName = source.itemName,
            icon = source.icon,
            hasAltIcon = source.hasAltIcon,
            hasColorBlindIcon = source.hasColorBlindIcon,
            altIcon = source.altIcon,
            hasMainInteract = source.hasMainInteract,
            mainInteractPrompt = source.mainInteractPrompt,
            hasSecondInteract = source.hasSecondInteract,
            secondaryInteractPrompt = source.secondaryInteractPrompt,
            hideSecondInteract = source.hideSecondInteract,
            hasScrollingInteract = source.hasScrollingInteract,
            scrollInteractPrompt = source.scrollInteractPrompt,
            canDrop = source.canDrop,
            canPocket = source.canPocket,
            canBackpack = source.canBackpack,
            canThrow = source.canThrow,
            isShootable = source.isShootable,
            hideFuel = source.hideFuel,
            iconPositionOffset = source.iconPositionOffset,
            iconRotationOffset = source.iconRotationOffset,
            iconScaleOffset = source.iconScaleOffset
        };
    }

    private static void SetTint(Material material, Color color)
    {
        string[] colorProperties =
        {
            "_BaseColor",
            "_Color",
            "_Tint",
            "_TopColor",
            "_BottomColor"
        };

        foreach (string property in colorProperties)
        {
            if (!material.HasProperty(property))
            {
                continue;
            }

            Color existing = material.GetColor(property);
            material.SetColor(property, new Color(color.r, color.g, color.b, existing.a));
        }
    }

    internal void ConfirmLocalPvpHit(Character target)
    {
        if (target == null || target.photonView == null)
        {
            return;
        }

        int viewId = target.photonView.ViewID;
        MarkConfirmedHit(viewId);
        if (!PhotonNetwork.InRoom)
        {
            return;
        }

        object[] payload = { KnockoutEventSignature, viewId };
        bool sent = PhotonNetwork.RaiseEvent(
            KnockoutEventCode,
            payload,
            new RaiseEventOptions { Receivers = ReceiverGroup.Others },
            SendOptions.SendReliable);
        if (!sent)
        {
            Plugin.Log.LogWarning(
                $"Failed to broadcast PVP blowgun attribution for character view {viewId}.");
        }
    }

    internal void ClearAttribution(Character target)
    {
        if (target != null && target.photonView != null)
        {
            confirmedPvpHits.Remove(target.photonView.ViewID);
        }
    }

    internal bool ConsumeConfirmedHit(Character target)
    {
        if (target == null || target.photonView == null)
        {
            return false;
        }

        int viewId = target.photonView.ViewID;
        if (!confirmedPvpHits.TryGetValue(viewId, out double expiresAt))
        {
            return false;
        }

        confirmedPvpHits.Remove(viewId);
        return expiresAt >= NetworkTime;
    }

    private void MarkConfirmedHit(int viewId)
    {
        confirmedPvpHits[viewId] = NetworkTime + AttributionLifetimeSeconds;
    }

    private void OnPhotonEvent(EventData photonEvent)
    {
        if (photonEvent.Code != KnockoutEventCode
            || photonEvent.CustomData is not object[] payload
            || payload.Length != 2
            || payload[0] is not string signature
            || signature != KnockoutEventSignature)
        {
            return;
        }

        try
        {
            MarkConfirmedHit(Convert.ToInt32(payload[1]));
        }
        catch (Exception)
        {
            Plugin.Log.LogWarning("Ignored malformed PVP blowgun attribution event.");
        }
    }

    private void Update()
    {
        if (IsReady && !ReferenceEquals(PhotonNetwork.PrefabPool, pvpPrefabPool))
        {
            // A later-loading mod may replace Photon's pool. Wrap the new pool
            // instead of replacing its behavior so both prefab sets keep working.
            InstallPrefabPool();
        }

        if (RaceSettingsManager.Current.Mode != RespawnMode.Pvp)
        {
            confirmedPvpHits.Clear();
            return;
        }

        double now = NetworkTime;
        foreach (int viewId in confirmedPvpHits
                     .Where(pair => pair.Value < now)
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            confirmedPvpHits.Remove(viewId);
        }
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        confirmedPvpHits.Clear();
    }

    private void OnDestroy()
    {
        if (initializationRoutine != null)
        {
            StopCoroutine(initializationRoutine);
        }

        PhotonNetwork.NetworkingClient.EventReceived -= OnPhotonEvent;
        SceneManager.sceneLoaded -= OnSceneLoaded;

        if (pvpPrefabPool != null && ReferenceEquals(PhotonNetwork.PrefabPool, pvpPrefabPool))
        {
            PhotonNetwork.PrefabPool = previousPrefabPool;
        }

        if (itemDatabase != null && registeredItem != null)
        {
            if (itemDatabase.itemLookup.TryGetValue(registeredItem.itemID, out Item current)
                && current == registeredItem)
            {
                itemDatabase.itemLookup.Remove(registeredItem.itemID);
            }

            itemDatabase.Objects.Remove(registeredItem);
        }

        if (prefabTemplate != null)
        {
            Destroy(prefabTemplate);
        }

        foreach (Material material in ownedMaterials)
        {
            if (material != null)
            {
                Destroy(material);
            }
        }

        foreach (Texture2D texture in ownedTextures)
        {
            if (texture != null)
            {
                Destroy(texture);
            }
        }

        if (Instance == this)
        {
            Instance = null;
        }
    }

    private sealed class PvpPrefabPool : IPunPrefabPool
    {
        private readonly IPunPrefabPool innerPool;
        private readonly GameObject template;

        internal PvpPrefabPool(IPunPrefabPool innerPool, GameObject template)
        {
            this.innerPool = innerPool;
            this.template = template;
        }

        public GameObject Instantiate(string prefabId, Vector3 position, Quaternion rotation)
        {
            if (prefabId == NetworkPrefabPath)
            {
                return UnityEngine.Object.Instantiate(template, position, rotation);
            }

            return innerPool?.Instantiate(prefabId, position, rotation);
        }

        public void Destroy(GameObject gameObject)
        {
            if (gameObject != null && gameObject.GetComponent<PvpBlowgunMarker>() != null)
            {
                UnityEngine.Object.Destroy(gameObject);
                return;
            }

            if (innerPool != null)
            {
                innerPool.Destroy(gameObject);
            }
            else
            {
                UnityEngine.Object.Destroy(gameObject);
            }
        }
    }
}

/// <summary>Runtime-only marker copied with the custom network prefab.</summary>
internal sealed class PvpBlowgunMarker : MonoBehaviour
{
}
