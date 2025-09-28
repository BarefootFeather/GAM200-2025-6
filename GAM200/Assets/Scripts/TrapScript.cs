using UnityEngine;
using UnityEngine.Tilemaps;

public class TrapScript : MonoBehaviour
{
    [Header("Trap Controller Reference")]
    [SerializeField] private GameObject TrapController;

    [Header("Tilemap Configuration")]
    [SerializeField] private Tilemap targetTilemap;
    [SerializeField] private Sprite inactiveTile;
    [SerializeField] private Sprite activeTile;

    [Header("Damage Settings")]
    [Tooltip("Amount of damage to deal to the player.")]
    [SerializeField] private int damageAmount = 1;

    [Tooltip("Log collision events for debugging.")]
    [SerializeField] private bool debugCollisions = false;

    // Internal state
    private bool isActive = false;
    private PlayerController player;
    private SpriteRenderer spriteRenderer;
    private Grid grid;

    private void Start()
    {
        // Get Player reference from TrapController
        TrapController temp = TrapController.GetComponent<TrapController>();
        
        grid = targetTilemap.layoutGrid;

        if (!grid)
        {
            Debug.LogError("No Grid component found on the Tilemap's parent GameObject!");
            return;
        }

        if (!temp) // Check if TrapController component exists
        {
            Debug.LogError("TrapController Gameobject does not contain TrapController Component!");
            return;
        }
        player = temp.GetPlayer().GetComponent<PlayerController>();

        if (!player) // Check if player reference is valid
        {
            Debug.LogError("Player reference is null in TrapController!");
            return;
        }

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
