using UnityEngine;
using Photon.Pun;
using Photon.Realtime;

public class GameUIManager : MonoBehaviourPunCallbacks
{
    public enum GameState
    {
        TeamSelect,
        InGame
    }

    [Header("Player")]
    public GameObject playerPrefab;
    public Transform phantomSpawnPoint;
    public Transform havocSpawnPoint;

    [Header("Settings")]
    public float mouseSensitivity = 2f;

    // Graphics settings
    private int shadowQuality = 1;
    private float shadowDistance = 40f;
    private int qualityLevel = 0;
    private float resolutionScale = 1f;
    private bool vSyncEnabled = false;

    // Audio settings
    private float masterVolume = 1f;

    // Display settings
    private bool fullscreenEnabled = true;
    private float fieldOfView = 75f;
    private bool showFPS = false;

    // Control settings
    private bool invertYAxis = false;

    // Singleton instance for settings access
    public static GameUIManager Instance { get; private set; }

    // Public getters for settings
    public static bool InvertY => Instance != null ? Instance.invertYAxis : false;
    public static float FOV => Instance != null ? Instance.fieldOfView : 75f;
    public static float Sensitivity => Instance != null ? Instance.mouseSensitivity : 2f;

    // FPS counter
    private float fpsUpdateTimer = 0f;
    private float currentFPS = 0f;
    private int frameCount = 0;

    // Current state
    public GameState currentState = GameState.TeamSelect;
    private Team selectedTeam = Team.None;

    // UI Styles
    private GUIStyle buttonStyle;
    private GUIStyle labelStyle;
    private GUIStyle smallLabelStyle;
    private bool stylesInitialized = false;

    void Start()
    {
        Instance = this;

        // Show cursor for team selection
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        // Load saved settings
        LoadSettings();
        ApplySettings();
    }

    void Update()
    {
        // FPS counter
        frameCount++;
        fpsUpdateTimer += Time.unscaledDeltaTime;
        if (fpsUpdateTimer >= 0.5f)
        {
            currentFPS = frameCount / fpsUpdateTimer;
            frameCount = 0;
            fpsUpdateTimer = 0f;
        }

        // Cursor handling
        if (currentState == GameState.InGame)
        {
            // In-game cursor handled by FPSController
        }
        else
        {
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }
    }

    void InitStyles()
    {
        if (stylesInitialized) return;

        buttonStyle = new GUIStyle(GUI.skin.button);
        buttonStyle.fontSize = 28;
        buttonStyle.fontStyle = FontStyle.Bold;
        buttonStyle.fixedHeight = 60;

        labelStyle = new GUIStyle(GUI.skin.label);
        labelStyle.fontSize = 24;
        labelStyle.fontStyle = FontStyle.Bold;
        labelStyle.alignment = TextAnchor.MiddleCenter;
        labelStyle.normal.textColor = Color.white;

        smallLabelStyle = new GUIStyle(GUI.skin.label);
        smallLabelStyle.fontSize = 18;
        smallLabelStyle.alignment = TextAnchor.MiddleCenter;
        smallLabelStyle.normal.textColor = new Color(0.8f, 0.8f, 0.8f);

        stylesInitialized = true;
    }

    void OnGUI()
    {
        InitStyles();

        if (currentState == GameState.TeamSelect)
        {
            DrawTeamSelect();
        }

        // FPS Counter
        if (showFPS)
        {
            DrawFPSCounter();
        }
    }

    void DrawFPSCounter()
    {
        GUIStyle fpsStyle = new GUIStyle(GUI.skin.label);
        fpsStyle.fontSize = 16;
        fpsStyle.fontStyle = FontStyle.Bold;
        fpsStyle.alignment = TextAnchor.UpperLeft;

        Color fpsColor;
        if (currentFPS >= 60f)
            fpsColor = Color.green;
        else if (currentFPS >= 30f)
            fpsColor = Color.yellow;
        else
            fpsColor = Color.red;

        GUI.color = new Color(0, 0, 0, 0.5f);
        GUI.DrawTexture(new Rect(8, 8, 80, 24), Texture2D.whiteTexture);

        GUI.color = fpsColor;
        GUI.Label(new Rect(12, 8, 80, 24), $"FPS: {Mathf.RoundToInt(currentFPS)}", fpsStyle);
        GUI.color = Color.white;
    }

    void DrawTeamSelect()
    {
        // Dark overlay
        GUI.color = new Color(0, 0, 0, 0.7f);
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = Color.white;

        float centerX = Screen.width / 2f;
        float centerY = Screen.height / 2f;
        float buttonWidth = 250f;
        float buttonHeight = 80f;
        float spacing = 40f;

        // Title
        GUI.Label(new Rect(0, centerY - 180, Screen.width, 60), "SELECT YOUR TEAM", labelStyle);

        // Team buttons side by side
        float totalWidth = buttonWidth * 2 + spacing;
        float startX = centerX - totalWidth / 2f;

        // Phantom button (blue tint)
        GUI.backgroundColor = new Color(0.3f, 0.5f, 1f);
        if (GUI.Button(new Rect(startX, centerY - 40, buttonWidth, buttonHeight), "PHANTOM", buttonStyle))
        {
            SelectTeam(Team.Phantom);
        }

        // Havoc button (red tint)
        GUI.backgroundColor = new Color(1f, 0.4f, 0.3f);
        if (GUI.Button(new Rect(startX + buttonWidth + spacing, centerY - 40, buttonWidth, buttonHeight), "HAVOC", buttonStyle))
        {
            SelectTeam(Team.Havoc);
        }

        GUI.backgroundColor = Color.white;

        // Player count
        if (PhotonNetwork.InRoom)
        {
            string roomInfo = $"Players in room: {PhotonNetwork.CurrentRoom.PlayerCount}";
            GUI.Label(new Rect(0, centerY + 80, Screen.width, 30), roomInfo, smallLabelStyle);
        }
    }

    void LoadSettings()
    {
        shadowQuality = PlayerPrefs.GetInt("ShadowQuality", 1);
        shadowDistance = PlayerPrefs.GetFloat("ShadowDistance", 40f);
        qualityLevel = PlayerPrefs.GetInt("QualityLevel", 0);
        resolutionScale = PlayerPrefs.GetFloat("ResolutionScale", 1f);
        mouseSensitivity = PlayerPrefs.GetFloat("MouseSensitivity", 2f);
        masterVolume = PlayerPrefs.GetFloat("MasterVolume", 1f);
        vSyncEnabled = PlayerPrefs.GetInt("VSync", 0) == 1;
        fullscreenEnabled = PlayerPrefs.GetInt("Fullscreen", 1) == 1;
        fieldOfView = PlayerPrefs.GetFloat("FOV", 75f);
        showFPS = PlayerPrefs.GetInt("ShowFPS", 0) == 1;
        invertYAxis = PlayerPrefs.GetInt("InvertY", 0) == 1;
    }

    void ApplySettings()
    {
        QualitySettings.SetQualityLevel(qualityLevel, true);

        switch (shadowQuality)
        {
            case 0:
                QualitySettings.shadows = ShadowQuality.Disable;
                break;
            case 1:
                QualitySettings.shadows = ShadowQuality.HardOnly;
                QualitySettings.shadowResolution = ShadowResolution.Low;
                break;
            case 2:
                QualitySettings.shadows = ShadowQuality.All;
                QualitySettings.shadowResolution = ShadowResolution.Medium;
                break;
            case 3:
                QualitySettings.shadows = ShadowQuality.All;
                QualitySettings.shadowResolution = ShadowResolution.High;
                break;
        }

        QualitySettings.shadowDistance = shadowDistance;
        QualitySettings.vSyncCount = vSyncEnabled ? 1 : 0;
        AudioListener.volume = masterVolume;

        if (Camera.main != null)
        {
            Camera.main.fieldOfView = fieldOfView;
        }
    }

    void SelectTeam(Team team)
    {
        Debug.Log($"[GameUIManager] SelectTeam called: {team}, InRoom: {PhotonNetwork.InRoom}, IsConnected: {PhotonNetwork.IsConnected}");

        if (!PhotonNetwork.InRoom)
        {
            Debug.LogError("[GameUIManager] Cannot spawn - not in a Photon room!");
            return;
        }

        selectedTeam = team;
        currentState = GameState.InGame;

        // Set team in Photon custom properties
        KillFeedManager.SetPlayerTeam(PhotonNetwork.LocalPlayer, team);
        KillFeedManager.Initialize();

        // Lock cursor for gameplay
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        SpawnPlayer();
    }

    void SpawnPlayer()
    {
        if (playerPrefab == null)
        {
            Debug.LogError("Player prefab not assigned!");
            return;
        }

        Transform spawnPoint = selectedTeam == Team.Phantom ? phantomSpawnPoint : havocSpawnPoint;
        Vector3 spawnPos = spawnPoint != null ? spawnPoint.position : Vector3.zero;
        Quaternion spawnRot = spawnPoint != null ? spawnPoint.rotation : Quaternion.identity;

        if (selectedTeam == Team.Phantom)
        {
            spawnRot *= Quaternion.Euler(0, 180f, 0);
        }

        spawnPos += new Vector3(Random.Range(-2f, 2f), 0, Random.Range(-2f, 2f));

        object[] instantiationData = new object[] { (int)selectedTeam };
        PhotonNetwork.Instantiate(playerPrefab.name, spawnPos, spawnRot, 0, instantiationData);
        Debug.Log($"Spawned {selectedTeam} player at {spawnPos}");
    }

    // Photon Callbacks - kept for reconnection handling
    public override void OnDisconnected(DisconnectCause cause)
    {
        Debug.Log($"Disconnected: {cause}");
        currentState = GameState.TeamSelect;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public override void OnPlayerEnteredRoom(Photon.Realtime.Player newPlayer)
    {
        Debug.Log($"Player {newPlayer.NickName} joined. Total: {PhotonNetwork.CurrentRoom.PlayerCount}");
    }

    public override void OnPlayerLeftRoom(Photon.Realtime.Player otherPlayer)
    {
        Debug.Log($"Player {otherPlayer.NickName} left. Total: {PhotonNetwork.CurrentRoom.PlayerCount}");
    }
}
