using UnityEngine;
using Mirror;
using Mirror.SimpleWeb;

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

    private string statusMessage = "";
    private bool isConnecting = false;

    void Start()
    {
        // Auto-start server if running as headless/batch mode
        if (Application.isBatchMode)
        {
            string portEnv = System.Environment.GetEnvironmentVariable("PORT");
            if (!string.IsNullOrEmpty(portEnv) && ushort.TryParse(portEnv, out ushort port))
            {
                serverPort = port;
                Debug.Log($"Using PORT from environment: {serverPort}");
            }

            StartServer();
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
        if (Application.isBatchMode) return;

        InitializeStyles();

        if (NetworkManager.singleton == null)
        {
            GUILayout.BeginArea(new Rect(10, 10, 300, 100));
            GUILayout.Label("NetworkManager not found!", labelStyle);
            GUILayout.EndArea();
            return;
        }

        GUILayout.BeginArea(new Rect(10, 10, 350, 400));

        if (!NetworkClient.isConnected && !NetworkServer.active)
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

        if (GUILayout.Button("JOIN SERVER", buttonStyle, GUILayout.Height(60)))
        {
            JoinServer();
        }

#if !UNITY_WEBGL || UNITY_EDITOR
        GUILayout.Space(20);
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
#endif

        GUI.enabled = true;
    }

    void DrawConnectedStatus()
    {
        GUILayout.Label("CONNECTED", labelStyle);
        GUILayout.Space(10);

        string status = NetworkServer.active && NetworkClient.isConnected ? "HOST" :
                       NetworkServer.active ? "SERVER" : "CLIENT";
        GUILayout.Label($"Status: {status}", labelStyle);

        if (NetworkServer.active)
        {
            GUILayout.Label($"Players: {NetworkServer.connections.Count}", labelStyle);
        }

        GUILayout.Space(20);

        if (GUILayout.Button("DISCONNECT", buttonStyle, GUILayout.Height(60)))
        {
            if (NetworkServer.active && NetworkClient.isConnected)
            {
                NetworkManager.singleton.StopHost();
            }
            else if (NetworkServer.active)
            {
                NetworkManager.singleton.StopServer();
            }
            else
            {
                NetworkManager.singleton.StopClient();
            }
            statusMessage = "";
        }
    }

    void JoinServer()
    {
        isConnecting = true;
        statusMessage = "Connecting...";

        // Configure SimpleWebTransport for WebGL
        var transport = NetworkManager.singleton.GetComponent<SimpleWebTransport>();
        if (transport != null)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            // WebGL must use WSS when page is served over HTTPS
            transport.clientUseWss = true;
            transport.port = 443;
            Debug.Log("WebGL: Using WSS on port 443");
#else
            transport.clientUseWss = false;
            transport.port = serverPort;
#endif
        }

        NetworkManager.singleton.networkAddress = serverAddress;
        NetworkManager.singleton.StartClient();

        Debug.Log($"Connecting to {serverAddress}");
    }

    void StartHost()
    {
        NetworkManager.singleton.StartHost();
        Debug.Log($"Host started");
    }

    void StartServer()
    {
        NetworkManager.singleton.StartServer();
        Debug.Log($"Server started");
    }

    void Update()
    {
        if (!isConnecting) return;
        if (NetworkManager.singleton == null) return;

        // Update connection status
        if (NetworkClient.isConnected)
        {
            statusMessage = "";
            isConnecting = false;
            Debug.Log("Connected to server!");
        }
        else if (!NetworkClient.active)
        {
            statusMessage = "Connection failed";
            isConnecting = false;
        }
    }
}
