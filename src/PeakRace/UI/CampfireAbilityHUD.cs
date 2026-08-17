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
    private GUIStyle titleStyle;
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

        const float width = 360f;
        const float height = 170f;
        float x = 18f;
        float y = Mathf.Max(18f, Screen.height - height - 38f);
        Rect panel = new(x, y, width, height);
        DrawSolidRect(panel, new Color(0.02f, 0.02f, 0.02f, 0.82f));

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
        Color abilityBorder = ability == CampfireAbility.None
            ? new Color(0.38f, 0.38f, 0.38f, 1f)
            : coolingDown
                ? new Color(0.48f, 0.48f, 0.48f, 1f)
                : passive
                    ? new Color(0.35f, 0.82f, 1f, 1f)
                    : new Color(0.43f, 1f, 0.55f, 1f);
        abilityIcons.TryGetValue(ability, out Texture2D abilityIcon);
        DrawFramedIcon(
            new Rect(x + 12f, y + 34f, 88f, 88f),
            abilityIcon,
            abilityBorder,
            dimmed: coolingDown,
            coolingDown ? Mathf.CeilToInt((float)megaCooldown).ToString() : null,
            cooldownProgress);

        GUI.color = Plugin.Color;
        GUI.Label(
            new Rect(x + 12f, y + 7f, width - 24f, 20f),
            "CAMPFIRE ABILITY",
            titleStyle);
        GUI.color = Color.white;
        GUI.Label(
            new Rect(x + 112f, y + 35f, width - 124f, 30f),
            CampfireAbilityInfo.GetName(ability),
            abilityStyle);

        float catchUpMultiplier = manager.GetCatchUpMultiplier(localCharacter);
        string abilityHint = ability == CampfireAbility.None
            ? "Activate a campfire to receive one"
            : coolingDown
                ? $"COOLDOWN  •  {Mathf.CeilToInt((float)megaCooldown)}s"
                : ability == CampfireAbility.CatchUp && catchUpMultiplier > 1f
                    ? $"PASSIVE  •  +{Mathf.RoundToInt((catchUpMultiplier - 1f) * 100f)}%"
                    : passive
                        ? "PASSIVE"
                        : $"{manager.AbilityKeyDisplayName}: ACTIVATE";
        GUI.Label(
            new Rect(x + 112f, y + 68f, width - 124f, 22f),
            abilityHint,
            hintStyle);

        if (manager.HasSystemCatchUp(localCharacter))
        {
            GUI.color = new Color(0.4f, 0.9f, 1f, 1f);
            GUI.Label(
                new Rect(x + 112f, y + 94f, width - 124f, 20f),
                $"LAST PLACE BOOST  +{Mathf.RoundToInt((catchUpMultiplier - 1f) * 100f)}%",
                hintStyle);
        }

        bool hasChaos = manager.HasChaos(localCharacter);
        Color chaosColor = hasChaos
            ? new Color(1f, 0.34f, 0.23f, 1f)
            : new Color(0.38f, 0.38f, 0.38f, 1f);
        DrawFramedIcon(
            new Rect(x + 12f, y + 128f, 36f, 36f),
            chaosIcon,
            chaosColor,
            dimmed: !hasChaos,
            overlayText: null,
            progress: -1f);
        GUI.color = chaosColor;
        GUI.Label(
            new Rect(x + 58f, y + 132f, width - 70f, 28f),
            hasChaos
                ? $"CHAOS  •  {manager.ChaosKeyDisplayName}: ACTIVATE"
                : "CHAOS  •  EMPTY",
            hintStyle);

        if (manager.TryGetFeedback(out string feedback, out Color feedbackColor))
        {
            Rect feedbackRect = new(
                Mathf.Max(16f, (Screen.width - 520f) * 0.5f),
                Screen.height * 0.19f,
                Mathf.Min(520f, Screen.width - 32f),
                54f);
            DrawSolidRect(feedbackRect, new Color(0f, 0f, 0f, 0.84f));
            GUI.color = feedbackColor;
            GUI.Label(feedbackRect, feedback, feedbackStyle);
        }

        GUI.color = Color.white;
        GUI.depth = previousDepth;
    }

    private void DrawFramedIcon(
        Rect rect,
        Texture2D icon,
        Color borderColor,
        bool dimmed,
        string overlayText,
        float progress)
    {
        DrawSolidRect(rect, borderColor);
        Rect inner = new(rect.x + 3f, rect.y + 3f, rect.width - 6f, rect.height - 6f);
        DrawSolidRect(inner, new Color(0.025f, 0.025f, 0.035f, 0.96f));
        Rect content = new(inner.x + 3f, inner.y + 3f, inner.width - 6f, inner.height - 6f);

        if (icon != null)
        {
            GUI.color = dimmed
                ? new Color(0.42f, 0.42f, 0.42f, 0.82f)
                : Color.white;
            GUI.DrawTexture(content, icon, ScaleMode.ScaleToFit, alphaBlend: true);
        }
        else
        {
            GUI.color = new Color(0.55f, 0.55f, 0.55f, 1f);
            GUI.Label(content, "—", iconOverlayStyle);
        }

        if (dimmed)
        {
            DrawSolidRect(content, new Color(0f, 0f, 0f, 0.48f));
        }

        if (progress >= 0f)
        {
            Rect bar = new(content.x, content.yMax - 7f, content.width, 7f);
            DrawSolidRect(bar, new Color(0.08f, 0.08f, 0.08f, 0.95f));
            if (progress > 0f)
            {
                DrawSolidRect(
                    new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(progress), bar.height),
                    Plugin.Color);
            }
        }

        if (!string.IsNullOrEmpty(overlayText))
        {
            GUI.color = Color.white;
            GUI.Label(content, overlayText, iconOverlayStyle);
        }
        GUI.color = Color.white;
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

        titleStyle ??= CreateStyle(12, TextAnchor.MiddleLeft, FontStyle.Bold);
        abilityStyle ??= CreateStyle(21, TextAnchor.MiddleLeft, FontStyle.Bold);
        hintStyle ??= CreateStyle(13, TextAnchor.MiddleLeft, FontStyle.Bold);
        feedbackStyle ??= CreateStyle(22, TextAnchor.MiddleCenter, FontStyle.Bold);
        iconOverlayStyle ??= CreateStyle(24, TextAnchor.MiddleCenter, FontStyle.Bold);
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
