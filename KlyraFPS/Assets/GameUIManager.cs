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
        Settings,
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

    // Graphics settings
    private int shadowQuality = 1;        // 0=Off, 1=Low, 2=Medium, 3=High
    private float shadowDistance = 40f;
    private int qualityLevel = 0;         // 0=Mobile, 1=PC
    private float resolutionScale = 1f;
    private bool vSyncEnabled = false;

    // Audio settings
    private float masterVolume = 1f;      // 0-1

    // Display settings
    private bool fullscreenEnabled = true;
    private float fieldOfView = 75f;      // 60-120
    private bool showFPS = false;

    // Control settings
    private bool invertYAxis = false;

    // Singleton instance for settings access
    public static GameUIManager Instance { get; private set; }

    // Public getters for settings that other scripts need
    public static bool InvertY => Instance != null ? Instance.invertYAxis : false;
    public static float FOV => Instance != null ? Instance.fieldOfView : 75f;
    public static float Sensitivity => Instance != null ? Instance.mouseSensitivity : 2f;

    // FPS counter
    private float fpsUpdateTimer = 0f;
    private float currentFPS = 0f;
    private int frameCount = 0;

    // Settings labels
    private static readonly string[] shadowOptions = { "Off", "Low", "Medium", "High" };
    private static readonly string[] qualityOptions = { "Mobile", "PC" };

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
    private float menuTimer = 0f;
    private int hoveredButton = -1;
    private float[] buttonHoverAnim = new float[4];

    // Particle system for background
    private Vector2[] particles = new Vector2[50];
    private float[] particleSpeeds = new float[50];
    private float[] particleSizes = new float[50];
    private float[] particleAlphas = new float[50];
    private bool particlesInitialized = false;

    void Start()
    {
        Instance = this;

        PhotonNetwork.AutomaticallySyncScene = true;
        PhotonNetwork.PhotonServerSettings.AppSettings.FixedRegion = "us";

        // Confine cursor to window on start screen
        Cursor.lockState = CursorLockMode.Confined;
        Cursor.visible = false;

        // Load saved settings
        LoadSettings();
        ApplySettings();

        InitParticles();
    }

    void InitParticles()
    {
        if (particlesInitialized) return;
        for (int i = 0; i < particles.Length; i++)
        {
            particles[i] = new Vector2(Random.Range(0f, 1f), Random.Range(0f, 1f));
            particleSpeeds[i] = Random.Range(0.01f, 0.05f);
            particleSizes[i] = Random.Range(1f, 4f);
            particleAlphas[i] = Random.Range(0.1f, 0.4f);
        }
        particlesInitialized = true;
    }

    void Update()
    {
        pressStartTimer += Time.deltaTime;
        menuTimer += Time.deltaTime;

        // FPS counter
        frameCount++;
        fpsUpdateTimer += Time.unscaledDeltaTime;
        if (fpsUpdateTimer >= 0.5f)
        {
            currentFPS = frameCount / fpsUpdateTimer;
            frameCount = 0;
            fpsUpdateTimer = 0f;
        }

        // Update particles
        for (int i = 0; i < particles.Length; i++)
        {
            particles[i].y -= particleSpeeds[i] * Time.deltaTime;
            if (particles[i].y < 0)
            {
                particles[i].y = 1f;
                particles[i].x = Random.Range(0f, 1f);
            }
        }

        // Update button hover animations
        for (int i = 0; i < buttonHoverAnim.Length; i++)
        {
            float target = (i == hoveredButton) ? 1f : 0f;
            buttonHoverAnim[i] = Mathf.Lerp(buttonHoverAnim[i], target, Time.deltaTime * 10f);
        }

        // Press Start screen - any key to continue
        if (currentState == GameState.PressStart)
        {
            Cursor.visible = false;
            Cursor.lockState = CursorLockMode.Confined;

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
                menuTimer = 0f;
            }
        }
        else if (currentState == GameState.InGame)
        {
            // In-game cursor handled by FPSController
        }
        else
        {
            // Show cursor for menus
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
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
            case GameState.Settings:
                DrawSettings();
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

        // FPS Counter (always on top)
        if (showFPS)
        {
            DrawFPSCounter();
        }
    }

    void DrawFPSCounter()
    {
        GUIStyle fpsStyle = new GUIStyle(GUI.skin.label);
        fpsStyle.fontSize = 16;
        fpsStyle.fontStyle = FontStyle.Bold;
        fpsStyle.alignment = TextAnchor.UpperLeft;

        // Color based on FPS
        Color fpsColor;
        if (currentFPS >= 60f)
            fpsColor = Color.green;
        else if (currentFPS >= 30f)
            fpsColor = Color.yellow;
        else
            fpsColor = Color.red;

        // Background
        GUI.color = new Color(0, 0, 0, 0.5f);
        GUI.DrawTexture(new Rect(8, 8, 80, 24), Texture2D.whiteTexture);

        // FPS text
        GUI.color = fpsColor;
        GUI.Label(new Rect(12, 8, 80, 24), $"FPS: {Mathf.RoundToInt(currentFPS)}", fpsStyle);
        GUI.color = Color.white;
    }

    void DrawPressStart()
    {
        float centerX = Screen.width / 2f;
        float centerY = Screen.height / 2f;

        // Cinematic dark gradient background
        DrawCinematicBackground();

        // Floating particles
        DrawParticles();

        // Animated light rays from center
        DrawLightRays(centerX, centerY * 0.7f);

        // Vignette effect
        DrawVignette();

        // Title animation - slides in from top
        float titleSlide = Mathf.Clamp01(pressStartTimer * 2f);
        float titleY = Mathf.Lerp(-200, centerY - 140, EaseOutBack(titleSlide));

        // Title glow effect
        GUIStyle titleStyle = new GUIStyle(GUI.skin.label);
        titleStyle.fontSize = Mathf.RoundToInt(Screen.height * 0.15f);
        titleStyle.fontStyle = FontStyle.Bold;
        titleStyle.alignment = TextAnchor.MiddleCenter;

        // Multiple glow layers
        float glowPulse = 0.7f + Mathf.Sin(pressStartTimer * 2f) * 0.3f;
        for (int i = 4; i >= 0; i--)
        {
            float glowAlpha = (0.15f - i * 0.025f) * glowPulse;
            GUI.color = new Color(1f, 0.4f, 0.1f, glowAlpha);
            GUI.Label(new Rect(-i * 3, titleY - i * 3, Screen.width + i * 6, 120), "KLYRA", titleStyle);
        }

        // Title shadow
        GUI.color = new Color(0, 0, 0, 0.9f);
        GUI.Label(new Rect(4, titleY + 4, Screen.width, 120), "KLYRA", titleStyle);

        // Main title with gradient effect (simulate with two colors)
        GUI.color = new Color(1f, 0.5f, 0.1f, 1f);
        GUI.Label(new Rect(0, titleY, Screen.width, 120), "KLYRA", titleStyle);

        // Subtitle with slide animation
        float subSlide = Mathf.Clamp01((pressStartTimer - 0.3f) * 2f);
        float subAlpha = EaseOutQuad(subSlide);

        GUIStyle subStyle = new GUIStyle(GUI.skin.label);
        subStyle.fontSize = Mathf.RoundToInt(Screen.height * 0.03f);
        subStyle.fontStyle = FontStyle.Normal;
        subStyle.alignment = TextAnchor.MiddleCenter;

        GUI.color = new Color(0.9f, 0.9f, 0.9f, subAlpha * 0.9f);
        GUI.Label(new Rect(0, titleY + 110, Screen.width, 40), "T A C T I C A L   W A R F A R E", subStyle);

        // Decorative line under subtitle
        float lineAlpha = EaseOutQuad(Mathf.Clamp01((pressStartTimer - 0.5f) * 2f));
        float lineWidth = 280f * lineAlpha;
        GUI.color = new Color(1f, 0.5f, 0.2f, lineAlpha * 0.7f);
        GUI.DrawTexture(new Rect(centerX - lineWidth/2, titleY + 145, lineWidth, 2), Texture2D.whiteTexture);

        // Glassmorphic panel for press any key
        float panelSlide = Mathf.Clamp01((pressStartTimer - 0.8f) * 1.5f);
        if (panelSlide > 0)
        {
            float panelY = centerY + 80;
            float panelWidth = 320f;
            float panelHeight = 70f;
            Rect panelRect = new Rect(centerX - panelWidth/2, panelY, panelWidth, panelHeight);

            DrawGlassPanel(panelRect, new Color(1f, 1f, 1f, 0.08f * panelSlide), 15f);

            // Pulsing border
            float pulse = (Mathf.Sin(pressStartTimer * 3f) + 1f) / 2f;
            Color borderColor = Color.Lerp(new Color(1f, 0.4f, 0.1f, 0.3f), new Color(1f, 0.6f, 0.2f, 0.8f), pulse);
            DrawGlassBorder(panelRect, borderColor, 2f);

            // Press any key text
            GUIStyle pressStyle = new GUIStyle(GUI.skin.label);
            pressStyle.fontSize = Mathf.RoundToInt(Screen.height * 0.028f);
            pressStyle.fontStyle = FontStyle.Bold;
            pressStyle.alignment = TextAnchor.MiddleCenter;

            GUI.color = Color.Lerp(new Color(0.8f, 0.8f, 0.8f, 0.6f), new Color(1f, 1f, 1f, 1f), pulse) * panelSlide;
            GUI.Label(panelRect, "PRESS ANY KEY", pressStyle);
        }

        // Bottom bar with version info
        float barAlpha = Mathf.Clamp01((pressStartTimer - 1f) * 2f);
        if (barAlpha > 0)
        {
            // Glassmorphic bottom bar
            Rect bottomBar = new Rect(0, Screen.height - 45, Screen.width, 45);
            GUI.color = new Color(0, 0, 0, 0.4f * barAlpha);
            GUI.DrawTexture(bottomBar, Texture2D.whiteTexture);
            GUI.color = new Color(1f, 0.5f, 0.2f, 0.3f * barAlpha);
            GUI.DrawTexture(new Rect(0, Screen.height - 45, Screen.width, 1), Texture2D.whiteTexture);

            GUIStyle infoStyle = new GUIStyle(GUI.skin.label);
            infoStyle.fontSize = Mathf.RoundToInt(Screen.height * 0.016f);
            infoStyle.alignment = TextAnchor.MiddleLeft;

            GUI.color = new Color(0.7f, 0.7f, 0.7f, barAlpha * 0.8f);
            GUI.Label(new Rect(25, Screen.height - 40, 200, 30), "ALPHA v0.1", infoStyle);

            infoStyle.alignment = TextAnchor.MiddleRight;
            GUI.color = new Color(1f, 0.5f, 0.2f, barAlpha * 0.6f);
            GUI.Label(new Rect(Screen.width - 225, Screen.height - 40, 200, 30),
                System.DateTime.Now.ToString("yyyy.MM.dd  HH:mm"), infoStyle);
        }

        GUI.color = Color.white;
    }

    void DrawCinematicBackground()
    {
        // Deep dark gradient
        Color topColor = new Color(0.02f, 0.02f, 0.04f);
        Color midColor = new Color(0.08f, 0.04f, 0.02f);
        Color bottomColor = new Color(0.01f, 0.01f, 0.02f);

        int steps = 30;
        for (int i = 0; i < steps; i++)
        {
            float t = i / (float)steps;
            Color c;
            if (t < 0.5f)
                c = Color.Lerp(topColor, midColor, t * 2f);
            else
                c = Color.Lerp(midColor, bottomColor, (t - 0.5f) * 2f);

            GUI.color = c;
            float y = Screen.height * t;
            float h = Screen.height / (float)steps + 1;
            GUI.DrawTexture(new Rect(0, y, Screen.width, h), Texture2D.whiteTexture);
        }

        // Subtle noise/grain effect
        GUI.color = new Color(1f, 1f, 1f, 0.02f);
        float noiseOffset = pressStartTimer * 100f;
        for (int i = 0; i < 100; i++)
        {
            float x = Mathf.PerlinNoise(i * 0.1f, noiseOffset) * Screen.width;
            float y = Mathf.PerlinNoise(noiseOffset, i * 0.1f) * Screen.height;
            GUI.DrawTexture(new Rect(x, y, 2, 2), Texture2D.whiteTexture);
        }
    }

    void DrawLightRays(float cx, float cy)
    {
        int rayCount = 12;
        float maxLength = Mathf.Max(Screen.width, Screen.height) * 0.8f;

        for (int i = 0; i < rayCount; i++)
        {
            float baseAngle = (i / (float)rayCount) * Mathf.PI * 2f;
            float animAngle = baseAngle + pressStartTimer * 0.1f;
            float rayAlpha = 0.03f + Mathf.Sin(pressStartTimer * 2f + i) * 0.02f;

            GUI.color = new Color(1f, 0.6f, 0.3f, rayAlpha);

            Vector2 start = new Vector2(cx, cy);
            Vector2 end = new Vector2(
                cx + Mathf.Cos(animAngle) * maxLength,
                cy + Mathf.Sin(animAngle) * maxLength * 0.5f
            );

            DrawLine(start, end, GUI.color, 40f);
        }
    }

    void DrawParticles()
    {
        InitParticles();
        for (int i = 0; i < particles.Length; i++)
        {
            float x = particles[i].x * Screen.width;
            float y = particles[i].y * Screen.height;
            float size = particleSizes[i];
            float alpha = particleAlphas[i] * (0.5f + Mathf.Sin(pressStartTimer * 3f + i) * 0.5f);

            GUI.color = new Color(1f, 0.7f, 0.4f, alpha);
            GUI.DrawTexture(new Rect(x, y, size, size), Texture2D.whiteTexture);
        }
    }

    void DrawVignette()
    {
        float vignetteStrength = 0.6f;
        int steps = 20;

        for (int i = 0; i < steps; i++)
        {
            float t = i / (float)steps;
            float alpha = t * t * vignetteStrength;
            float inset = (1f - t) * Mathf.Min(Screen.width, Screen.height) * 0.5f;

            GUI.color = new Color(0, 0, 0, alpha);

            // Top
            GUI.DrawTexture(new Rect(0, 0, Screen.width, inset * 0.3f), Texture2D.whiteTexture);
            // Bottom
            GUI.DrawTexture(new Rect(0, Screen.height - inset * 0.3f, Screen.width, inset * 0.3f), Texture2D.whiteTexture);
            // Left
            GUI.DrawTexture(new Rect(0, 0, inset * 0.3f, Screen.height), Texture2D.whiteTexture);
            // Right
            GUI.DrawTexture(new Rect(Screen.width - inset * 0.3f, 0, inset * 0.3f, Screen.height), Texture2D.whiteTexture);
        }
    }

    void DrawGlassPanel(Rect rect, Color bgColor, float cornerRadius)
    {
        // Background with slight transparency
        GUI.color = bgColor;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);

        // Inner highlight (top edge)
        GUI.color = new Color(1f, 1f, 1f, 0.1f);
        GUI.DrawTexture(new Rect(rect.x + 2, rect.y + 2, rect.width - 4, 1), Texture2D.whiteTexture);

        // Inner shadow (bottom edge)
        GUI.color = new Color(0f, 0f, 0f, 0.2f);
        GUI.DrawTexture(new Rect(rect.x + 2, rect.y + rect.height - 3, rect.width - 4, 1), Texture2D.whiteTexture);
    }

    void DrawGlassBorder(Rect rect, Color color, float thickness)
    {
        GUI.color = color;
        // Top
        GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, thickness), Texture2D.whiteTexture);
        // Bottom
        GUI.DrawTexture(new Rect(rect.x, rect.y + rect.height - thickness, rect.width, thickness), Texture2D.whiteTexture);
        // Left
        GUI.DrawTexture(new Rect(rect.x, rect.y, thickness, rect.height), Texture2D.whiteTexture);
        // Right
        GUI.DrawTexture(new Rect(rect.x + rect.width - thickness, rect.y, thickness, rect.height), Texture2D.whiteTexture);

        // Corner accents
        float accentSize = 8f;
        GUI.color = new Color(color.r, color.g, color.b, color.a * 1.5f);
        GUI.DrawTexture(new Rect(rect.x - 1, rect.y - 1, accentSize, thickness + 2), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(rect.x - 1, rect.y - 1, thickness + 2, accentSize), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(rect.x + rect.width - accentSize + 1, rect.y - 1, accentSize, thickness + 2), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(rect.x + rect.width - thickness, rect.y - 1, thickness + 2, accentSize), Texture2D.whiteTexture);
    }

    float EaseOutBack(float t)
    {
        float c1 = 1.70158f;
        float c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
    }

    float EaseOutQuad(float t)
    {
        return 1f - (1f - t) * (1f - t);
    }

    void DrawLine(Vector2 start, Vector2 end, Color color, float width)
    {
        GUI.color = color;
        Vector2 dir = (end - start).normalized;
        float length = Vector2.Distance(start, end);
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

        Matrix4x4 matrixBackup = GUI.matrix;
        GUIUtility.RotateAroundPivot(angle, start);
        GUI.DrawTexture(new Rect(start.x, start.y - width/2, length, width), Texture2D.whiteTexture);
        GUI.matrix = matrixBackup;
    }

    void DrawMainMenu()
    {
        float centerX = Screen.width / 2f;
        float centerY = Screen.height / 2f;

        // Cool blue/teal gradient background (different from warm start screen)
        DrawMenuBackground();

        // Subtle animated grid
        DrawAnimatedGrid();

        // Floating particles (blue tint)
        DrawMenuParticles();

        // Vignette
        DrawVignette();

        // Main content panel - glassmorphic
        float panelWidth = 400f;
        float panelHeight = 450f;
        float panelSlide = Mathf.Clamp01(menuTimer * 2f);
        float panelX = Mathf.Lerp(-panelWidth, centerX - panelWidth / 2f, EaseOutBack(panelSlide));
        Rect panelRect = new Rect(panelX, centerY - panelHeight / 2f + 20, panelWidth, panelHeight);

        // Panel glow
        for (int i = 3; i >= 0; i--)
        {
            float glowAlpha = 0.03f - i * 0.007f;
            GUI.color = new Color(0.2f, 0.6f, 0.9f, glowAlpha * panelSlide);
            GUI.DrawTexture(new Rect(panelRect.x - i * 8, panelRect.y - i * 8,
                panelRect.width + i * 16, panelRect.height + i * 16), Texture2D.whiteTexture);
        }

        // Main panel
        DrawGlassPanel(panelRect, new Color(0.05f, 0.08f, 0.15f, 0.85f * panelSlide), 0);
        DrawMenuPanelBorder(panelRect, new Color(0.3f, 0.7f, 0.9f, 0.4f * panelSlide));

        // Title with glow
        float titleSlide = Mathf.Clamp01((menuTimer - 0.2f) * 2.5f);
        float titleY = panelRect.y + 35;

        GUIStyle menuTitleStyle = new GUIStyle(GUI.skin.label);
        menuTitleStyle.fontSize = Mathf.RoundToInt(Screen.height * 0.085f);
        menuTitleStyle.fontStyle = FontStyle.Bold;
        menuTitleStyle.alignment = TextAnchor.MiddleCenter;

        // Title glow
        float glowPulse = 0.7f + Mathf.Sin(menuTimer * 1.5f) * 0.3f;
        for (int i = 3; i >= 0; i--)
        {
            float ga = (0.12f - i * 0.03f) * glowPulse * titleSlide;
            GUI.color = new Color(0.3f, 0.7f, 1f, ga);
            GUI.Label(new Rect(panelRect.x - i * 2, titleY - i * 2, panelRect.width + i * 4, 80), "KLYRA", menuTitleStyle);
        }

        // Title shadow
        GUI.color = new Color(0, 0, 0, 0.8f * titleSlide);
        GUI.Label(new Rect(panelRect.x + 3, titleY + 3, panelRect.width, 80), "KLYRA", menuTitleStyle);

        // Main title
        GUI.color = new Color(0.9f, 0.95f, 1f, titleSlide);
        GUI.Label(new Rect(panelRect.x, titleY, panelRect.width, 80), "KLYRA", menuTitleStyle);

        // Decorative line under title
        float lineAlpha = EaseOutQuad(Mathf.Clamp01((menuTimer - 0.3f) * 2f));
        float lineWidth = 180f * lineAlpha;
        GUI.color = new Color(0.3f, 0.7f, 0.9f, lineAlpha * 0.6f);
        GUI.DrawTexture(new Rect(centerX - lineWidth / 2, titleY + 75, lineWidth, 2), Texture2D.whiteTexture);

        // Menu buttons
        float buttonWidth = 280f;
        float buttonHeight = 55f;
        float buttonSpacing = 15f;
        float buttonX = panelRect.x + (panelRect.width - buttonWidth) / 2f;
        float buttonStartY = titleY + 110;

        string[] buttonLabels = { "PLAY", "SETTINGS", "QUIT" };
        int clickedButton = -1;

        for (int i = 0; i < 3; i++)
        {
            float buttonSlide = Mathf.Clamp01((menuTimer - 0.4f - i * 0.1f) * 2.5f);
            float buttonY = buttonStartY + i * (buttonHeight + buttonSpacing);
            float slideX = Mathf.Lerp(buttonX + 100, buttonX, EaseOutBack(buttonSlide));

            Rect btnRect = new Rect(slideX, buttonY, buttonWidth, buttonHeight);

            // Check hover
            bool isHovered = btnRect.Contains(Event.current.mousePosition);
            if (isHovered && hoveredButton != i)
            {
                hoveredButton = i;
            }
            else if (!isHovered && hoveredButton == i)
            {
                hoveredButton = -1;
            }

            float hoverAnim = buttonHoverAnim[i];

            // Button glow on hover
            if (hoverAnim > 0.01f)
            {
                GUI.color = new Color(0.3f, 0.7f, 1f, 0.15f * hoverAnim * buttonSlide);
                GUI.DrawTexture(new Rect(btnRect.x - 5, btnRect.y - 5, btnRect.width + 10, btnRect.height + 10), Texture2D.whiteTexture);
            }

            // Button background
            Color bgColor = Color.Lerp(new Color(0.1f, 0.15f, 0.25f, 0.7f), new Color(0.15f, 0.25f, 0.4f, 0.9f), hoverAnim);
            GUI.color = bgColor * buttonSlide;
            GUI.DrawTexture(btnRect, Texture2D.whiteTexture);

            // Button border
            Color borderColor = Color.Lerp(new Color(0.3f, 0.5f, 0.7f, 0.4f), new Color(0.4f, 0.8f, 1f, 0.9f), hoverAnim);
            DrawButtonBorder(btnRect, borderColor * buttonSlide, 1.5f + hoverAnim);

            // Left accent bar (animated on hover)
            float accentWidth = Mathf.Lerp(3f, 6f, hoverAnim);
            GUI.color = Color.Lerp(new Color(0.3f, 0.6f, 0.9f, 0.6f), new Color(0.4f, 0.9f, 1f, 1f), hoverAnim) * buttonSlide;
            GUI.DrawTexture(new Rect(btnRect.x, btnRect.y, accentWidth, btnRect.height), Texture2D.whiteTexture);

            // Button text
            GUIStyle btnTextStyle = new GUIStyle(GUI.skin.label);
            btnTextStyle.fontSize = Mathf.RoundToInt(Screen.height * 0.028f);
            btnTextStyle.fontStyle = FontStyle.Bold;
            btnTextStyle.alignment = TextAnchor.MiddleCenter;

            // Text shadow
            GUI.color = new Color(0, 0, 0, 0.5f * buttonSlide);
            GUI.Label(new Rect(btnRect.x + 2, btnRect.y + 2, btnRect.width, btnRect.height), buttonLabels[i], btnTextStyle);

            // Main text
            GUI.color = Color.Lerp(new Color(0.8f, 0.85f, 0.9f, 1f), Color.white, hoverAnim) * buttonSlide;
            GUI.Label(btnRect, buttonLabels[i], btnTextStyle);

            // Invisible button for click detection
            GUI.color = new Color(0, 0, 0, 0);
            if (GUI.Button(btnRect, "") && buttonSlide > 0.8f)
            {
                clickedButton = i;
            }
        }

        // Handle button clicks
        if (clickedButton == 0)
        {
            ConnectToServer();
        }
        else if (clickedButton == 1)
        {
            currentState = GameState.Settings;
            menuTimer = 0f;
        }
        else if (clickedButton == 2)
        {
            #if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
            #else
            Application.Quit();
            #endif
        }

        // Bottom info bar
        float barAlpha = Mathf.Clamp01((menuTimer - 0.8f) * 2f);
        if (barAlpha > 0)
        {
            GUIStyle infoStyle = new GUIStyle(GUI.skin.label);
            infoStyle.fontSize = Mathf.RoundToInt(Screen.height * 0.015f);
            infoStyle.alignment = TextAnchor.MiddleCenter;

            GUI.color = new Color(0.5f, 0.6f, 0.7f, barAlpha * 0.6f);
            GUI.Label(new Rect(0, Screen.height - 35, Screen.width, 25),
                "USE WASD TO MOVE  |  MOUSE TO AIM  |  LEFT CLICK TO SHOOT  |  CAPS LOCK FOR SCOREBOARD", infoStyle);
        }

        GUI.color = Color.white;
    }

    void DrawMenuBackground()
    {
        // Cool blue gradient (contrasting with warm orange start screen)
        Color topColor = new Color(0.02f, 0.04f, 0.08f);
        Color midColor = new Color(0.03f, 0.06f, 0.12f);
        Color bottomColor = new Color(0.01f, 0.02f, 0.05f);

        int steps = 30;
        for (int i = 0; i < steps; i++)
        {
            float t = i / (float)steps;
            Color c;
            if (t < 0.5f)
                c = Color.Lerp(topColor, midColor, t * 2f);
            else
                c = Color.Lerp(midColor, bottomColor, (t - 0.5f) * 2f);

            GUI.color = c;
            float y = Screen.height * t;
            float h = Screen.height / (float)steps + 1;
            GUI.DrawTexture(new Rect(0, y, Screen.width, h), Texture2D.whiteTexture);
        }
    }

    void DrawAnimatedGrid()
    {
        float gridSize = 60f;
        float gridAlpha = 0.03f + Mathf.Sin(menuTimer * 0.5f) * 0.01f;
        GUI.color = new Color(0.3f, 0.5f, 0.7f, gridAlpha);

        float offsetX = (menuTimer * 10f) % gridSize;
        float offsetY = (menuTimer * 5f) % gridSize;

        // Vertical lines
        for (float x = -offsetX; x < Screen.width + gridSize; x += gridSize)
        {
            GUI.DrawTexture(new Rect(x, 0, 1, Screen.height), Texture2D.whiteTexture);
        }

        // Horizontal lines
        for (float y = -offsetY; y < Screen.height + gridSize; y += gridSize)
        {
            GUI.DrawTexture(new Rect(0, y, Screen.width, 1), Texture2D.whiteTexture);
        }
    }

    void DrawMenuParticles()
    {
        InitParticles();
        for (int i = 0; i < particles.Length; i++)
        {
            float x = particles[i].x * Screen.width;
            float y = particles[i].y * Screen.height;
            float size = particleSizes[i];
            float alpha = particleAlphas[i] * 0.7f * (0.5f + Mathf.Sin(menuTimer * 2f + i * 0.5f) * 0.5f);

            // Blue-tinted particles for menu
            GUI.color = new Color(0.4f, 0.7f, 1f, alpha);
            GUI.DrawTexture(new Rect(x, y, size, size), Texture2D.whiteTexture);
        }
    }

    void DrawMenuPanelBorder(Rect rect, Color color)
    {
        float thickness = 1.5f;
        GUI.color = color;

        // Top
        GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, thickness), Texture2D.whiteTexture);
        // Bottom
        GUI.DrawTexture(new Rect(rect.x, rect.y + rect.height - thickness, rect.width, thickness), Texture2D.whiteTexture);
        // Left
        GUI.DrawTexture(new Rect(rect.x, rect.y, thickness, rect.height), Texture2D.whiteTexture);
        // Right
        GUI.DrawTexture(new Rect(rect.x + rect.width - thickness, rect.y, thickness, rect.height), Texture2D.whiteTexture);

        // Corner highlights
        float cornerSize = 15f;
        GUI.color = new Color(color.r, color.g, color.b, color.a * 1.5f);

        // Top-left
        GUI.DrawTexture(new Rect(rect.x - 1, rect.y - 1, cornerSize, 2), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(rect.x - 1, rect.y - 1, 2, cornerSize), Texture2D.whiteTexture);

        // Top-right
        GUI.DrawTexture(new Rect(rect.x + rect.width - cornerSize + 1, rect.y - 1, cornerSize, 2), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(rect.x + rect.width - 1, rect.y - 1, 2, cornerSize), Texture2D.whiteTexture);

        // Bottom-left
        GUI.DrawTexture(new Rect(rect.x - 1, rect.y + rect.height - 1, cornerSize, 2), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(rect.x - 1, rect.y + rect.height - cornerSize + 1, 2, cornerSize), Texture2D.whiteTexture);

        // Bottom-right
        GUI.DrawTexture(new Rect(rect.x + rect.width - cornerSize + 1, rect.y + rect.height - 1, cornerSize, 2), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(rect.x + rect.width - 1, rect.y + rect.height - cornerSize + 1, 2, cornerSize), Texture2D.whiteTexture);
    }

    void DrawButtonBorder(Rect rect, Color color, float thickness)
    {
        GUI.color = color;
        // Top
        GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, thickness), Texture2D.whiteTexture);
        // Bottom
        GUI.DrawTexture(new Rect(rect.x, rect.y + rect.height - thickness, rect.width, thickness), Texture2D.whiteTexture);
        // Left (skip as we have accent bar)
        // Right
        GUI.DrawTexture(new Rect(rect.x + rect.width - thickness, rect.y, thickness, rect.height), Texture2D.whiteTexture);
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

    void DrawSettings()
    {
        // Dark overlay
        GUI.color = new Color(0, 0, 0, 0.85f);
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = Color.white;

        float centerX = Screen.width / 2f;
        float panelWidth = 500f;
        float panelX = centerX - panelWidth / 2f;
        float startY = 80f;
        float rowHeight = 50f;
        float labelWidth = 180f;
        float controlWidth = 280f;

        // Title
        GUI.Label(new Rect(0, startY, Screen.width, 50), "SETTINGS", labelStyle);
        startY += 70f;

        // Settings panel background
        GUI.color = new Color(0.15f, 0.15f, 0.15f, 0.9f);
        GUI.DrawTexture(new Rect(panelX - 20, startY - 10, panelWidth + 40, 700), Texture2D.whiteTexture);
        GUI.color = Color.white;

        GUIStyle settingLabel = new GUIStyle(GUI.skin.label);
        settingLabel.fontSize = 20;
        settingLabel.alignment = TextAnchor.MiddleLeft;
        settingLabel.normal.textColor = Color.white;

        GUIStyle sliderThumb = GUI.skin.horizontalSliderThumb;
        GUIStyle sliderBg = GUI.skin.horizontalSlider;

        // --- GRAPHICS SECTION ---
        GUI.Label(new Rect(panelX, startY, panelWidth, 30), "GRAPHICS", labelStyle);
        startY += 40f;

        // Quality Preset
        GUI.Label(new Rect(panelX, startY, labelWidth, rowHeight), "Quality:", settingLabel);
        int newQuality = GUI.SelectionGrid(new Rect(panelX + labelWidth, startY + 10, controlWidth, 30),
            qualityLevel, qualityOptions, qualityOptions.Length);
        if (newQuality != qualityLevel)
        {
            qualityLevel = newQuality;
            ApplySettings();
        }
        startY += rowHeight;

        // Shadows
        GUI.Label(new Rect(panelX, startY, labelWidth, rowHeight), "Shadows:", settingLabel);
        int newShadow = GUI.SelectionGrid(new Rect(panelX + labelWidth, startY + 10, controlWidth, 30),
            shadowQuality, shadowOptions, shadowOptions.Length);
        if (newShadow != shadowQuality)
        {
            shadowQuality = newShadow;
            ApplySettings();
        }
        startY += rowHeight;

        // Shadow Distance (only show if shadows enabled)
        if (shadowQuality > 0)
        {
            GUI.Label(new Rect(panelX, startY, labelWidth, rowHeight), "Shadow Dist:", settingLabel);
            float newShadowDist = GUI.HorizontalSlider(new Rect(panelX + labelWidth, startY + 18, controlWidth - 60, 20),
                shadowDistance, 10f, 100f);
            GUI.Label(new Rect(panelX + labelWidth + controlWidth - 50, startY, 50, rowHeight),
                Mathf.RoundToInt(newShadowDist).ToString(), settingLabel);
            if (Mathf.Abs(newShadowDist - shadowDistance) > 1f)
            {
                shadowDistance = newShadowDist;
                ApplySettings();
            }
            startY += rowHeight;
        }

        // Resolution Scale
        GUI.Label(new Rect(panelX, startY, labelWidth, rowHeight), "Resolution:", settingLabel);
        float newResScale = GUI.HorizontalSlider(new Rect(panelX + labelWidth, startY + 18, controlWidth - 60, 20),
            resolutionScale, 0.5f, 1f);
        GUI.Label(new Rect(panelX + labelWidth + controlWidth - 50, startY, 50, rowHeight),
            Mathf.RoundToInt(newResScale * 100) + "%", settingLabel);
        if (Mathf.Abs(newResScale - resolutionScale) > 0.01f)
        {
            resolutionScale = newResScale;
            ApplySettings();
        }
        startY += rowHeight;

        // Fullscreen Toggle
        GUI.Label(new Rect(panelX, startY, labelWidth, rowHeight), "Fullscreen:", settingLabel);
        bool newFullscreen = GUI.Toggle(new Rect(panelX + labelWidth, startY + 12, controlWidth, 30),
            fullscreenEnabled, fullscreenEnabled ? " Enabled" : " Disabled");
        if (newFullscreen != fullscreenEnabled)
        {
            fullscreenEnabled = newFullscreen;
            ApplySettings();
        }
        startY += rowHeight;

        // Field of View
        GUI.Label(new Rect(panelX, startY, labelWidth, rowHeight), "FOV:", settingLabel);
        float newFOV = GUI.HorizontalSlider(new Rect(panelX + labelWidth, startY + 18, controlWidth - 60, 20),
            fieldOfView, 60f, 120f);
        GUI.Label(new Rect(panelX + labelWidth + controlWidth - 50, startY, 50, rowHeight),
            Mathf.RoundToInt(newFOV).ToString() + "°", settingLabel);
        if (Mathf.Abs(newFOV - fieldOfView) > 0.5f)
        {
            fieldOfView = newFOV;
            ApplySettings();
        }
        startY += rowHeight;

        // Show FPS
        GUI.Label(new Rect(panelX, startY, labelWidth, rowHeight), "Show FPS:", settingLabel);
        bool newShowFPS = GUI.Toggle(new Rect(panelX + labelWidth, startY + 12, controlWidth, 30),
            showFPS, showFPS ? " Enabled" : " Disabled");
        if (newShowFPS != showFPS)
        {
            showFPS = newShowFPS;
        }
        startY += rowHeight;

        // --- AUDIO SECTION ---
        startY += 20f;
        GUI.Label(new Rect(panelX, startY, panelWidth, 30), "AUDIO", labelStyle);
        startY += 40f;

        // Master Volume
        GUI.Label(new Rect(panelX, startY, labelWidth, rowHeight), "Volume:", settingLabel);
        float newVolume = GUI.HorizontalSlider(new Rect(panelX + labelWidth, startY + 18, controlWidth - 60, 20),
            masterVolume, 0f, 1f);
        GUI.Label(new Rect(panelX + labelWidth + controlWidth - 50, startY, 50, rowHeight),
            Mathf.RoundToInt(newVolume * 100) + "%", settingLabel);
        if (Mathf.Abs(newVolume - masterVolume) > 0.01f)
        {
            masterVolume = newVolume;
            ApplySettings();
        }
        startY += rowHeight;

        // --- CONTROLS SECTION ---
        startY += 20f;
        GUI.Label(new Rect(panelX, startY, panelWidth, 30), "CONTROLS", labelStyle);
        startY += 40f;

        // Mouse Sensitivity
        GUI.Label(new Rect(panelX, startY, labelWidth, rowHeight), "Sensitivity:", settingLabel);
        mouseSensitivity = GUI.HorizontalSlider(new Rect(panelX + labelWidth, startY + 18, controlWidth - 60, 20),
            mouseSensitivity, 0.5f, 5f);
        GUI.Label(new Rect(panelX + labelWidth + controlWidth - 50, startY, 50, rowHeight),
            mouseSensitivity.ToString("F1"), settingLabel);
        startY += rowHeight;

        // Invert Y-Axis
        GUI.Label(new Rect(panelX, startY, labelWidth, rowHeight), "Invert Y:", settingLabel);
        bool newInvertY = GUI.Toggle(new Rect(panelX + labelWidth, startY + 12, controlWidth, 30),
            invertYAxis, invertYAxis ? " Enabled" : " Disabled");
        if (newInvertY != invertYAxis)
        {
            invertYAxis = newInvertY;
        }
        startY += rowHeight;

        // --- BUTTONS ---
        startY += 30f;
        float buttonWidth = 140f;
        float buttonSpacing = 20f;
        float buttonsX = centerX - (buttonWidth * 2 + buttonSpacing) / 2f;

        if (GUI.Button(new Rect(buttonsX, startY, buttonWidth, 50), "SAVE", buttonStyle))
        {
            SaveSettings();
            currentState = GameState.MainMenu;
        }

        if (GUI.Button(new Rect(buttonsX + buttonWidth + buttonSpacing, startY, buttonWidth, 50), "BACK", buttonStyle))
        {
            LoadSettings(); // Revert unsaved changes
            ApplySettings();
            currentState = GameState.MainMenu;
        }

        // Performance tip
        startY += 70f;
        GUIStyle tipStyle = new GUIStyle(smallLabelStyle);
        tipStyle.wordWrap = true;
        GUI.Label(new Rect(panelX, startY, panelWidth, 40),
            "Tip: Lower shadows and resolution for better FPS on slower devices.", tipStyle);
    }

    void LoadSettings()
    {
        shadowQuality = PlayerPrefs.GetInt("ShadowQuality", 1);
        shadowDistance = PlayerPrefs.GetFloat("ShadowDistance", 40f);
        qualityLevel = PlayerPrefs.GetInt("QualityLevel", 0);
        resolutionScale = PlayerPrefs.GetFloat("ResolutionScale", 1f);
        mouseSensitivity = PlayerPrefs.GetFloat("MouseSensitivity", 2f);
        masterVolume = PlayerPrefs.GetFloat("MasterVolume", 1f);
        vSyncEnabled = PlayerPrefs.GetInt("VSync", 0) == 1;
        fullscreenEnabled = PlayerPrefs.GetInt("Fullscreen", 1) == 1;
        fieldOfView = PlayerPrefs.GetFloat("FOV", 75f);
        showFPS = PlayerPrefs.GetInt("ShowFPS", 0) == 1;
        invertYAxis = PlayerPrefs.GetInt("InvertY", 0) == 1;
    }

    void SaveSettings()
    {
        PlayerPrefs.SetInt("ShadowQuality", shadowQuality);
        PlayerPrefs.SetFloat("ShadowDistance", shadowDistance);
        PlayerPrefs.SetInt("QualityLevel", qualityLevel);
        PlayerPrefs.SetFloat("ResolutionScale", resolutionScale);
        PlayerPrefs.SetFloat("MouseSensitivity", mouseSensitivity);
        PlayerPrefs.SetFloat("MasterVolume", masterVolume);
        PlayerPrefs.SetInt("VSync", vSyncEnabled ? 1 : 0);
        PlayerPrefs.SetInt("Fullscreen", fullscreenEnabled ? 1 : 0);
        PlayerPrefs.SetFloat("FOV", fieldOfView);
        PlayerPrefs.SetInt("ShowFPS", showFPS ? 1 : 0);
        PlayerPrefs.SetInt("InvertY", invertYAxis ? 1 : 0);
        PlayerPrefs.Save();
        Debug.Log("Settings saved!");
    }

    void ApplySettings()
    {
        // Apply quality level
        QualitySettings.SetQualityLevel(qualityLevel, true);

        // Apply shadow settings
        switch (shadowQuality)
        {
            case 0: // Off
                QualitySettings.shadows = ShadowQuality.Disable;
                break;
            case 1: // Low
                QualitySettings.shadows = ShadowQuality.HardOnly;
                QualitySettings.shadowResolution = ShadowResolution.Low;
                break;
            case 2: // Medium
                QualitySettings.shadows = ShadowQuality.All;
                QualitySettings.shadowResolution = ShadowResolution.Medium;
                break;
            case 3: // High
                QualitySettings.shadows = ShadowQuality.All;
                QualitySettings.shadowResolution = ShadowResolution.High;
                break;
        }

        QualitySettings.shadowDistance = shadowDistance;

        // Apply fullscreen mode
        #if !UNITY_EDITOR
        if (Screen.fullScreen != fullscreenEnabled)
        {
            if (fullscreenEnabled)
            {
                int targetWidth = Mathf.RoundToInt(Screen.currentResolution.width * resolutionScale);
                int targetHeight = Mathf.RoundToInt(Screen.currentResolution.height * resolutionScale);
                Screen.SetResolution(targetWidth, targetHeight, true);
            }
            else
            {
                // Switch to windowed mode at 1280x720
                Screen.SetResolution(1280, 720, false);
            }
        }
        else if (Screen.fullScreen)
        {
            // Apply resolution scale in fullscreen
            int targetWidth = Mathf.RoundToInt(Screen.currentResolution.width * resolutionScale);
            int targetHeight = Mathf.RoundToInt(Screen.currentResolution.height * resolutionScale);
            Screen.SetResolution(targetWidth, targetHeight, true);
        }
        #endif

        // VSync (limited in WebGL)
        QualitySettings.vSyncCount = vSyncEnabled ? 1 : 0;

        // Apply master volume
        AudioListener.volume = masterVolume;

        // Apply FOV to all cameras
        Camera[] cameras = Camera.allCameras;
        foreach (Camera cam in cameras)
        {
            if (cam != null && !cam.orthographic)
            {
                cam.fieldOfView = fieldOfView;
            }
        }
        // Also set main camera if exists
        if (Camera.main != null)
        {
            Camera.main.fieldOfView = fieldOfView;
        }

        Debug.Log($"Applied settings - Quality:{qualityOptions[qualityLevel]}, FOV:{fieldOfView}, Volume:{Mathf.RoundToInt(masterVolume*100)}%");
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

        // Set team in Photon custom properties for scoreboard
        KillFeedManager.SetPlayerTeam(PhotonNetwork.LocalPlayer, team);

        // Initialize kill feed manager
        KillFeedManager.Initialize();

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
