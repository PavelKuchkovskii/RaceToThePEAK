using PeakRace.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PeakRace.UI;

/// <summary>Icon HUD for the main Campfire Ability slot and separate Chaos slot.</summary>
internal sealed class CampfireAbilityHUD : MonoBehaviour
{
    private readonly Dictionary<CampfireAbility, Texture2D> abilityIcons = new();

    private Texture2D whiteTexture;
    private Texture2D chaosIcon;
    private GUIStyle abilityStyle;
    private GUIStyle hintStyle;
    private GUIStyle feedbackStyle;
    private GUIStyle iconOverlayStyle;

    private void Awake()
    {
        DontDestroyOnLoad(gameObject);
        LoadIcons();
    }

    private void OnGUI()
    {
        CampfireAbilityManager manager = CampfireAbilityManager.Instance;
        Character localCharacter = Character.localCharacter;
        if (manager == null
            || localCharacter == null
            || RaceSettingsManager.Current.Mode != RespawnMode.Pvp
            || SceneManager.GetActiveScene().name is "Airport" or "Title")
        {
            return;
        }

        EnsureStyles();
        int previousDepth = GUI.depth;
        GUI.depth = -510;

        CampfireAbility ability = manager.GetAbility(localCharacter);
        bool passive = CampfireAbilityInfo.IsPassive(ability);
        double megaCooldown = ability == CampfireAbility.MegaLaunch
            ? manager.GetMegaLaunchCooldownRemaining(localCharacter)
            : 0d;
        bool coolingDown = megaCooldown > 0d;
        float cooldownProgress = coolingDown
            ? 1f - Mathf.Clamp01(
                (float)(megaCooldown / Math.Max(1f, manager.MegaLaunchCooldownSeconds)))
            : -1f;
        Color abilityColor = coolingDown
            ? new Color(0.62f, 0.62f, 0.62f, 1f)
            : passive
                ? new Color(0.35f, 0.82f, 1f, 1f)
                : Plugin.Color;
        float catchUpMultiplier = manager.GetCatchUpMultiplier(localCharacter);
        bool hasSystemCatchUp = manager.HasSystemCatchUp(localCharacter);
        string abilityHint = coolingDown
                ? $"COOLDOWN  •  {Mathf.CeilToInt((float)megaCooldown)}s"
                : ability == CampfireAbility.CatchUp && catchUpMultiplier > 1f
                    ? $"PASSIVE  •  +{Mathf.RoundToInt((catchUpMultiplier - 1f) * 100f)}%"
                    : passive
                        ? "PASSIVE"
                        : $"{manager.AbilityKeyDisplayName}: ACTIVATE";

        Rect safeArea = Screen.safeArea;
        float safeTop = Screen.height - safeArea.yMax;
        float safeBottom = Screen.height - safeArea.yMin;
        float minY = safeTop + 130f;
        float maxY = Mathf.Max(minY, safeBottom - 210f);
        float cursorY = Mathf.Clamp(Screen.height * 0.54f, minY, maxY);
        float left = safeArea.xMin + 22f;
        float textX = left + 82f;
        float textWidth = Mathf.Max(120f, Mathf.Min(310f, safeArea.xMax - textX - 18f));
        if (ability != CampfireAbility.None)
        {
            abilityIcons.TryGetValue(ability, out Texture2D abilityIcon);
            DrawFloatingIcon(
                new Rect(left, cursorY, 68f, 68f),
                abilityIcon,
                abilityColor,
                dimmed: coolingDown,
                coolingDown ? Mathf.CeilToInt((float)megaCooldown).ToString() : null,
                cooldownProgress);

            DrawOutlinedLabel(
                new Rect(textX, cursorY + 5f, textWidth, 30f),
                CampfireAbilityInfo.GetName(ability),
                abilityStyle,
                Color.white,
                2f);
            DrawOutlinedLabel(
                new Rect(textX, cursorY + 36f, textWidth, 22f),
                abilityHint,
                hintStyle,
                abilityColor,
                1f);

            if (hasSystemCatchUp)
            {
                DrawOutlinedLabel(
                    new Rect(textX, cursorY + 57f, textWidth, 19f),
                    $"LAST PLACE  +{Mathf.RoundToInt((catchUpMultiplier - 1f) * 100f)}%",
                    hintStyle,
                    new Color(0.55f, 0.95f, 1f, 1f),
                    1f);
            }

            cursorY += hasSystemCatchUp ? 90f : 80f;
        }

        bool hasChaos = manager.HasChaos(localCharacter);
        if (hasChaos)
        {
            Color chaosColor = new(1f, 0.34f, 0.23f, 1f);
            DrawFloatingIcon(
                new Rect(left + 7f, cursorY, 54f, 54f),
                chaosIcon,
                chaosColor,
                dimmed: false,
                overlayText: null,
                progress: -1f);
            DrawOutlinedLabel(
                new Rect(textX, cursorY + 2f, textWidth, 26f),
                "CHAOS",
                abilityStyle,
                Color.white,
                2f);
            DrawOutlinedLabel(
                new Rect(textX, cursorY + 28f, textWidth, 22f),
                $"{manager.ChaosKeyDisplayName}: ACTIVATE",
                hintStyle,
                chaosColor,
                1f);
        }

        if (ability == CampfireAbility.None && hasSystemCatchUp)
        {
            DrawOutlinedLabel(
                new Rect(left, cursorY + (hasChaos ? 62f : 0f), 260f, 24f),
                $"LAST PLACE  +{Mathf.RoundToInt((catchUpMultiplier - 1f) * 100f)}%",
                hintStyle,
                new Color(0.55f, 0.95f, 1f, 1f),
                1f);
        }

        if (manager.TryGetFeedback(out string feedback, out Color feedbackColor))
        {
            Rect feedbackRect = new(
                Mathf.Max(16f, (Screen.width - 520f) * 0.5f),
                Screen.height * 0.19f,
                Mathf.Min(520f, Screen.width - 32f),
                54f);
            DrawOutlinedLabel(feedbackRect, feedback, feedbackStyle, feedbackColor, 3f);
        }

        GUI.color = Color.white;
        GUI.depth = previousDepth;
    }

    private void DrawFloatingIcon(
        Rect rect,
        Texture2D icon,
        Color accentColor,
        bool dimmed,
        string overlayText,
        float progress)
    {
        if (icon != null)
        {
            GUI.color = new Color(0f, 0f, 0f, 0.58f);
            GUI.DrawTexture(
                new Rect(rect.x + 4f, rect.y + 5f, rect.width, rect.height),
                icon,
                ScaleMode.ScaleToFit,
                alphaBlend: true);
            GUI.color = dimmed
                ? new Color(0.42f, 0.42f, 0.42f, 0.68f)
                : Color.white;
            GUI.DrawTexture(rect, icon, ScaleMode.ScaleToFit, alphaBlend: true);
        }
        else
        {
            DrawOutlinedLabel(rect, "—", iconOverlayStyle, Color.white, 2f);
        }

        Rect progressTrack = new(rect.x + 9f, rect.yMax + 1f, rect.width - 18f, 4f);
        DrawSolidRect(
            new Rect(progressTrack.x + 2f, progressTrack.y + 2f, progressTrack.width, progressTrack.height),
            new Color(0f, 0f, 0f, 0.66f));
        if (progress >= 0f)
        {
            DrawSolidRect(progressTrack, new Color(0.18f, 0.18f, 0.18f, 0.9f));
            if (progress > 0f)
            {
                DrawSolidRect(
                    new Rect(
                        progressTrack.x,
                        progressTrack.y,
                        progressTrack.width * Mathf.Clamp01(progress),
                        progressTrack.height),
                    accentColor);
            }
        }
        else
        {
            DrawSolidRect(progressTrack, accentColor);
        }

        if (!string.IsNullOrEmpty(overlayText))
        {
            DrawOutlinedLabel(rect, overlayText, iconOverlayStyle, Color.white, 2f);
        }
        GUI.color = Color.white;
    }

    private static void DrawOutlinedLabel(
        Rect rect,
        string text,
        GUIStyle style,
        Color color,
        float outline)
    {
        Color previousColor = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.86f);
        GUI.Label(new Rect(rect.x - outline, rect.y, rect.width, rect.height), text, style);
        GUI.Label(new Rect(rect.x + outline, rect.y, rect.width, rect.height), text, style);
        GUI.Label(new Rect(rect.x, rect.y - outline, rect.width, rect.height), text, style);
        GUI.Label(new Rect(rect.x, rect.y + outline, rect.width, rect.height), text, style);
        GUI.color = color;
        GUI.Label(rect, text, style);
        GUI.color = previousColor;
    }

    private void DrawSolidRect(Rect rect, Color color)
    {
        Color previousColor = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, whiteTexture);
        GUI.color = previousColor;
    }

    private void EnsureStyles()
    {
        if (whiteTexture == null)
        {
            whiteTexture = new Texture2D(1, 1)
            {
                name = "RaceToThePeak HUD White",
                hideFlags = HideFlags.HideAndDontSave
            };
            whiteTexture.SetPixel(0, 0, Color.white);
            whiteTexture.Apply();
        }

        abilityStyle ??= CreateStyle(23, TextAnchor.MiddleLeft, FontStyle.Bold);
        hintStyle ??= CreateStyle(14, TextAnchor.MiddleLeft, FontStyle.Bold);
        feedbackStyle ??= CreateStyle(24, TextAnchor.MiddleCenter, FontStyle.Bold);
        iconOverlayStyle ??= CreateStyle(25, TextAnchor.MiddleCenter, FontStyle.Bold);
    }

    private void LoadIcons()
    {
        Assembly assembly = typeof(CampfireAbilityHUD).Assembly;
        string[] resourceNames = assembly.GetManifestResourceNames();
        LoadAbilityIcon(assembly, resourceNames, CampfireAbility.Adrenaline, "adrenaline.png");
        LoadAbilityIcon(assembly, resourceNames, CampfireAbility.Shield, "shield.png");
        LoadAbilityIcon(assembly, resourceNames, CampfireAbility.Exhaust, "exhaust.png");
        LoadAbilityIcon(assembly, resourceNames, CampfireAbility.SecondWind, "second_wind.png");
        LoadAbilityIcon(assembly, resourceNames, CampfireAbility.CatchUp, "catch_up.png");
        LoadAbilityIcon(assembly, resourceNames, CampfireAbility.Recall, "recall.png");
        LoadAbilityIcon(assembly, resourceNames, CampfireAbility.ChaosHorn, "chaos_horn.png");
        LoadAbilityIcon(assembly, resourceNames, CampfireAbility.GhostRunner, "ghost_runner.png");
        LoadAbilityIcon(assembly, resourceNames, CampfireAbility.MegaLaunch, "mega_launch.png");
        chaosIcon = LoadIcon(assembly, resourceNames, "chaos.png");
    }

    private void LoadAbilityIcon(
        Assembly assembly,
        string[] resourceNames,
        CampfireAbility ability,
        string fileName)
    {
        Texture2D icon = LoadIcon(assembly, resourceNames, fileName);
        if (icon != null)
        {
            abilityIcons[ability] = icon;
        }
    }

    private static Texture2D LoadIcon(
        Assembly assembly,
        IEnumerable<string> resourceNames,
        string fileName)
    {
        string resourceName = resourceNames.FirstOrDefault(name =>
            name.EndsWith(".AbilityIcons." + fileName, StringComparison.OrdinalIgnoreCase));
        if (resourceName == null)
        {
            Plugin.Log.LogWarning($"Missing embedded ability icon '{fileName}'.");
            return null;
        }

        try
        {
            using Stream stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                return null;
            }

            byte[] bytes = new byte[(int)stream.Length];
            int offset = 0;
            while (offset < bytes.Length)
            {
                int read = stream.Read(bytes, offset, bytes.Length - offset);
                if (read <= 0)
                {
                    break;
                }
                offset += read;
            }

            Texture2D texture = new(2, 2, TextureFormat.RGBA32, mipChain: false)
            {
                name = "RaceToThePeak " + fileName,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            if (!texture.LoadImage(bytes, markNonReadable: true))
            {
                UnityEngine.Object.Destroy(texture);
                Plugin.Log.LogWarning($"Could not decode ability icon '{fileName}'.");
                return null;
            }
            return texture;
        }
        catch (Exception exception)
        {
            Plugin.Log.LogWarning(
                $"Could not load ability icon '{fileName}': {exception.Message}");
            return null;
        }
    }

    private static GUIStyle CreateStyle(int size, TextAnchor anchor, FontStyle fontStyle)
    {
        return new GUIStyle(GUI.skin.label)
        {
            fontSize = size,
            alignment = anchor,
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
        foreach (Texture2D icon in abilityIcons.Values)
        {
            if (icon != null)
            {
                Destroy(icon);
            }
        }
        if (chaosIcon != null)
        {
            Destroy(chaosIcon);
        }
        abilityIcons.Clear();
    }
}
