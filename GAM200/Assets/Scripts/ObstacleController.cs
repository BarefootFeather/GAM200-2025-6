using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

public class ObstacleController : MonoBehaviour
{
    [Header("Tilemap Configuration")]
    [SerializeField] private Tilemap targetTilemap;
    [SerializeField] private float boundInset = 0.1f;

    private HashSet<Vector3Int> occupiedTiles = new();

    private Grid grid;
    void Awake()
    {
        // Get all children
        foreach (Transform child in transform)
        {
            // Get bounds from child's transform
            Vector3 position = child.position;
            Vector3 scale = child.localScale;

            // Calculate min/max in world space
            Vector3 min = position - (scale / 2f) + new Vector3(boundInset, boundInset, 0);
            Vector3 max = position + (scale / 2f) - new Vector3(boundInset, boundInset, 0);

            // Convert to cells
            Vector3Int minCell = targetTilemap.WorldToCell(min);
            Vector3Int maxCell = targetTilemap.WorldToCell(max);

            // Add all tiles in range
            for (int x = minCell.x; x <= maxCell.x; x++)
            {
                for (int y = minCell.y; y <= maxCell.y; y++)
                {
                    occupiedTiles.Add(new Vector3Int(x, y, 0));
                }
            }
        }
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        grid = targetTilemap.layoutGrid;

        if (!grid)
        {
            Debug.LogError("No Grid component found on the Tilemap's parent GameObject!");
            return;
        }

        /* ========= Set the tile position to the center of the grid cell =========
        foreach (Transform childTransform in GetComponentInChildren<Transform>())
        {

            if (childTransform != null)
            {

                Vector3Int cellPosition = grid.WorldToCell(childTransform.position);

                // Convert cell coordinates back to centered world position
                Vector3 centeredWorldPos = grid.GetCellCenterWorld(cellPosition);

                // Move GameObject to center of cell
                childTransform.position = centeredWorldPos;
            }
        }*/
    }

    public bool CanMoveTo(Vector3 worldPosition)
    {
        Vector3Int cellPosition = targetTilemap.WorldToCell(worldPosition);
        return !occupiedTiles.Contains(cellPosition);
    }
}
