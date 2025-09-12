using TMPro;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.UI;

public class TrapController : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    [SerializeField] PlayerController player;

    public GameObject debug;
    public TileBase inactiveTile;
    public TileBase activeTile;
    public Tilemap targetTilemap; // Reference to the Tilemap component

    private bool isActive = false;

    public void ToggleTrap()
    {
        isActive = !isActive;
        debug.GetComponent<SpriteRenderer>().color = !isActive ? Color.white : Color.red;
        BoundsInt bounds = targetTilemap.cellBounds;
        foreach (Vector3Int pos in bounds.allPositionsWithin)
        {
            if (targetTilemap.HasTile(pos))
            {
                
                targetTilemap.SetTile(pos, isActive ? activeTile : inactiveTile);
            }
        }
    }

    private void OnTriggerStay2D(Collider2D other)
    {
        // This fires EVERY FRAME while the player is inside the trigger
        if (other.CompareTag("Player") && isActive)
        {
            player.TakeDamage(1);
        }
    }
}
