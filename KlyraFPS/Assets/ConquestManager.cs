using UnityEngine;
using Photon.Pun;
using System.Collections.Generic;

public class ConquestManager : MonoBehaviourPunCallbacks, IPunObservable
{
    [Header("Game Settings")]
    public int startingTickets = 500;
    public float ticketBleedRate = 1f; // Tickets lost per second when enemy has majority
    public float ticketBleedPerPoint = 0.5f; // Additional bleed per extra point owned

    [Header("References")]
    public List<CapturePoint> capturePoints = new List<CapturePoint>();

    // Team tickets
    public int phantomTickets;
    public int havocTickets;

    // Point counts
    private int phantomPoints = 0;
    private int havocPoints = 0;

    // Bleed accumulator (fixes the CeilToInt bug)
    private float phantomBleedAccum = 0f;
    private float havocBleedAccum = 0f;

    // Grace period - no bleed until both teams have captured at least 1 point
    private bool gameStarted = false;

    // Game state
    public bool gameActive = true;
    public Team winningTeam = Team.None;

    // UI
    private GUIStyle ticketStyle;
    private GUIStyle pointStyle;
    private GUIStyle headerStyle;
    private GUIStyle winStyle;
    private bool stylesInitialized = false;

    void Start()
    {
        phantomTickets = startingTickets;
        havocTickets = startingTickets;

        // Auto-find capture points if not assigned
        if (capturePoints.Count == 0)
        {
            capturePoints.AddRange(FindObjectsOfType<CapturePoint>());
        }
    }

    void InitStyles()
    {
        if (stylesInitialized) return;

        ticketStyle = new GUIStyle(GUI.skin.label);
        ticketStyle.fontSize = 28;
        ticketStyle.fontStyle = FontStyle.Bold;
        ticketStyle.alignment = TextAnchor.MiddleCenter;

        pointStyle = new GUIStyle(GUI.skin.label);
        pointStyle.fontSize = 20;
        pointStyle.fontStyle = FontStyle.Bold;
        pointStyle.alignment = TextAnchor.MiddleCenter;

        headerStyle = new GUIStyle(GUI.skin.label);
        headerStyle.fontSize = 16;
        headerStyle.alignment = TextAnchor.MiddleCenter;
        headerStyle.normal.textColor = Color.gray;

        winStyle = new GUIStyle(GUI.skin.label);
        winStyle.fontSize = 48;
        winStyle.fontStyle = FontStyle.Bold;
        winStyle.alignment = TextAnchor.MiddleCenter;

        stylesInitialized = true;
    }

    void Update()
    {
        if (!gameActive) return;

        // Count points owned by each team
        phantomPoints = 0;
        havocPoints = 0;

        foreach (var point in capturePoints)
        {
            if (point.owningTeam == Team.Phantom) phantomPoints++;
            else if (point.owningTeam == Team.Havoc) havocPoints++;
        }

        // Grace period - wait until both teams have at least 1 point
        if (!gameStarted)
        {
            if (phantomPoints > 0 && havocPoints > 0)
            {
                gameStarted = true;
                Debug.Log("Both teams have captured a point - ticket bleed begins!");
            }
            return; // No bleed during grace period
        }

        // Apply ticket bleed (only when one team has MORE points)
        if (phantomPoints > havocPoints)
        {
            // Havoc loses tickets
            int advantage = phantomPoints - havocPoints;
            float bleed = ticketBleedRate + (ticketBleedPerPoint * advantage);
            havocBleedAccum += bleed * Time.deltaTime;

            // Only subtract whole tickets
            if (havocBleedAccum >= 1f)
            {
                int ticketsToLose = Mathf.FloorToInt(havocBleedAccum);
                havocTickets -= ticketsToLose;
                havocBleedAccum -= ticketsToLose;
            }
        }
        else if (havocPoints > phantomPoints)
        {
            // Phantom loses tickets
            int advantage = havocPoints - phantomPoints;
            float bleed = ticketBleedRate + (ticketBleedPerPoint * advantage);
            phantomBleedAccum += bleed * Time.deltaTime;

            // Only subtract whole tickets
            if (phantomBleedAccum >= 1f)
            {
                int ticketsToLose = Mathf.FloorToInt(phantomBleedAccum);
                phantomTickets -= ticketsToLose;
                phantomBleedAccum -= ticketsToLose;
            }
        }

        // Clamp tickets
        phantomTickets = Mathf.Max(0, phantomTickets);
        havocTickets = Mathf.Max(0, havocTickets);

        // Check win condition
        if (phantomTickets <= 0)
        {
            gameActive = false;
            winningTeam = Team.Havoc;
        }
        else if (havocTickets <= 0)
        {
            gameActive = false;
            winningTeam = Team.Phantom;
        }
    }

    void OnGUI()
    {
        // HUD disabled for now - will be replaced with proper UI later
    }

    // Network sync
    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
    {
        if (stream.IsWriting)
        {
            stream.SendNext(phantomTickets);
            stream.SendNext(havocTickets);
            stream.SendNext(gameActive);
            stream.SendNext((int)winningTeam);
        }
        else
        {
            phantomTickets = (int)stream.ReceiveNext();
            havocTickets = (int)stream.ReceiveNext();
            gameActive = (bool)stream.ReceiveNext();
            winningTeam = (Team)(int)stream.ReceiveNext();
        }
    }
}
