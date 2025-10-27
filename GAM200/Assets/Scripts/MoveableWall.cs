using UnityEngine;
using UnityEngine.Tilemaps;

[RequireComponent(typeof(Collider2D))]
public class MoveableWall : MonoBehaviour
{
    private Tilemap tilemap;
    private ObstacleController obstacleController;
    private bool isMoving = false;
    private Vector3 targetPosition;
    private float moveSpeed = 10f; // slide animation

    private void Start()
    {
        // Cache references
        tilemap = FindFirstObjectByType<Tilemap>();
        obstacleController = FindFirstObjectByType<ObstacleController>();
        targetPosition = transform.position;
    }

    private void Update()
    {
        // Smooth move to target position if animating
        if (isMoving)
        {
            transform.position = Vector3.MoveTowards(transform.position, targetPosition, moveSpeed * Time.deltaTime);
            if (Vector3.Distance(transform.position, targetPosition) < 0.01f)
            {
                transform.position = targetPosition;
                isMoving = false;
            }
        }
    }

    public bool TryPush(Vector3Int direction)
    {
        if (isMoving) return false; // ignore during movement

        Vector3Int currentCell = tilemap.WorldToCell(transform.position);
        Vector3Int targetCell = currentCell + direction;
        Vector3 targetWorld = tilemap.GetCellCenterWorld(targetCell);

        // check if destination is free
        if (obstacleController != null && obstacleController.CanMoveTo(targetWorld))
        {
            // tell ObstacleController to update occupied tiles
            obstacleController.OnObjectDestroyed(transform);
            targetPosition = targetWorld;
            isMoving = true;

            // update position in obstacle controller after move
            obstacleController.OnObjectAdded(transform);

            return true;
        }

        return false;
    }
}
