using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

[CustomEditor(typeof(TankWaypoint))]
public class TankWaypointEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        TankWaypoint waypoint = (TankWaypoint)target;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Quick Actions", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("+ Ahead"))
        {
            CreateConnectedWaypoint(waypoint, waypoint.transform.forward * 30f);
        }
        if (GUILayout.Button("+ Behind"))
        {
            CreateConnectedWaypoint(waypoint, -waypoint.transform.forward * 30f);
        }
        if (GUILayout.Button("+ Left"))
        {
            CreateConnectedWaypoint(waypoint, -waypoint.transform.right * 30f);
        }
        if (GUILayout.Button("+ Right"))
        {
            CreateConnectedWaypoint(waypoint, waypoint.transform.right * 30f);
        }
        EditorGUILayout.EndHorizontal();

        if (GUILayout.Button("Auto Connect Nearby"))
        {
            Undo.RecordObject(waypoint, "Auto Connect");
            waypoint.AutoConnectNearby();
            EditorUtility.SetDirty(waypoint);
        }

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "Open Tools > Tank Waypoint Editor for full editing mode.\n" +
            "• Click in scene to place waypoints\n" +
            "• Shift+Click to connect waypoints\n" +
            "• Press Delete to remove selected",
            MessageType.Info);
    }

    void CreateConnectedWaypoint(TankWaypoint source, Vector3 offset)
    {
        Vector3 newPos = source.transform.position + offset;

        if (Physics.Raycast(newPos + Vector3.up * 100f, Vector3.down, out RaycastHit hit, 200f))
        {
            newPos = hit.point;
        }

        GameObject newObj = new GameObject("TankWaypoint");
        newObj.transform.position = newPos;
        newObj.transform.SetParent(source.transform.parent);

        TankWaypoint newWaypoint = newObj.AddComponent<TankWaypoint>();
        newWaypoint.reachRadius = source.reachRadius;
        newWaypoint.ownerTeam = source.ownerTeam;

        source.connections.Add(newWaypoint);
        newWaypoint.connections.Add(source);

        Undo.RegisterCreatedObjectUndo(newObj, "Create Tank Waypoint");
        EditorUtility.SetDirty(source);

        Selection.activeGameObject = newObj;
    }
}

public class TankWaypointEditorWindow : EditorWindow
{
    // Settings
    private float waypointRadius = 4f;
    private Team waypointTeam = Team.None;
    #pragma warning disable CS0414
    private float autoConnectDistance = 80f;  // Used in Inspector
    #pragma warning restore CS0414
    private bool autoConnect = true;
    private bool snapToGround = true;

    // State
    private bool isEditMode = false;
    private TankWaypoint connectFrom = null;
    private TankWaypoint lastPlaced = null;  // For chaining waypoints
    private Vector2 scrollPos;

    // Colors
    private static readonly Color phantomColor = new Color(0.2f, 0.5f, 1f);
    private static readonly Color havocColor = new Color(1f, 0.3f, 0.2f);
    private static readonly Color neutralColor = new Color(1f, 0.9f, 0.2f);
    private static readonly Color connectionColor = new Color(0f, 1f, 1f, 0.8f);
    private static readonly Color selectedColor = Color.white;

    [MenuItem("Tools/Tank Waypoint Editor")]
    public static void ShowWindow()
    {
        var window = GetWindow<TankWaypointEditorWindow>("Tank Waypoints");
        window.minSize = new Vector2(280, 400);
    }

    void OnEnable()
    {
        SceneView.duringSceneGui += OnSceneGUI;
    }

    void OnDisable()
    {
        SceneView.duringSceneGui -= OnSceneGUI;
        isEditMode = false;
    }

    void OnGUI()
    {
        scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

        // Edit Mode Toggle
        EditorGUILayout.Space(5);
        GUI.backgroundColor = isEditMode ? Color.green : Color.gray;
        if (GUILayout.Button(isEditMode ? "■ EDIT MODE ON" : "▶ EDIT MODE OFF", GUILayout.Height(35)))
        {
            isEditMode = !isEditMode;
            if (isEditMode)
            {
                Tools.current = Tool.None;
                SceneView.RepaintAll();
            }
            connectFrom = null;
        }
        GUI.backgroundColor = Color.white;

        if (isEditMode)
        {
            string deleteKey = Application.platform == RuntimePlatform.OSXEditor ? "CMD" : "CTRL";
            EditorGUILayout.HelpBox(
                "CLICK empty = Place + chain\n" +
                "CLICK waypoint = Set chain point\n" +
                "SHIFT+CLICK = Connect/disconnect\n" +
                $"{deleteKey}+CLICK = Delete",
                MessageType.Info);

            if (connectFrom != null)
            {
                EditorGUILayout.HelpBox($"Connecting from: {connectFrom.name}\nClick another waypoint to connect, or ESC to cancel.", MessageType.Warning);
            }
        }

        // Waypoint Settings
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("New Waypoint Settings", EditorStyles.boldLabel);
        waypointRadius = EditorGUILayout.Slider("Reach Radius", waypointRadius, 5f, 30f);
        waypointTeam = (Team)EditorGUILayout.EnumPopup("Team", waypointTeam);
        snapToGround = EditorGUILayout.Toggle("Snap to Ground", snapToGround);
        autoConnect = EditorGUILayout.Toggle("Chain Mode", autoConnect);

        if (autoConnect && lastPlaced != null)
        {
            EditorGUILayout.HelpBox($"Next waypoint will connect to: {lastPlaced.name}", MessageType.None);
        }

        if (GUILayout.Button("Start New Path"))
        {
            lastPlaced = null;
        }

        // Quick Create
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Quick Create", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Ring (8)"))
        {
            CreateRing(8, 80f);
        }
        if (GUILayout.Button("Ring (12)"))
        {
            CreateRing(12, 120f);
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Grid 3x3"))
        {
            CreateGrid(3, 3, 40f);
        }
        if (GUILayout.Button("Grid 5x5"))
        {
            CreateGrid(5, 5, 40f);
        }
        EditorGUILayout.EndHorizontal();

        // Selection Actions
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Selection Actions", EditorStyles.boldLabel);

        TankWaypoint[] selected = Selection.GetFiltered<TankWaypoint>(SelectionMode.TopLevel);
        EditorGUILayout.LabelField($"Selected: {selected.Length} waypoints");

        EditorGUI.BeginDisabledGroup(selected.Length < 2);
        if (GUILayout.Button($"Connect Selected ({selected.Length})"))
        {
            ConnectSelected(selected);
        }
        if (GUILayout.Button($"Chain Selected ({selected.Length})"))
        {
            ChainSelected(selected);
        }
        if (GUILayout.Button($"Disconnect Selected ({selected.Length})"))
        {
            DisconnectSelected(selected);
        }
        EditorGUI.EndDisabledGroup();

        // All Waypoints
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("All Waypoints", EditorStyles.boldLabel);

        TankWaypoint[] all = FindObjectsByType<TankWaypoint>(FindObjectsSortMode.None);
        EditorGUILayout.LabelField($"Total: {all.Length} waypoints");

        if (GUILayout.Button("Auto-Connect All"))
        {
            AutoConnectAll(all);
        }

        if (GUILayout.Button("Select All Waypoints"))
        {
            Selection.objects = System.Array.ConvertAll(all, w => w.gameObject);
        }

        EditorGUILayout.Space(5);
        GUI.backgroundColor = new Color(1f, 0.5f, 0.5f);
        if (GUILayout.Button("Delete All Waypoints") && all.Length > 0)
        {
            if (EditorUtility.DisplayDialog("Delete All Waypoints",
                $"Delete all {all.Length} waypoints?", "Delete", "Cancel"))
            {
                foreach (var wp in all)
                {
                    Undo.DestroyObjectImmediate(wp.gameObject);
                }
            }
        }
        GUI.backgroundColor = Color.white;

        EditorGUILayout.EndScrollView();
    }

    void OnSceneGUI(SceneView sceneView)
    {
        // Always draw waypoints with enhanced visuals
        DrawAllWaypoints();

        if (!isEditMode) return;

        Event e = Event.current;

        // Allow normal scene navigation (right-click, middle-click, alt+click, scroll)
        bool isNavigating = e.button == 1 || e.button == 2 || e.alt ||
                           e.type == EventType.ScrollWheel;

        if (isNavigating) return;

        // ESC to cancel connection
        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
        {
            connectFrom = null;
            e.Use();
            Repaint();
        }

        // Mouse click handling - only left click
        if (e.type == EventType.MouseDown && e.button == 0)
        {
            Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);

            // Check if clicking on existing waypoint
            TankWaypoint clickedWaypoint = GetWaypointAtRay(ray);

            if ((e.control || e.command) && clickedWaypoint != null)
            {
                // CTRL/CMD+Click = Delete
                DeleteWaypoint(clickedWaypoint);
                e.Use();
            }
            else if (e.shift)
            {
                // SHIFT+Click = Connect mode
                if (clickedWaypoint != null)
                {
                    if (connectFrom == null)
                    {
                        connectFrom = clickedWaypoint;
                    }
                    else if (connectFrom != clickedWaypoint)
                    {
                        // Complete connection
                        ConnectWaypoints(connectFrom, clickedWaypoint);
                        connectFrom = null;
                    }
                    e.Use();
                    Repaint();
                }
            }
            else if (!e.alt)
            {
                // Check if clicking existing waypoint - set as chain point
                if (clickedWaypoint != null)
                {
                    lastPlaced = clickedWaypoint;
                    Selection.activeGameObject = clickedWaypoint.gameObject;
                    e.Use();
                    Repaint();
                }
                // Regular click on empty space = Place waypoint
                else if (Physics.Raycast(ray, out RaycastHit hit, 1000f))
                {
                    Vector3 pos = hit.point;
                    if (snapToGround)
                    {
                        if (Physics.Raycast(pos + Vector3.up * 50f, Vector3.down, out RaycastHit groundHit, 100f))
                        {
                            pos = groundHit.point;
                        }
                    }

                    CreateWaypoint(pos);
                    e.Use();
                }
            }
        }

        // Draw connection line while connecting
        if (connectFrom != null)
        {
            Handles.color = Color.yellow;
            Ray mouseRay = HandleUtility.GUIPointToWorldRay(Event.current.mousePosition);
            if (Physics.Raycast(mouseRay, out RaycastHit hit, 1000f))
            {
                Handles.DrawDottedLine(connectFrom.transform.position + Vector3.up * 2f,
                    hit.point + Vector3.up * 2f, 4f);
            }
            sceneView.Repaint();
        }

        // Instructions overlay
        Handles.BeginGUI();
        string delKey = Application.platform == RuntimePlatform.OSXEditor ? "Cmd" : "Ctrl";
        GUILayout.BeginArea(new Rect(10, 10, 230, 110));
        GUI.Box(new Rect(0, 0, 230, 110), "");
        GUILayout.Label(" Tank Waypoint Edit Mode", EditorStyles.boldLabel);
        GUILayout.Label(" Click empty: Place waypoint");
        GUILayout.Label(" Click waypoint: Set chain point");
        GUILayout.Label(" Shift+Click: Connect/disconnect");
        GUILayout.Label($" {delKey}+Click: Delete");
        if (lastPlaced != null)
            GUILayout.Label($" Chain from: {lastPlaced.name}");
        else
            GUILayout.Label(" (New path)");
        GUILayout.EndArea();
        Handles.EndGUI();
    }

    void DrawAllWaypoints()
    {
        TankWaypoint[] waypoints = FindObjectsByType<TankWaypoint>(FindObjectsSortMode.None);
        TankWaypoint[] selected = Selection.GetFiltered<TankWaypoint>(SelectionMode.TopLevel);
        HashSet<TankWaypoint> selectedSet = new HashSet<TankWaypoint>(selected);

        // Draw connections first (behind waypoints)
        Handles.color = connectionColor;
        HashSet<(TankWaypoint, TankWaypoint)> drawnConnections = new HashSet<(TankWaypoint, TankWaypoint)>();

        foreach (var wp in waypoints)
        {
            if (wp == null) continue;
            foreach (var conn in wp.connections)
            {
                if (conn == null) continue;

                // Avoid drawing same connection twice
                var key = wp.GetInstanceID() < conn.GetInstanceID()
                    ? (wp, conn) : (conn, wp);
                if (drawnConnections.Contains(key)) continue;
                drawnConnections.Add(key);

                Vector3 start = wp.transform.position + Vector3.up * 1.5f;
                Vector3 end = conn.transform.position + Vector3.up * 1.5f;

                // Draw arrow
                Handles.DrawLine(start, end);
                Vector3 mid = (start + end) * 0.5f;
                Vector3 dir = (end - start).normalized;
                Vector3 right = Vector3.Cross(Vector3.up, dir) * 2f;
                Handles.DrawLine(mid, mid - dir * 3f + right);
                Handles.DrawLine(mid, mid - dir * 3f - right);
            }
        }

        // Draw waypoints
        foreach (var wp in waypoints)
        {
            if (wp == null) continue;

            Vector3 pos = wp.transform.position;
            bool isSelected = selectedSet.Contains(wp);
            bool isConnectSource = wp == connectFrom;

            // Choose color
            Color wpColor = wp.ownerTeam == Team.Phantom ? phantomColor :
                           wp.ownerTeam == Team.Havoc ? havocColor : neutralColor;

            if (isConnectSource)
            {
                wpColor = Color.yellow;
            }
            else if (isSelected)
            {
                wpColor = selectedColor;
            }

            // Draw disc
            Handles.color = new Color(wpColor.r, wpColor.g, wpColor.b, 0.3f);
            Handles.DrawSolidDisc(pos + Vector3.up * 0.5f, Vector3.up, wp.reachRadius);

            // Draw ring
            Handles.color = wpColor;
            Handles.DrawWireDisc(pos + Vector3.up * 0.5f, Vector3.up, wp.reachRadius);

            // Draw center marker
            float markerSize = 2f;
            Handles.DrawLine(pos + Vector3.up * 0.5f + Vector3.forward * markerSize,
                           pos + Vector3.up * 0.5f - Vector3.forward * markerSize);
            Handles.DrawLine(pos + Vector3.up * 0.5f + Vector3.right * markerSize,
                           pos + Vector3.up * 0.5f - Vector3.right * markerSize);

            // Draw vertical line
            Handles.DrawLine(pos, pos + Vector3.up * 5f);

            // Label
            GUIStyle style = new GUIStyle(EditorStyles.boldLabel);
            style.normal.textColor = wpColor;
            style.alignment = TextAnchor.MiddleCenter;
            Handles.Label(pos + Vector3.up * 6f, wp.name, style);

            // Spawn point indicator
            if (wp.isSpawnPoint)
            {
                Handles.color = Color.green;
                Handles.DrawWireDisc(pos + Vector3.up * 0.5f, Vector3.up, wp.reachRadius + 2f);
                Handles.Label(pos + Vector3.up * 8f, "SPAWN", style);
            }
        }
    }

    TankWaypoint GetWaypointAtRay(Ray ray)
    {
        TankWaypoint[] waypoints = FindObjectsByType<TankWaypoint>(FindObjectsSortMode.None);
        TankWaypoint closest = null;
        float closestDist = float.MaxValue;

        foreach (var wp in waypoints)
        {
            if (wp == null) continue;

            // Check if ray passes near waypoint
            Vector3 wpPos = wp.transform.position + Vector3.up * 2f;
            float dist = HandleUtility.DistancePointLine(wpPos, ray.origin, ray.origin + ray.direction * 1000f);

            if (dist < wp.reachRadius && dist < closestDist)
            {
                closestDist = dist;
                closest = wp;
            }
        }

        return closest;
    }

    void CreateWaypoint(Vector3 position)
    {
        // Find or create parent
        GameObject parent = GameObject.Find("TankWaypoints");
        if (parent == null)
        {
            parent = new GameObject("TankWaypoints");
            Undo.RegisterCreatedObjectUndo(parent, "Create Waypoints Parent");
        }

        GameObject obj = new GameObject("TankWaypoint");
        obj.transform.position = position;
        obj.transform.SetParent(parent.transform);

        TankWaypoint wp = obj.AddComponent<TankWaypoint>();
        wp.reachRadius = waypointRadius;
        wp.ownerTeam = waypointTeam;

        Undo.RegisterCreatedObjectUndo(obj, "Create Tank Waypoint");

        // Chain mode - connect to last placed waypoint only
        if (autoConnect && lastPlaced != null)
        {
            Undo.RecordObject(wp, "Connect");
            Undo.RecordObject(lastPlaced, "Connect");
            wp.connections.Add(lastPlaced);
            lastPlaced.connections.Add(wp);
            EditorUtility.SetDirty(wp);
            EditorUtility.SetDirty(lastPlaced);
        }

        lastPlaced = wp;
        Selection.activeGameObject = obj;
        SceneView.RepaintAll();
    }

    void DeleteWaypoint(TankWaypoint waypoint)
    {
        if (waypoint == null) return;

        // Remove from all connections first
        TankWaypoint[] all = FindObjectsByType<TankWaypoint>(FindObjectsSortMode.None);
        foreach (var wp in all)
        {
            if (wp != null && wp != waypoint && wp.connections.Contains(waypoint))
            {
                Undo.RecordObject(wp, "Remove Connection");
                wp.connections.Remove(waypoint);
                EditorUtility.SetDirty(wp);
            }
        }

        // Clear chain if this was the last placed
        if (lastPlaced == waypoint)
        {
            lastPlaced = null;
        }

        // Clear connect from if this was selected
        if (connectFrom == waypoint)
        {
            connectFrom = null;
        }

        Undo.DestroyObjectImmediate(waypoint.gameObject);
        SceneView.RepaintAll();
        Repaint();
    }

    void ConnectWaypoints(TankWaypoint a, TankWaypoint b)
    {
        Undo.RecordObject(a, "Connect Waypoints");
        Undo.RecordObject(b, "Connect Waypoints");

        // Toggle connection - if already connected, disconnect
        if (a.connections.Contains(b))
        {
            a.connections.Remove(b);
            b.connections.Remove(a);
        }
        else
        {
            if (!a.connections.Contains(b)) a.connections.Add(b);
            if (!b.connections.Contains(a)) b.connections.Add(a);
        }

        EditorUtility.SetDirty(a);
        EditorUtility.SetDirty(b);
        SceneView.RepaintAll();
    }

    void ConnectSelected(TankWaypoint[] waypoints)
    {
        // Connect all to each other
        foreach (var wp in waypoints)
        {
            Undo.RecordObject(wp, "Connect Selected");
        }

        for (int i = 0; i < waypoints.Length; i++)
        {
            for (int j = i + 1; j < waypoints.Length; j++)
            {
                if (!waypoints[i].connections.Contains(waypoints[j]))
                    waypoints[i].connections.Add(waypoints[j]);
                if (!waypoints[j].connections.Contains(waypoints[i]))
                    waypoints[j].connections.Add(waypoints[i]);
            }
            EditorUtility.SetDirty(waypoints[i]);
        }

        Debug.Log($"Connected {waypoints.Length} waypoints to each other");
    }

    void ChainSelected(TankWaypoint[] waypoints)
    {
        // Connect in order of selection/proximity
        foreach (var wp in waypoints)
        {
            Undo.RecordObject(wp, "Chain Selected");
        }

        for (int i = 0; i < waypoints.Length - 1; i++)
        {
            if (!waypoints[i].connections.Contains(waypoints[i + 1]))
                waypoints[i].connections.Add(waypoints[i + 1]);
            if (!waypoints[i + 1].connections.Contains(waypoints[i]))
                waypoints[i + 1].connections.Add(waypoints[i]);
            EditorUtility.SetDirty(waypoints[i]);
        }
        EditorUtility.SetDirty(waypoints[waypoints.Length - 1]);

        Debug.Log($"Chained {waypoints.Length} waypoints");
    }

    void DisconnectSelected(TankWaypoint[] waypoints)
    {
        foreach (var wp in waypoints)
        {
            Undo.RecordObject(wp, "Disconnect Selected");
        }

        HashSet<TankWaypoint> set = new HashSet<TankWaypoint>(waypoints);
        foreach (var wp in waypoints)
        {
            wp.connections.RemoveAll(c => set.Contains(c));
            EditorUtility.SetDirty(wp);
        }

        Debug.Log($"Disconnected {waypoints.Length} waypoints from each other");
    }

    void AutoConnectAll(TankWaypoint[] waypoints)
    {
        foreach (var wp in waypoints)
        {
            Undo.RecordObject(wp, "Auto Connect All");
            wp.AutoConnectNearby();
            EditorUtility.SetDirty(wp);
        }

        Debug.Log($"Auto-connected {waypoints.Length} waypoints");
    }

    void CreateRing(int count, float radius)
    {
        SceneView sceneView = SceneView.lastActiveSceneView;
        Vector3 center = sceneView != null ? sceneView.pivot : Vector3.zero;

        // Find or create parent
        GameObject parent = GameObject.Find("TankWaypoints");
        if (parent == null)
        {
            parent = new GameObject("TankWaypoints");
            Undo.RegisterCreatedObjectUndo(parent, "Create Waypoints Parent");
        }

        List<TankWaypoint> created = new List<TankWaypoint>();

        for (int i = 0; i < count; i++)
        {
            float angle = (360f / count) * i;
            Vector3 offset = Quaternion.Euler(0, angle, 0) * Vector3.forward * radius;
            Vector3 pos = center + offset;

            if (snapToGround && Physics.Raycast(pos + Vector3.up * 100f, Vector3.down, out RaycastHit hit, 200f))
            {
                pos = hit.point;
            }

            GameObject obj = new GameObject("TankWaypoint");
            obj.transform.position = pos;
            obj.transform.SetParent(parent.transform);

            TankWaypoint wp = obj.AddComponent<TankWaypoint>();
            wp.reachRadius = waypointRadius;
            wp.ownerTeam = waypointTeam;

            Undo.RegisterCreatedObjectUndo(obj, "Create Ring");
            created.Add(wp);
        }

        // Connect ring
        for (int i = 0; i < created.Count; i++)
        {
            int next = (i + 1) % created.Count;
            created[i].connections.Add(created[next]);
            created[next].connections.Add(created[i]);
            EditorUtility.SetDirty(created[i]);
        }

        Debug.Log($"Created ring of {count} waypoints");
    }

    void CreateGrid(int width, int height, float spacing)
    {
        SceneView sceneView = SceneView.lastActiveSceneView;
        Vector3 center = sceneView != null ? sceneView.pivot : Vector3.zero;
        Vector3 start = center - new Vector3((width - 1) * spacing / 2f, 0, (height - 1) * spacing / 2f);

        // Find or create parent
        GameObject parent = GameObject.Find("TankWaypoints");
        if (parent == null)
        {
            parent = new GameObject("TankWaypoints");
            Undo.RegisterCreatedObjectUndo(parent, "Create Waypoints Parent");
        }

        TankWaypoint[,] grid = new TankWaypoint[width, height];

        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                Vector3 pos = start + new Vector3(x * spacing, 0, z * spacing);

                if (snapToGround && Physics.Raycast(pos + Vector3.up * 100f, Vector3.down, out RaycastHit hit, 200f))
                {
                    pos = hit.point;
                }

                GameObject obj = new GameObject("TankWaypoint");
                obj.transform.position = pos;
                obj.transform.SetParent(parent.transform);

                TankWaypoint wp = obj.AddComponent<TankWaypoint>();
                wp.reachRadius = waypointRadius;
                wp.ownerTeam = waypointTeam;

                Undo.RegisterCreatedObjectUndo(obj, "Create Grid");
                grid[x, z] = wp;
            }
        }

        // Connect grid
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                if (x > 0)
                {
                    grid[x, z].connections.Add(grid[x - 1, z]);
                }
                if (x < width - 1)
                {
                    grid[x, z].connections.Add(grid[x + 1, z]);
                }
                if (z > 0)
                {
                    grid[x, z].connections.Add(grid[x, z - 1]);
                }
                if (z < height - 1)
                {
                    grid[x, z].connections.Add(grid[x, z + 1]);
                }
                EditorUtility.SetDirty(grid[x, z]);
            }
        }

        Debug.Log($"Created {width}x{height} waypoint grid");
    }
}
