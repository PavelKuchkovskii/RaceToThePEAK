using PeakRace.Core;
using System;
using UnityEngine;

namespace PeakRace.UI;

/// <summary>
/// Scrollable host-only lobby editor. Choices are validated independently so
/// selecting one rule never silently rewrites another rule.
/// </summary>
internal sealed class RaceSettingsMenu : MenuWindow
{
    private const float PreferredPanelWidth = 680f;
    private const float PreferredPanelHeight = 820f;
    private const float MinimumPanelWidth = 380f;
    private const float MinimumPanelHeight = 420f;
    private const float Padding = 16f;
    private const float ChoiceHeight = 38f;
    private const float RowHeight = 44f;

    private Texture2D whiteTexture;
    private GUIStyle titleStyle;
    private GUIStyle sectionStyle;
    private GUIStyle labelStyle;
    private GUIStyle bodyStyle;
    private GUIStyle warningStyle;
    private GUIStyle summaryStyle;
    private Vector2 scrollPosition;
    private float measuredContentHeight = 1200f;

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

        titleStyle ??= new GUIStyle(GUI.skin.label)
        {
            fontSize = 22,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft
        };
        sectionStyle ??= new GUIStyle(GUI.skin.label)
        {
            fontSize = 18,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft
        };
        labelStyle ??= new GUIStyle(GUI.skin.label)
        {
            fontSize = 15,
            alignment = TextAnchor.MiddleLeft
        };
        bodyStyle ??= new GUIStyle(GUI.skin.label)
        {
            fontSize = 13,
            alignment = TextAnchor.UpperLeft,
            wordWrap = true
        };
        warningStyle ??= new GUIStyle(bodyStyle)
        {
            fontStyle = FontStyle.Bold
        };
        warningStyle.normal.textColor = new Color(1f, 0.72f, 0.28f, 1f);
        summaryStyle ??= new GUIStyle(bodyStyle)
        {
            fontSize = 14,
            padding = new RectOffset(10, 10, 8, 8)
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

        float availableWidth = Mathf.Max(1f, Screen.width - 40f);
        float availableHeight = Mathf.Max(1f, Screen.height - 40f);
        float panelWidth = Mathf.Min(
            PreferredPanelWidth,
            Mathf.Max(MinimumPanelWidth, availableWidth));
        float panelHeight = Mathf.Min(
            PreferredPanelHeight,
            Mathf.Max(MinimumPanelHeight, availableHeight));
        panelWidth = Mathf.Min(panelWidth, availableWidth);
        panelHeight = Mathf.Min(panelHeight, availableHeight);

        Rect panel = new(
            Mathf.Max(20f, Screen.width - panelWidth - 20f),
            20f,
            panelWidth,
            panelHeight);
        DrawPanelBackground(panel);

        GUI.Label(
            new Rect(panel.x + Padding, panel.y + 10f, panel.width - Padding * 2f, 34f),
            $"Race to the PEAK rules | v{Plugin.Version}",
            titleStyle);

        const float footerHeight = 42f;
        Rect viewport = new(
            panel.x + Padding,
            panel.y + 50f,
            panel.width - Padding * 2f,
            panel.height - 50f - footerHeight);
        float viewWidth = Mathf.Max(280f, viewport.width - 18f);
        scrollPosition = GUI.BeginScrollView(
            viewport,
            scrollPosition,
            new Rect(0f, 0f, viewWidth, Mathf.Max(viewport.height, measuredContentHeight)));

        bool editable = manager.CanEditLobbySettings;
        GUI.enabled = editable;
        float y = 0f;
        DrawProgressionSection(manager, settings, viewWidth, ref y);
        DrawRespawnSection(manager, settings, viewWidth, ref y);
        DrawPvpSection(manager, settings, viewWidth, ref y);
        GUI.enabled = true;
        DrawCurrentRulesSection(settings, viewWidth, ref y);
        measuredContentHeight = y + 12f;
        GUI.EndScrollView();

        string footer = editable
            ? $"{Plugin.MenuKeyDisplayName}: close  •  Host settings sync to all players and lock when the run starts."
            : $"Read only  •  Only the lobby host can edit rules.  {Plugin.MenuKeyDisplayName}: close.";
        GUI.Label(
            new Rect(
                panel.x + Padding,
                panel.yMax - footerHeight + 4f,
                panel.width - Padding * 2f,
                footerHeight - 6f),
            footer,
            bodyStyle);
    }

    private void DrawProgressionSection(
        RaceSettingsManager manager,
        RaceSettingsSnapshot settings,
        float width,
        ref float y)
    {
        DrawSectionTitle("PROGRESSION", width, ref y);
        DrawBody(
            "Choose who must satisfy a campfire before crossing into the next biome. "
            + "Disconnected players and bots are ignored; an unconscious scout is still living and must be brought into range.",
            width,
            ref y,
            58f);

        CampfireWaitMode[] options =
        {
            CampfireWaitMode.Nobody,
            CampfireWaitMode.Team,
            CampfireWaitMode.Lobby
        };
        string[] labels = { "Nobody", "Team", "Lobby" };
        DrawChoiceRow(
            options,
            labels,
            settings.WaitMode,
            manager.CanSelectWaitMode,
            manager.SetWaitMode,
            width,
            ref y);

        DrawBody(WaitModeDescription(settings.WaitMode), width, ref y, 64f);
        if (!manager.CanSelectWaitMode(CampfireWaitMode.Nobody))
        {
            DrawWarning(
                "Nobody is unavailable while a selected death rule uses Next campfire. "
                + "That combination could push a player or team through an unearned biome.",
                width,
                ref y,
                50f);
        }
        else if (settings.WaitMode == CampfireWaitMode.Nobody)
        {
            DrawWarning(
                "Final rising lava/Gloom is per player in this mode. "
                + "Next-campfire respawning is intentionally unavailable.",
                width,
                ref y,
                45f);
        }

        DrawDivider(width, ref y);
    }

    private void DrawRespawnSection(
        RaceSettingsManager manager,
        RaceSettingsSnapshot settings,
        float width,
        ref float y)
    {
        DrawSectionTitle("RESPAWN", width, ref y);
        RespawnMode[] options =
        {
            RespawnMode.NextCampfire,
            RespawnMode.CorpseTimer,
            RespawnMode.PreviousCampfire,
            RespawnMode.Pvp
        };
        string[] labels = { "Next fire", "Corpse timer", "Previous fire", "PVP" };
        DrawChoiceGrid(
            options,
            labels,
            settings.Mode,
            manager.CanSelectRespawnMode,
            manager.SetMode,
            width,
            ref y);

        DrawBody(RespawnDescription(settings.Mode), width, ref y, 62f);
        if (!manager.CanSelectRespawnMode(RespawnMode.NextCampfire))
        {
            DrawWarning(
                "Next fire is disabled when progression waits for nobody.",
                width,
                ref y,
                28f);
        }

        DrawNumberRow(
            width,
            ref y,
            "Death penalty",
            settings.ActivePenaltyMinutes,
            "min",
            () => manager.AdjustPenalty(settings.Mode, -1),
            () => manager.AdjustPenalty(settings.Mode, 1));

        if (settings.Mode == RespawnMode.CorpseTimer)
        {
            DrawNumberRow(
                width,
                ref y,
                "Respawn delay",
                settings.CorpseRespawnDelaySeconds,
                "sec",
                () => manager.AdjustCorpseDelay(-5),
                () => manager.AdjustCorpseDelay(5));
        }

        DrawBody(
            "Checkpoint flags keep priority. A successful flag revive never adds this penalty.",
            width,
            ref y,
            34f);
        DrawDivider(width, ref y);
    }

    private void DrawPvpSection(
        RaceSettingsManager manager,
        RaceSettingsSnapshot settings,
        float width,
        ref float y)
    {
        DrawSectionTitle("PVP", width, ref y);
        if (settings.Mode != RespawnMode.Pvp)
        {
            DrawBody(
                "PVP controls are hidden until PVP is selected as the respawn mode.",
                width,
                ref y,
                38f);
            DrawDivider(width, ref y);
            return;
        }

        DrawBody(
            "The crimson blowgun always sends its knocked-out victim to the previous campfire immediately and adds no penalty. "
            + "The setting below applies only to a real skeleton death.",
            width,
            ref y,
            58f);

        PvpDeathRespawnMode[] options =
        {
            PvpDeathRespawnMode.PreviousCampfire,
            PvpDeathRespawnMode.CorpseTimer,
            PvpDeathRespawnMode.NextCampfire
        };
        string[] labels = { "Previous fire", "Corpse timer", "Next fire" };
        DrawChoiceRow(
            options,
            labels,
            settings.PvpDeathRespawn,
            manager.CanSelectPvpDeathRespawn,
            manager.SetPvpDeathRespawn,
            width,
            ref y);

        if (!manager.CanSelectPvpDeathRespawn(PvpDeathRespawnMode.NextCampfire))
        {
            DrawWarning(
                "Next fire is disabled when progression waits for nobody.",
                width,
                ref y,
                28f);
        }

        if (settings.PvpDeathRespawn == PvpDeathRespawnMode.CorpseTimer)
        {
            DrawNumberRow(
                width,
                ref y,
                "Corpse respawn delay",
                settings.CorpseRespawnDelaySeconds,
                "sec",
                () => manager.AdjustCorpseDelay(-5),
                () => manager.AdjustCorpseDelay(5));
        }

        if (GUI.Button(
            new Rect(0f, y, width, RowHeight),
            $"Refresh opened luggage: {(settings.PvpChestRefreshEnabled ? "ON" : "OFF")}"))
        {
            manager.TogglePvpChestRefresh();
        }
        y += RowHeight + 6f;

        if (settings.PvpChestRefreshEnabled)
        {
            DrawNumberRow(
                width,
                ref y,
                "Luggage refresh delay",
                settings.PvpChestRefreshSeconds,
                "sec",
                () => manager.AdjustPvpChestRefreshSeconds(-30),
                () => manager.AdjustPvpChestRefreshSeconds(30));
        }

        DrawDivider(width, ref y);
    }

    private void DrawCurrentRulesSection(
        RaceSettingsSnapshot settings,
        float width,
        ref float y)
    {
        DrawSectionTitle("CURRENT RULES", width, ref y);
        string summary =
            $"Progression: {WaitModeLabel(settings.WaitMode)}\n"
            + $"Respawn: {ModeLabel(settings.Mode)} ({settings.ActivePenaltyMinutes} min penalty)\n"
            + $"Final hazard: {HazardScopeLabel(settings.WaitMode)}\n"
            + $"Old biomes: {RetentionLabel(settings)}";
        float summaryHeight = summaryStyle.CalcHeight(new GUIContent(summary), width - 20f) + 16f;
        GUI.Box(new Rect(0f, y, width, summaryHeight), GUIContent.none);
        GUI.Label(new Rect(0f, y, width, summaryHeight), summary, summaryStyle);
        y += summaryHeight + 8f;
    }

    private void DrawChoiceRow<T>(
        T[] options,
        string[] labels,
        T selected,
        Func<T, bool> canSelect,
        Func<T, bool> select,
        float width,
        ref float y)
        where T : struct, Enum
    {
        float gap = 6f;
        float buttonWidth = (width - gap * (options.Length - 1)) / options.Length;
        for (int index = 0; index < options.Length; index++)
        {
            T option = options[index];
            DrawChoiceButton(
                new Rect(index * (buttonWidth + gap), y, buttonWidth, ChoiceHeight),
                labels[index],
                option.Equals(selected),
                canSelect(option),
                () => select(option));
        }
        y += ChoiceHeight + 8f;
    }

    private void DrawChoiceGrid<T>(
        T[] options,
        string[] labels,
        T selected,
        Func<T, bool> canSelect,
        Func<T, bool> select,
        float width,
        ref float y)
        where T : struct, Enum
    {
        const float gap = 6f;
        float buttonWidth = (width - gap) * 0.5f;
        for (int index = 0; index < options.Length; index++)
        {
            T option = options[index];
            int column = index % 2;
            int row = index / 2;
            DrawChoiceButton(
                new Rect(
                    column * (buttonWidth + gap),
                    y + row * (ChoiceHeight + gap),
                    buttonWidth,
                    ChoiceHeight),
                labels[index],
                option.Equals(selected),
                canSelect(option),
                () => select(option));
        }
        y += ChoiceHeight * 2f + gap + 8f;
    }

    private static void DrawChoiceButton(
        Rect rect,
        string label,
        bool selected,
        bool compatible,
        Action onClick)
    {
        bool previousEnabled = GUI.enabled;
        Color previousBackground = GUI.backgroundColor;
        GUI.enabled = previousEnabled && compatible;
        if (selected)
        {
            GUI.backgroundColor = new Color(0.28f, 0.68f, 0.42f, 1f);
        }

        if (GUI.Button(rect, selected ? $"✓ {label}" : label))
        {
            onClick();
        }

        GUI.backgroundColor = previousBackground;
        GUI.enabled = previousEnabled;
    }

    private void DrawNumberRow(
        float width,
        ref float y,
        string label,
        int value,
        string suffix,
        Action decrease,
        Action increase)
    {
        const float buttonWidth = 42f;
        Rect row = new(0f, y, width, RowHeight);
        GUI.Box(row, GUIContent.none);
        GUI.Label(new Rect(10f, y, width - 170f, RowHeight), label, labelStyle);

        float controlsX = width - 154f;
        if (GUI.Button(
            new Rect(controlsX, y + 5f, buttonWidth, RowHeight - 10f),
            "−"))
        {
            decrease();
        }
        GUI.Label(
            new Rect(controlsX + buttonWidth + 4f, y, 64f, RowHeight),
            $"{value} {suffix}",
            labelStyle);
        if (GUI.Button(
            new Rect(width - buttonWidth, y + 5f, buttonWidth, RowHeight - 10f),
            "+"))
        {
            increase();
        }
        y += RowHeight + 6f;
    }

    private void DrawSectionTitle(string title, float width, ref float y)
    {
        GUI.Label(new Rect(0f, y, width, 28f), title, sectionStyle);
        y += 30f;
    }

    private void DrawBody(
        string text,
        float width,
        ref float y,
        float minimumHeight)
    {
        float height = Mathf.Max(minimumHeight, bodyStyle.CalcHeight(new GUIContent(text), width));
        GUI.Label(new Rect(0f, y, width, height), text, bodyStyle);
        y += height + 6f;
    }

    private void DrawWarning(
        string text,
        float width,
        ref float y,
        float minimumHeight)
    {
        float height = Mathf.Max(
            minimumHeight,
            warningStyle.CalcHeight(new GUIContent(text), width));
        GUI.Label(new Rect(0f, y, width, height), text, warningStyle);
        y += height + 6f;
    }

    private static void DrawDivider(float width, ref float y)
    {
        GUI.Box(new Rect(0f, y + 5f, width, 1f), GUIContent.none);
        y += 18f;
    }

    private void DrawPanelBackground(Rect panel)
    {
        Color previousColor = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.88f);
        GUI.DrawTexture(panel, whiteTexture);
        GUI.color = previousColor;
    }

    private static string WaitModeDescription(CampfireWaitMode mode)
    {
        return mode switch
        {
            CampfireWaitMode.Team =>
                "Each team unlocks a loaded transition independently. One living teammate must be at the fire; every other connected teammate must be in range or actually dead.",
            CampfireWaitMode.Lobby =>
                "The fire activates only when every connected living player is in range. Loading, access, and the final rising hazard remain global.",
            _ =>
                "The first living racer activates the fire, loads the next biome for everyone, and nobody else needs to claim that transition."
        };
    }

    private static string RespawnDescription(RespawnMode mode)
    {
        return mode switch
        {
            RespawnMode.CorpseTimer =>
                "After real death, wait for the configured timer and revive at your own remains.",
            RespawnMode.PreviousCampfire =>
                "After real death, revive immediately at the last checkpoint legitimately completed by your progression scope.",
            RespawnMode.Pvp =>
                "Enables the crimson PVP blowgun and separate real-death behavior configured below.",
            _ =>
                "Dead teammates wait until their own team or lobby completes the next fire. A fully wiped team returns to its own last checkpoint instead of being pushed forward."
        };
    }

    private static string WaitModeLabel(CampfireWaitMode mode)
    {
        return mode switch
        {
            CampfireWaitMode.Team => "wait for each team",
            CampfireWaitMode.Lobby => "wait for the lobby",
            _ => "wait for nobody"
        };
    }

    private static string ModeLabel(RespawnMode mode)
    {
        return mode switch
        {
            RespawnMode.CorpseTimer => "timed at corpse",
            RespawnMode.PreviousCampfire => "immediate at previous campfire",
            RespawnMode.Pvp => "PVP",
            _ => "when the next campfire is completed"
        };
    }

    private static string HazardScopeLabel(CampfireWaitMode mode)
    {
        return mode switch
        {
            CampfireWaitMode.Team => "shared within each team",
            CampfireWaitMode.Lobby => "vanilla global timeline",
            _ => "separate for every player"
        };
    }

    private static string RetentionLabel(RaceSettingsSnapshot settings)
    {
        if (settings.UsesPreviousCampfireRespawn
            || settings.UsesNextCampfireRespawn
            || settings.Mode == RespawnMode.Pvp)
        {
            return "retain a contiguous route through every valid checkpoint target";
        }

        return "unload after all players and pending corpse respawns have safely passed";
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
