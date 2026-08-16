using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PeakRace.Patch;

public class TimerUI : MonoBehaviour
{
    private readonly int leaderboardPosX = 23;
    private readonly int leaderboardPosY = -60;
    private readonly int troopPosX = 270;
    private readonly int troopPosY = 10;
    private readonly int clockPosX = 20;
    private readonly int clockPosY = 5;
    private Canvas canvas;
    private TextMeshProUGUI leaderboard;
    private TextMeshProUGUI troop;
    private TextMeshProUGUI clock;
    private static Material shadowMaterial;
    private float nextLeaderboardRefresh;

    private void Awake()
    {
        canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        CanvasScaler scaler = canvas.gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        if (shadowMaterial == null)
        {
            shadowMaterial = Resources.FindObjectsOfTypeAll<Material>()
                .FirstOrDefault(material => material.name == "DarumaDropOne-Regular SDF Shadow");
        }

        if (Plugin.FontAsset == null)
        {
            Plugin.FontAsset = Resources.FindObjectsOfTypeAll<TMP_FontAsset>()
                .FirstOrDefault(font => font.name == "DarumaDropOne-Regular SDF");
        }

        InitializeUI();
    }

    private void InitializeUI()
    {
        leaderboard = CreateText(
            "LeaderboardUI",
            new Vector3(leaderboardPosX, leaderboardPosY),
            string.Empty,
            18,
            Plugin.Color);

        troop = CreateText(
            "TroopUI",
            new Vector3(troopPosX, troopPosY),
            "Troop Null",
            50,
            Plugin.Color);

        clock = CreateText(
            "ClockUI",
            new Vector3(clockPosX, clockPosY),
            "00:00:00",
            46,
            Plugin.Color);

        Debug.Log("[RaceToThePeak] GUI Initialized");
    }

    private TextMeshProUGUI CreateText(
        string objectName,
        Vector3 position,
        string initialText,
        float fontSize,
        Color color)
    {
        GameObject textObject = new(objectName);
        textObject.transform.SetParent(canvas.transform, worldPositionStays: false);
        TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
        text.text = initialText;
        text.font = Plugin.FontAsset;
        if (shadowMaterial != null)
        {
            text.fontMaterial = shadowMaterial;
        }
        text.color = color;
        text.fontSize = fontSize;
        SetupText(text, position);
        return text;
    }

    public static void SetupText(TextMeshProUGUI text, Vector3 anchoredPos)
    {
        RectTransform rectTransform = text.rectTransform;
        rectTransform.anchorMin = new Vector2(0f, 1f);
        rectTransform.anchorMax = new Vector2(0f, 1f);
        rectTransform.pivot = new Vector2(0f, 1f);
        rectTransform.position += anchoredPos;
        rectTransform.sizeDelta = new Vector2(500f, 500f);
        text.alignment = TextAlignmentOptions.TopLeft;
    }

    private void LateUpdate()
    {
        if (Character.localCharacter == null)
        {
            return;
        }

        CharacterTeamInfo teamInfo = Character.localCharacter.GetComponent<CharacterTeamInfo>();
        if (teamInfo == null
            || teamInfo.teamInt < 0
            || teamInfo.teamInt >= Plugin.teamList.Count)
        {
            return;
        }

        troop.text = Plugin.teamList[teamInfo.teamInt].Item1;
        troop.color = Plugin.teamList[teamInfo.teamInt].Item2;

        if (leaderboard.enabled != teamInfo.teamOn)
        {
            leaderboard.enabled = teamInfo.teamOn;
            troop.enabled = teamInfo.teamOn;
            clock.enabled = teamInfo.teamOn;
        }

        clock.text = teamInfo.timeString;
        if (SceneManager.GetActiveScene().name != "Airport"
            && teamInfo.teamGUIOn
            && Time.unscaledTime >= nextLeaderboardRefresh)
        {
            nextLeaderboardRefresh = Time.unscaledTime + 0.25f;
            UpdateScoreboard(teamInfo);
        }
    }

    private void UpdateScoreboard(CharacterTeamInfo yourTeamInfo)
    {
        List<(int team, float time, float height)> standings = Character.AllCharacters
            .Where(character => character != null && !character.isBot)
            .Select(character => character.GetComponent<CharacterTeamInfo>())
            .Where(teamInfo => teamInfo != null
                && teamInfo.teamInt >= 0
                && teamInfo.teamInt < Plugin.teamList.Count)
            .GroupBy(teamInfo => teamInfo.teamInt)
            .Select(team => (
                team: team.Key,
                time: team.Average(member => member.time),
                height: team.Average(GetDisplayHeight)))
            .OrderBy(entry => entry.time)
            .ThenByDescending(entry => entry.height)
            .ThenBy(entry => entry.team)
            .ToList();

        if (standings.Count == 0)
        {
            leaderboard.text = string.Empty;
            return;
        }

        int yourIndex = standings.FindIndex(entry => entry.team == yourTeamInfo.teamInt);
        int visibleCount = Mathf.Min(5, standings.Count);
        List<string> rows = new();

        if (yourIndex >= 0 && yourIndex < visibleCount)
        {
            for (int index = 0; index < visibleCount; index++)
            {
                rows.Add(FormatStanding(standings[index], index, index == yourIndex));
            }
        }
        else if (yourIndex >= 0)
        {
            rows.Add(FormatStanding(standings[0], 0, false));
            if (standings.Count > 1)
            {
                rows.Add(FormatStanding(standings[1], 1, false));
            }
            rows.Add("- - - - - - - - - -");
            rows.Add(FormatStanding(standings[yourIndex - 1], yourIndex - 1, false));
            rows.Add(FormatStanding(standings[yourIndex], yourIndex, true));
        }
        else
        {
            for (int index = 0; index < visibleCount; index++)
            {
                rows.Add(FormatStanding(standings[index], index, false));
            }
        }

        leaderboard.text = string.Join("\n", rows);
    }

    private static float GetDisplayHeight(CharacterTeamInfo teamInfo)
    {
        Character character = teamInfo.myChar;
        if (character == null)
        {
            return 0f;
        }

        float units = character.data != null && character.data.dead
            ? character.LastLivingPosition.y
            : character.HipPos().y;
        return Mathf.Max(0f, CharacterStats.UnitsToMeters(units));
    }

    private string FormatStanding(
        (int team, float time, float height) standing,
        int zeroBasedPlace,
        bool isYourTeam)
    {
        Color color = Plugin.teamList[standing.team].Item2;
        string colorText = ColorUtility.ToHtmlStringRGBA(color);
        string row = $"<color=#{colorText}>{Ordinal(zeroBasedPlace + 1)} "
            + $"{TimeToString(standing.time)} • {Mathf.RoundToInt(standing.height)}m</color>";
        return isYourTeam ? row + " (You)" : row;
    }

    private static string Ordinal(int place)
    {
        int lastTwo = place % 100;
        if (lastTwo is >= 11 and <= 13)
        {
            return $"{place}th";
        }

        return (place % 10) switch
        {
            1 => $"{place}st",
            2 => $"{place}nd",
            3 => $"{place}rd",
            _ => $"{place}th"
        };
    }

    private static string TimeToString(float time)
    {
        int hours = (int)(time / 3600);
        int minutes = (int)(time % 3600 / 60);
        int seconds = (int)(time % 60);
        return $"{WithLeadingZero(hours)}:{WithLeadingZero(minutes)}:{WithLeadingZero(seconds)}";
    }

    private static string WithLeadingZero(int time)
    {
        return time < 10 ? $"0{time}" : time.ToString();
    }
}
