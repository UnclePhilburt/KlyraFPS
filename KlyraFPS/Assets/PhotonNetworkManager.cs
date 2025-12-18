using UnityEngine;
using Photon.Pun;
using Photon.Realtime;

public enum Team
{
    None,
    Phantom,
    Havoc
}

public class PhotonNetworkManager : MonoBehaviourPunCallbacks
{
    [Header("Player Settings")]
    public GameObject playerPrefab;
    public Transform spawnPoint;
    public Transform phantomSpawnPoint;
    public Transform havocSpawnPoint;

    [Header("UI")]
    private bool isConnecting = false;
    private string statusMessage = "Click to Connect";
    private bool showTeamSelection = false;
    private Team selectedTeam = Team.None;

    private GUIStyle buttonStyle;
    private GUIStyle labelStyle;
    private bool stylesInitialized = false;

    void Start()
    {
        // Ensure we can sync scenes
        PhotonNetwork.AutomaticallySyncScene = true;

        // Force a specific region so all players connect to the same server
        PhotonNetwork.PhotonServerSettings.AppSettings.FixedRegion = "us";
    }

    void InitializeStyles()
    {
        if (stylesInitialized) return;

        buttonStyle = new GUIStyle(GUI.skin.button);
        buttonStyle.fontSize = 24;
        buttonStyle.fontStyle = FontStyle.Bold;

        labelStyle = new GUIStyle(GUI.skin.label);
        labelStyle.fontSize = 20;
        labelStyle.fontStyle = FontStyle.Bold;
        labelStyle.normal.textColor = Color.white;

        stylesInitialized = true;
    }

    void OnGUI()
    {
        InitializeStyles();

        GUILayout.BeginArea(new Rect(10, 10, 350, 350));

        if (!PhotonNetwork.IsConnected)
        {
            DrawConnectMenu();
        }
        else if (!PhotonNetwork.InRoom)
        {
            GUILayout.Label("Connected to Photon", labelStyle);
            GUILayout.Label("Joining room...", labelStyle);
        }
        else if (showTeamSelection)
        {
            DrawTeamSelection();
        }
        else
        {
            DrawConnectedStatus();
        }

        GUILayout.EndArea();
    }

    void DrawTeamSelection()
    {
        GUILayout.Label("SELECT YOUR TEAM", labelStyle);
        GUILayout.Space(20);

        if (GUILayout.Button("PHANTOM", buttonStyle, GUILayout.Height(60)))
        {
            selectedTeam = Team.Phantom;
            showTeamSelection = false;
            SpawnPlayer();
        }

        GUILayout.Space(10);

        if (GUILayout.Button("HAVOC", buttonStyle, GUILayout.Height(60)))
        {
            selectedTeam = Team.Havoc;
            showTeamSelection = false;
            SpawnPlayer();
        }
    }

    void DrawConnectMenu()
    {
        GUILayout.Label("KLYRA FPS", labelStyle);
        GUILayout.Space(10);
        GUILayout.Label(statusMessage, labelStyle);
        GUILayout.Space(20);

        GUI.enabled = !isConnecting;

        if (GUILayout.Button("JOIN GAME", buttonStyle, GUILayout.Height(60)))
        {
            Connect();
        }

        GUI.enabled = true;
    }

    void DrawConnectedStatus()
    {
        // HUD hidden during gameplay
    }

    void Connect()
    {
        isConnecting = true;
        statusMessage = "Connecting...";

        if (PhotonNetwork.IsConnected)
        {
            PhotonNetwork.JoinRandomRoom();
        }
        else
        {
            PhotonNetwork.ConnectUsingSettings();
        }
    }

    void Disconnect()
    {
        PhotonNetwork.Disconnect();
        statusMessage = "Disconnected";
    }

    // Photon Callbacks
    public override void OnConnectedToMaster()
    {
        Debug.Log("Connected to Photon Master Server");
        statusMessage = "Connected! Joining room...";

        // Join or create a room
        RoomOptions roomOptions = new RoomOptions();
        roomOptions.MaxPlayers = 10;
        roomOptions.IsVisible = true;
        roomOptions.IsOpen = true;
        Debug.Log($"Attempting to join room 'KlyraFPS_Room' in region: {PhotonNetwork.CloudRegion}");
        PhotonNetwork.JoinOrCreateRoom("KlyraFPS_Room", roomOptions, TypedLobby.Default);
    }

    public override void OnJoinedRoom()
    {
        Debug.Log($"Joined room: {PhotonNetwork.CurrentRoom.Name}");
        isConnecting = false;
        statusMessage = "In Game";

        // Show team selection instead of spawning immediately
        showTeamSelection = true;
    }

    public override void OnJoinRandomFailed(short returnCode, string message)
    {
        Debug.Log("No random room available, creating one...");
        RoomOptions roomOptions = new RoomOptions();
        roomOptions.MaxPlayers = 10;
        PhotonNetwork.CreateRoom("KlyraFPS_" + Random.Range(1000, 9999), roomOptions);
    }

    public override void OnDisconnected(DisconnectCause cause)
    {
        Debug.Log($"Disconnected: {cause}");
        isConnecting = false;
        statusMessage = "Disconnected: " + cause;
    }

    public override void OnPlayerEnteredRoom(Photon.Realtime.Player newPlayer)
    {
        Debug.Log($"Player {newPlayer.NickName} (ID: {newPlayer.ActorNumber}) joined the room. Total players: {PhotonNetwork.CurrentRoom.PlayerCount}");
    }

    public override void OnPlayerLeftRoom(Photon.Realtime.Player otherPlayer)
    {
        Debug.Log($"Player {otherPlayer.NickName} (ID: {otherPlayer.ActorNumber}) left the room. Total players: {PhotonNetwork.CurrentRoom.PlayerCount}");
    }

    void SpawnPlayer()
    {
        if (playerPrefab == null)
        {
            Debug.LogError("Player prefab not assigned!");
            return;
        }

        // Choose spawn point based on team
        Transform teamSpawn = spawnPoint;
        if (selectedTeam == Team.Phantom && phantomSpawnPoint != null)
            teamSpawn = phantomSpawnPoint;
        else if (selectedTeam == Team.Havoc && havocSpawnPoint != null)
            teamSpawn = havocSpawnPoint;

        Vector3 spawnPos = teamSpawn != null ? teamSpawn.position : Vector3.zero;
        Quaternion spawnRot = teamSpawn != null ? teamSpawn.rotation : Quaternion.identity;

        // Rotate Phantom spawn 180 degrees so they face the right direction
        if (selectedTeam == Team.Phantom)
        {
            spawnRot *= Quaternion.Euler(0, 180f, 0);
        }

        // Add small random offset to prevent spawning on top of each other
        spawnPos += new Vector3(Random.Range(-2f, 2f), 0, Random.Range(-2f, 2f));

        // Pass team as instantiation data
        object[] instantiationData = new object[] { (int)selectedTeam };

        // PhotonNetwork.Instantiate spawns for all clients
        GameObject player = PhotonNetwork.Instantiate(playerPrefab.name, spawnPos, spawnRot, 0, instantiationData);
        Debug.Log($"Spawned {selectedTeam} player at {spawnPos}");
    }
}
