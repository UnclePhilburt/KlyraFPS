using UnityEngine;
using Unity.Netcode;
using System;
using System.Threading.Tasks;
using Unity.Services.Core;
using Unity.Services.Authentication;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using Unity.Netcode.Transports.UTP;

public class SimpleNetworkMenu : MonoBehaviour
{
    private int playerCount = 0;
    private GUIStyle buttonStyle;
    private GUIStyle labelStyle;
    private GUIStyle textFieldStyle;
    private GUIStyle codeDisplayStyle;
    private bool stylesInitialized = false;

    // Relay
    private string joinCode = "";
    private string currentHostCode = "";
    private string statusMessage = "";
    private bool isConnecting = false;
    private bool servicesReady = false;

    [Header("Settings")]
    public int maxConnections = 16;

    [Tooltip("Enable if browser players will join. Host will use WebSockets for compatibility.")]
    public bool supportWebGLClients = true;

    // Check if we need WebSocket mode (WebGL or hosting for WebGL clients)
    private bool UseWebSockets
    {
        get
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return true;
#else
            return supportWebGLClients;
#endif
        }
    }

    async void Start()
    {
        // Enable WebSockets on transport if needed
        var transport = NetworkManager.Singleton?.GetComponent<UnityTransport>();
        if (transport != null && UseWebSockets)
        {
            transport.UseWebSockets = true;
        }

        await InitializeServices();
    }

    async Task InitializeServices()
    {
        try
        {
            statusMessage = "Initializing...";

            if (UnityServices.State != ServicesInitializationState.Initialized)
            {
                await UnityServices.InitializeAsync();
            }

            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }

            servicesReady = true;
            statusMessage = "";
            Debug.Log($"Services ready. Player ID: {AuthenticationService.Instance.PlayerId}");
        }
        catch (Exception e)
        {
            statusMessage = $"Error: {e.Message}";
            Debug.LogError($"Failed to initialize services: {e.Message}");
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
        textFieldStyle.fontSize = 28;
        textFieldStyle.alignment = TextAnchor.MiddleCenter;
        textFieldStyle.fontStyle = FontStyle.Bold;

        codeDisplayStyle = new GUIStyle(GUI.skin.box);
        codeDisplayStyle.fontSize = 32;
        codeDisplayStyle.fontStyle = FontStyle.Bold;
        codeDisplayStyle.alignment = TextAnchor.MiddleCenter;
        codeDisplayStyle.normal.textColor = Color.yellow;

        stylesInitialized = true;
    }

    void OnGUI()
    {
        InitializeStyles();

        if (NetworkManager.Singleton == null)
        {
            GUILayout.BeginArea(new Rect(10, 10, 300, 100));
            GUILayout.Label("NetworkManager not found in scene!", labelStyle);
            GUILayout.Label("Please add NetworkManager to your scene.", labelStyle);
            GUILayout.EndArea();
            return;
        }

        GUILayout.BeginArea(new Rect(10, 10, 300, 500));

        // Show connection buttons if not connected
        if (!NetworkManager.Singleton.IsClient && !NetworkManager.Singleton.IsServer)
        {
            GUILayout.Label("MULTIPLAYER MENU", labelStyle);
            GUILayout.Space(10);

            // Show status message if any
            if (!string.IsNullOrEmpty(statusMessage))
            {
                GUILayout.Label(statusMessage, labelStyle);
                GUILayout.Space(10);
            }

            // Disable buttons if connecting or services not ready
            GUI.enabled = servicesReady && !isConnecting;

            if (GUILayout.Button("HOST GAME", buttonStyle, GUILayout.Height(60)))
            {
                HostWithRelay();
            }

            GUILayout.Space(20);

            GUILayout.Label("JOIN WITH CODE:", labelStyle);
            GUILayout.Space(5);

            joinCode = GUILayout.TextField(joinCode.ToUpper(), 6, textFieldStyle, GUILayout.Height(50));

            GUILayout.Space(10);

            // Only enable join if code is entered
            GUI.enabled = servicesReady && !isConnecting && joinCode.Length >= 6;

            if (GUILayout.Button("JOIN GAME", buttonStyle, GUILayout.Height(60)))
            {
                JoinWithRelay(joinCode);
            }

            GUI.enabled = true;
        }
        else
        {
            // Show status when connected
            GUILayout.Label("CONNECTED", labelStyle);
            GUILayout.Space(10);

            string status = NetworkManager.Singleton.IsHost ? "HOST" :
                           NetworkManager.Singleton.IsServer ? "SERVER" : "CLIENT";
            GUILayout.Label($"Status: {status}", labelStyle);

            // Show join code if hosting
            if (NetworkManager.Singleton.IsHost && !string.IsNullOrEmpty(currentHostCode))
            {
                GUILayout.Space(10);
                GUILayout.Label("JOIN CODE:", labelStyle);
                GUILayout.Box(currentHostCode, codeDisplayStyle, GUILayout.Height(50));

                if (GUILayout.Button("COPY CODE", buttonStyle, GUILayout.Height(40)))
                {
                    GUIUtility.systemCopyBuffer = currentHostCode;
                    statusMessage = "Copied!";
                }
            }

            // Show player count if host/server
            if (NetworkManager.Singleton.IsServer)
            {
                GUILayout.Space(10);
                int connectedCount = NetworkManager.Singleton.ConnectedClients.Count;
                GUILayout.Label($"Players: {connectedCount}", labelStyle);
            }

            GUILayout.Space(20);

            if (GUILayout.Button("DISCONNECT", buttonStyle, GUILayout.Height(60)))
            {
                NetworkManager.Singleton.Shutdown();
                currentHostCode = "";
                statusMessage = "";
                Debug.Log("Disconnected");
            }
        }

        GUILayout.EndArea();
    }

    // Get the appropriate server endpoint based on platform
    private RelayServerEndpoint GetServerEndpoint(Allocation allocation)
    {
        string connectionType = UseWebSockets ? "wss" : "dtls";

        foreach (var endpoint in allocation.ServerEndpoints)
        {
            if (endpoint.ConnectionType == connectionType)
            {
                return endpoint;
            }
        }

        // Fallback to first available
        Debug.LogWarning($"Could not find {connectionType} endpoint, using first available");
        return allocation.ServerEndpoints[0];
    }

    private RelayServerEndpoint GetServerEndpoint(JoinAllocation allocation)
    {
        string connectionType = UseWebSockets ? "wss" : "dtls";

        foreach (var endpoint in allocation.ServerEndpoints)
        {
            if (endpoint.ConnectionType == connectionType)
            {
                return endpoint;
            }
        }

        // Fallback to first available
        Debug.LogWarning($"Could not find {connectionType} endpoint, using first available");
        return allocation.ServerEndpoints[0];
    }

    async void HostWithRelay()
    {
        try
        {
            isConnecting = true;
            statusMessage = "Creating game...";

            // Create relay allocation
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(maxConnections);

            // Get join code
            currentHostCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
            Debug.Log($"Join Code: {currentHostCode}");

            // Get appropriate endpoint for platform
            var endpoint = GetServerEndpoint(allocation);
            Debug.Log($"Using endpoint: {endpoint.ConnectionType} - {endpoint.Host}:{endpoint.Port}");

            // Configure transport
            var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();

            // Enable WebSockets if needed
            if (UseWebSockets)
            {
                transport.UseWebSockets = true;
            }

            transport.SetRelayServerData(
                endpoint.Host,
                (ushort)endpoint.Port,
                allocation.AllocationIdBytes,
                allocation.Key,
                allocation.ConnectionData,
                null,
                UseWebSockets // isSecure - true for wss
            );

            // Start host
            NetworkManager.Singleton.StartHost();

            statusMessage = "";
            Debug.Log("Host started with Relay");
        }
        catch (Exception e)
        {
            statusMessage = $"Host failed: {e.Message}";
            Debug.LogError($"Failed to host: {e.Message}");
        }
        finally
        {
            isConnecting = false;
        }
    }

    async void JoinWithRelay(string code)
    {
        try
        {
            isConnecting = true;
            statusMessage = "Joining game...";

            code = code.Trim().ToUpper();
            Debug.Log($"Joining with code: {code}");

            // Join relay allocation
            JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(code);

            // Get appropriate endpoint for platform
            var endpoint = GetServerEndpoint(joinAllocation);
            Debug.Log($"Using endpoint: {endpoint.ConnectionType} - {endpoint.Host}:{endpoint.Port}");

            // Configure transport
            var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();

            // Enable WebSockets if needed
            if (UseWebSockets)
            {
                transport.UseWebSockets = true;
            }

            transport.SetRelayServerData(
                endpoint.Host,
                (ushort)endpoint.Port,
                joinAllocation.AllocationIdBytes,
                joinAllocation.Key,
                joinAllocation.ConnectionData,
                joinAllocation.HostConnectionData,
                UseWebSockets // isSecure - true for wss
            );

            // Start client
            NetworkManager.Singleton.StartClient();

            statusMessage = "";
            Debug.Log("Client started with Relay");
        }
        catch (Exception e)
        {
            statusMessage = $"Join failed: {e.Message}";
            Debug.LogError($"Failed to join: {e.Message}");
        }
        finally
        {
            isConnecting = false;
        }
    }

    void OnEnable()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
        }
    }

    void OnDisable()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }
    }

    void OnClientConnected(ulong clientId)
    {
        Debug.Log($"Client {clientId} connected");
        playerCount++;
    }

    void OnClientDisconnected(ulong clientId)
    {
        Debug.Log($"Client {clientId} disconnected");
        playerCount--;
    }
}
