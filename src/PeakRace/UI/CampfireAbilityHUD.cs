using PeakRace.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PeakRace.UI;

/// <summary>Compact, non-interactive display for the main slot and Chaos.</summary>
internal sealed class CampfireAbilityHUD : MonoBehaviour
{
    private Texture2D whiteTexture;
    private GUIStyle titleStyle;
    private GUIStyle abilityStyle;
    private GUIStyle hintStyle;
    private GUIStyle feedbackStyle;

    private void Awake()
    {
        DontDestroyOnLoad(gameObject);
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

        const float width = 286f;
        float x = 18f;
        float y = Screen.height - 154f;
        Rect panel = new(x, y, width, 116f);
        GUI.color = new Color(0.02f, 0.02f, 0.02f, 0.78f);
        GUI.DrawTexture(panel, whiteTexture);

        CampfireAbility ability = manager.GetAbility(localCharacter);
        GUI.color = Plugin.Color;
        GUI.Label(new Rect(x + 12f, y + 6f, width - 24f, 20f), "CAMPFIRE ABILITY", titleStyle);
        GUI.color = Color.white;
        GUI.Label(
            new Rect(x + 12f, y + 27f, width - 24f, 28f),
            CampfireAbilityInfo.GetName(ability),
            abilityStyle);

        string abilityHint = ability == CampfireAbility.None
            ? "Activate a campfire to receive one"
            : CampfireAbilityInfo.IsPassive(ability)
                ? "PASSIVE"
                : $"{manager.AbilityKeyDisplayName}: ACTIVATE";
        GUI.Label(new Rect(x + 12f, y + 56f, width - 24f, 20f), abilityHint, hintStyle);

        bool hasChaos = manager.HasChaos(localCharacter);
        GUI.color = hasChaos ? new Color(1f, 0.34f, 0.23f, 1f) : new Color(0.58f, 0.58f, 0.58f, 1f);
        GUI.Label(
            new Rect(x + 12f, y + 82f, width - 24f, 22f),
            hasChaos ? $"CHAOS  •  {manager.ChaosKeyDisplayName}: ACTIVATE" : "CHAOS  •  EMPTY",
            hintStyle);

        if (manager.TryGetFeedback(out string feedback, out Color feedbackColor))
        {
            GUI.color = new Color(0f, 0f, 0f, 0.82f);
            Rect feedbackRect = new(
                Mathf.Max(16f, (Screen.width - 520f) * 0.5f),
                Screen.height * 0.19f,
                Mathf.Min(520f, Screen.width - 32f),
                54f);
            GUI.DrawTexture(feedbackRect, whiteTexture);
            GUI.color = feedbackColor;
            GUI.Label(feedbackRect, feedback, feedbackStyle);
        }

        GUI.color = Color.white;
        GUI.depth = previousDepth;
    }

    private void EnsureStyles()
    {
        if (whiteTexture == null)
        {
            whiteTexture = new Texture2D(1, 1);
            whiteTexture.SetPixel(0, 0, Color.white);
            whiteTexture.Apply();
        }

        titleStyle ??= CreateStyle(12, TextAnchor.MiddleLeft, FontStyle.Bold);
        abilityStyle ??= CreateStyle(21, TextAnchor.MiddleLeft, FontStyle.Bold);
        hintStyle ??= CreateStyle(13, TextAnchor.MiddleLeft, FontStyle.Bold);
        feedbackStyle ??= CreateStyle(22, TextAnchor.MiddleCenter, FontStyle.Bold);
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
    }
}
