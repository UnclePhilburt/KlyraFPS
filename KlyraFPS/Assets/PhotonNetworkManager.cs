using UnityEngine;
using Photon.Pun;
using Photon.Realtime;

public class PhotonNetworkManager : MonoBehaviourPunCallbacks
{
    [Header("Player Settings")]
    public GameObject playerPrefab;
    public Transform spawnPoint;

    [Header("UI")]
    private bool isConnecting = false;
    private string statusMessage = "Click to Connect";

    private GUIStyle buttonStyle;
    private GUIStyle labelStyle;
    private bool stylesInitialized = false;

    void Start()
    {
        // Ensure we can sync scenes
        PhotonNetwork.AutomaticallySyncScene = true;
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

        GUILayout.BeginArea(new Rect(10, 10, 350, 300));

        if (!PhotonNetwork.IsConnected)
        {
            DrawConnectMenu();
        }
        else if (!PhotonNetwork.InRoom)
        {
            GUILayout.Label("Connected to Photon", labelStyle);
            GUILayout.Label("Joining room...", labelStyle);
        }
        else
        {
            DrawConnectedStatus();
        }

        GUILayout.EndArea();
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
        GUILayout.Label("IN GAME", labelStyle);
        GUILayout.Space(10);
        GUILayout.Label($"Room: {PhotonNetwork.CurrentRoom.Name}", labelStyle);
        GUILayout.Label($"Players: {PhotonNetwork.CurrentRoom.PlayerCount}", labelStyle);
        GUILayout.Space(20);

        if (GUILayout.Button("DISCONNECT", buttonStyle, GUILayout.Height(60)))
        {
            Disconnect();
        }
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
        PhotonNetwork.JoinOrCreateRoom("KlyraFPS_Room", roomOptions, TypedLobby.Default);
    }

    public override void OnJoinedRoom()
    {
        Debug.Log($"Joined room: {PhotonNetwork.CurrentRoom.Name}");
        isConnecting = false;
        statusMessage = "In Game";

        // Spawn player
        SpawnPlayer();
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

    void SpawnPlayer()
    {
        if (playerPrefab == null)
        {
            Debug.LogError("Player prefab not assigned!");
            return;
        }

        Vector3 spawnPos = spawnPoint != null ? spawnPoint.position : Vector3.zero;
        Quaternion spawnRot = spawnPoint != null ? spawnPoint.rotation : Quaternion.identity;

        // Add small random offset to prevent spawning on top of each other
        spawnPos += new Vector3(Random.Range(-2f, 2f), 0, Random.Range(-2f, 2f));

        // PhotonNetwork.Instantiate spawns for all clients
        GameObject player = PhotonNetwork.Instantiate(playerPrefab.name, spawnPos, spawnRot);
        Debug.Log($"Spawned player at {spawnPos}");
    }
}
