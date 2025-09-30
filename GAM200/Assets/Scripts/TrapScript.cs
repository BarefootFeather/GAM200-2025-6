using UnityEngine;
using UnityEngine.Tilemaps;

public class TrapScript : MonoBehaviour
{
    private GameObject TrapController;

    private Tilemap targetTilemap;
    private Sprite inactiveTile;
    private Sprite activeTile;


    private int damageAmount;
    
    private bool debugCollisions;

    // Internal state
    private bool isActive = false;
    private PlayerController player;
    private SpriteRenderer spriteRenderer;
    private Grid grid;


    [Header("Beat Scheduling")]
    [Tooltip("Length of the repeating cycle in beats (e.g. 4).")]
    [SerializeField] private int toggleEveryNIntervals = 4;

    [Tooltip("Offset for this trap�s phase in the cycle.")]
    [SerializeField] private int toggleOffset = 0;


    

    private void OnEnable() => BeatBus.Beat += OnBeat;
    private void OnDisable() => BeatBus.Beat -= OnBeat;


    private void Start()
    {

        // Get Player reference from TrapController
        TrapController temp = GetComponentInParent<TrapController>();
        if (!temp) // Check if TrapController component exists
        {
            Debug.LogError("TrapController Gameobject does not contain TrapController Component!");
            return;
        }

        // ======== Get configurations from TrapController =========
        // Grid
        grid = temp.GetGrid();
        if (!grid)
        {
            Debug.LogError("No Grid component found on the Tilemap's parent GameObject!");
            return;
        }

        //Player
        player = temp.GetPlayer().GetComponent<PlayerController>();
        if (!player) // Check if player reference is valid
        {
            Debug.LogError("Player reference is null in TrapController!");
            return;
        }

        // Tiles
        inactiveTile = temp.GetInactiveTile();
        activeTile = temp.GetActiveTile();
        if (!inactiveTile || !activeTile) // Check if tile references are valid
        {
            Debug.LogError("Inactive or Active tile reference is null in TrapController!");
            return;
        }

        // debugCollisions
        debugCollisions = temp.GetDebugCollisions();

        // Damage Amount
        damageAmount = temp.GetDamageAmount();

        // ========= End configurations =========

        // Get Sprite Renderer component
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (!spriteRenderer) // Check if SpriteRenderer component exists
        {
            Debug.LogError("No SpriteRenderer component found on Trap GameObject!");
            return;
        }

        // ========= Set the tile position to the center of the grid cell =========
        // Convert world position to cell coordinates
        Vector3Int cellPosition = grid.WorldToCell(transform.position);

        // Convert cell coordinates back to centered world position
        Vector3 centeredWorldPos = grid.GetCellCenterWorld(cellPosition);

        // Move GameObject to center of cell
        transform.position = centeredWorldPos;
    }

    private void OnBeat(int beatIndex)
    {
        if (toggleEveryNIntervals <= 0) return;

        // Work out this trap�s phase in the cycle
        int phase = (beatIndex + toggleOffset) % toggleEveryNIntervals;

        // Rule: first half of cycle = active, second half = inactive
        bool shouldBeActive = phase < (toggleEveryNIntervals / 2);

        SetTrapState(shouldBeActive);
    }

    private void SetTrapState(bool active)
    {
        isActive = active;
        spriteRenderer.sprite = isActive ? activeTile : inactiveTile;

        if (debugCollisions)
            Debug.Log($"[TrapScript] {(isActive ? "Active" : "Inactive")} at {transform.position}");
    }


    private static bool IsScheduled(int count, int everyN, int offset) =>
        ((count - offset) % Mathf.Max(1, everyN)) == 0;

    public void ToggleTrap()
    {
        // Toggle the trap's active state
        isActive = !isActive;

        // Update visual feedback
        if (isActive) spriteRenderer.sprite = activeTile;
        else spriteRenderer.sprite = inactiveTile;

        if (debugCollisions)
        {
            Debug.Log($"[TrapController] Trap {(isActive ? "activated" : "deactivated")} at {transform.position}");
        }
    }

    private void Update()
    {
        // Only check for player collision when the trap is active
        if (isActive && player != null)
        {
            CheckPlayerOnTrapTiles();
        }
    }

    private void CheckPlayerOnTrapTiles()
    {
        // Get the player's current grid position
        Vector3Int playerGridPos = grid.WorldToCell(player.transform.position);

        // Get this trap's grid position
        Vector3Int trapGridPos = grid.WorldToCell(transform.position);

        // Check if player and trap are in the same cell
        if (playerGridPos == trapGridPos && !player.IsInvulnerable())
        {
            player.TakeDamage(damageAmount);

            if (debugCollisions)
            {
                Debug.Log($"[TrapController] Player on active trap at {trapGridPos}, dealt {damageAmount} damage");
            }
        }
    }
}
