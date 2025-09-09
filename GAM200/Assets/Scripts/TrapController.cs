using TMPro;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.UI;

public class TrapController : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    public GameObject debug;
    public TileBase inactiveTile;
    public TileBase activeTile;
    public Tilemap targetTilemap; // Reference to the Tilemap component

    private bool isActive = false;

    void Start()
    {
        /*
        debug.GetComponent<SpriteRenderer>().color = Color.red;
        //isActive = !isActive;

        BoundsInt bounds = targetTilemap.cellBounds;

        foreach (Vector3Int pos in bounds.allPositionsWithin)
        {
            if (targetTilemap.HasTile(pos))
            {
                targetTilemap.SetTile(pos, activeTile);
            }
        }
        */
    }


    // Update is called once per frame
    void Update()
    {
        
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        
    }

    public void ToggleTrap()
    {
        isActive = !isActive;
        debug.GetComponent<SpriteRenderer>().color = isActive ? Color.green : Color.red;
        BoundsInt bounds = targetTilemap.cellBounds;
        foreach (Vector3Int pos in bounds.allPositionsWithin)
        {
            if (targetTilemap.HasTile(pos))
            {
                targetTilemap.SetTile(pos, isActive ? activeTile : inactiveTile);
            }
        }
    }




}
