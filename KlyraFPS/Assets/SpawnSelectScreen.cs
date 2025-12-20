using UnityEngine;
using UnityEngine.InputSystem;
using Photon.Pun;
using System.Collections.Generic;

public class SpawnSelectScreen : MonoBehaviour
{
    public static SpawnSelectScreen Instance { get; private set; }

    [Header("Camera Settings")]
    public float mapHeight = 80f;
    public float riseSpeed = 30f;
    public float lookDownSpeed = 2f;

    [Header("UI")]
    public Color friendlyColor = new Color(0.3f, 0.7f, 1f);
    public Color enemyColor = new Color(1f, 0.3f, 0.3f);
    public Color neutralColor = new Color(0.7f, 0.7f, 0.7f);
    public Color selectedColor = new Color(0f, 1f, 0.5f);

    // State
    private bool isActive = false;
    private Camera playerCamera;
    private Transform cameraTransform;
    private Team playerTeam;
    private CapturePoint selectedPoint;
    private CapturePoint[] allPoints;
    private float respawnTimer = 0f;
    private float respawnDelay = 5f;
    private bool baseSelected = false;
    private Transform baseSpawnPoint;

    // Camera animation
    private Vector3 deathPosition;
    private Vector3 targetCameraPosition;
    private Quaternion targetCameraRotation;
    private bool cameraReachedTarget = false;

    // Cached
    private FPSControllerPhoton localPlayer;
    private GUIStyle labelStyle;
    private GUIStyle buttonStyle;
    private GUIStyle timerStyle;
    private bool stylesInitialized = false;

    // Callback for when spawn is selected
    public System.Action<Vector3, Quaternion> OnSpawnSelected;

    void Awake()
    {
        Instance = this;
    }

    void InitStyles()
    {
        if (stylesInitialized) return;

        labelStyle = new GUIStyle(GUI.skin.label);
        labelStyle.fontSize = 24;
        labelStyle.fontStyle = FontStyle.Bold;
        labelStyle.alignment = TextAnchor.MiddleCenter;
        labelStyle.normal.textColor = Color.white;

        buttonStyle = new GUIStyle(GUI.skin.button);
        buttonStyle.fontSize = 20;
        buttonStyle.fontStyle = FontStyle.Bold;

        timerStyle = new GUIStyle(GUI.skin.label);
        timerStyle.fontSize = 48;
        timerStyle.fontStyle = FontStyle.Bold;
        timerStyle.alignment = TextAnchor.MiddleCenter;
        timerStyle.normal.textColor = Color.white;

        stylesInitialized = true;
    }

    public void Show(FPSControllerPhoton player, float delay = 5f)
    {
        localPlayer = player;
        playerTeam = player.playerTeam;
        respawnDelay = delay;
        respawnTimer = delay;
        selectedPoint = null;
        baseSelected = false;
        cameraReachedTarget = false;
        isActive = true;

        // Get player's camera
        playerCamera = Camera.main;
        if (playerCamera == null)
        {
            // Try to find player camera by name
            GameObject camObj = GameObject.Find($"PlayerCamera_{player.photonView.ViewID}");
            if (camObj != null)
            {
                playerCamera = camObj.GetComponent<Camera>();
            }
        }

        if (playerCamera != null)
        {
            cameraTransform = playerCamera.transform;
            deathPosition = cameraTransform.position;

            // Ensure camera is unparented and reset (important after vehicle deaths)
            cameraTransform.SetParent(null);

            // Sanity check - if camera is in a weird position (underground or too high), reset it
            if (deathPosition.y < -10f || deathPosition.y > 500f)
            {
                deathPosition = player.transform.position + Vector3.up * 20f;
                cameraTransform.position = deathPosition;
            }

            // Reset camera rotation to look forward (avoids weird angles from vehicle deaths)
            cameraTransform.rotation = Quaternion.Euler(0f, cameraTransform.eulerAngles.y, 0f);
        }

        // Find all capture points
        allPoints = FindObjectsOfType<CapturePoint>();

        // Find base spawn point
        GameUIManager gameUI = FindObjectOfType<GameUIManager>();
        if (gameUI != null)
        {
            baseSpawnPoint = playerTeam == Team.Phantom ? gameUI.phantomSpawnPoint : gameUI.havocSpawnPoint;
        }

        // Auto-select first friendly point, or base if none owned
        bool foundFriendlyPoint = false;
        foreach (var point in allPoints)
        {
            if (point.owningTeam == playerTeam)
            {
                selectedPoint = point;
                foundFriendlyPoint = true;
                break;
            }
        }

        // If no friendly points, default to base
        if (!foundFriendlyPoint)
        {
            baseSelected = true;
        }

        // Calculate target camera position (above map center)
        Vector3 mapCenter = CalculateMapCenter();
        targetCameraPosition = new Vector3(mapCenter.x, mapHeight, mapCenter.z);
        targetCameraRotation = Quaternion.Euler(90f, 0f, 0f); // Look straight down

        // Show cursor
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public void Hide()
    {
        isActive = false;
        cameraReachedTarget = false;
    }

    Vector3 CalculateMapCenter()
    {
        if (allPoints == null || allPoints.Length == 0)
            return Vector3.zero;

        Vector3 sum = Vector3.zero;
        foreach (var point in allPoints)
        {
            sum += point.transform.position;
        }
        return sum / allPoints.Length;
    }

    void Update()
    {
        if (!isActive) return;

        // Animate camera rising up and looking down
        if (cameraTransform != null)
        {
            // Smoothly move camera to target position
            cameraTransform.position = Vector3.Lerp(cameraTransform.position, targetCameraPosition, Time.deltaTime * riseSpeed * 0.1f);

            // Smoothly rotate to look down
            cameraTransform.rotation = Quaternion.Slerp(cameraTransform.rotation, targetCameraRotation, Time.deltaTime * lookDownSpeed);

            // Check if camera reached target
            if (Vector3.Distance(cameraTransform.position, targetCameraPosition) < 1f)
            {
                cameraReachedTarget = true;
            }
        }

        // Countdown timer
        if (respawnTimer > 0)
        {
            respawnTimer -= Time.deltaTime;
        }

        var mouse = Mouse.current;
        var keyboard = Keyboard.current;

        // Handle click to select point (only when camera is in position)
        if (cameraReachedTarget && mouse != null && mouse.leftButton.wasPressedThisFrame)
        {
            TrySelectPoint();
        }

        // Scroll to zoom (adjust camera height)
        if (cameraReachedTarget && mouse != null)
        {
            float scroll = mouse.scroll.ReadValue().y;
            if (scroll != 0)
            {
                targetCameraPosition.y = Mathf.Clamp(targetCameraPosition.y - scroll * 2f, 40f, 150f);
            }
        }

        // WASD to pan camera
        if (cameraReachedTarget && keyboard != null)
        {
            Vector3 pan = Vector3.zero;
            if (keyboard.wKey.isPressed) pan.z += 1;
            if (keyboard.sKey.isPressed) pan.z -= 1;
            if (keyboard.aKey.isPressed) pan.x -= 1;
            if (keyboard.dKey.isPressed) pan.x += 1;

            if (pan != Vector3.zero)
            {
                targetCameraPosition += pan.normalized * 50f * Time.deltaTime;
            }
        }
    }

    void TrySelectPoint()
    {
        var mouse = Mouse.current;
        if (mouse == null || playerCamera == null) return;

        Vector2 mousePos = mouse.position.ReadValue();

        // Check if clicking on base spawn
        if (baseSpawnPoint != null)
        {
            Vector3 baseScreenPos = playerCamera.WorldToScreenPoint(baseSpawnPoint.position);
            if (baseScreenPos.z > 0)
            {
                float baseDist = Vector2.Distance(mousePos, new Vector2(baseScreenPos.x, baseScreenPos.y));
                if (baseDist < 50f)
                {
                    baseSelected = true;
                    selectedPoint = null;
                    return;
                }
            }
        }

        // Find closest capture point to click
        float closestDist = 50f; // Click radius in pixels
        CapturePoint closestPoint = null;

        foreach (var point in allPoints)
        {
            // Only allow friendly points (can't spawn at neutral or enemy)
            if (point.owningTeam != playerTeam)
                continue;

            Vector3 pointScreenPos = playerCamera.WorldToScreenPoint(point.transform.position);
            if (pointScreenPos.z < 0) continue;

            float dist = Vector2.Distance(mousePos, new Vector2(pointScreenPos.x, pointScreenPos.y));

            if (dist < closestDist)
            {
                closestDist = dist;
                closestPoint = point;
            }
        }

        if (closestPoint != null)
        {
            selectedPoint = closestPoint;
            baseSelected = false;
        }
    }

    void OnGUI()
    {
        if (!isActive) return;

        // Refresh camera reference if lost
        if (playerCamera == null)
        {
            playerCamera = Camera.main;
            if (playerCamera != null)
                cameraTransform = playerCamera.transform;
        }

        InitStyles();

        // Draw title
        GUI.Label(new Rect(0, 20, Screen.width, 40), "SELECT SPAWN POINT", labelStyle);

        // Draw timer
        if (respawnTimer > 0)
        {
            GUI.Label(new Rect(0, 70, Screen.width, 60), $"Respawn in: {Mathf.CeilToInt(respawnTimer)}", timerStyle);
        }

        // Draw capture points
        foreach (var point in allPoints)
        {
            DrawPointMarker(point);
        }

        // Draw team base spawns
        DrawBaseSpawns();

        // Draw spawn button
        float buttonWidth = 200f;
        float buttonHeight = 50f;
        float buttonX = Screen.width / 2f - buttonWidth / 2f;
        float buttonY = Screen.height - 100f;

        bool canSpawn = respawnTimer <= 0 && (selectedPoint != null || baseSelected);
        GUI.enabled = canSpawn;

        string spawnLocation = baseSelected ? "BASE" : (selectedPoint != null ? selectedPoint.pointName : "---");
        if (GUI.Button(new Rect(buttonX, buttonY, buttonWidth, buttonHeight), $"DEPLOY TO {spawnLocation}", buttonStyle))
        {
            Spawn();
        }

        GUI.enabled = true;

        // Instructions
        GUI.Label(new Rect(0, Screen.height - 40, Screen.width, 30),
            "Click to select spawn point | Scroll to zoom | WASD to pan",
            new GUIStyle(labelStyle) { fontSize = 14 });
    }

    void DrawPointMarker(CapturePoint point)
    {
        if (playerCamera == null) return;

        Vector3 screenPos = playerCamera.WorldToScreenPoint(point.transform.position);

        // Check if on screen
        if (screenPos.z < 0) return;

        // Flip Y for GUI
        screenPos.y = Screen.height - screenPos.y;

        // Determine color
        Color color;
        if (point == selectedPoint)
        {
            color = selectedColor;
        }
        else if (point.owningTeam == playerTeam)
        {
            color = friendlyColor;
        }
        else if (point.owningTeam == Team.None)
        {
            color = neutralColor;
        }
        else
        {
            color = enemyColor;
        }

        // Draw marker
        float size = point == selectedPoint ? 40f : 30f;
        Rect markerRect = new Rect(screenPos.x - size/2, screenPos.y - size/2, size, size);

        // Draw filled circle (using box as placeholder)
        GUI.color = color;
        GUI.DrawTexture(markerRect, Texture2D.whiteTexture);

        // Draw outline if selected
        if (point == selectedPoint)
        {
            GUI.color = Color.white;
            float outlineSize = size + 6f;
            Rect outlineRect = new Rect(screenPos.x - outlineSize/2, screenPos.y - outlineSize/2, outlineSize, outlineSize);
            // Draw outline by drawing larger box behind
        }

        // Draw point name
        GUI.color = Color.white;
        GUIStyle nameStyle = new GUIStyle(labelStyle);
        nameStyle.fontSize = 16;

        string status = "";
        if (point.owningTeam == playerTeam)
            status = " (Friendly)";
        else if (point.owningTeam == Team.None)
            status = " (Neutral)";
        else
            status = " (Enemy)";

        GUI.Label(new Rect(screenPos.x - 50, screenPos.y + size/2 + 5, 100, 25),
            point.pointName + status, nameStyle);

        // Show capture progress
        if (Mathf.Abs(point.captureProgress) > 0.01f && point.owningTeam == Team.None)
        {
            float progressWidth = 60f;
            float progressHeight = 8f;
            Rect progressBg = new Rect(screenPos.x - progressWidth/2, screenPos.y + size/2 + 28, progressWidth, progressHeight);

            GUI.color = Color.black;
            GUI.DrawTexture(progressBg, Texture2D.whiteTexture);

            float progress = (point.captureProgress + 1f) / 2f; // Convert -1,1 to 0,1
            GUI.color = point.captureProgress > 0 ? friendlyColor : enemyColor;
            Rect progressFill = new Rect(progressBg.x, progressBg.y, progressWidth * Mathf.Abs(point.captureProgress), progressHeight);
            GUI.DrawTexture(progressFill, Texture2D.whiteTexture);
        }

        GUI.color = Color.white;
    }

    void DrawBaseSpawns()
    {
        // Draw team base spawn point
        if (baseSpawnPoint == null) return;
        if (playerCamera == null) playerCamera = Camera.main;
        if (playerCamera == null) return;

        Vector3 screenPos = playerCamera.WorldToScreenPoint(baseSpawnPoint.position);
        if (screenPos.z > 0)
        {
            screenPos.y = Screen.height - screenPos.y;

            // Highlight if selected
            Color color = baseSelected ? selectedColor : friendlyColor;
            float size = baseSelected ? 35f : 25f;

            GUI.color = color;
            Rect rect = new Rect(screenPos.x - size/2, screenPos.y - size/2, size, size);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);

            // Draw selection ring if selected
            if (baseSelected)
            {
                GUI.color = Color.white;
                float ringSize = size + 8f;
                // Simple outline effect
            }

            GUI.color = Color.white;
            GUIStyle style = new GUIStyle(labelStyle) { fontSize = 14 };
            string label = baseSelected ? ">> BASE <<" : "BASE";
            GUI.Label(new Rect(screenPos.x - 50, screenPos.y + size/2 + 2, 100, 20), label, style);
        }

        GUI.color = Color.white;
    }

    void Spawn()
    {
        Vector3 spawnPos;
        Quaternion spawnRot;

        if (baseSelected && baseSpawnPoint != null)
        {
            // Spawn at team base
            Vector2 offset = Random.insideUnitCircle * 3f;
            spawnPos = baseSpawnPoint.position + new Vector3(offset.x, 0, offset.y);
            spawnRot = baseSpawnPoint.rotation;

            // Rotate Phantom 180 degrees
            if (playerTeam == Team.Phantom)
            {
                spawnRot *= Quaternion.Euler(0, 180f, 0);
            }
        }
        else if (selectedPoint != null)
        {
            // Spawn at capture point
            Vector2 offset = Random.insideUnitCircle * 5f;
            spawnPos = selectedPoint.transform.position + new Vector3(offset.x, 0, offset.y);

            // Face toward center of map
            Vector3 mapCenter = CalculateMapCenter();
            Vector3 lookDir = (mapCenter - spawnPos).normalized;
            lookDir.y = 0;
            spawnRot = lookDir != Vector3.zero ? Quaternion.LookRotation(lookDir) : Quaternion.identity;
        }
        else
        {
            return; // Nothing selected
        }

        Hide();

        // Trigger spawn callback
        OnSpawnSelected?.Invoke(spawnPos, spawnRot);
    }

    public bool IsActive => isActive;
}
