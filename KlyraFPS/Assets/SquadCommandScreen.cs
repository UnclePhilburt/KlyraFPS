using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;

public class SquadCommandScreen : MonoBehaviour
{
    public static SquadCommandScreen Instance { get; private set; }

    [Header("Camera Settings")]
    public float mapHeight = 80f;
    public float riseSpeed = 30f;
    public float lookDownSpeed = 2f;
    public float panSpeed = 80f;
    public float edgePanSpeed = 60f;
    public float zoomSpeed = 30f;
    public float minZoom = 30f;
    public float maxZoom = 150f;
    public int edgePanMargin = 20;
    public float rotateSpeed = 0.3f;
    public float tiltSpeed = 0.2f;
    public float minTilt = 30f;  // Minimum angle (more horizontal)
    public float maxTilt = 90f;  // Maximum angle (straight down)

    // State
    private bool isActive = false;
    private FPSControllerPhoton player;
    private Camera playerCamera;
    private Transform cameraTransform;
    private List<AIController> squadMembers = new List<AIController>();
    private CapturePoint[] allPoints;

    // Camera
    private Vector3 savedCameraPos;
    private Quaternion savedCameraRot;
    private Vector3 targetCameraPos;
    private Quaternion targetCameraRot;
    private float currentZoom;
    private bool cameraReachedTarget = false;
    private float cameraYaw = 0f;    // Horizontal rotation
    private float cameraTilt = 90f;  // Vertical angle (90 = straight down)

    // Selection
    private List<AIController> selectedUnits = new List<AIController>();
    private bool isDragging = false;
    private Vector2 dragStart;
    private Vector2 dragEnd;

    // Orders
    private bool isIssuingOrder = false;
    private Vector3 orderTargetPos;

    // Detail panel
    private AIController detailUnit = null;

    // Formation drag
    private bool isFormationDragging = false;
    private Vector3 formationDragStart;
    private Vector3 formationDragEnd;
    private Vector2 formationDragStartScreen;
    private float minFormationDragDistance = 30f; // Minimum screen pixels to count as drag

    // UI
    private Texture2D selectionTex;
    private Texture2D whiteTex;
    private Texture2D darkTex;
    private Texture2D healthBarBg;
    private Texture2D healthBarFill;

    void Awake()
    {
        Instance = this;
        CreateTextures();
    }

    void OnDestroy()
    {
        // Clear static instance when destroyed
        if (Instance == this)
        {
            Instance = null;
        }
    }

    void CreateTextures()
    {
        // Selection box texture (semi-transparent green)
        selectionTex = new Texture2D(1, 1);
        selectionTex.SetPixel(0, 0, new Color(0.2f, 1f, 0.3f, 0.2f));
        selectionTex.Apply();

        whiteTex = new Texture2D(1, 1);
        whiteTex.SetPixel(0, 0, Color.white);
        whiteTex.Apply();

        darkTex = new Texture2D(1, 1);
        darkTex.SetPixel(0, 0, new Color(0.1f, 0.1f, 0.12f, 0.95f));
        darkTex.Apply();

        healthBarBg = new Texture2D(1, 1);
        healthBarBg.SetPixel(0, 0, new Color(0.2f, 0.2f, 0.2f, 0.8f));
        healthBarBg.Apply();

        healthBarFill = new Texture2D(1, 1);
        healthBarFill.SetPixel(0, 0, new Color(0.3f, 0.9f, 0.3f, 1f));
        healthBarFill.Apply();
    }

    public void Show(FPSControllerPhoton playerController)
    {
        Debug.Log($"SquadCommandScreen.Show() called. Was active: {isActive}");

        if (isActive)
        {
            Debug.LogWarning("SquadCommandScreen.Show() called but already active - ignoring");
            return;
        }

        player = playerController;
        isActive = true;
        selectedUnits.Clear();
        cameraReachedTarget = false;
        isDragging = false;

        // Get camera
        playerCamera = Camera.main;
        if (playerCamera == null)
        {
            GameObject camObj = GameObject.Find($"PlayerCamera_{player.photonView.ViewID}");
            if (camObj != null) playerCamera = camObj.GetComponent<Camera>();
        }

        if (playerCamera != null)
        {
            cameraTransform = playerCamera.transform;
            savedCameraPos = cameraTransform.position;
            savedCameraRot = cameraTransform.rotation;
        }

        // Setup overhead view
        allPoints = FindObjectsOfType<CapturePoint>();
        Vector3 mapCenter = CalculateMapCenter();
        currentZoom = mapHeight;
        cameraYaw = 0f;
        cameraTilt = 90f;
        targetCameraPos = new Vector3(mapCenter.x, currentZoom, mapCenter.z);
        targetCameraRot = Quaternion.Euler(cameraTilt, cameraYaw, 0f);

        RefreshSquadList();

        // Select all by default
        selectedUnits.AddRange(squadMembers);

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public void Hide()
    {
        Debug.Log($"SquadCommandScreen.Hide() called. Was active: {isActive}");

        if (!isActive)
        {
            Debug.LogWarning("SquadCommandScreen.Hide() called but not active - ignoring");
            return;
        }

        isActive = false;
        cameraReachedTarget = false;

        if (cameraTransform != null)
        {
            cameraTransform.position = savedCameraPos;
            cameraTransform.rotation = savedCameraRot;
        }

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void RefreshSquadList()
    {
        squadMembers.Clear();
        if (player == null) return;

        AIController[] allAI = FindObjectsOfType<AIController>();
        foreach (var ai in allAI)
        {
            if (ai.IsInSquad() && ai.GetPlayerSquadLeader() == player)
            {
                squadMembers.Add(ai);
            }
        }
    }

    Vector3 CalculateMapCenter()
    {
        if (allPoints == null || allPoints.Length == 0)
            return player != null ? player.transform.position : Vector3.zero;

        Vector3 sum = Vector3.zero;
        foreach (var point in allPoints) sum += point.transform.position;
        return sum / allPoints.Length;
    }

    void Update()
    {
        if (!isActive) return;

        var keyboard = Keyboard.current;
        var mouse = Mouse.current;

        // Close on ESC only (TAB is handled by FPSControllerPhoton)
        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
        {
            Debug.Log($"SquadCommandScreen: ESC pressed while active, calling Hide()");
            Hide();
            return;
        }

        // Camera animation
        if (cameraTransform != null)
        {
            cameraTransform.position = Vector3.Lerp(cameraTransform.position, targetCameraPos, Time.deltaTime * riseSpeed * 0.1f);
            cameraTransform.rotation = Quaternion.Slerp(cameraTransform.rotation, targetCameraRot, Time.deltaTime * lookDownSpeed);

            if (Vector3.Distance(cameraTransform.position, targetCameraPos) < 2f)
            {
                cameraReachedTarget = true;
            }
        }

        if (!cameraReachedTarget) return;

        // === RTS CONTROLS ===
        HandleCameraControls(keyboard, mouse);
        HandleSelection(mouse, keyboard);
        HandleOrders(mouse, keyboard);
        HandleHotkeys(keyboard);
    }

    void HandleCameraControls(Keyboard keyboard, Mouse mouse)
    {
        Vector3 pan = Vector3.zero;

        // WASD - relative to camera rotation
        if (keyboard != null)
        {
            if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) pan.z += 1;
            if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) pan.z -= 1;
            if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) pan.x -= 1;
            if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) pan.x += 1;
        }

        // Edge pan
        if (mouse != null)
        {
            Vector2 mousePos = mouse.position.ReadValue();
            if (mousePos.x < edgePanMargin) pan.x -= 1;
            if (mousePos.x > Screen.width - edgePanMargin) pan.x += 1;
            if (mousePos.y < edgePanMargin) pan.z -= 1;
            if (mousePos.y > Screen.height - edgePanMargin) pan.z += 1;
        }

        if (pan != Vector3.zero)
        {
            float speed = keyboard != null && keyboard.shiftKey.isPressed ? panSpeed * 2f : panSpeed;

            // Rotate pan direction by camera yaw
            Quaternion yawRotation = Quaternion.Euler(0f, cameraYaw, 0f);
            Vector3 rotatedPan = yawRotation * pan.normalized;

            targetCameraPos += rotatedPan * speed * Time.deltaTime;
        }

        // Zoom
        if (mouse != null)
        {
            float scroll = mouse.scroll.ReadValue().y;
            if (scroll != 0)
            {
                currentZoom = Mathf.Clamp(currentZoom - scroll * zoomSpeed * Time.deltaTime * 5f, minZoom, maxZoom);
                targetCameraPos.y = currentZoom;
            }

            // Middle mouse - rotate and tilt camera
            if (mouse.middleButton.isPressed)
            {
                Vector2 mouseDelta = mouse.delta.ReadValue();

                // Horizontal movement rotates camera
                cameraYaw += mouseDelta.x * rotateSpeed;

                // Vertical movement tilts camera
                cameraTilt -= mouseDelta.y * tiltSpeed;
                cameraTilt = Mathf.Clamp(cameraTilt, minTilt, maxTilt);

                // Update target rotation
                targetCameraRot = Quaternion.Euler(cameraTilt, cameraYaw, 0f);
            }
        }

        // Reset camera angle with R key
        if (keyboard != null && keyboard.rKey.wasPressedThisFrame)
        {
            cameraYaw = 0f;
            cameraTilt = 90f;
            targetCameraRot = Quaternion.Euler(cameraTilt, cameraYaw, 0f);
        }
    }

    void HandleSelection(Mouse mouse, Keyboard keyboard)
    {
        if (mouse == null) return;

        Vector2 mousePos = mouse.position.ReadValue();

        // Left click - start selection
        if (mouse.leftButton.wasPressedThisFrame)
        {
            dragStart = mousePos;
            isDragging = true;

            // Check if clicking on a unit
            AIController clickedUnit = GetUnitAtScreenPos(mousePos);
            if (clickedUnit != null)
            {
                bool addToSelection = keyboard != null && (keyboard.shiftKey.isPressed || keyboard.ctrlKey.isPressed);

                if (addToSelection)
                {
                    if (selectedUnits.Contains(clickedUnit))
                        selectedUnits.Remove(clickedUnit);
                    else
                        selectedUnits.Add(clickedUnit);
                }
                else
                {
                    selectedUnits.Clear();
                    selectedUnits.Add(clickedUnit);
                }
                isDragging = false;
            }
            else if (keyboard == null || !keyboard.shiftKey.isPressed)
            {
                selectedUnits.Clear();
            }
        }

        // Dragging
        if (mouse.leftButton.isPressed && isDragging)
        {
            dragEnd = mousePos;
        }

        // Release - complete selection
        if (mouse.leftButton.wasReleasedThisFrame && isDragging)
        {
            isDragging = false;
            dragEnd = mousePos;

            // Box select
            Rect selectionRect = GetSelectionRect(dragStart, dragEnd);
            if (selectionRect.width > 10 || selectionRect.height > 10)
            {
                bool addToSelection = keyboard != null && keyboard.shiftKey.isPressed;
                if (!addToSelection) selectedUnits.Clear();

                foreach (var unit in squadMembers)
                {
                    if (unit == null) continue;
                    Vector3 screenPos = playerCamera.WorldToScreenPoint(unit.transform.position);
                    if (screenPos.z > 0 && selectionRect.Contains(new Vector2(screenPos.x, screenPos.y)))
                    {
                        if (!selectedUnits.Contains(unit))
                            selectedUnits.Add(unit);
                    }
                }
            }
        }
    }

    void HandleOrders(Mouse mouse, Keyboard keyboard)
    {
        if (mouse == null || selectedUnits.Count == 0) return;

        Vector2 mousePos = mouse.position.ReadValue();

        // Right click pressed - start potential formation drag
        if (mouse.rightButton.wasPressedThisFrame)
        {
            Ray ray = playerCamera.ScreenPointToRay(mousePos);
            RaycastHit hit;

            if (Physics.Raycast(ray, out hit, 500f))
            {
                formationDragStart = hit.point;
                formationDragEnd = hit.point;
                formationDragStartScreen = mousePos;
                isFormationDragging = true;
            }
        }

        // Right click held - update formation drag end
        if (mouse.rightButton.isPressed && isFormationDragging)
        {
            Ray ray = playerCamera.ScreenPointToRay(mousePos);
            RaycastHit hit;

            if (Physics.Raycast(ray, out hit, 500f))
            {
                formationDragEnd = hit.point;
            }
        }

        // Right click released - execute order
        if (mouse.rightButton.wasReleasedThisFrame && isFormationDragging)
        {
            isFormationDragging = false;

            float screenDragDist = Vector2.Distance(formationDragStartScreen, mousePos);
            bool isFormationOrder = screenDragDist > minFormationDragDistance && selectedUnits.Count > 1;

            if (isFormationOrder)
            {
                // Formation order - spread units along the drag line
                IssueFormationOrder(formationDragStart, formationDragEnd);
            }
            else
            {
                // Single point order - all units go to same spot
                IssueSinglePointOrder(formationDragStart);
            }

            // Visual feedback
            orderTargetPos = formationDragEnd;
            isIssuingOrder = true;
            Invoke("ClearOrderVisual", 0.5f);
        }
    }

    void IssueFormationOrder(Vector3 start, Vector3 end)
    {
        int unitCount = selectedUnits.Count;
        if (unitCount == 0) return;

        // Calculate facing direction (the direction of the drag)
        Vector3 facingDir = (end - start).normalized;
        float dragLength = Vector3.Distance(start, end);

        // Calculate the row direction (perpendicular to facing, horizontal)
        Vector3 rowDir = Vector3.Cross(facingDir, Vector3.up).normalized;

        // If facing is nearly vertical, use a default right direction
        if (rowDir.magnitude < 0.1f)
        {
            rowDir = Vector3.right;
        }

        // Center point of the formation (end of the drag)
        Vector3 centerPoint = end;

        // Spread width based on drag length (minimum 2m per unit)
        float spreadWidth = Mathf.Max(dragLength, unitCount * 2f);

        // If only one unit, just send to the end point
        if (unitCount == 1)
        {
            if (selectedUnits[0] != null)
                selectedUnits[0].SetOrder(AIController.OrderType.HoldPosition, null, centerPoint, facingDir);
            return;
        }

        // Spread units in a row perpendicular to facing direction
        float halfWidth = spreadWidth / 2f;
        for (int i = 0; i < unitCount; i++)
        {
            if (selectedUnits[i] == null) continue;

            // Position along the row (-0.5 to 0.5 mapped to the spread width)
            float t = (float)i / (unitCount - 1); // 0 to 1
            float offset = (t - 0.5f) * spreadWidth; // -halfWidth to +halfWidth

            Vector3 position = centerPoint + rowDir * offset;

            selectedUnits[i].SetOrder(AIController.OrderType.HoldPosition, null, position, facingDir);
        }

        Debug.Log($"Formation order: {unitCount} units in row, {spreadWidth:F1}m wide, facing {facingDir}");
    }

    void IssueSinglePointOrder(Vector3 point)
    {
        // Check what's at the target
        Collider[] colliders = Physics.OverlapSphere(point, 3f);

        CapturePoint targetPoint = null;
        foreach (var col in colliders)
        {
            targetPoint = col.GetComponentInParent<CapturePoint>();
            if (targetPoint != null) break;
        }

        foreach (var unit in selectedUnits)
        {
            if (unit == null) continue;

            if (targetPoint != null)
            {
                // Order to capture/defend point
                if (targetPoint.owningTeam == player.playerTeam)
                    unit.SetOrder(AIController.OrderType.DefendPoint, targetPoint);
                else
                    unit.SetOrder(AIController.OrderType.CapturePoint, targetPoint);
            }
            else
            {
                // Move order - move to position and hold
                unit.SetOrder(AIController.OrderType.HoldPosition, null, point);
            }
        }
    }

    void ClearOrderVisual()
    {
        isIssuingOrder = false;
    }

    void HandleHotkeys(Keyboard keyboard)
    {
        if (keyboard == null) return;

        // 1-7 to select individual units
        for (int i = 0; i < 7; i++)
        {
            Key key = (Key)((int)Key.Digit1 + i);
            if (keyboard[key].wasPressedThisFrame && i < squadMembers.Count)
            {
                if (keyboard.ctrlKey.isPressed)
                {
                    // Ctrl+number - add to selection
                    if (!selectedUnits.Contains(squadMembers[i]))
                        selectedUnits.Add(squadMembers[i]);
                }
                else
                {
                    // Number only - select just that unit
                    selectedUnits.Clear();
                    selectedUnits.Add(squadMembers[i]);
                }
            }
        }

        // Ctrl+A - select all
        if (keyboard.ctrlKey.isPressed && keyboard.aKey.wasPressedThisFrame)
        {
            selectedUnits.Clear();
            selectedUnits.AddRange(squadMembers);
        }

        // F - follow me (all selected)
        if (keyboard.fKey.wasPressedThisFrame)
        {
            foreach (var unit in selectedUnits)
            {
                if (unit != null)
                    unit.SetOrder(AIController.OrderType.FollowLeader);
            }
        }

        // H - hold position
        if (keyboard.hKey.wasPressedThisFrame)
        {
            foreach (var unit in selectedUnits)
            {
                if (unit != null)
                    unit.SetOrder(AIController.OrderType.HoldPosition, null, unit.transform.position);
            }
        }

        // Space - center on selected
        if (keyboard.spaceKey.wasPressedThisFrame && selectedUnits.Count > 0)
        {
            Vector3 center = Vector3.zero;
            foreach (var unit in selectedUnits)
            {
                if (unit != null) center += unit.transform.position;
            }
            center /= selectedUnits.Count;
            targetCameraPos = new Vector3(center.x, currentZoom, center.z);
        }
    }

    AIController GetUnitAtScreenPos(Vector2 screenPos)
    {
        foreach (var unit in squadMembers)
        {
            if (unit == null) continue;
            Vector3 unitScreen = playerCamera.WorldToScreenPoint(unit.transform.position);
            if (unitScreen.z > 0)
            {
                float dist = Vector2.Distance(screenPos, new Vector2(unitScreen.x, unitScreen.y));
                if (dist < 30f) return unit;
            }
        }
        return null;
    }

    Rect GetSelectionRect(Vector2 start, Vector2 end)
    {
        float x = Mathf.Min(start.x, end.x);
        float y = Mathf.Min(start.y, end.y);
        float w = Mathf.Abs(end.x - start.x);
        float h = Mathf.Abs(end.y - start.y);
        return new Rect(x, y, w, h);
    }

    void OnGUI()
    {
        if (!isActive) return;

        // Draw world elements
        DrawCapturePoints();
        DrawOtherTeammates(); // Draw non-squad teammates first (underneath)
        DrawUnits();          // Draw squad members on top
        DrawPlayer();

        // Draw selection box
        if (isDragging)
        {
            DrawSelectionBox();
        }

        // Draw formation drag preview
        if (isFormationDragging && selectedUnits.Count > 0)
        {
            DrawFormationPreview();
        }

        // Draw order indicator
        if (isIssuingOrder)
        {
            DrawOrderIndicator();
        }

        // Draw UI panels
        DrawBottomBar();
        DrawDetailPanel();
        DrawMinimap();
        DrawControlsHelp();
    }

    void DrawFormationPreview()
    {
        if (playerCamera == null) return;

        Vector3 screenStart = playerCamera.WorldToScreenPoint(formationDragStart);
        Vector3 screenEnd = playerCamera.WorldToScreenPoint(formationDragEnd);

        if (screenStart.z < 0 || screenEnd.z < 0) return;

        // Flip Y for GUI
        screenStart.y = Screen.height - screenStart.y;
        screenEnd.y = Screen.height - screenEnd.y;

        // Draw the drag direction line (shows facing direction)
        GUI.color = new Color(0.2f, 1f, 0.4f, 0.7f);
        DrawLine(new Vector2(screenStart.x, screenStart.y), new Vector2(screenEnd.x, screenEnd.y), 2f);

        // Calculate formation positions (same logic as IssueFormationOrder)
        int unitCount = selectedUnits.Count;
        Vector3 facingDir = (formationDragEnd - formationDragStart).normalized;
        float dragLength = Vector3.Distance(formationDragStart, formationDragEnd);
        Vector3 rowDir = Vector3.Cross(facingDir, Vector3.up).normalized;

        if (rowDir.magnitude < 0.1f)
            rowDir = Vector3.right;

        Vector3 centerPoint = formationDragEnd;
        float spreadWidth = Mathf.Max(dragLength, unitCount * 2f);

        if (unitCount > 1)
        {
            // Calculate row endpoints for preview line
            Vector3 rowStart = centerPoint - rowDir * (spreadWidth / 2f);
            Vector3 rowEnd = centerPoint + rowDir * (spreadWidth / 2f);

            Vector3 screenRowStart = playerCamera.WorldToScreenPoint(rowStart);
            Vector3 screenRowEnd = playerCamera.WorldToScreenPoint(rowEnd);

            if (screenRowStart.z > 0 && screenRowEnd.z > 0)
            {
                screenRowStart.y = Screen.height - screenRowStart.y;
                screenRowEnd.y = Screen.height - screenRowEnd.y;

                // Draw the row line
                GUI.color = new Color(1f, 0.8f, 0.2f, 0.9f);
                DrawLine(new Vector2(screenRowStart.x, screenRowStart.y), new Vector2(screenRowEnd.x, screenRowEnd.y), 3f);
            }

            // Draw circles for each unit position
            for (int i = 0; i < unitCount; i++)
            {
                float t = (float)i / (unitCount - 1);
                float offset = (t - 0.5f) * spreadWidth;
                Vector3 worldPos = centerPoint + rowDir * offset;
                Vector3 screenPos = playerCamera.WorldToScreenPoint(worldPos);

                if (screenPos.z > 0)
                {
                    screenPos.y = Screen.height - screenPos.y;
                    GUI.color = new Color(1f, 0.8f, 0.2f, 0.8f);
                    DrawCircle(new Vector2(screenPos.x, screenPos.y), 10f, 2f);

                    // Draw facing direction arrow
                    Vector3 arrowEnd = worldPos + facingDir * 3f;
                    Vector3 arrowScreenEnd = playerCamera.WorldToScreenPoint(arrowEnd);

                    if (arrowScreenEnd.z > 0)
                    {
                        arrowScreenEnd.y = Screen.height - arrowScreenEnd.y;
                        GUI.color = new Color(0.2f, 1f, 0.4f, 0.8f);
                        DrawLine(new Vector2(screenPos.x, screenPos.y), new Vector2(arrowScreenEnd.x, arrowScreenEnd.y), 2f);
                    }
                }
            }
        }
        else if (unitCount == 1)
        {
            // Single unit - show at end point
            Vector3 screenPos = playerCamera.WorldToScreenPoint(centerPoint);

            if (screenPos.z > 0)
            {
                screenPos.y = Screen.height - screenPos.y;
                GUI.color = new Color(1f, 0.8f, 0.2f, 0.8f);
                DrawCircle(new Vector2(screenPos.x, screenPos.y), 10f, 2f);

                // Draw facing arrow
                Vector3 arrowEnd = centerPoint + facingDir * 3f;
                Vector3 arrowScreenEnd = playerCamera.WorldToScreenPoint(arrowEnd);
                if (arrowScreenEnd.z > 0)
                {
                    arrowScreenEnd.y = Screen.height - arrowScreenEnd.y;
                    GUI.color = new Color(0.2f, 1f, 0.4f, 0.8f);
                    DrawLine(new Vector2(screenPos.x, screenPos.y), new Vector2(arrowScreenEnd.x, arrowScreenEnd.y), 2f);
                }
            }
        }

        GUI.color = Color.white;
    }

    void DrawSelectionBox()
    {
        Rect rect = GetSelectionRect(dragStart, dragEnd);
        rect.y = Screen.height - rect.y - rect.height; // Flip Y for GUI

        // Fill
        GUI.color = new Color(0.2f, 1f, 0.3f, 0.15f);
        GUI.DrawTexture(rect, whiteTex);

        // Border
        GUI.color = new Color(0.3f, 1f, 0.4f, 0.8f);
        GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, 2), whiteTex);
        GUI.DrawTexture(new Rect(rect.x, rect.y + rect.height - 2, rect.width, 2), whiteTex);
        GUI.DrawTexture(new Rect(rect.x, rect.y, 2, rect.height), whiteTex);
        GUI.DrawTexture(new Rect(rect.x + rect.width - 2, rect.y, 2, rect.height), whiteTex);

        GUI.color = Color.white;
    }

    void DrawOrderIndicator()
    {
        if (playerCamera == null) return;

        Vector3 screenPos = playerCamera.WorldToScreenPoint(orderTargetPos);
        if (screenPos.z > 0)
        {
            screenPos.y = Screen.height - screenPos.y;

            // Pulsing circle
            float pulse = Mathf.PingPong(Time.time * 4f, 1f);
            float size = 20f + pulse * 10f;

            GUI.color = new Color(0.3f, 1f, 0.4f, 1f - pulse * 0.5f);
            DrawCircle(new Vector2(screenPos.x, screenPos.y), size, 3);
            GUI.color = Color.white;
        }
    }

    void DrawCircle(Vector2 center, float radius, float thickness)
    {
        int segments = 32;
        for (int i = 0; i < segments; i++)
        {
            float angle1 = (float)i / segments * Mathf.PI * 2;
            float angle2 = (float)(i + 1) / segments * Mathf.PI * 2;

            Vector2 p1 = center + new Vector2(Mathf.Cos(angle1), Mathf.Sin(angle1)) * radius;
            Vector2 p2 = center + new Vector2(Mathf.Cos(angle2), Mathf.Sin(angle2)) * radius;

            DrawLine(p1, p2, thickness);
        }
    }

    void DrawLine(Vector2 start, Vector2 end, float thickness)
    {
        Vector2 dir = (end - start).normalized;
        float length = Vector2.Distance(start, end);
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

        Matrix4x4 matrix = GUI.matrix;
        GUIUtility.RotateAroundPivot(angle, start);
        GUI.DrawTexture(new Rect(start.x, start.y - thickness / 2, length, thickness), whiteTex);
        GUI.matrix = matrix;
    }

    void DrawCapturePoints()
    {
        if (playerCamera == null) return;

        foreach (var point in allPoints)
        {
            if (point == null) continue;

            Vector3 screenPos = playerCamera.WorldToScreenPoint(point.transform.position);
            if (screenPos.z < 0) continue;

            screenPos.y = Screen.height - screenPos.y;

            // Determine color
            Color color = point.owningTeam == player.playerTeam ? new Color(0.3f, 0.7f, 1f) :
                          point.owningTeam == Team.None ? new Color(0.6f, 0.6f, 0.6f) :
                          new Color(1f, 0.3f, 0.3f);

            // Draw flag icon
            float size = 30f;
            GUI.color = color;
            Rect flagRect = new Rect(screenPos.x - size / 2, screenPos.y - size / 2, size, size);
            GUI.DrawTexture(flagRect, whiteTex);

            // Border
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(flagRect.x - 2, flagRect.y - 2, flagRect.width + 4, 2), whiteTex);
            GUI.DrawTexture(new Rect(flagRect.x - 2, flagRect.yMax, flagRect.width + 4, 2), whiteTex);
            GUI.DrawTexture(new Rect(flagRect.x - 2, flagRect.y, 2, flagRect.height + 2), whiteTex);
            GUI.DrawTexture(new Rect(flagRect.xMax, flagRect.y, 2, flagRect.height + 2), whiteTex);

            // Label
            GUIStyle labelStyle = new GUIStyle();
            labelStyle.fontSize = 12;
            labelStyle.fontStyle = FontStyle.Bold;
            labelStyle.alignment = TextAnchor.MiddleCenter;
            labelStyle.normal.textColor = Color.white;

            GUI.Label(new Rect(screenPos.x - 50, screenPos.y + size / 2 + 5, 100, 20), point.pointName, labelStyle);
        }
    }

    void DrawUnits()
    {
        if (playerCamera == null) return;

        foreach (var unit in squadMembers)
        {
            if (unit == null || unit.isDead) continue;

            // Get ground position (feet)
            Vector3 groundPos = unit.transform.position;
            Vector3 screenPos = playerCamera.WorldToScreenPoint(groundPos);
            if (screenPos.z < 0) continue;

            screenPos.y = Screen.height - screenPos.y;

            bool isSelected = selectedUnits.Contains(unit);

            // Calculate circle size based on distance (perspective)
            float baseRadius = isSelected ? 22f : 16f;
            float distanceFactor = Mathf.Clamp(50f / screenPos.z, 0.3f, 2f);
            float radius = baseRadius * distanceFactor;

            // Draw ground circle (transparent, underneath soldier)
            if (isSelected)
            {
                // Selected: bright green circle
                GUI.color = new Color(0.3f, 1f, 0.4f, 0.5f);
                DrawFilledCircle(new Vector2(screenPos.x, screenPos.y), radius, 16);
                GUI.color = new Color(0.3f, 1f, 0.4f, 0.9f);
                DrawCircle(new Vector2(screenPos.x, screenPos.y), radius, 2f);
            }
            else
            {
                // Not selected: blue circle
                GUI.color = new Color(0.3f, 0.6f, 1f, 0.35f);
                DrawFilledCircle(new Vector2(screenPos.x, screenPos.y), radius, 16);
                GUI.color = new Color(0.3f, 0.6f, 1f, 0.7f);
                DrawCircle(new Vector2(screenPos.x, screenPos.y), radius, 1.5f);
            }

            // Number label (inside circle)
            int index = squadMembers.IndexOf(unit) + 1;
            GUIStyle numStyle = new GUIStyle();
            numStyle.fontSize = (int)(10 * distanceFactor);
            numStyle.fontStyle = FontStyle.Bold;
            numStyle.alignment = TextAnchor.MiddleCenter;
            numStyle.normal.textColor = Color.white;
            GUI.Label(new Rect(screenPos.x - 15, screenPos.y - 8, 30, 16), index.ToString(), numStyle);

            // Health bar (small, below the circle)
            float healthPct = unit.currentHealth / unit.maxHealth;
            float barWidth = radius * 1.5f;
            float barHeight = 3f;
            Rect bgRect = new Rect(screenPos.x - barWidth / 2, screenPos.y + radius + 2, barWidth, barHeight);
            GUI.color = new Color(0.1f, 0.1f, 0.1f, 0.6f);
            GUI.DrawTexture(bgRect, whiteTex);

            GUI.color = healthPct > 0.5f ? new Color(0.3f, 0.9f, 0.3f, 0.9f) :
                        healthPct > 0.25f ? new Color(0.9f, 0.9f, 0.3f, 0.9f) :
                        new Color(0.9f, 0.3f, 0.3f, 0.9f);
            GUI.DrawTexture(new Rect(bgRect.x, bgRect.y, barWidth * healthPct, barHeight), whiteTex);

            GUI.color = Color.white;
        }
    }

    void DrawOtherTeammates()
    {
        if (playerCamera == null || player == null) return;

        // Draw other friendly AI (not in squad)
        AIController[] allAI = FindObjectsOfType<AIController>();
        foreach (var ai in allAI)
        {
            if (ai == null || ai.isDead) continue;
            if (ai.team != player.playerTeam) continue;
            if (squadMembers.Contains(ai)) continue; // Skip squad members

            Vector3 groundPos = ai.transform.position;
            Vector3 screenPos = playerCamera.WorldToScreenPoint(groundPos);
            if (screenPos.z < 0) continue;

            screenPos.y = Screen.height - screenPos.y;

            // Smaller, more transparent circle for non-squad friendlies
            float baseRadius = 12f;
            float distanceFactor = Mathf.Clamp(50f / screenPos.z, 0.3f, 2f);
            float radius = baseRadius * distanceFactor;

            // Teal/cyan color for other friendlies
            GUI.color = new Color(0.2f, 0.7f, 0.7f, 0.25f);
            DrawFilledCircle(new Vector2(screenPos.x, screenPos.y), radius, 12);
            GUI.color = new Color(0.2f, 0.7f, 0.7f, 0.5f);
            DrawCircle(new Vector2(screenPos.x, screenPos.y), radius, 1f);
        }

        // Draw other friendly players
        FPSControllerPhoton[] allPlayers = FindObjectsOfType<FPSControllerPhoton>();
        foreach (var otherPlayer in allPlayers)
        {
            if (otherPlayer == null || otherPlayer == player || otherPlayer.isDead) continue;
            if (otherPlayer.playerTeam != player.playerTeam) continue;

            Vector3 groundPos = otherPlayer.transform.position;
            Vector3 screenPos = playerCamera.WorldToScreenPoint(groundPos);
            if (screenPos.z < 0) continue;

            screenPos.y = Screen.height - screenPos.y;

            // Circle for friendly players
            float baseRadius = 14f;
            float distanceFactor = Mathf.Clamp(50f / screenPos.z, 0.3f, 2f);
            float radius = baseRadius * distanceFactor;

            // Green color for friendly players
            GUI.color = new Color(0.2f, 0.9f, 0.3f, 0.3f);
            DrawFilledCircle(new Vector2(screenPos.x, screenPos.y), radius, 12);
            GUI.color = new Color(0.2f, 0.9f, 0.3f, 0.6f);
            DrawCircle(new Vector2(screenPos.x, screenPos.y), radius, 1.5f);

            // Player icon (P)
            GUIStyle pStyle = new GUIStyle();
            pStyle.fontSize = (int)(9 * distanceFactor);
            pStyle.fontStyle = FontStyle.Bold;
            pStyle.alignment = TextAnchor.MiddleCenter;
            pStyle.normal.textColor = new Color(0.2f, 0.9f, 0.3f);
            GUI.Label(new Rect(screenPos.x - 10, screenPos.y - 6, 20, 12), "P", pStyle);
        }

        GUI.color = Color.white;
    }

    void DrawFilledCircle(Vector2 center, float radius, int segments)
    {
        // Simple filled circle using concentric rectangles (much more efficient)
        // This avoids the massive memory allocations of the triangle approach
        float step = Mathf.Max(1f, radius / 8f);
        for (float r = radius; r > 0; r -= step)
        {
            float size = r * 2f;
            GUI.DrawTexture(new Rect(center.x - r, center.y - r, size, size), whiteTex);
        }
    }

    void DrawPlayer()
    {
        if (playerCamera == null || player == null) return;

        Vector3 screenPos = playerCamera.WorldToScreenPoint(player.transform.position);
        if (screenPos.z < 0) return;

        screenPos.y = Screen.height - screenPos.y;

        // Diamond shape for player
        float size = 12f;
        GUI.color = new Color(0.2f, 1f, 0.4f);

        Matrix4x4 matrix = GUI.matrix;
        GUIUtility.RotateAroundPivot(45, new Vector2(screenPos.x, screenPos.y));
        GUI.DrawTexture(new Rect(screenPos.x - size / 2, screenPos.y - size / 2, size, size), whiteTex);
        GUI.matrix = matrix;

        // Label
        GUIStyle style = new GUIStyle();
        style.fontSize = 11;
        style.fontStyle = FontStyle.Bold;
        style.alignment = TextAnchor.MiddleCenter;
        style.normal.textColor = new Color(0.3f, 1f, 0.5f);
        GUI.Label(new Rect(screenPos.x - 30, screenPos.y + 12, 60, 20), "YOU", style);

        GUI.color = Color.white;
    }

    void DrawBottomBar()
    {
        float barHeight = 100f;
        Rect barRect = new Rect(0, Screen.height - barHeight, Screen.width, barHeight);

        // Background
        GUI.DrawTexture(barRect, darkTex);

        // All squad member portraits (always visible)
        float portraitSize = 70f;
        float startX = 20f;

        for (int i = 0; i < squadMembers.Count && i < 7; i++)
        {
            AIController unit = squadMembers[i];
            if (unit == null) continue;

            bool isSelected = selectedUnits.Contains(unit);
            Rect portraitRect = new Rect(startX + i * (portraitSize + 10), Screen.height - barHeight + 15, portraitSize, portraitSize);

            // Portrait background - brighter if selected
            GUI.color = isSelected ? new Color(0.25f, 0.35f, 0.4f) : new Color(0.15f, 0.18f, 0.2f);
            GUI.DrawTexture(portraitRect, whiteTex);

            // Border - green if selected, gray if not
            GUI.color = isSelected ? new Color(0.3f, 1f, 0.4f) : new Color(0.4f, 0.4f, 0.45f);
            GUI.DrawTexture(new Rect(portraitRect.x - 2, portraitRect.y - 2, portraitRect.width + 4, 2), whiteTex);
            GUI.DrawTexture(new Rect(portraitRect.x - 2, portraitRect.yMax, portraitRect.width + 4, 2), whiteTex);
            GUI.DrawTexture(new Rect(portraitRect.x - 2, portraitRect.y, 2, portraitRect.height + 4), whiteTex);
            GUI.DrawTexture(new Rect(portraitRect.xMax, portraitRect.y, 2, portraitRect.height + 4), whiteTex);

            // Unit info
            GUI.color = Color.white;
            GUIStyle nameStyle = new GUIStyle();
            nameStyle.fontSize = 10;
            nameStyle.fontStyle = FontStyle.Bold;
            nameStyle.alignment = TextAnchor.UpperCenter;
            nameStyle.normal.textColor = isSelected ? Color.white : new Color(0.7f, 0.7f, 0.7f);
            nameStyle.clipping = TextClipping.Clip;

            string name = unit.identity != null ? unit.identity.lastName : $"Unit {i + 1}";
            string rank = unit.identity != null ? unit.identity.rank.Substring(0, Mathf.Min(3, unit.identity.rank.Length)) : "";

            GUI.Label(new Rect(portraitRect.x, portraitRect.y + 5, portraitRect.width, 15), rank, nameStyle);
            GUI.Label(new Rect(portraitRect.x, portraitRect.y + 20, portraitRect.width, 15), name, nameStyle);

            // Health bar
            float healthPct = unit.currentHealth / unit.maxHealth;
            Rect hpBg = new Rect(portraitRect.x + 5, portraitRect.yMax - 15, portraitRect.width - 10, 8);
            GUI.color = new Color(0.15f, 0.15f, 0.15f);
            GUI.DrawTexture(hpBg, whiteTex);

            GUI.color = healthPct > 0.5f ? new Color(0.3f, 0.9f, 0.3f) : new Color(0.9f, 0.3f, 0.3f);
            GUI.DrawTexture(new Rect(hpBg.x, hpBg.y, hpBg.width * healthPct, hpBg.height), whiteTex);

            // Order status
            GUIStyle orderStyle = new GUIStyle();
            orderStyle.fontSize = 9;
            orderStyle.alignment = TextAnchor.MiddleCenter;
            orderStyle.normal.textColor = new Color(0.6f, 0.6f, 0.6f);

            string order = GetOrderText(unit);
            GUI.Label(new Rect(portraitRect.x, portraitRect.y + 40, portraitRect.width, 15), order, orderStyle);

            // Hotkey number
            GUIStyle hotkeyStyle = new GUIStyle();
            hotkeyStyle.fontSize = 12;
            hotkeyStyle.fontStyle = FontStyle.Bold;
            hotkeyStyle.normal.textColor = isSelected ? new Color(1f, 0.9f, 0.4f) : new Color(0.8f, 0.7f, 0.3f);
            GUI.Label(new Rect(portraitRect.x + 3, portraitRect.y + 3, 15, 15), (i + 1).ToString(), hotkeyStyle);

            // Click portrait to select unit
            if (GUI.Button(portraitRect, "", GUIStyle.none))
            {
                // Select this unit (clear others unless shift held)
                var keyboard = Keyboard.current;
                bool addToSelection = keyboard != null && keyboard.shiftKey.isPressed;

                if (addToSelection)
                {
                    if (selectedUnits.Contains(unit))
                        selectedUnits.Remove(unit);
                    else
                        selectedUnits.Add(unit);
                }
                else
                {
                    selectedUnits.Clear();
                    selectedUnits.Add(unit);
                }
            }

            // Small info button for details (top right corner)
            Rect infoBtn = new Rect(portraitRect.xMax - 18, portraitRect.y + 2, 16, 16);
            GUI.color = detailUnit == unit ? new Color(0.3f, 1f, 0.4f) : new Color(0.5f, 0.5f, 0.5f);
            if (GUI.Button(infoBtn, "i"))
            {
                detailUnit = (detailUnit == unit) ? null : unit; // Toggle detail panel
            }

            // Show indicator if details panel is open for this unit
            if (detailUnit == unit)
            {
                GUI.color = new Color(0.3f, 1f, 0.4f, 0.8f);
                GUIStyle detailHint = new GUIStyle();
                detailHint.fontSize = 8;
                detailHint.alignment = TextAnchor.MiddleCenter;
                detailHint.normal.textColor = new Color(0.3f, 1f, 0.4f);
                GUI.Label(new Rect(portraitRect.x, portraitRect.y - 12, portraitRect.width, 12), "▲ DETAILS", detailHint);
            }

            GUI.color = Color.white;
        }

        // Command buttons on the right
        float btnWidth = 80f;
        float btnHeight = 35f;
        float btnX = Screen.width - btnWidth * 3 - 40;
        float btnY = Screen.height - barHeight + 15;

        GUIStyle btnStyle = new GUIStyle(GUI.skin.button);
        btnStyle.fontSize = 12;
        btnStyle.fontStyle = FontStyle.Bold;

        if (GUI.Button(new Rect(btnX, btnY, btnWidth, btnHeight), "FOLLOW (F)"))
        {
            foreach (var unit in selectedUnits)
                if (unit != null) unit.SetOrder(AIController.OrderType.FollowLeader);
        }

        if (GUI.Button(new Rect(btnX + btnWidth + 5, btnY, btnWidth, btnHeight), "HOLD (H)"))
        {
            foreach (var unit in selectedUnits)
                if (unit != null) unit.SetOrder(AIController.OrderType.HoldPosition, null, unit.transform.position);
        }

        if (GUI.Button(new Rect(btnX + (btnWidth + 5) * 2, btnY, btnWidth, btnHeight), "SELECT ALL"))
        {
            selectedUnits.Clear();
            selectedUnits.AddRange(squadMembers);
        }

        btnY += btnHeight + 5;

        if (GUI.Button(new Rect(btnX, btnY, btnWidth, btnHeight), "DISBAND"))
        {
            player.DisbandSquad();
            Hide();
        }

        if (GUI.Button(new Rect(btnX + btnWidth + 5, btnY, btnWidth * 2 + 5, btnHeight), "CLOSE (TAB)"))
        {
            Hide();
        }
    }

    void DrawDetailPanel()
    {
        if (detailUnit == null || detailUnit.identity == null) return;

        SoldierIdentity id = detailUnit.identity;

        // Panel above bottom bar
        float panelWidth = 400f;
        float panelHeight = 280f;
        float panelX = 20f;
        float panelY = Screen.height - 100f - panelHeight - 10f;

        Rect panelRect = new Rect(panelX, panelY, panelWidth, panelHeight);

        // Background
        GUI.color = new Color(0.08f, 0.1f, 0.12f, 0.95f);
        GUI.DrawTexture(panelRect, whiteTex);

        // Border
        GUI.color = new Color(0.3f, 1f, 0.4f, 0.8f);
        GUI.DrawTexture(new Rect(panelRect.x - 2, panelRect.y - 2, panelRect.width + 4, 2), whiteTex);
        GUI.DrawTexture(new Rect(panelRect.x - 2, panelRect.yMax, panelRect.width + 4, 2), whiteTex);
        GUI.DrawTexture(new Rect(panelRect.x - 2, panelRect.y, 2, panelRect.height + 4), whiteTex);
        GUI.DrawTexture(new Rect(panelRect.xMax, panelRect.y, 2, panelRect.height + 4), whiteTex);

        GUI.color = Color.white;

        float y = panelY + 10f;
        float x = panelX + 15f;
        float lineHeight = 16f;

        // Styles
        GUIStyle titleStyle = new GUIStyle();
        titleStyle.fontSize = 14;
        titleStyle.fontStyle = FontStyle.Bold;
        titleStyle.normal.textColor = new Color(0.3f, 1f, 0.4f);

        GUIStyle headerStyle = new GUIStyle();
        headerStyle.fontSize = 11;
        headerStyle.fontStyle = FontStyle.Bold;
        headerStyle.normal.textColor = new Color(0.8f, 0.8f, 0.8f);

        GUIStyle textStyle = new GUIStyle();
        textStyle.fontSize = 10;
        textStyle.normal.textColor = new Color(0.7f, 0.7f, 0.7f);
        textStyle.wordWrap = true;

        GUIStyle quoteStyle = new GUIStyle();
        quoteStyle.fontSize = 10;
        quoteStyle.fontStyle = FontStyle.Italic;
        quoteStyle.normal.textColor = new Color(0.6f, 0.8f, 0.6f);

        // Title - Full name and rank
        GUI.Label(new Rect(x, y, panelWidth - 30, 20), $"{id.rank} {id.FullName}", titleStyle);
        y += 22f;

        // Callsign and specialty
        GUI.Label(new Rect(x, y, panelWidth - 30, lineHeight), $"Callsign: {id.callsign}  |  {id.combatSpecialty}  |  Age: {id.age}", headerStyle);
        y += lineHeight + 2f;

        GUI.Label(new Rect(x, y, panelWidth - 30, lineHeight), $"From: {id.hometown}", textStyle);
        y += lineHeight + 8f;

        // Stats
        GUI.Label(new Rect(x, y, panelWidth - 30, lineHeight), $"Kills: {id.kills}  |  Deaths: {id.deaths}  |  Missions: {id.missionsCompleted}", headerStyle);
        y += lineHeight + 8f;

        // Backstory
        GUI.Label(new Rect(x, y, 80, lineHeight), "BACKSTORY:", headerStyle);
        y += lineHeight;
        GUI.Label(new Rect(x, y, panelWidth - 30, 45), id.backstory, textStyle);
        y += 48f;

        // Family
        GUI.Label(new Rect(x, y, 80, lineHeight), "FAMILY:", headerStyle);
        y += lineHeight;
        GUI.Label(new Rect(x, y, panelWidth - 30, 30), id.familyInfo, textStyle);
        y += 33f;

        // Likes/Dislikes
        string likes = id.likes != null ? string.Join(", ", id.likes) : "Unknown";
        string dislikes = id.dislikes != null ? string.Join(", ", id.dislikes) : "Unknown";

        GUI.Label(new Rect(x, y, panelWidth - 30, lineHeight), $"Likes: {likes}", textStyle);
        y += lineHeight + 2f;
        GUI.Label(new Rect(x, y, panelWidth - 30, lineHeight), $"Dislikes: {dislikes}", textStyle);
        y += lineHeight + 8f;

        // Quote
        GUI.Label(new Rect(x, y, panelWidth - 30, 20), $"\"{id.personalQuote}\"", quoteStyle);

        // Close button
        if (GUI.Button(new Rect(panelRect.xMax - 25, panelRect.y + 5, 20, 20), "X"))
        {
            detailUnit = null;
        }
    }

    string GetOrderText(AIController unit)
    {
        switch (unit.currentOrder)
        {
            case AIController.OrderType.FollowLeader: return "Following";
            case AIController.OrderType.DefendPoint: return "Defending";
            case AIController.OrderType.CapturePoint: return "Capturing";
            case AIController.OrderType.HoldPosition: return "Holding";
            default: return "";
        }
    }

    void DrawMinimap()
    {
        float mapSize = 150f;
        float margin = 10f;
        Rect mapRect = new Rect(Screen.width - mapSize - margin, margin, mapSize, mapSize);

        // Background
        GUI.color = new Color(0.1f, 0.1f, 0.12f, 0.9f);
        GUI.DrawTexture(mapRect, whiteTex);

        // Border
        GUI.color = new Color(0.3f, 0.3f, 0.35f);
        GUI.DrawTexture(new Rect(mapRect.x - 2, mapRect.y - 2, mapRect.width + 4, 2), whiteTex);
        GUI.DrawTexture(new Rect(mapRect.x - 2, mapRect.yMax, mapRect.width + 4, 2), whiteTex);
        GUI.DrawTexture(new Rect(mapRect.x - 2, mapRect.y, 2, mapRect.height + 4), whiteTex);
        GUI.DrawTexture(new Rect(mapRect.xMax, mapRect.y, 2, mapRect.height + 4), whiteTex);

        // Calculate bounds
        float minX = float.MaxValue, maxX = float.MinValue;
        float minZ = float.MaxValue, maxZ = float.MinValue;

        foreach (var point in allPoints)
        {
            minX = Mathf.Min(minX, point.transform.position.x);
            maxX = Mathf.Max(maxX, point.transform.position.x);
            minZ = Mathf.Min(minZ, point.transform.position.z);
            maxZ = Mathf.Max(maxZ, point.transform.position.z);
        }

        float padding = 50f;
        minX -= padding; maxX += padding;
        minZ -= padding; maxZ += padding;

        // Prevent division by zero
        float rangeX = maxX - minX;
        float rangeZ = maxZ - minZ;
        if (rangeX < 1f) rangeX = 1f;
        if (rangeZ < 1f) rangeZ = 1f;

        // Draw points on minimap
        foreach (var point in allPoints)
        {
            float nx = (point.transform.position.x - minX) / rangeX;
            float ny = (point.transform.position.z - minZ) / rangeZ;

            float px = mapRect.x + nx * mapRect.width;
            float py = mapRect.y + (1 - ny) * mapRect.height;

            GUI.color = point.owningTeam == player.playerTeam ? new Color(0.3f, 0.7f, 1f) :
                        point.owningTeam == Team.None ? Color.gray : new Color(1f, 0.3f, 0.3f);
            GUI.DrawTexture(new Rect(px - 4, py - 4, 8, 8), whiteTex);
        }

        // Draw units on minimap
        foreach (var unit in squadMembers)
        {
            if (unit == null) continue;

            float nx = (unit.transform.position.x - minX) / rangeX;
            float ny = (unit.transform.position.z - minZ) / rangeZ;

            float px = mapRect.x + nx * mapRect.width;
            float py = mapRect.y + (1 - ny) * mapRect.height;

            GUI.color = selectedUnits.Contains(unit) ? new Color(0.4f, 1f, 0.5f) : new Color(0.3f, 0.6f, 1f);
            GUI.DrawTexture(new Rect(px - 3, py - 3, 6, 6), whiteTex);
        }

        // Draw player on minimap
        if (player != null)
        {
            float nx = (player.transform.position.x - minX) / rangeX;
            float ny = (player.transform.position.z - minZ) / rangeZ;

            float px = mapRect.x + nx * mapRect.width;
            float py = mapRect.y + (1 - ny) * mapRect.height;

            GUI.color = new Color(0.2f, 1f, 0.4f);
            GUI.DrawTexture(new Rect(px - 4, py - 4, 8, 8), whiteTex);
        }

        GUI.color = Color.white;
    }

    void DrawControlsHelp()
    {
        GUIStyle style = new GUIStyle();
        style.fontSize = 11;
        style.normal.textColor = new Color(0.7f, 0.7f, 0.7f);

        float y = 10;
        float x = 10;

        GUI.Label(new Rect(x, y, 450, 20), "Left Click - Select | Drag - Box Select | Right Click - Move | Right Drag - Formation", style);
        GUI.Label(new Rect(x, y + 15, 450, 20), "Middle Mouse - Rotate/Tilt Camera | Scroll - Zoom | R - Reset Camera", style);
        GUI.Label(new Rect(x, y + 30, 450, 20), "1-7 - Select unit | Ctrl+A - Select all | F - Follow | H - Hold | Space - Center", style);
    }

    public bool IsActive => isActive;
}
