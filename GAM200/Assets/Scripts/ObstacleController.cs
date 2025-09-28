using UnityEngine;
using UnityEngine.Tilemaps;

public class ObstacleController : MonoBehaviour
{
    [Header("Tilemap Configuration")]
    [SerializeField] private Tilemap targetTilemap;

    private Grid grid;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        grid = targetTilemap.layoutGrid;

        if (!grid)
        {
            Debug.LogError("No Grid component found on the Tilemap's parent GameObject!");
            return;
        }

        // ========= Set the tile position to the center of the grid cell =========
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
        }


    }
}
