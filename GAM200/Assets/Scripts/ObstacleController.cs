using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

public class ObstacleController : MonoBehaviour
{
    [Header("Tilemap Configuration")]
    [SerializeField] private Tilemap targetTilemap;
    [SerializeField] private float boundInset = 0.1f;

    private HashSet<Vector3Int> occupiedTiles = new();
    private Dictionary<Transform, Vector3> lastPositions = new();
    private Dictionary<Transform, Vector3Int> lastPositions_Cell = new();
    private Dictionary<Transform, HashSet<Vector3Int>> lastTiles = new();
    private HashSet<Transform> trackedBreakables = new();
    private HashSet<Transform> trackedMoveables = new();

    void Awake()
    {
        InitializeAllTiles();
    }

    void Update()
    {
        CheckMoveableObjects();
        CheckBreakableObjects();
    }

    private void InitializeAllTiles()
    {
        occupiedTiles.Clear();
        lastPositions.Clear();
        lastPositions_Cell.Clear();
        lastTiles.Clear();
        trackedBreakables.Clear();
        trackedMoveables.Clear();

        foreach (Transform child in transform)
        {
            if (child != null)
            {
                var tiles = GetObjectTiles(child);

                foreach (var tile in tiles)
                {
                    occupiedTiles.Add(tile);
                }

                if (child.CompareTag("Moveable"))
                {
                    trackedMoveables.Add(child);
                    lastPositions[child] = child.position;
                    lastPositions_Cell[child] = targetTilemap.WorldToCell(child.position);
                    lastTiles[child] = tiles;
                }
                else if (child.CompareTag("Breakable"))
                {
                    trackedBreakables.Add(child);
                    lastTiles[child] = tiles;
                }
            }
        }
    }

    private void CheckMoveableObjects()
    {
        // FIX: Use trackedMoveables instead of iterating through all children
        foreach (Transform moveableObj in trackedMoveables)
        {
            if (moveableObj != null && moveableObj.CompareTag("Moveable"))
            {
                if (!lastPositions.ContainsKey(moveableObj) || moveableObj.position != lastPositions[moveableObj])
                {
                    UpdateMoveableObject(moveableObj);
                }
            }
        }
    }

    private void CheckBreakableObjects()
    {
        var toRemove = new List<Transform>();

        foreach (Transform breakable in trackedBreakables)
        {
            if (breakable == null)
            {
                toRemove.Add(breakable);
            }
        }

        foreach (Transform destroyed in toRemove)
        {
            RemoveBreakableObject(destroyed);
        }
    }

    private void UpdateMoveableObject(Transform obj)
    {
        if (lastTiles.ContainsKey(obj))
        {
            foreach (var tile in lastTiles[obj])
            {
                occupiedTiles.Remove(tile);
            }
        }

        var newTiles = GetObjectTiles(obj);
        foreach (var tile in newTiles)
        {
            occupiedTiles.Add(tile);
        }

        // FIX: Update both position and cell position tracking
        lastPositions[obj] = obj.position;
        lastPositions_Cell[obj] = targetTilemap.WorldToCell(obj.position);
        lastTiles[obj] = newTiles;
    }

    private void RemoveBreakableObject(Transform obj)
    {
        if (lastTiles.ContainsKey(obj))
        {
            foreach (var tile in lastTiles[obj])
            {
                occupiedTiles.Remove(tile);
            }
            lastTiles.Remove(obj);
        }
        trackedBreakables.Remove(obj);
    }

    private HashSet<Vector3Int> GetObjectTiles(Transform obj)
    {
        var tiles = new HashSet<Vector3Int>();
        Vector3 position = obj.position;
        Vector3 scale = obj.localScale;
        Vector3 inset = new Vector3(boundInset, boundInset, 0);

        Vector3 min = position - scale / 2f + inset;
        Vector3 max = position + scale / 2f - inset;

        Vector3Int minCell = targetTilemap.WorldToCell(min);
        Vector3Int maxCell = targetTilemap.WorldToCell(max);

        for (int x = minCell.x; x <= maxCell.x; x++)
        {
            for (int y = minCell.y; y <= maxCell.y; y++)
            {
                tiles.Add(new Vector3Int(x, y, 0));
            }
        }

        return tiles;
    }

    public bool CanMoveTo(Vector3 worldPosition, Vector3Int dir)
    {
        Vector3Int cellPosition = targetTilemap.WorldToCell(worldPosition);

        // Check if there is an occupied tile at the target position
        if (occupiedTiles.Contains(cellPosition))
        {
            // Check if it is occupied by a moveable object
            if (lastPositions_Cell.ContainsValue(cellPosition))
            {
                foreach (var moveable in lastPositions_Cell)
                {
                    if (moveable.Value == cellPosition)
                    {
                        var moveableWall = moveable.Key.GetComponent<MoveableWall>();
                        bool canPush = false;
                        if (moveableWall != null)
                        {
                            canPush = moveableWall.TryPush(dir);
                        }
                        return canPush; // Allow move onto moveable object
                    }
                }
            }
            return false; // Occupied by non-moveable object
        }
        return true; // Free to move
    }

    public bool CanMoveToObj(Vector3 worldPosition)
    {
        Vector3Int cellPosition = targetTilemap.WorldToCell(worldPosition);
        Debug.Log("Checking CanMoveToObj for cell " + occupiedTiles.Contains(cellPosition));
        return !occupiedTiles.Contains(cellPosition); // Free to move
    }

    public void OnObjectDestroyed(Transform obj)
    {
        if (obj.CompareTag("Breakable"))
        {
            RemoveBreakableObject(obj);
        }
        else if (obj.CompareTag("Moveable"))
        {
            // Remove from all tracking dictionaries and sets
            if (lastTiles.ContainsKey(obj))
            {
                foreach (var tile in lastTiles[obj])
                {
                    occupiedTiles.Remove(tile);
                }
                lastTiles.Remove(obj);
            }
            lastPositions.Remove(obj);
            lastPositions_Cell.Remove(obj);
            trackedMoveables.Remove(obj);
        }
        else
        {
            // Static object destroyed, need full refresh
            InitializeAllTiles();
        }
    }

    public void OnObjectAdded(Transform obj)
    {
        var tiles = GetObjectTiles(obj);

        foreach (var tile in tiles)
        {
            occupiedTiles.Add(tile);
        }

        if (obj.CompareTag("Moveable"))
        {
            trackedMoveables.Add(obj);
            lastPositions[obj] = obj.position;
            lastPositions_Cell[obj] = targetTilemap.WorldToCell(obj.position);
            lastTiles[obj] = tiles;
        }
        else if (obj.CompareTag("Breakable"))
        {
            trackedBreakables.Add(obj);
            lastTiles[obj] = tiles;
        }
    }
}