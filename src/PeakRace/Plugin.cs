using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using PeakRace.Core;
using PeakRace.Patch;
using PeakRace.UI;
using Photon.Pun;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace PeakRace;

[BepInAutoPlugin]
[BepInDependency("PEAKUnlimited", BepInDependency.DependencyFlags.SoftDependency)]
public partial class Plugin : BaseUnityPlugin
{
    internal static ManualLogSource Log { get; private set; } = null!;
    private readonly Harmony harmony = new(Name);
    public static List<(string, Color)> teamList;
    public static TMP_FontAsset FontAsset; //DarumaDropOne-Regular SDF
    //public static int shadowMaterialID; //DarumaDropOne-Regular SDF Shadow (Instance)
    public static Shader shader;
    public static Color Color = new Color(0.8745f, 0.8549f, 0.7608f, 1f); //Standard Color
    private GameObject systemsObject;
    private GameObject settingsMenuObject;
    private GameObject runControlMenuObject;
    private RaceSettingsMenu settingsMenu;
    private RunControlMenu runControlMenu;
    private static ConfigEntry<Key> menuKeyConfig;

    internal static string MenuKeyDisplayName => menuKeyConfig?.Value.ToString() ?? Key.F3.ToString();

    private void Awake()
    {
        Log = base.Logger;
        Log.LogInfo("Plugin RaceToThePeak Loaded");

        menuKeyConfig = Config.Bind(
            "UI",
            "MenuKey",
            Key.F3,
            "Key used by the host to open RaceToThePeak lobby settings and in-run controls. "
            + "F3 is intentionally separate from PEAK Unlimited's default F2 menu.");

        systemsObject = new GameObject("RaceToThePeakSystems");
        DontDestroyOnLoad(systemsObject);
        RaceSettingsManager settingsManager = systemsObject.AddComponent<RaceSettingsManager>();
        settingsManager.Initialize(Config);
        systemsObject.AddComponent<PlayerCampfireProgressTracker>();
        systemsObject.AddComponent<RaceRespawnController>();
        systemsObject.AddComponent<PvpBlowgunManager>();
        systemsObject.AddComponent<PvpChestRefreshManager>();
        systemsObject.AddComponent<LocalBiomeEnvironmentController>();
        systemsObject.AddComponent<FinalHazardController>();
        systemsObject.AddComponent<RespawnCountdownUI>();

        settingsMenuObject = new GameObject("RaceToThePeakSettingsUI");
        DontDestroyOnLoad(settingsMenuObject);
        settingsMenu = settingsMenuObject.AddComponent<RaceSettingsMenu>();

        runControlMenuObject = new GameObject("RaceToThePeakRunControlUI");
        DontDestroyOnLoad(runControlMenuObject);
        runControlMenu = runControlMenuObject.AddComponent<RunControlMenu>();

        SceneManager.sceneLoaded += OnSceneLoaded;

        // Sets up team Names and colors
        teamList = new List<(string, Color)>
        {
            ("Troop BingBong"   ,new Color(0.5f, 1f, 0.42f, 1f)),
            ("Troop Antlion"    ,new Color(1f, 0.6f, 0.34f, 1f)),
            ("Troop Scorpion"   ,new Color(0.56f, 0.25f, 1f, 1f)),
            ("Troop Cheetah"    ,new Color(1f, 1f, 0f, 1f)),
            ("Troop Lovebird"   ,new Color(1f, 0.5f, 1f, 1f)),
            ("Troop Narwhal"    ,new Color(.25f, .5f, 1f, 1f)),
            ("Troop AntEater"   ,new Color(0.3f, 0.82f, 0.28f, 1f)),
            ("Troop Condor"     ,new Color(0.36f, 1f, 1f, 1)),
            ("Troop Crab"       ,new Color(1f, 0.24f, 0.28f, 1f)),
            ("Troop Salamander" ,new Color(0.9f, 0.29f, 1f, 1f)),
            ("Troop Capybara"   ,new Color(0.64f, 0.44f, 0.25f, 1f)),
            ("Troop Mushroom"   ,new Color(0.6f, 0.57f, 0.79f, 1f))
        };

        //Initializing Team Handler
        TeamHandler.Initialize();

        //Initializing Timer Handler
        TimerHandler.Initialize();

        //Initializing Team Selector Handler
        TeamSelectorHandler.Initialize();

        //Character Team Handler
        harmony.PatchAll(typeof(CharacterTeamInfo));
        Log.LogInfo("Character Team Handler Successful");

        harmony.PatchAll(typeof(RespawnPatch));
        Log.LogInfo("Respawn Strategies Successful");

        harmony.PatchAll(typeof(PvpBlowgunPatch));
        harmony.PatchAll(typeof(PvpLuggageLootPatch));
        PvpChestRefreshPatch.Apply(harmony);
        Log.LogInfo("PVP Blowgun Successful");

        //ArmBand
        harmony.PatchAll(typeof(Armband));
        Log.LogInfo("Armband Successful");
        
        //Map Patches
        harmony.PatchAll(typeof(MapPatch));
        Log.LogInfo("Map Patches Successful");

        //Disable Scoutmaster
        harmony.PatchAll(typeof(ScoutmasterPatch));
        Log.LogInfo("Scoutmaster Disabled");

        //Keep completed biomes loaded for racers who are still climbing
        MapTransitionPatch.Apply(harmony);
        Log.LogInfo("Persistent Biome Patch Applied");

        harmony.PatchAll(typeof(LocalBiomeEnvironmentPatch));
        Log.LogInfo("Local Biome Environment Patch Applied");

        harmony.PatchAll(typeof(FinalHazardPatch));
        Log.LogInfo("Personal Final Hazard Patch Applied");

        Log.LogInfo($"Plugin {Name} is loaded!");
    }

    private void Update()
    {
        if (settingsMenu == null || runControlMenu == null)
        {
            return;
        }

        bool inLobby = SceneManager.GetActiveScene().name == "Airport";
        bool isHost = !PhotonNetwork.InRoom || PhotonNetwork.IsMasterClient;
        bool menuKeyPressed = MenuKeyWasPressedThisFrame();

        if (!isHost)
        {
            settingsMenu.CloseMenu();
            runControlMenu.CloseMenu();
            return;
        }

        if (inLobby)
        {
            runControlMenu.CloseMenu();
            if (menuKeyPressed)
            {
                settingsMenu.ToggleMenu();
            }
            return;
        }

        settingsMenu.CloseMenu();
        if (!RunControlMenu.CanHostEndCurrentRun)
        {
            runControlMenu.CloseMenu();
            return;
        }

        if (menuKeyPressed)
        {
            runControlMenu.ToggleMenu();
        }
    }

    private static bool MenuKeyWasPressedThisFrame()
    {
        Key configuredKey = menuKeyConfig?.Value ?? Key.F3;
        return configuredKey != Key.None
            && Keyboard.current != null
            && Keyboard.current[configuredKey].wasPressedThisFrame;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        settingsMenu?.CloseMenu();
        runControlMenu?.CloseMenu();
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        settingsMenu?.CloseMenu();
        runControlMenu?.CloseMenu();
        harmony.UnpatchSelf();
        if (settingsMenuObject != null)
        {
            Destroy(settingsMenuObject);
        }
        if (runControlMenuObject != null)
        {
            Destroy(runControlMenuObject);
        }
        if (systemsObject != null)
        {
            Destroy(systemsObject);
        }
    }
}
