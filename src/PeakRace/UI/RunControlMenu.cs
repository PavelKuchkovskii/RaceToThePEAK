using Photon.Pun;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PeakRace.UI;

/// <summary>
/// A small host-only run panel. It is intentionally separate from the Airport
/// settings menu because race rules remain locked after the run has started.
/// </summary>
internal sealed class RunControlMenu : MenuWindow
{
    private const int PanelWidth = 440;
    private const int PanelHeight = 230;
    private const int Padding = 16;

    private Texture2D whiteTexture;
    private GUIStyle titleStyle;
    private GUIStyle bodyStyle;
    private bool awaitingConfirmation;
    private bool endRequested;

    internal bool IsVisible { get; private set; }

    internal static bool CanHostEndCurrentRun
    {
        get
        {
            string scene = SceneManager.GetActiveScene().name;
            Character localCharacter = Character.localCharacter;
            return scene != "Airport"
                && scene != "Title"
                && (!PhotonNetwork.InRoom || PhotonNetwork.IsMasterClient)
                && RunManager.Instance != null
                && localCharacter != null
                && !localCharacter.inAirport
                && !localCharacter.gameOver;
        }
    }

    private void Awake()
    {
        DontDestroyOnLoad(gameObject);
        StartClosed();
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        CloseMenu();
        if (scene.name == "Airport")
        {
            endRequested = false;
        }
    }

    internal void ToggleMenu()
    {
        if (!CanHostEndCurrentRun || endRequested)
        {
            CloseMenu();
            return;
        }

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
        awaitingConfirmation = false;
        IsVisible = false;

        // Do not use the view flag as the source of truth here. The base
        // MenuWindow can still be registered with GUIManager even after an
        // IMGUI panel has stopped drawing, which would keep input blocked.
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
        bodyStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 15,
            alignment = TextAnchor.UpperLeft,
            wordWrap = true
        };
    }

    private void OnGUI()
    {
        if (!IsVisible || !CanHostEndCurrentRun || endRequested)
        {
            return;
        }

        EnsureStyles();
        float x = Mathf.Max(20f, Screen.width - PanelWidth - 20f);
        Rect panel = new(x, 20f, PanelWidth, PanelHeight);

        GUI.color = new Color(0f, 0f, 0f, 0.84f);
        GUI.DrawTexture(panel, whiteTexture);
        GUI.color = Color.white;

        GUI.Label(
            new Rect(panel.x + Padding, panel.y + Padding, panel.width - Padding * 2, 32f),
            $"Race controls | v{Plugin.Version}",
            titleStyle);

        GUI.Label(
            new Rect(panel.x + Padding, panel.y + 56f, panel.width - Padding * 2, 58f),
            "Host only. Ends the current run through PEAK's normal results screen. "
            + "Afterwards the room can return to the Airport without recreating the lobby.",
            bodyStyle);

        if (!awaitingConfirmation)
        {
            Color previousBackground = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.85f, 0.16f, 0.16f, 1f);
            if (GUI.Button(
                new Rect(panel.x + Padding, panel.yMax - 62f, panel.width - Padding * 2, 44f),
                "END CURRENT RUN"))
            {
                awaitingConfirmation = true;
            }
            GUI.backgroundColor = previousBackground;
            return;
        }

        GUI.Label(
            new Rect(panel.x + Padding, panel.y + 116f, panel.width - Padding * 2, 28f),
            "End the run for every player?",
            bodyStyle);

        float buttonWidth = (panel.width - Padding * 2 - 10f) * 0.5f;
        if (GUI.Button(
            new Rect(panel.x + Padding, panel.yMax - 62f, buttonWidth, 44f),
            "CANCEL"))
        {
            awaitingConfirmation = false;
        }

        Color confirmBackground = GUI.backgroundColor;
        GUI.backgroundColor = new Color(0.85f, 0.16f, 0.16f, 1f);
        if (GUI.Button(
            new Rect(panel.x + Padding + buttonWidth + 10f, panel.yMax - 62f, buttonWidth, 44f),
            "CONFIRM"))
        {
            EndCurrentRun();
        }
        GUI.backgroundColor = confirmBackground;
    }

    private void EndCurrentRun()
    {
        if (!CanHostEndCurrentRun || endRequested)
        {
            return;
        }

        endRequested = true;
        Character hostCharacter = Character.localCharacter;
        CloseMenu();
        Plugin.Log.LogInfo("The host ended the current run from the race controls panel.");

        // This bypasses CheckEndGame (which self-respawn modes intentionally
        // suppress) but keeps PEAK's complete networked results/return flow.
        hostCharacter.EndGame();
    }

    private new void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        MenuWindow.AllActiveWindows.Remove(this);
        if (whiteTexture != null)
        {
            Destroy(whiteTexture);
        }
    }
}
