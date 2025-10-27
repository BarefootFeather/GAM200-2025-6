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
    private Dictionary<Transform, HashSet<Vector3Int>> lastTiles = new();
    private HashSet<Transform> trackedBreakables = new();

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
        lastTiles.Clear();
        trackedBreakables.Clear();

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
                    lastPositions[child] = child.position;
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
        foreach (Transform child in transform)
        {
            if (child != null && child.CompareTag("Moveable"))
            {
                if (!lastPositions.ContainsKey(child) || child.position != lastPositions[child])
                {
                    UpdateMoveableObject(child);
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

        // Update tracking
        lastPositions[obj] = obj.position;
        lastTiles[obj] = newTiles;
    }

    private void RemoveBreakableObject(Transform obj)
    {
        if (lastTiles.ContainsKey(obj))
        {
            // Instead of ExceptWith, use simple Remove loop
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

    public bool CanMoveTo(Vector3 worldPosition)
    {
        Vector3Int cellPosition = targetTilemap.WorldToCell(worldPosition);
        return !occupiedTiles.Contains(cellPosition);
    }

    public void OnObjectDestroyed(Transform obj)
    {
        if (obj.CompareTag("Breakable"))
        {
            RemoveBreakableObject(obj);
        }
        else if (obj.CompareTag("Moveable"))
        {
            if (lastTiles.ContainsKey(obj))
            {
                foreach (var tile in lastTiles[obj])
                {
                    occupiedTiles.Remove(tile);
                }
                lastTiles.Remove(obj);
            }
            lastPositions.Remove(obj);
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
            lastPositions[obj] = obj.position;
            lastTiles[obj] = tiles;
        }
        else if (obj.CompareTag("Breakable"))
        {
            trackedBreakables.Add(obj);
            lastTiles[obj] = tiles;
        }
    }
}