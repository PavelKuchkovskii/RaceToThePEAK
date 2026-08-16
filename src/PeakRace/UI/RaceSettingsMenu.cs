using PeakRace.Core;
using UnityEngine;

namespace PeakRace.UI;

/// <summary>
/// Compact host settings panel opened by the configured menu key in the Airport.
/// </summary>
internal sealed class RaceSettingsMenu : MenuWindow
{
    private const int PanelWidth = 510;
    private const int PanelHeight = 550;
    private const int Padding = 14;
    private const int RowHeight = 44;

    private Texture2D whiteTexture;
    private GUIStyle titleStyle;
    private GUIStyle rowStyle;
    private GUIStyle hintStyle;

    internal bool IsVisible { get; private set; }

    private void Awake()
    {
        DontDestroyOnLoad(gameObject);
        StartClosed();
    }

    internal void ToggleMenu()
    {
        if (IsVisible || isOpen)
        {
            CloseMenu();
            return;
        }

        IsVisible = true;
        Open();
    }

    internal void CloseMenu()
    {
        IsVisible = false;
        if (!isOpen && !inputActive && !MenuWindow.AllActiveWindows.Contains(this))
        {
            return;
        }

        Close();
    }

    private void EnsureStyles()
    {
        if (whiteTexture == null)
        {
            whiteTexture = new Texture2D(1, 1);
            whiteTexture.SetPixel(0, 0, Color.white);
            whiteTexture.Apply();
        }

        titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 22,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft
        };
        rowStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 16,
            alignment = TextAnchor.MiddleLeft
        };
        hintStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 14,
            alignment = TextAnchor.MiddleLeft,
            wordWrap = true
        };
    }

    private void OnGUI()
    {
        if (!IsVisible || RaceSettingsManager.Instance == null)
        {
            return;
        }

        EnsureStyles();
        RaceSettingsSnapshot settings = RaceSettingsManager.Current;
        RaceSettingsManager manager = RaceSettingsManager.Instance;
        float x = Mathf.Max(20f, Screen.width - PanelWidth - 20f);
        Rect panel = new(x, 20f, PanelWidth, PanelHeight);

        GUI.color = new Color(0f, 0f, 0f, 0.82f);
        GUI.DrawTexture(panel, whiteTexture);
        GUI.color = Color.white;

        float y = panel.y + Padding;
        GUI.Label(
            new Rect(panel.x + Padding, y, panel.width - Padding * 2, 32f),
            $"Race to the PEAK Settings | v{Plugin.Version}",
            titleStyle);
        y += 42f;

        GUI.enabled = manager.CanEditLobbySettings;
        if (GUI.Button(
            new Rect(panel.x + Padding, y, panel.width - Padding * 2, RowHeight),
            $"Respawn mode: {ModeLabel(settings.Mode)}",
            GUI.skin.button))
        {
            RespawnMode next = (RespawnMode)(((int)settings.Mode + 1) % 4);
            manager.SetMode(next);
        }
        y += RowHeight + 5f;

        if (GUI.Button(
            new Rect(panel.x + Padding, y, panel.width - Padding * 2, RowHeight),
            $"PVP real death: {PvpDeathModeLabel(settings.PvpDeathRespawn)}",
            GUI.skin.button))
        {
            manager.CyclePvpDeathRespawn();
        }
        y += RowHeight + 5f;

        DrawNumberRow(
            panel,
            ref y,
            "Next campfire penalty",
            settings.NextCampfirePenaltyMinutes,
            "min",
            () => manager.AdjustPenalty(RespawnMode.NextCampfire, -1),
            () => manager.AdjustPenalty(RespawnMode.NextCampfire, 1));
        DrawNumberRow(
            panel,
            ref y,
            "Corpse timer penalty",
            settings.CorpsePenaltyMinutes,
            "min",
            () => manager.AdjustPenalty(RespawnMode.CorpseTimer, -1),
            () => manager.AdjustPenalty(RespawnMode.CorpseTimer, 1));
        DrawNumberRow(
            panel,
            ref y,
            "Previous campfire penalty",
            settings.PreviousCampfirePenaltyMinutes,
            "min",
            () => manager.AdjustPenalty(RespawnMode.PreviousCampfire, -1),
            () => manager.AdjustPenalty(RespawnMode.PreviousCampfire, 1));
        DrawNumberRow(
            panel,
            ref y,
            "PVP death penalty",
            settings.PvpPenaltyMinutes,
            "min",
            () => manager.AdjustPenalty(RespawnMode.Pvp, -1),
            () => manager.AdjustPenalty(RespawnMode.Pvp, 1));
        DrawNumberRow(
            panel,
            ref y,
            "Corpse respawn delay",
            settings.CorpseRespawnDelaySeconds,
            "sec",
            () => manager.AdjustCorpseDelay(-5),
            () => manager.AdjustCorpseDelay(5));

        if (GUI.Button(
            new Rect(panel.x + Padding, y, panel.width - Padding * 2, RowHeight),
            $"PVP chest refresh: {(settings.PvpChestRefreshEnabled ? "ON" : "OFF")}",
            GUI.skin.button))
        {
            manager.TogglePvpChestRefresh();
        }
        y += RowHeight + 5f;

        bool settingsEditable = GUI.enabled;
        GUI.enabled = settingsEditable && settings.PvpChestRefreshEnabled;
        DrawNumberRow(
            panel,
            ref y,
            "PVP chest refresh time",
            settings.PvpChestRefreshSeconds,
            "sec",
            () => manager.AdjustPvpChestRefreshSeconds(-30),
            () => manager.AdjustPvpChestRefreshSeconds(30));
        GUI.enabled = settingsEditable;
        GUI.enabled = true;

        string hint = manager.CanEditLobbySettings
            ? $"{Plugin.MenuKeyDisplayName}: close • Settings are synced by the host and locked after leaving the Airport."
            : $"Read only • Only the lobby host can change these settings. {Plugin.MenuKeyDisplayName}: close.";
        GUI.Label(
            new Rect(panel.x + Padding, panel.yMax - 38f, panel.width - Padding * 2, 30f),
            hint,
            hintStyle);
    }

    private void DrawNumberRow(
        Rect panel,
        ref float y,
        string label,
        int value,
        string suffix,
        System.Action decrease,
        System.Action increase)
    {
        const float buttonWidth = 38f;
        Rect row = new(panel.x + Padding, y, panel.width - Padding * 2, RowHeight);
        GUI.Box(row, GUIContent.none);
        GUI.Label(
            new Rect(row.x + 10f, row.y, row.width - 150f, row.height),
            label,
            rowStyle);

        float controlsX = row.xMax - 140f;
        if (GUI.Button(new Rect(controlsX, row.y + 6f, buttonWidth, row.height - 12f), "−"))
        {
            decrease();
        }
        GUI.Label(
            new Rect(controlsX + buttonWidth, row.y, 64f, row.height),
            $"{value} {suffix}",
            rowStyle);
        if (GUI.Button(new Rect(row.xMax - buttonWidth, row.y + 6f, buttonWidth, row.height - 12f), "+"))
        {
            increase();
        }

        y += RowHeight + 5f;
    }

    private static string ModeLabel(RespawnMode mode)
    {
        return mode switch
        {
            RespawnMode.CorpseTimer => "Timed at corpse",
            RespawnMode.PreviousCampfire => "Immediate at previous campfire",
            RespawnMode.Pvp => "PVP",
            _ => "When next campfire is lit"
        };
    }

    private static string PvpDeathModeLabel(PvpDeathRespawnMode mode)
    {
        return mode switch
        {
            PvpDeathRespawnMode.CorpseTimer => "Timed at corpse",
            PvpDeathRespawnMode.NextCampfire => "Next campfire",
            _ => "Previous campfire"
        };
    }

    private new void OnDestroy()
    {
        MenuWindow.AllActiveWindows.Remove(this);
        if (whiteTexture != null)
        {
            Destroy(whiteTexture);
        }
    }
}
