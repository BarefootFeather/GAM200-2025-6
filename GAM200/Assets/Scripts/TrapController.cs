using TMPro;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.UI;

public class TrapController : MonoBehaviour
{
    [Header("Player Reference")]
    [SerializeField] private PlayerController player;

    [Header("Visual Debug")]
    public GameObject debug;
    
    [Header("Tilemap Configuration")]
    public TileBase inactiveTile;
    public TileBase activeTile;
    public Tilemap targetTilemap;

    [Header("Damage Settings")]
    [Tooltip("Amount of damage to deal to the player.")]
    [SerializeField] private int damageAmount = 1;
    [Tooltip("Log collision events for debugging.")]
    [SerializeField] private bool debugCollisions = false;

    private bool isActive = false;

    public void ToggleTrap()
    {
        isActive = !isActive;
        
        // Update visual feedback
        if (debug != null)
        {
            debug.GetComponent<SpriteRenderer>().color = !isActive ? Color.white : Color.red;
        }
        
        // Update tilemap tiles
        if (targetTilemap != null)
        {
            BoundsInt bounds = targetTilemap.cellBounds;
            foreach (Vector3Int pos in bounds.allPositionsWithin)
            {
                if (targetTilemap.HasTile(pos))
                {
                    targetTilemap.SetTile(pos, isActive ? activeTile : inactiveTile);
                }
            }
        }

        if (debugCollisions)
        {
            Debug.Log($"[TrapController] Trap {(isActive ? "activated" : "deactivated")} at {transform.position}");
        }
    }

    private void Update()
    {
        // Only check for player collision when the trap is active
        if (isActive && player != null && targetTilemap != null)
        {
            CheckPlayerOnTrapTiles();
        }
    }

    private void CheckPlayerOnTrapTiles()
    {
        // Get the player's current grid position
        Vector3Int playerGridPos = targetTilemap.WorldToCell(player.transform.position);
        
        // Check if there's a tile at the player's position and if it's an active trap tile
        if (targetTilemap.HasTile(playerGridPos))
        {
            TileBase currentTile = targetTilemap.GetTile(playerGridPos);
            
            // If the player is on an active trap tile, deal damage
            if (currentTile == activeTile && !player.IsInvulnerable())
            {
                player.TakeDamage(damageAmount);
                
                if (debugCollisions)
                {
                    Debug.Log($"[TrapController] Player on active trap tile at {playerGridPos}, dealt {damageAmount} damage");
                }
            }
        }
    }
}
