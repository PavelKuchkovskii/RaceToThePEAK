using HarmonyLib;
using Photon.Pun;
using PeakRace.Core;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using Zorro.Core;

namespace PeakRace.Patch;

[HarmonyPatch]
internal class CharacterTeamInfo : MonoBehaviourPunCallbacks
{
    public bool teamOn;
    public bool teamGUIOn;
    public bool timeOn;
    public int teamInt;
    public float time;
    public string timeString;
    public float checkpointRadius;
    private List<Campfire> campfireList;
    private bool checkpointsInitialized;
    public Character myChar;

    [HarmonyPatch(typeof(Character), nameof(Character.Awake))]
    [HarmonyPostfix]
    static void PosPatch(Character __instance)
    {
        __instance.gameObject.AddComponent<CharacterTeamInfo>();
        Debug.Log($"[RaceToThePeak] TeamInfo Object Created for {__instance.name}");
    }

    // Scout flags resolve through TryCheckpoint before RPCA_Die is ever sent.
    // Consequently a successful flag revive reaches neither this penalty nor
    // the custom respawn controller.
    [HarmonyPatch(typeof(Character), nameof(Character.RPCA_Die))]
    [HarmonyPostfix]
    private static void deathTimer(Character __instance)
    {
        CharacterTeamInfo teamHandler = __instance.GetComponent<CharacterTeamInfo>();
        if (teamHandler != null)
        {
            teamHandler.time += RaceSettingsManager.Current.ActivePenaltySeconds;
        }
    }

    void Awake()
    {
        string scene = SceneManager.GetActiveScene().name;
        if (scene == "Title")
        {
            return;
        }

        teamOn = true;
        teamGUIOn = true;

        checkpointRadius = 15;

        //links to Character
        myChar = this.gameObject.GetComponent<Character>();

        //Checks if timer should start
        if (scene == "Airport")
        { timeOn = false; }
        else
        { timeOn = true; }

        //TODO Maybe set an 'onsceneload' for the timer
        //Sets starting timer to 0
        time = 0;
        //Sets initial 0 time clock
        timeString = "00:00:00";

        //Initializes Campfire Checkpoints
        if (scene != "Airport")
        {
            campfireList = new List<Campfire>();
            InitializeCheckpoints();
        }

        //Debug.Log("[RaceToThePeak] TeamInfo Initialized");
    }

    //This is a call to make initial changeTeam call after the Armband Awake so it doesnt throw an error
    public void InitializeTeam()
    {
        changeTeam(TeamHandler.getPlayerTeam(myChar.name));
    }

    private void FixedUpdate()
    {
        string scene = SceneManager.GetActiveScene().name;

        if (timeOn)
        {
            time += Time.fixedDeltaTime;
            timeToString();
        }
        //Debug.Log("[RaceToThePeak] TeamInfo Updated");
        if (scene != "Airport")
        {
            checkpointHandler();
        }
    }

    // Converts time float to timer string
    private void timeToString()
    {
        int hourTime = (int)(time / 3600);
        int minTime = (int)(time % 3600 / 60);
        int secTime = (int)(time % 60);

        timeString =  $"{needZero(hourTime)}:{needZero(minTime)}:{needZero(secTime)}";
    }

    // Determines whether string needs extra 0 for timer format
    private string needZero(int time)
    {
        if (time / 10 < 1)
        {
            return $"0{time}";
        }
        return $"{time}";
    }

    private void checkpointHandler()
    {
        if (!checkpointsInitialized)
        {
            InitializeCheckpoints();
        }

        if (!checkpointsInitialized || campfireList == null)
        {
            return;
        }

        // Checks if in range of one of the campfires
        int idx = 0;
        foreach (Campfire campfire in campfireList)
        {
            if (campfire == null)
            {
                campfireList.RemoveAt(idx);
                return;
            }

            if (Vector3.Distance(campfire.transform.position, myChar.Center) <= checkpointRadius)
            {
                campfireList.RemoveAt(idx);
                // A racer arriving after somebody else advanced must keep timing.
                // Only pause while this racer still has to activate an unlit fire.
                if (!campfire.Lit)
                {
                    timeOn = false;
                    Debug.Log($"[RaceToThePeak] Timer was paused for {myChar.name} at an unlit campfire");
                }
                return;
            }
            idx++;
        }
    
        // PEAK 2.x uses generated/variant biomes, so a hard-coded flag path is
        // not stable. Ask the game's progress handler whether this is the peak.
        MountainProgressHandler progressHandler = Singleton<MountainProgressHandler>.Instance;
        if (progressHandler != null && progressHandler.IsAtPeak(myChar.Center))
        {
            timeOn = false;
            Debug.Log($"[RaceToThePeak] Timer was turned off for {myChar.name} due to reaching the PEAK");
            return;
        }
    }

    private void InitializeCheckpoints()
    {
        if (!MapHandler.Exists)
        {
            return;
        }

        campfireList = Singleton<MapHandler>.Instance
            .GetComponentsInChildren<Campfire>(true)
            .Where(campfire => campfire != null && campfire.advanceToSegment != Segment.Beach)
            .ToList();
        checkpointsInitialized = true;
    }

    // sends new team data to all clients
    public void changeTeam(int newTeam)
    {
        if(!photonView.IsMine)
        { return; }
        photonView.RPC("RPCA_ChangeTeam", RpcTarget.AllBuffered, newTeam);
    }

    [PunRPC]
    public void RPCA_ChangeTeam(int newTeam)
    {
        teamInt = newTeam;
        TeamHandler.addCharacter(myChar.name, newTeam);
        if(myChar.TryGetComponent<Armband>(out Armband armband))
        {
            armband.changeArmband(newTeam);
        }
    }
}
