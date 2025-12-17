using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;

public class SimpleNetworkMenu : MonoBehaviour
{
    private GUIStyle buttonStyle;
    private GUIStyle labelStyle;
    private GUIStyle textFieldStyle;
    private bool stylesInitialized = false;

    [Header("Server Settings")]
    [Tooltip("Address of your Fly.io server")]
    public string serverAddress = "klyrafps.fly.dev";

    [Tooltip("Port for the game server")]
    public ushort serverPort = 7777;

    [Tooltip("Enable for WebGL builds - uses WebSockets")]
    public bool useWebSockets = true;

    private string statusMessage = "";
    private bool isConnecting = false;

    void Start()
    {
        // Auto-start server if running as headless/batch mode (on Render)
        if (Application.isBatchMode)
        {
            // Check for PORT environment variable (Render sets this)
            string portEnv = System.Environment.GetEnvironmentVariable("PORT");
            if (!string.IsNullOrEmpty(portEnv) && ushort.TryParse(portEnv, out ushort port))
            {
                serverPort = port;
                Debug.Log($"Using PORT from environment: {serverPort}");
            }

            StartServer();
        }

        // Configure transport
        ConfigureTransport();
    }

    void ConfigureTransport()
    {
        var transport = NetworkManager.Singleton?.GetComponent<UnityTransport>();
        if (transport != null)
        {
            // Always use WebSockets for browser compatibility
#if UNITY_WEBGL && !UNITY_EDITOR
            transport.UseWebSockets = true;
#else
            transport.UseWebSockets = useWebSockets;
#endif
        }
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

        textFieldStyle = new GUIStyle(GUI.skin.textField);
        textFieldStyle.fontSize = 18;
        textFieldStyle.alignment = TextAnchor.MiddleCenter;

        stylesInitialized = true;
    }

    void OnGUI()
    {
        // Don't show UI in headless mode
        if (Application.isBatchMode) return;

        InitializeStyles();

        if (NetworkManager.Singleton == null)
        {
            GUILayout.BeginArea(new Rect(10, 10, 300, 100));
            GUILayout.Label("NetworkManager not found!", labelStyle);
            GUILayout.EndArea();
            return;
        }

        GUILayout.BeginArea(new Rect(10, 10, 350, 400));

        if (!NetworkManager.Singleton.IsClient && !NetworkManager.Singleton.IsServer)
        {
            DrawConnectionMenu();
        }
        else
        {
            DrawConnectedStatus();
        }

        GUILayout.EndArea();
    }

    void DrawConnectionMenu()
    {
        GUILayout.Label("KLYRA FPS", labelStyle);
        GUILayout.Space(10);

        if (!string.IsNullOrEmpty(statusMessage))
        {
            GUILayout.Label(statusMessage, labelStyle);
            GUILayout.Space(10);
        }

        GUILayout.Label("Server Address:", labelStyle);
        serverAddress = GUILayout.TextField(serverAddress, textFieldStyle, GUILayout.Height(35));
        GUILayout.Space(10);

        GUI.enabled = !isConnecting;

        // Join server button (for players)
        if (GUILayout.Button("JOIN SERVER", buttonStyle, GUILayout.Height(60)))
        {
            JoinServer();
        }

        GUILayout.Space(20);

        // Local testing options
        GUILayout.Label("--- Local Testing ---", labelStyle);
        GUILayout.Space(5);

        if (GUILayout.Button("HOST (Local)", buttonStyle, GUILayout.Height(40)))
        {
            StartHost();
        }

        if (GUILayout.Button("SERVER (Local)", buttonStyle, GUILayout.Height(40)))
        {
            StartServer();
        }

        GUI.enabled = true;
    }

    void DrawConnectedStatus()
    {
        GUILayout.Label("CONNECTED", labelStyle);
        GUILayout.Space(10);

        string status = NetworkManager.Singleton.IsHost ? "HOST" :
                       NetworkManager.Singleton.IsServer ? "SERVER" : "CLIENT";
        GUILayout.Label($"Status: {status}", labelStyle);

        if (NetworkManager.Singleton.IsServer)
        {
            int playerCount = NetworkManager.Singleton.ConnectedClients.Count;
            GUILayout.Label($"Players: {playerCount}", labelStyle);
        }

        GUILayout.Space(20);

        if (GUILayout.Button("DISCONNECT", buttonStyle, GUILayout.Height(60)))
        {
            NetworkManager.Singleton.Shutdown();
            statusMessage = "";
        }
    }

    void JoinServer()
    {
        isConnecting = true;
        statusMessage = "Connecting...";

        var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();

        // Configure connection
#if UNITY_WEBGL && !UNITY_EDITOR
        transport.UseWebSockets = true;
#else
        transport.UseWebSockets = useWebSockets;
#endif

        transport.ConnectionData.Address = serverAddress;
        transport.ConnectionData.Port = serverPort;

        Debug.Log($"Connecting to {serverAddress}:{serverPort} (WebSockets: {transport.UseWebSockets})");

        NetworkManager.Singleton.OnClientConnectedCallback += OnConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnDisconnected;

        if (!NetworkManager.Singleton.StartClient())
        {
            statusMessage = "Failed to start client";
            isConnecting = false;
        }
    }

    void StartHost()
    {
        var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        transport.UseWebSockets = useWebSockets;
        transport.ConnectionData.Address = "0.0.0.0";
        transport.ConnectionData.Port = serverPort;

        if (NetworkManager.Singleton.StartHost())
        {
            Debug.Log($"Host started on port {serverPort}");
        }
    }

    void StartServer()
    {
        var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        transport.UseWebSockets = true; // Always WebSockets for browser clients
        transport.ConnectionData.Address = "0.0.0.0";
        transport.ConnectionData.Port = serverPort;

        if (NetworkManager.Singleton.StartServer())
        {
            Debug.Log($"Server started on port {serverPort} with WebSockets");
        }
        else
        {
            Debug.LogError("Failed to start server");
        }
    }

    void OnConnected(ulong clientId)
    {
        if (clientId == NetworkManager.Singleton.LocalClientId)
        {
            statusMessage = "";
            isConnecting = false;
            Debug.Log("Connected to server!");
        }
    }

    void OnDisconnected(ulong clientId)
    {
        if (clientId == NetworkManager.Singleton.LocalClientId)
        {
            statusMessage = "Disconnected";
            isConnecting = false;
        }
    }

    void OnDestroy()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnDisconnected;
        }
    }
}
