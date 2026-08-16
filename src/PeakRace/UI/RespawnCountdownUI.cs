using PeakRace.Core;
using PeakRace.Patch;
using Photon.Pun;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PeakRace.UI;

/// <summary>
/// Non-interactive countdown HUD for corpse-timer respawns. The local player's
/// card stays below the center of view; teammate cards use a compact right-side
/// stack so climbing visibility and controls remain unobstructed.
/// </summary>
internal sealed class RespawnCountdownUI : MonoBehaviour
{
    private const int MaxTeammateCards = 6;
    private readonly List<RaceRespawnController.Countdown> countdowns = new();
    private Texture2D whiteTexture;
    private GUIStyle captionStyle;
    private GUIStyle localTimerStyle;
    private GUIStyle teammateNameStyle;
    private GUIStyle teammateTimerStyle;
    private GUIStyle overflowStyle;
    private float nextCollectionTime;

    private void Awake()
    {
        DontDestroyOnLoad(gameObject);
    }

    private void Update()
    {
        if (Time.unscaledTime < nextCollectionTime)
        {
            return;
        }

        nextCollectionTime = Time.unscaledTime + 0.1f;
        if (!RaceSettingsManager.Current.UsesCorpseTimerRespawn
            || RaceRespawnController.Instance == null
            || SceneManager.GetActiveScene().name is "Airport" or "Title")
        {
            countdowns.Clear();
            return;
        }

        RaceRespawnController.Instance.CollectCountdowns(countdowns);
        countdowns.Sort((left, right) => left.DueAt.CompareTo(right.DueAt));
    }

    private void OnGUI()
    {
        if (countdowns.Count == 0 || Character.localCharacter == null)
        {
            return;
        }

        EnsureResources();
        int previousDepth = GUI.depth;
        GUI.depth = -500;

        Character localCharacter = Character.localCharacter;
        CharacterTeamInfo localTeamInfo = localCharacter.GetComponent<CharacterTeamInfo>();
        int localTeam = localTeamInfo != null ? localTeamInfo.teamInt : -1;
        Color localColor = GetTeamColor(localTeam);

        DrawLocalCountdown(localCharacter, localColor);
        DrawTeammateCountdowns(localCharacter, localTeam, localColor);

        GUI.color = Color.white;
        GUI.depth = previousDepth;
    }

    private void DrawLocalCountdown(Character localCharacter, Color teamColor)
    {
        int localViewId = localCharacter.photonView.ViewID;
        foreach (RaceRespawnController.Countdown countdown in countdowns)
        {
            if (countdown.Character == null
                || countdown.Character.photonView.ViewID != localViewId
                || countdown.Character.data == null
                || !countdown.Character.data.dead)
            {
                continue;
            }

            float width = Mathf.Min(340f, Screen.width - 30f);
            Rect card = new((Screen.width - width) * 0.5f, Screen.height - 112f, width, 62f);
            float remaining = GetRemainingSeconds(countdown);
            DrawCardBackground(card, teamColor, remaining, countdown.TotalDelaySeconds, 5f);

            GUI.color = new Color(teamColor.r, teamColor.g, teamColor.b, 1f);
            GUI.Label(new Rect(card.x + 12f, card.y + 4f, card.width - 24f, 20f), "RESPAWN IN", captionStyle);
            GUI.color = Color.white;
            GUI.Label(
                new Rect(card.x + 12f, card.y + 19f, card.width - 24f, 35f),
                FormatTime(remaining),
                localTimerStyle);
            return;
        }
    }

    private void DrawTeammateCountdowns(
        Character localCharacter,
        int localTeam,
        Color teamColor)
    {
        if (localTeam < 0)
        {
            return;
        }

        int localViewId = localCharacter.photonView.ViewID;
        int visible = 0;
        int totalTeammatesWaiting = 0;
        float cardWidth = Mathf.Min(260f, Screen.width * 0.32f);
        float x = Screen.width - cardWidth - 18f;
        float y = 92f;

        foreach (RaceRespawnController.Countdown countdown in countdowns)
        {
            Character character = countdown.Character;
            if (character == null
                || character.data == null
                || !character.data.dead
                || character.photonView.ViewID == localViewId)
            {
                continue;
            }

            CharacterTeamInfo teamInfo = character.GetComponent<CharacterTeamInfo>();
            if (teamInfo == null || teamInfo.teamInt != localTeam)
            {
                continue;
            }

            totalTeammatesWaiting++;
            if (visible >= MaxTeammateCards)
            {
                continue;
            }

            Rect card = new(x, y + visible * 38f, cardWidth, 32f);
            float remaining = GetRemainingSeconds(countdown);
            DrawCardBackground(card, teamColor, remaining, countdown.TotalDelaySeconds, 3f);

            GUI.color = Color.white;
            GUI.Label(
                new Rect(card.x + 12f, card.y + 1f, card.width - 88f, card.height - 4f),
                character.characterName,
                teammateNameStyle);
            GUI.color = new Color(teamColor.r, teamColor.g, teamColor.b, 1f);
            GUI.Label(
                new Rect(card.xMax - 82f, card.y + 1f, 70f, card.height - 4f),
                FormatTime(remaining),
                teammateTimerStyle);
            visible++;
        }

        int hidden = totalTeammatesWaiting - visible;
        if (hidden > 0)
        {
            Rect overflow = new(x, y + visible * 38f, cardWidth, 24f);
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(overflow, whiteTexture);
            GUI.color = new Color(teamColor.r, teamColor.g, teamColor.b, 1f);
            GUI.Label(overflow, $"+{hidden} teammate(s) waiting", overflowStyle);
        }
    }

    private void DrawCardBackground(
        Rect card,
        Color teamColor,
        float remaining,
        int totalDelaySeconds,
        float accentWidth)
    {
        GUI.color = new Color(0.025f, 0.025f, 0.025f, 0.72f);
        GUI.DrawTexture(card, whiteTexture);

        GUI.color = new Color(teamColor.r, teamColor.g, teamColor.b, 0.13f);
        GUI.DrawTexture(new Rect(card.x + accentWidth, card.y, card.width - accentWidth, card.height), whiteTexture);

        GUI.color = new Color(teamColor.r, teamColor.g, teamColor.b, 1f);
        GUI.DrawTexture(new Rect(card.x, card.y, accentWidth, card.height), whiteTexture);

        float progress = totalDelaySeconds <= 0
            ? 0f
            : Mathf.Clamp01(remaining / totalDelaySeconds);
        GUI.color = new Color(0f, 0f, 0f, 0.75f);
        GUI.DrawTexture(new Rect(card.x, card.yMax - 3f, card.width, 3f), whiteTexture);
        GUI.color = new Color(teamColor.r, teamColor.g, teamColor.b, 0.95f);
        GUI.DrawTexture(new Rect(card.x, card.yMax - 3f, card.width * progress, 3f), whiteTexture);
    }

    private static float GetRemainingSeconds(RaceRespawnController.Countdown countdown)
    {
        return Mathf.Max(0f, (float)(countdown.DueAt - PhotonNetwork.Time));
    }

    private static string FormatTime(float seconds)
    {
        if (seconds < 10f)
        {
            return $"00:{seconds:00.0}";
        }

        int wholeSeconds = Mathf.CeilToInt(seconds);
        int minutes = wholeSeconds / 60;
        int remainingSeconds = wholeSeconds % 60;
        return $"{minutes:00}:{remainingSeconds:00}";
    }

    private static Color GetTeamColor(int team)
    {
        return team >= 0 && team < Plugin.teamList.Count
            ? Plugin.teamList[team].Item2
            : Plugin.Color;
    }

    private void EnsureResources()
    {
        if (whiteTexture == null)
        {
            whiteTexture = new Texture2D(1, 1);
            whiteTexture.SetPixel(0, 0, Color.white);
            whiteTexture.Apply();
        }

        captionStyle ??= CreateStyle(13, TextAnchor.MiddleCenter, FontStyle.Bold);
        localTimerStyle ??= CreateStyle(28, TextAnchor.MiddleCenter, FontStyle.Bold);
        teammateNameStyle ??= CreateStyle(15, TextAnchor.MiddleLeft, FontStyle.Normal);
        teammateTimerStyle ??= CreateStyle(17, TextAnchor.MiddleRight, FontStyle.Bold);
        overflowStyle ??= CreateStyle(13, TextAnchor.MiddleCenter, FontStyle.Normal);
    }

    private static GUIStyle CreateStyle(int fontSize, TextAnchor alignment, FontStyle fontStyle)
    {
        return new GUIStyle(GUI.skin.label)
        {
            fontSize = fontSize,
            alignment = alignment,
            fontStyle = fontStyle,
            clipping = TextClipping.Clip
        };
    }

    private void OnDestroy()
    {
        if (whiteTexture != null)
        {
            Destroy(whiteTexture);
        }
    }
}
