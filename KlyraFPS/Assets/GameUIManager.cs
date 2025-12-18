using UnityEngine;
using UnityEngine.InputSystem;
using Photon.Pun;
using Photon.Realtime;

public class GameUIManager : MonoBehaviourPunCallbacks
{
    public enum GameState
    {
        PressStart,
        MainMenu,
        Connecting,
        TeamSelect,
        InGame
    }

    [Header("Player")]
    public GameObject playerPrefab;
    public Transform phantomSpawnPoint;
    public Transform havocSpawnPoint;

    [Header("Settings")]
    public float mouseSensitivity = 2f;

    // Current state
    public GameState currentState = GameState.PressStart;
    private Team selectedTeam = Team.None;
    private string statusMessage = "";

    // UI Styles
    private GUIStyle titleStyle;
    private GUIStyle buttonStyle;
    private GUIStyle labelStyle;
    private GUIStyle smallLabelStyle;
    private bool stylesInitialized = false;

    // Press Start animation
    private float pressStartTimer = 0f;

    void Start()
    {
        PhotonNetwork.AutomaticallySyncScene = true;
        PhotonNetwork.PhotonServerSettings.AppSettings.FixedRegion = "us";

        // Show cursor on menu
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    void Update()
    {
        pressStartTimer += Time.deltaTime;

        // Press Start screen - any key to continue
        if (currentState == GameState.PressStart)
        {
            var keyboard = Keyboard.current;
            var gamepad = Gamepad.current;

            bool anyKeyPressed = false;

            if (keyboard != null && keyboard.anyKey.wasPressedThisFrame)
            {
                anyKeyPressed = true;
            }

            if (gamepad != null && (gamepad.startButton.wasPressedThisFrame ||
                                    gamepad.buttonSouth.wasPressedThisFrame))
            {
                anyKeyPressed = true;
            }

            if (anyKeyPressed)
            {
                currentState = GameState.MainMenu;
            }
        }
    }

    void InitStyles()
    {
        if (stylesInitialized) return;

        titleStyle = new GUIStyle(GUI.skin.label);
        titleStyle.fontSize = 72;
        titleStyle.fontStyle = FontStyle.Bold;
        titleStyle.alignment = TextAnchor.MiddleCenter;
        titleStyle.normal.textColor = Color.white;

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

        switch (currentState)
        {
            case GameState.PressStart:
                DrawPressStart();
                break;
            case GameState.MainMenu:
                DrawMainMenu();
                break;
            case GameState.Connecting:
                DrawConnecting();
                break;
            case GameState.TeamSelect:
                DrawTeamSelect();
                break;
            case GameState.InGame:
                // No UI during gameplay (or minimal HUD)
                break;
        }
    }

    void DrawPressStart()
    {
        // Dark overlay
        GUI.color = new Color(0, 0, 0, 0.7f);
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = Color.white;

        // Title
        float centerX = Screen.width / 2f;
        float centerY = Screen.height / 2f;

        GUI.Label(new Rect(0, centerY - 150, Screen.width, 100), "KLYRA", titleStyle);

        // Subtitle
        GUI.Label(new Rect(0, centerY - 60, Screen.width, 40), "TACTICAL WARFARE", labelStyle);

        // Flashing "Press Any Key"
        float alpha = Mathf.PingPong(pressStartTimer * 2f, 1f);
        GUI.color = new Color(1, 1, 1, alpha);
        GUI.Label(new Rect(0, centerY + 80, Screen.width, 40), "PRESS ANY KEY TO START", labelStyle);
        GUI.color = Color.white;

        // Version/credits at bottom
        GUI.Label(new Rect(0, Screen.height - 40, Screen.width, 30), "v0.1 Alpha", smallLabelStyle);
    }

    void DrawMainMenu()
    {
        // Dark overlay
        GUI.color = new Color(0, 0, 0, 0.7f);
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = Color.white;

        float centerX = Screen.width / 2f;
        float centerY = Screen.height / 2f;
        float buttonWidth = 300f;

        // Title
        GUI.Label(new Rect(0, centerY - 200, Screen.width, 100), "KLYRA", titleStyle);

        // Menu buttons
        float buttonX = centerX - buttonWidth / 2f;
        float buttonY = centerY - 50;

        if (GUI.Button(new Rect(buttonX, buttonY, buttonWidth, 60), "PLAY", buttonStyle))
        {
            ConnectToServer();
        }

        buttonY += 80;
        if (GUI.Button(new Rect(buttonX, buttonY, buttonWidth, 60), "SETTINGS", buttonStyle))
        {
            // TODO: Settings menu
        }

        buttonY += 80;
        if (GUI.Button(new Rect(buttonX, buttonY, buttonWidth, 60), "QUIT", buttonStyle))
        {
            #if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
            #else
            Application.Quit();
            #endif
        }
    }

    void DrawConnecting()
    {
        // Dark overlay
        GUI.color = new Color(0, 0, 0, 0.8f);
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = Color.white;

        float centerY = Screen.height / 2f;

        GUI.Label(new Rect(0, centerY - 50, Screen.width, 50), "CONNECTING...", labelStyle);
        GUI.Label(new Rect(0, centerY + 10, Screen.width, 30), statusMessage, smallLabelStyle);
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

    void ConnectToServer()
    {
        currentState = GameState.Connecting;
        statusMessage = "Connecting to server...";

        if (PhotonNetwork.IsConnected)
        {
            PhotonNetwork.JoinRandomRoom();
        }
        else
        {
            PhotonNetwork.ConnectUsingSettings();
        }
    }

    void SelectTeam(Team team)
    {
        selectedTeam = team;
        currentState = GameState.InGame;

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

        // Choose spawn point based on team
        Transform spawnPoint = selectedTeam == Team.Phantom ? phantomSpawnPoint : havocSpawnPoint;

        Vector3 spawnPos = spawnPoint != null ? spawnPoint.position : Vector3.zero;
        Quaternion spawnRot = spawnPoint != null ? spawnPoint.rotation : Quaternion.identity;

        // Rotate Phantom 180 degrees
        if (selectedTeam == Team.Phantom)
        {
            spawnRot *= Quaternion.Euler(0, 180f, 0);
        }

        // Random offset to prevent spawning on each other
        spawnPos += new Vector3(Random.Range(-2f, 2f), 0, Random.Range(-2f, 2f));

        // Pass team data
        object[] instantiationData = new object[] { (int)selectedTeam };

        GameObject player = PhotonNetwork.Instantiate(playerPrefab.name, spawnPos, spawnRot, 0, instantiationData);
        Debug.Log($"Spawned {selectedTeam} player at {spawnPos}");
    }

    // Photon Callbacks
    public override void OnConnectedToMaster()
    {
        Debug.Log("Connected to Photon Master Server");
        statusMessage = "Connected! Joining room...";

        RoomOptions roomOptions = new RoomOptions();
        roomOptions.MaxPlayers = 10;
        roomOptions.IsVisible = true;
        roomOptions.IsOpen = true;

        PhotonNetwork.JoinOrCreateRoom("KlyraFPS_Room", roomOptions, TypedLobby.Default);
    }

    public override void OnJoinedRoom()
    {
        Debug.Log($"Joined room: {PhotonNetwork.CurrentRoom.Name}");
        statusMessage = "Joined!";
        currentState = GameState.TeamSelect;
    }

    public override void OnJoinRandomFailed(short returnCode, string message)
    {
        Debug.Log("No random room available, creating one...");
        statusMessage = "Creating room...";

        RoomOptions roomOptions = new RoomOptions();
        roomOptions.MaxPlayers = 10;
        PhotonNetwork.CreateRoom("KlyraFPS_" + Random.Range(1000, 9999), roomOptions);
    }

    public override void OnDisconnected(DisconnectCause cause)
    {
        Debug.Log($"Disconnected: {cause}");
        statusMessage = "Disconnected: " + cause;
        currentState = GameState.MainMenu;

        // Show cursor again
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
