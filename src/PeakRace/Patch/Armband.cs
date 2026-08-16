using HarmonyLib;
using PeakRace.src;
using UnityEngine;

namespace PeakRace.Patch;

[HarmonyPatch]
internal class Armband : MonoBehaviour
{
    int teamInt = 1;
    public Character myChar;
    public GameObject myArmband;
    public Renderer renderer;


    [HarmonyPatch(typeof(HideTheBody), nameof(HideTheBody.Start))]
    [HarmonyPostfix]
    static void PosPatch(HideTheBody __instance)
    {

        // Loads Character shader
        if (TeamHandler.CharShader == null)
        {
            TeamHandler.findShader();
        }
        Character character = __instance.GetComponentInParent<Character>();
        character.gameObject.AddComponent<Armband>();
        Debug.Log($"[RaceToThePeak] Armband Created for {character.name}");
    }

    void Awake()
    {
        //links to Character
        myChar = this.gameObject.GetComponent<Character>();

        teamInt = TeamHandler.getPlayerTeam(myChar.name);

        Rigidbody arm  = myChar.GetBodypartRig(BodypartType.Arm_L);

        //Spawns Armband
        //Local Position: -0.0276 0.4798 0.0064
        //Rotation      : 89.5015 303.6035 130.6423
        //Rotation Quat : -0.4857 0.7043 0.4976 0.143
        //Scale         : 46 46 46
        myArmband = Instantiate(TeamHandler.baseArmband, new Vector3(-0.0276f, 0.4798f, 0.0064f), Quaternion.identity);
        myArmband.transform.SetParent(arm.transform, worldPositionStays: false);
        myArmband.transform.localScale = new Vector3(2.33f, 2.33f, 2.33f);
        myArmband.transform.localEulerAngles = new Vector3(89.5015f, 303.6035f, 130.6423f);
        myArmband.name = "TeamArmband";
        renderer = myArmband.GetComponent<Renderer>();
        renderer.material = TeamHandler.TroopMat[teamInt];

        Debug.Log($"[RaceToThePeak] Finished Armband Load Step");

        myChar.GetComponent<CharacterTeamInfo>().InitializeTeam();
    }

    [HarmonyPatch(typeof(HideTheBody), nameof(HideTheBody.Toggle))]
    [HarmonyPostfix]
    static void wornOnPlayer(HideTheBody __instance, bool show)
    {

        if (!__instance.character.gameObject.TryGetComponent<Armband>(out Armband armband))
        {
            Debug.Log($"[RaceToThePeak] Armband hasnt been built yet");
            return;
        }
        Renderer armRender = armband.renderer;
        if (show)
        {
            __instance.SetShowing(armRender, 0f);
        }
        else
        {
            __instance.SetShowing(armRender, 1f);
        }
    }

    public void changeArmband(int team)
    {
        teamInt = team;
        renderer.material = TeamHandler.TroopMat[team];
        myChar.refs.hideTheBody.Refresh();
    }
}
