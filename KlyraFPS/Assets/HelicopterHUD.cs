using UnityEngine;

public class HelicopterHUD : MonoBehaviour
{
    private HelicopterController helicopter;
    private HelicopterSeat currentSeat;
    private HelicopterWeapon currentWeapon;

    private GUIStyle labelStyle;
    private GUIStyle warningStyle;
    private GUIStyle boxStyle;
    private bool stylesInitialized = false;
    private Texture2D bgTexture;
    private Texture2D barTexture;

    void Awake()
    {
        // Create textures
        bgTexture = new Texture2D(1, 1);
        bgTexture.SetPixel(0, 0, new Color(0, 0, 0, 0.5f));
        bgTexture.Apply();

        barTexture = new Texture2D(1, 1);
        barTexture.SetPixel(0, 0, Color.white);
        barTexture.Apply();
    }

    void InitStyles()
    {
        if (stylesInitialized) return;

        labelStyle = new GUIStyle(GUI.skin.label);
        labelStyle.fontSize = 18;
        labelStyle.fontStyle = FontStyle.Bold;
        labelStyle.normal.textColor = Color.white;

        warningStyle = new GUIStyle(labelStyle);
        warningStyle.normal.textColor = Color.red;
        warningStyle.fontSize = 24;
        warningStyle.alignment = TextAnchor.MiddleCenter;

        boxStyle = new GUIStyle(GUI.skin.box);
        boxStyle.normal.background = bgTexture;

        stylesInitialized = true;
    }

    public void SetHelicopter(HelicopterController heli)
    {
        helicopter = heli;
    }

    public void SetSeat(HelicopterSeat seat)
    {
        currentSeat = seat;
        currentWeapon = seat != null ? seat.mountedWeapon : null;
    }

    void OnGUI()
    {
        if (helicopter == null) return;

        InitStyles();

        float panelWidth = 250f;
        float panelHeight = 150f;
        float padding = 10f;

        // Draw helicopter status in bottom left
        Rect panelRect = new Rect(padding, Screen.height - panelHeight - padding, panelWidth, panelHeight);
        GUI.Box(panelRect, "", boxStyle);

        GUILayout.BeginArea(new Rect(panelRect.x + 10, panelRect.y + 10, panelWidth - 20, panelHeight - 20));

        // Altitude
        float altitude = helicopter.transform.position.y;
        GUILayout.Label($"ALT: {altitude:F0}m", labelStyle);

        // Speed
        Rigidbody rb = helicopter.GetComponent<Rigidbody>();
        float speed = rb != null ? rb.linearVelocity.magnitude * 3.6f : 0; // Convert to km/h
        GUILayout.Label($"SPD: {speed:F0} km/h", labelStyle);

        // Health bar
        float healthPercent = helicopter.currentHealth / helicopter.maxHealth;
        DrawBar("HULL", healthPercent, healthPercent > 0.3f ? Color.green : Color.red);

        // Engine status
        string engineStatus = helicopter.currentHealth > 0 ? (helicopter.engineOn ? "ON" : "OFF") : "DESTROYED";
        Color engineColor = helicopter.engineOn ? Color.green : Color.gray;
        if (helicopter.isDestroyed) engineColor = Color.red;
        GUI.color = engineColor;
        GUILayout.Label($"ENGINE: {engineStatus}", labelStyle);
        GUI.color = Color.white;

        GUILayout.EndArea();

        // Draw weapon info for door gunners
        if (currentWeapon != null &&
            (currentSeat.seatType == SeatType.DoorGunnerLeft || currentSeat.seatType == SeatType.DoorGunnerRight))
        {
            DrawWeaponHUD();
        }

        // Draw controls help in bottom right
        DrawControlsHelp();

        // Draw warnings
        if (helicopter.isDestroyed)
        {
            GUI.Label(new Rect(0, Screen.height / 2 - 50, Screen.width, 50), "HELICOPTER DESTROYED", warningStyle);
        }

        // Draw crosshair for door gunners
        if (currentWeapon != null)
        {
            DrawCrosshair();
        }
    }

    void DrawBar(string label, float percent, Color color)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label($"{label}:", labelStyle, GUILayout.Width(60));

        Rect barBg = GUILayoutUtility.GetRect(150, 16);
        GUI.color = Color.gray;
        GUI.DrawTexture(barBg, barTexture);

        Rect barFill = new Rect(barBg.x, barBg.y, barBg.width * Mathf.Clamp01(percent), barBg.height);
        GUI.color = color;
        GUI.DrawTexture(barFill, barTexture);

        GUI.color = Color.white;
        GUILayout.EndHorizontal();
    }

    void DrawWeaponHUD()
    {
        float panelWidth = 200f;
        float panelHeight = 100f;
        float padding = 10f;

        // Weapon panel in bottom center
        Rect panelRect = new Rect(Screen.width / 2 - panelWidth / 2, Screen.height - panelHeight - padding, panelWidth, panelHeight);
        GUI.Box(panelRect, "", boxStyle);

        GUILayout.BeginArea(new Rect(panelRect.x + 10, panelRect.y + 10, panelWidth - 20, panelHeight - 20));

        if (currentWeapon.weaponType == HeliWeaponType.Minigun)
        {
            GUILayout.Label("MINIGUN", labelStyle);

            // Heat bar
            float heatPercent = currentWeapon.GetHeatPercentage();
            Color heatColor = heatPercent < 0.7f ? Color.cyan : (heatPercent < 0.9f ? Color.yellow : Color.red);
            DrawBar("HEAT", heatPercent, heatColor);

            if (currentWeapon.IsOverheated())
            {
                GUI.color = Color.red;
                GUILayout.Label("OVERHEATED!", labelStyle);
                GUI.color = Color.white;
            }
        }
        else if (currentWeapon.weaponType == HeliWeaponType.RocketPod)
        {
            GUILayout.Label("ROCKETS", labelStyle);
            GUILayout.Label($"AMMO: {currentWeapon.GetRocketCount()}", labelStyle);
        }

        GUILayout.EndArea();
    }

    void DrawControlsHelp()
    {
        float panelWidth = 200f;
        float panelHeight = 120f;
        float padding = 10f;

        Rect panelRect = new Rect(Screen.width - panelWidth - padding, Screen.height - panelHeight - padding, panelWidth, panelHeight);
        GUI.Box(panelRect, "", boxStyle);

        GUIStyle smallStyle = new GUIStyle(labelStyle);
        smallStyle.fontSize = 12;

        GUILayout.BeginArea(new Rect(panelRect.x + 10, panelRect.y + 10, panelWidth - 20, panelHeight - 20));

        if (currentSeat != null)
        {
            switch (currentSeat.seatType)
            {
                case SeatType.Pilot:
                    GUILayout.Label("PILOT CONTROLS", smallStyle);
                    GUILayout.Label("WASD - Move", smallStyle);
                    GUILayout.Label("Space/Ctrl - Up/Down", smallStyle);
                    GUILayout.Label("Mouse - Pitch/Yaw", smallStyle);
                    GUILayout.Label("E - Toggle Engine", smallStyle);
                    GUILayout.Label("F - Exit (when slow)", smallStyle);
                    break;

                case SeatType.DoorGunnerLeft:
                case SeatType.DoorGunnerRight:
                    GUILayout.Label("GUNNER CONTROLS", smallStyle);
                    GUILayout.Label("Mouse - Aim", smallStyle);
                    GUILayout.Label("LMB - Fire", smallStyle);
                    GUILayout.Label("1-6 - Switch Seat", smallStyle);
                    GUILayout.Label("F - Exit (when slow)", smallStyle);
                    break;

                default:
                    GUILayout.Label("PASSENGER", smallStyle);
                    GUILayout.Label("Mouse - Look Around", smallStyle);
                    GUILayout.Label("1-6 - Switch Seat", smallStyle);
                    GUILayout.Label("F - Exit (when slow)", smallStyle);
                    break;
            }
        }

        GUILayout.EndArea();
    }

    void DrawCrosshair()
    {
        float size = 20f;
        float centerX = Screen.width / 2f;
        float centerY = Screen.height / 2f;

        GUI.color = currentWeapon.IsOverheated() ? Color.red : Color.green;

        // Draw crosshair lines
        GUI.DrawTexture(new Rect(centerX - size, centerY - 1, size * 0.8f, 2), barTexture);
        GUI.DrawTexture(new Rect(centerX + size * 0.2f, centerY - 1, size * 0.8f, 2), barTexture);
        GUI.DrawTexture(new Rect(centerX - 1, centerY - size, 2, size * 0.8f), barTexture);
        GUI.DrawTexture(new Rect(centerX - 1, centerY + size * 0.2f, 2, size * 0.8f), barTexture);

        // Center dot
        GUI.DrawTexture(new Rect(centerX - 2, centerY - 2, 4, 4), barTexture);

        GUI.color = Color.white;
    }
}
