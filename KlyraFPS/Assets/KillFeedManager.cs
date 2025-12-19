using UnityEngine;
using UnityEngine.InputSystem;
using Photon.Pun;
using Photon.Realtime;
using System.Collections.Generic;
using ExitGames.Client.Photon;

public class KillFeedManager : MonoBehaviour
{
    public static KillFeedManager Instance { get; private set; }

    [System.Serializable]
    public class KillEntry
    {
        public string killerName;
        public string victimName;
        public Team killerTeam;
        public Team victimTeam;
        public string weapon;
        public float timestamp;
        public bool isHeadshot;
    }

    // AI stats tracking
    public class AIStats
    {
        public string name;
        public Team team;
        public int kills;
        public int deaths;
        public bool isAlive;
        public AIController controller;
    }

    private List<KillEntry> killFeed = new List<KillEntry>();
    private const int MAX_KILL_FEED_ENTRIES = 5;
    private const float KILL_FEED_DURATION = 6f;

    private Dictionary<int, AIStats> aiStatsDict = new Dictionary<int, AIStats>();

    private bool scoreboardOpen = false;

    // Styles
    private GUIStyle killFeedStyle;
    private GUIStyle killerStyle;
    private GUIStyle victimStyle;
    private GUIStyle scoreboardTitleStyle;
    private GUIStyle scoreboardHeaderStyle;
    private GUIStyle scoreboardRowStyle;
    private GUIStyle scoreboardLocalStyle;
    private bool stylesInitialized = false;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    void Update()
    {
        // Remove old entries
        killFeed.RemoveAll(k => Time.time - k.timestamp > KILL_FEED_DURATION);

        // Check for scoreboard toggle (Caps Lock)
        var keyboard = Keyboard.current;
        if (keyboard != null)
        {
            scoreboardOpen = keyboard.capsLockKey.isPressed;
        }
    }

    void InitStyles()
    {
        if (stylesInitialized) return;

        killFeedStyle = new GUIStyle(GUI.skin.label);
        killFeedStyle.fontSize = 14;
        killFeedStyle.alignment = TextAnchor.MiddleRight;
        killFeedStyle.normal.textColor = Color.white;

        killerStyle = new GUIStyle(killFeedStyle);
        victimStyle = new GUIStyle(killFeedStyle);

        scoreboardTitleStyle = new GUIStyle(GUI.skin.label);
        scoreboardTitleStyle.fontSize = 28;
        scoreboardTitleStyle.fontStyle = FontStyle.Bold;
        scoreboardTitleStyle.alignment = TextAnchor.MiddleCenter;
        scoreboardTitleStyle.normal.textColor = Color.white;

        scoreboardHeaderStyle = new GUIStyle(GUI.skin.label);
        scoreboardHeaderStyle.fontSize = 16;
        scoreboardHeaderStyle.fontStyle = FontStyle.Bold;
        scoreboardHeaderStyle.alignment = TextAnchor.MiddleCenter;
        scoreboardHeaderStyle.normal.textColor = new Color(0.8f, 0.8f, 0.8f);

        scoreboardRowStyle = new GUIStyle(GUI.skin.label);
        scoreboardRowStyle.fontSize = 15;
        scoreboardRowStyle.alignment = TextAnchor.MiddleCenter;
        scoreboardRowStyle.normal.textColor = Color.white;

        scoreboardLocalStyle = new GUIStyle(scoreboardRowStyle);
        scoreboardLocalStyle.fontStyle = FontStyle.Bold;
        scoreboardLocalStyle.normal.textColor = new Color(1f, 0.9f, 0.4f);

        stylesInitialized = false; // Re-init each frame for color changes
    }

    void OnGUI()
    {
        // Only draw during gameplay
        GameUIManager gameUI = FindObjectOfType<GameUIManager>();
        if (gameUI != null && gameUI.currentState != GameUIManager.GameState.InGame)
            return;

        InitStyles();
        DrawKillFeed();

        if (scoreboardOpen)
        {
            DrawScoreboard();
        }
    }

    void DrawKillFeed()
    {
        float startX = Screen.width - 320;
        float startY = 10;
        float entryHeight = 24;
        float padding = 4;

        for (int i = 0; i < killFeed.Count; i++)
        {
            KillEntry entry = killFeed[i];
            float y = startY + i * (entryHeight + padding);

            // Calculate fade based on age
            float age = Time.time - entry.timestamp;
            float alpha = Mathf.Clamp01(1f - (age / KILL_FEED_DURATION) * 0.5f);

            // Background
            GUI.color = new Color(0, 0, 0, 0.5f * alpha);
            GUI.DrawTexture(new Rect(startX - 10, y - 2, 320, entryHeight), Texture2D.whiteTexture);

            // Killer name color based on team
            Color killerColor = entry.killerTeam == Team.Phantom ?
                new Color(0.4f, 0.6f, 1f) : new Color(1f, 0.4f, 0.4f);
            Color victimColor = entry.victimTeam == Team.Phantom ?
                new Color(0.4f, 0.6f, 1f) : new Color(1f, 0.4f, 0.4f);

            // Build the kill text
            string killIcon = entry.isHeadshot ? " [HS] " : " ";

            // Draw killer name
            GUI.color = new Color(killerColor.r, killerColor.g, killerColor.b, alpha);
            killerStyle.alignment = TextAnchor.MiddleRight;
            GUI.Label(new Rect(startX - 10, y, 120, entryHeight), entry.killerName, killerStyle);

            // Draw kill icon/weapon
            GUI.color = new Color(1f, 1f, 1f, alpha);
            killFeedStyle.alignment = TextAnchor.MiddleCenter;
            string weaponText = entry.isHeadshot ? "[HS]" : "[K]";
            GUI.Label(new Rect(startX + 110, y, 60, entryHeight), weaponText, killFeedStyle);

            // Draw victim name
            GUI.color = new Color(victimColor.r, victimColor.g, victimColor.b, alpha);
            victimStyle.alignment = TextAnchor.MiddleLeft;
            GUI.Label(new Rect(startX + 170, y, 120, entryHeight), entry.victimName, victimStyle);
        }

        GUI.color = Color.white;
    }

    // Helper class for unified scoreboard entries
    private class ScoreboardEntry
    {
        public string name;
        public Team team;
        public int kills;
        public int deaths;
        public bool isPlayer;
        public bool isLocalPlayer;
        public bool isAlive;
    }

    void DrawScoreboard()
    {
        // Build unified list of all combatants
        List<ScoreboardEntry> allEntries = new List<ScoreboardEntry>();

        // Add players
        foreach (Player player in PhotonNetwork.PlayerList)
        {
            allEntries.Add(new ScoreboardEntry
            {
                name = player.NickName,
                team = GetPlayerTeam(player),
                kills = GetPlayerKills(player),
                deaths = GetPlayerDeaths(player),
                isPlayer = true,
                isLocalPlayer = player == PhotonNetwork.LocalPlayer,
                isAlive = true
            });
        }

        // Add AI
        foreach (var aiStats in aiStatsDict.Values)
        {
            allEntries.Add(new ScoreboardEntry
            {
                name = aiStats.name,
                team = aiStats.team,
                kills = aiStats.kills,
                deaths = aiStats.deaths,
                isPlayer = false,
                isLocalPlayer = false,
                isAlive = aiStats.isAlive
            });
        }

        // Sort by kills descending
        allEntries.Sort((a, b) => b.kills.CompareTo(a.kills));

        // Calculate panel size based on entries
        float rowHeight = 24;
        float headerHeight = 100;
        float footerHeight = 40;
        int maxVisibleRows = 16;
        int rowCount = Mathf.Min(allEntries.Count, maxVisibleRows);
        float panelHeight = headerHeight + (rowCount * rowHeight) + footerHeight;
        float panelWidth = 650;
        float panelX = (Screen.width - panelWidth) / 2f;
        float panelY = (Screen.height - panelHeight) / 2f;

        // Dark background
        GUI.color = new Color(0.1f, 0.12f, 0.1f, 0.95f);
        GUI.DrawTexture(new Rect(panelX, panelY, panelWidth, panelHeight), Texture2D.whiteTexture);

        // Border
        GUI.color = new Color(0.4f, 0.5f, 0.3f, 0.8f);
        DrawBorder(new Rect(panelX, panelY, panelWidth, panelHeight), 2);

        // Title
        GUI.color = new Color(1f, 0.7f, 0.2f);
        GUI.Label(new Rect(panelX, panelY + 10, panelWidth, 40), "SCOREBOARD", scoreboardTitleStyle);

        // Team counts
        int phantomCount = allEntries.FindAll(e => e.team == Team.Phantom).Count;
        int havocCount = allEntries.FindAll(e => e.team == Team.Havoc).Count;
        GUIStyle teamCountStyle = new GUIStyle(GUI.skin.label);
        teamCountStyle.fontSize = 14;
        teamCountStyle.alignment = TextAnchor.MiddleCenter;

        GUI.color = new Color(0.5f, 0.7f, 1f);
        GUI.Label(new Rect(panelX + 50, panelY + 45, 150, 20), $"PHANTOM: {phantomCount}", teamCountStyle);
        GUI.color = new Color(1f, 0.5f, 0.5f);
        GUI.Label(new Rect(panelX + panelWidth - 200, panelY + 45, 150, 20), $"HAVOC: {havocCount}", teamCountStyle);

        // Divider
        GUI.color = new Color(0.4f, 0.5f, 0.3f, 0.5f);
        GUI.DrawTexture(new Rect(panelX + 20, panelY + 70, panelWidth - 40, 1), Texture2D.whiteTexture);

        // Column headers
        float headerY = panelY + 75;
        float col0 = panelX + 20;  // Type icon
        float col1 = panelX + 50;  // Name
        float col2 = panelX + 320; // Team
        float col3 = panelX + 420; // Kills
        float col4 = panelX + 490; // Deaths
        float col5 = panelX + 560; // K/D

        GUI.color = new Color(0.7f, 0.8f, 0.6f);
        scoreboardHeaderStyle.alignment = TextAnchor.MiddleLeft;
        GUI.Label(new Rect(col1, headerY, 200, 22), "NAME", scoreboardHeaderStyle);
        scoreboardHeaderStyle.alignment = TextAnchor.MiddleCenter;
        GUI.Label(new Rect(col2, headerY, 80, 22), "TEAM", scoreboardHeaderStyle);
        GUI.Label(new Rect(col3, headerY, 50, 22), "K", scoreboardHeaderStyle);
        GUI.Label(new Rect(col4, headerY, 50, 22), "D", scoreboardHeaderStyle);
        GUI.Label(new Rect(col5, headerY, 60, 22), "K/D", scoreboardHeaderStyle);

        // Divider under headers
        GUI.color = new Color(0.4f, 0.5f, 0.3f, 0.3f);
        GUI.DrawTexture(new Rect(panelX + 20, headerY + 22, panelWidth - 40, 1), Texture2D.whiteTexture);

        // Entry rows
        float rowY = headerY + 28;

        for (int i = 0; i < rowCount && i < allEntries.Count; i++)
        {
            ScoreboardEntry entry = allEntries[i];
            GUIStyle rowStyle = entry.isLocalPlayer ? scoreboardLocalStyle : scoreboardRowStyle;

            // Row background for local player
            if (entry.isLocalPlayer)
            {
                GUI.color = new Color(0.3f, 0.4f, 0.2f, 0.4f);
                GUI.DrawTexture(new Rect(panelX + 10, rowY - 1, panelWidth - 20, rowHeight), Texture2D.whiteTexture);
            }
            // Alternating row background
            else if (i % 2 == 0)
            {
                GUI.color = new Color(0.15f, 0.18f, 0.12f, 0.3f);
                GUI.DrawTexture(new Rect(panelX + 10, rowY - 1, panelWidth - 20, rowHeight), Texture2D.whiteTexture);
            }

            float kd = entry.deaths > 0 ? (float)entry.kills / entry.deaths : entry.kills;

            // Type indicator (Player or AI)
            GUIStyle typeStyle = new GUIStyle(GUI.skin.label);
            typeStyle.fontSize = 10;
            typeStyle.alignment = TextAnchor.MiddleCenter;
            GUI.color = entry.isPlayer ? new Color(0.4f, 1f, 0.4f) : new Color(0.7f, 0.7f, 0.7f);
            GUI.Label(new Rect(col0, rowY, 25, rowHeight), entry.isPlayer ? "[P]" : "[AI]", typeStyle);

            // Name (dimmed if dead)
            Color nameColor = entry.isLocalPlayer ? new Color(1f, 0.9f, 0.4f) :
                              (entry.isAlive ? Color.white : new Color(0.5f, 0.5f, 0.5f));
            GUI.color = nameColor;
            rowStyle.alignment = TextAnchor.MiddleLeft;
            string displayName = entry.isAlive ? entry.name : entry.name + " (KIA)";
            GUI.Label(new Rect(col1, rowY, 260, rowHeight), displayName, rowStyle);

            // Team
            Color teamColor = entry.team == Team.Phantom ?
                new Color(0.5f, 0.7f, 1f) : new Color(1f, 0.5f, 0.5f);
            string teamName = entry.team == Team.Phantom ? "PHANTOM" : (entry.team == Team.Havoc ? "HAVOC" : "-");
            GUI.color = teamColor;
            rowStyle.alignment = TextAnchor.MiddleCenter;
            GUI.Label(new Rect(col2, rowY, 80, rowHeight), teamName, rowStyle);

            // Stats
            GUI.color = entry.isLocalPlayer ? new Color(1f, 0.9f, 0.4f) : Color.white;
            GUI.Label(new Rect(col3, rowY, 50, rowHeight), entry.kills.ToString(), rowStyle);
            GUI.Label(new Rect(col4, rowY, 50, rowHeight), entry.deaths.ToString(), rowStyle);
            GUI.Label(new Rect(col5, rowY, 60, rowHeight), kd.ToString("F1"), rowStyle);

            rowY += rowHeight;
        }

        // Show if there are more entries
        if (allEntries.Count > maxVisibleRows)
        {
            GUI.color = new Color(0.6f, 0.6f, 0.6f);
            GUIStyle moreStyle = new GUIStyle(GUI.skin.label);
            moreStyle.fontSize = 11;
            moreStyle.alignment = TextAnchor.MiddleCenter;
            GUI.Label(new Rect(panelX, rowY, panelWidth, 20),
                $"... and {allEntries.Count - maxVisibleRows} more", moreStyle);
        }

        // Footer hint
        GUI.color = new Color(0.5f, 0.6f, 0.4f, 0.6f);
        GUIStyle hintStyle = new GUIStyle(GUI.skin.label);
        hintStyle.fontSize = 12;
        hintStyle.alignment = TextAnchor.MiddleCenter;
        GUI.Label(new Rect(panelX, panelY + panelHeight - 25, panelWidth, 20),
            "Release CAPS LOCK to close", hintStyle);

        GUI.color = Color.white;
    }

    void DrawBorder(Rect rect, float thickness)
    {
        GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, thickness), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(rect.x, rect.y + rect.height - thickness, rect.width, thickness), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(rect.x, rect.y, thickness, rect.height), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(rect.x + rect.width - thickness, rect.y, thickness, rect.height), Texture2D.whiteTexture);
    }

    // Get player stats from Photon custom properties
    int GetPlayerKills(Player player)
    {
        if (player.CustomProperties.TryGetValue("kills", out object kills))
            return (int)kills;
        return 0;
    }

    int GetPlayerDeaths(Player player)
    {
        if (player.CustomProperties.TryGetValue("deaths", out object deaths))
            return (int)deaths;
        return 0;
    }

    Team GetPlayerTeam(Player player)
    {
        if (player.CustomProperties.TryGetValue("team", out object team))
            return (Team)(int)team;
        return Team.None;
    }

    // Call this when a player gets a kill (local only - use BroadcastKill for network)
    public void ReportKill(string killerName, string victimName, Team killerTeam, Team victimTeam, bool isHeadshot = false)
    {
        AddKillEntry(killerName, victimName, killerTeam, victimTeam, isHeadshot);
    }

    // Add a kill entry locally
    public void AddKillEntry(string killerName, string victimName, Team killerTeam, Team victimTeam, bool isHeadshot = false)
    {
        KillEntry entry = new KillEntry
        {
            killerName = killerName,
            victimName = victimName,
            killerTeam = killerTeam,
            victimTeam = victimTeam,
            weapon = "Rifle",
            timestamp = Time.time,
            isHeadshot = isHeadshot
        };

        killFeed.Insert(0, entry);

        // Trim to max entries
        while (killFeed.Count > MAX_KILL_FEED_ENTRIES)
        {
            killFeed.RemoveAt(killFeed.Count - 1);
        }
    }

    // Static method to add kill from RPC
    public static void AddKillToFeed(string killerName, string victimName, int killerTeam, int victimTeam, bool isHeadshot)
    {
        if (Instance != null)
        {
            Instance.AddKillEntry(killerName, victimName, (Team)killerTeam, (Team)victimTeam, isHeadshot);
        }
    }

    // AI Stats tracking methods
    public static void RegisterAI(AIController ai)
    {
        if (ai == null) return;

        // Auto-initialize if needed
        if (Instance == null)
        {
            Initialize();
        }

        if (Instance == null) return;

        int id = ai.GetInstanceID();
        if (!Instance.aiStatsDict.ContainsKey(id))
        {
            Instance.aiStatsDict[id] = new AIStats
            {
                name = ai.identity != null ? ai.identity.RankAndName : ai.gameObject.name,
                team = ai.team,
                kills = 0,
                deaths = 0,
                isAlive = true,
                controller = ai
            };
        }
    }

    public static void UnregisterAI(AIController ai)
    {
        if (Instance == null || ai == null) return;
        int id = ai.GetInstanceID();
        if (Instance.aiStatsDict.ContainsKey(id))
        {
            Instance.aiStatsDict[id].isAlive = false;
        }
    }

    public static void AddAIKill(AIController ai)
    {
        if (Instance == null || ai == null) return;
        int id = ai.GetInstanceID();
        if (Instance.aiStatsDict.ContainsKey(id))
        {
            Instance.aiStatsDict[id].kills++;
        }
    }

    public static void AddAIDeath(AIController ai)
    {
        if (Instance == null || ai == null) return;
        int id = ai.GetInstanceID();
        if (Instance.aiStatsDict.ContainsKey(id))
        {
            Instance.aiStatsDict[id].deaths++;
            Instance.aiStatsDict[id].isAlive = false;
        }
    }

    public List<AIStats> GetAllAIStats()
    {
        // Clean up dead AI references and return list
        List<int> toRemove = new List<int>();
        foreach (var kvp in aiStatsDict)
        {
            if (kvp.Value.controller == null && !kvp.Value.isAlive)
            {
                // Keep dead AI in list for a while, remove if too old
            }
        }
        return new List<AIStats>(aiStatsDict.Values);
    }

    // Update player stats in Photon custom properties
    public static void AddKill(Player player)
    {
        int currentKills = 0;
        if (player.CustomProperties.TryGetValue("kills", out object kills))
            currentKills = (int)kills;

        Hashtable props = new Hashtable { { "kills", currentKills + 1 } };
        player.SetCustomProperties(props);
    }

    public static void AddDeath(Player player)
    {
        int currentDeaths = 0;
        if (player.CustomProperties.TryGetValue("deaths", out object deaths))
            currentDeaths = (int)deaths;

        Hashtable props = new Hashtable { { "deaths", currentDeaths + 1 } };
        player.SetCustomProperties(props);
    }

    public static void SetPlayerTeam(Player player, Team team)
    {
        Hashtable props = new Hashtable { { "team", (int)team } };
        player.SetCustomProperties(props);
    }

    // Ensure instance exists
    public static void Initialize()
    {
        if (Instance == null)
        {
            GameObject obj = new GameObject("KillFeedManager");
            Instance = obj.AddComponent<KillFeedManager>();
        }
    }
}
