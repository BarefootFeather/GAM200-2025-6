using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Grid-style enemy that steps along a configurable path (direction + step counts).
/// Movement is synced to BPM intervals (hook OnIntervalReached from your beat system).
/// </summary>
public class EnemyScript : MonoBehaviour
{
    [Header("Grid Alignment")]
    [Tooltip("Tilemap to align movement with (should be same as player's tilemap)")]
    [SerializeField] private Tilemap gridTilemap;
    [SerializeField] private Vector2Int spawnGridCoords;

    [Header("Move Root")]
    [Tooltip("Which Transform actually moves (e.g., a child sprite). If empty, this object moves.")]
    [SerializeField] private Transform moveRoot;

    [Header("Beat Scheduling")]
    [Tooltip("Move every N intervals from your BPM system (1 = every interval).")]
    [SerializeField] private int moveEveryNIntervals = 1;
    [Tooltip("Optional offset so this enemy moves on a different beat than others.")]
    [SerializeField] private int moveOffset = 0;

    [Header("Movement")]
    [Tooltip("How far to step each move).")]
    [SerializeField] private float stepDistance = 1.0f;
    [Tooltip("If on, positions are rounded to the grid so drift doesn't build up.")]
    [SerializeField] private bool snapToGrid = false;

    [Header("Smooth Step (optional)")]
    [Tooltip("If off, we teleport one step each beat. If on, we glide between cells.")]
    [SerializeField] private bool smoothStep = false;
    [Tooltip("How long the glide lasts, as a fraction of one interval.")]
    [SerializeField, Range(0.05f, 0.9f)] private float moveLerpFractionOfInterval = 0.35f;
    [Tooltip("Only used to calculate the glide time (no need to match the real BPM exactly).")]
    [SerializeField] private int bpmForSmoothing = 120;
    [Tooltip("How many beats between moves when calculating the glide duration (usually 1).")]
    [SerializeField] private float intervalBeatsForSmoothing = 1f;

    [Header("Path (used for movement)")]
    [Tooltip("Sequence of direction + step counts. Movement loops over this list forever.")]
    [SerializeField] private List<Step> path = new List<Step>();

    [Header("Collision LOGGING (does NOT block)")]
    [Tooltip("Log colliders at the destination using Physics2D OverlapBox.")]
    [SerializeField] private bool logPhysicsCollisions = true;
    [Tooltip("If true, auto-use BoxCollider2D size on Move Root (if found).")]
    [SerializeField] private bool useColliderSize = true;
    [Tooltip("Fallback/override size used for OverlapBox if no BoxCollider2D or auto is off.")]
    [SerializeField] private Vector2 boxCheckSize = new Vector2(0.8f, 0.8f);
    [Tooltip("Include trigger colliders in logs.")]
    [SerializeField] private bool includeTriggers = false;
    [Tooltip("Layer mask for physics logging.")]
    [SerializeField] private LayerMask physicsMask = ~0;
    [Tooltip("Log if destination cell on this Tilemap has a tile (e.g., walls).")]
    [SerializeField] private bool logTilemapCollisions = false;
    [Tooltip("Tilemap used for tile-based collision logging (e.g., Walls).")]
    [SerializeField] private Tilemap collisionTilemap;

    [Header("Player Damage")]
    [Tooltip("Reference to the PlayerController for dealing damage.")]
    [SerializeField] private PlayerController player;
    [Tooltip("Amount of damage to deal to the player per contact.")]
    [SerializeField] private int damageAmount = 1;
    [Tooltip("If true, enemy can damage the player. If false, enemy is harmless.")]
    [SerializeField] private bool canDamagePlayer = true;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = false;

    [Header("-------------- Enemy Variables --------------")]
    [SerializeField] private int enemyHealth = 1;
    [SerializeField] private Animator animator;
    [SerializeField] private SpriteRenderer spriteRenderer;

    private bool isDying = false;
    private bool deathAnimationComplete = false;

    // ---------------- Internals ----------------
    private int intervalCounter = -1;
    private Coroutine moveCo;
    private Rigidbody2D rb2d;
    private BoxCollider2D cachedBox;

    private Transform M => moveRoot != null ? moveRoot : transform;

    // Public because it's used by the public Step struct (serialized in Inspector)
    public enum Dir { Up, Right, Down, Left }

    [System.Serializable]
    public struct Step
    {
        public Dir direction;
        [Min(0)] public int count;
        public Step(Dir d, int c) { direction = d; count = c; }
    }

    // Runtime state for walking the path
    private int pathIndex = 0;
    private int stepsTakenInCurrent = 0;

    private void Awake()
    {
        if (moveRoot == null) moveRoot = transform;

        rb2d = moveRoot.GetComponent<Rigidbody2D>();
        cachedBox = moveRoot.GetComponent<BoxCollider2D>();

        // spawn
        //SpawnAtGridPosition(spawnGridCoords);

        // ========= Set the tile position to the center of the grid cell =========
        // Convert world position to cell coordinates
        Vector3Int cellPosition = gridTilemap.WorldToCell(transform.position);

        // Convert cell coordinates back to centered world position
        Vector3 centeredWorldPos = gridTilemap.GetCellCenterWorld(cellPosition);

        // Move GameObject to center of cell
        transform.position = centeredWorldPos;

        // sanitize path once at start
        SanitizePath();
    }

    private void OnValidate()
    {
        moveEveryNIntervals = Mathf.Max(1, moveEveryNIntervals);
        SanitizePath();
    }

    private void Update()
    {
        if (canDamagePlayer && player != null && gridTilemap != null)
            CheckPlayerCollision();
    }

    // Ensure non-negative counts; keep at least an empty path item to avoid division-by-zero when author forgets to fill it
    private void SanitizePath()
    {
        if (path == null) path = new List<Step>();
        for (int i = 0; i < path.Count; ++i)
            path[i] = new Step(path[i].direction, Mathf.Max(0, path[i].count));

        if (path.Count == 0)
        {
            // optional: remain idle if empty. No auto-fallback to legacy.
            pathIndex = 0;
            stepsTakenInCurrent = 0;
        }
        else
        {
            pathIndex %= Mathf.Max(1, path.Count);
            stepsTakenInCurrent = Mathf.Clamp(stepsTakenInCurrent, 0, path[pathIndex].count);
        }
    }

    private void SpawnAtGridPosition(Vector2Int gridCoords)
    {
        Vector3 worldPos;

        if (gridTilemap != null)
        {
            Vector3Int cellPos = new Vector3Int(gridCoords.x, gridCoords.y, 0);
            worldPos = gridTilemap.GetCellCenterWorld(cellPos);
        }
        else
        {
            worldPos = new Vector3(gridCoords.x * stepDistance, gridCoords.y * stepDistance, 0);
        }

        if (rb2d) rb2d.MovePosition(worldPos);
        else M.position = worldPos;

        if (debugLogs)
            Debug.Log($"[EnemyScript] Spawned at grid {gridCoords} → world {worldPos}");
    }

    private void CheckPlayerCollision()
    {
        Vector3Int playerGridPos = gridTilemap.WorldToCell(player.transform.position);
        Vector3Int enemyGridPos = gridTilemap.WorldToCell(M.position);

        if (playerGridPos == enemyGridPos && !player.IsInvulnerable())
        {
            player.TakeDamage(damageAmount);
            if (debugLogs)
                Debug.Log($"[EnemyScript] Player and enemy both at grid {playerGridPos}, dealt {damageAmount} damage");
        }
    }

    /// <summary>Hook this to your beat/interval event.</summary>
    public void OnIntervalReached()
    {
        intervalCounter++;
        if (IsScheduled(intervalCounter, moveEveryNIntervals, moveOffset))
            DoMoveTick();
    }

    private static bool IsScheduled(int count, int everyN, int offset) => ((count - offset) % everyN) == 0;

    private void DoMoveTick()
    {
        if (isDying) return;
        if (path == null || path.Count == 0) return;

        // Skip zero-count steps safely
        int guard = 0;
        while (path[pathIndex].count == 0 && guard++ < 16)
            AdvancePath();

        var current = path[pathIndex];
        Vector2 dir = DirToVector(current.direction);

        HandleSpriteFlipping(current.direction);

        Vector2 from = M.position;
        Vector2 to = from + dir * stepDistance;

        if (gridTilemap)
        {
            Vector3Int gridPos = gridTilemap.WorldToCell(to);
            to = gridTilemap.GetCellCenterWorld(gridPos);
        }
        else if (snapToGrid)
        {
            to = Snap(to, stepDistance);
        }

        LogPotentialCollisions(from, to);

        if (debugLogs)
            Debug.Log($"[EnemyScript] Moving {current.direction} ({stepsTakenInCurrent + 1}/{current.count}) → {to}");

        MoveTo(to);

        stepsTakenInCurrent++;
        if (stepsTakenInCurrent >= current.count)
        {
            stepsTakenInCurrent = 0;
            AdvancePath();
        }
    }

    private void AdvancePath()
    {
        if (path == null || path.Count == 0) return;
        pathIndex = (pathIndex + 1) % path.Count;
    }

    private void LogPotentialCollisions(Vector2 toFrom, Vector2 to)
    {
        if (logPhysicsCollisions)
        {
            Vector2 size = useColliderSize && cachedBox != null ? cachedBox.size : boxCheckSize;
            float angle = moveRoot ? moveRoot.eulerAngles.z : transform.eulerAngles.z;

            var hits = Physics2D.OverlapBoxAll(to, size, angle, physicsMask);
            foreach (var h in hits)
            {
                if (!h) continue;
                if (!includeTriggers && h.isTrigger) continue;
                if (h.transform == (moveRoot ? moveRoot : transform)) continue;
                // Debug.Log($"[EnemyScript] Would collide with '{h.name}' at {to}");
            }
        }

        if (logTilemapCollisions && collisionTilemap != null)
        {
            Vector3Int cell = collisionTilemap.WorldToCell(to);
            // if (collisionTilemap.HasTile(cell))
            //     Debug.Log($"[EnemyScript] Would collide with Tilemap at cell {cell} (world {to})");
        }
    }

    private void MoveTo(Vector2 nextPos)
    {
        if (!smoothStep)
        {
            if (rb2d) rb2d.MovePosition(nextPos);
            else M.position = nextPos;
            return;
        }

        float dur = CalcLerpDuration();
        if (moveCo != null) StopCoroutine(moveCo);
        moveCo = StartCoroutine(StepLerp2D(rb2d, M.position, nextPos, dur));
    }

    private float CalcLerpDuration()
    {
        float intervalSec = (60f / Mathf.Max(1, bpmForSmoothing)) * Mathf.Max(0.0001f, intervalBeatsForSmoothing);
        return Mathf.Max(0.01f, intervalSec * moveLerpFractionOfInterval);
    }

    private IEnumerator StepLerp2D(Rigidbody2D body, Vector2 from, Vector2 to, float duration)
    {
        if (duration <= 0f)
        {
            if (body) body.MovePosition(to);
            else M.position = to;
            yield break;
        }

        float t = 0f;

        if (body)
        {
            while (t < 1f)
            {
                t += Time.fixedDeltaTime / duration;
                body.MovePosition(Vector2.Lerp(from, to, Mathf.Clamp01(t)));
                yield return new WaitForFixedUpdate();
            }
        }
        else
        {
            while (t < 1f)
            {
                t += Time.deltaTime / duration;
                M.position = Vector2.Lerp(from, to, Mathf.Clamp01(t));
                yield return null;
            }
        }
    }

    private static Vector2 DirToVector(Dir d)
    {
        switch (d)
        {
            case Dir.Up: return Vector2.up;
            case Dir.Right: return Vector2.right;
            case Dir.Down: return Vector2.down;
            default: return Vector2.left;
        }
    }

    private static Vector2 Snap(Vector2 pos, float cell)
    {
        if (cell <= 0f) return pos;
        float x = Mathf.Round(pos.x / cell) * cell;
        float y = Mathf.Round(pos.y / cell) * cell;
        return new Vector2(x, y);
    }

    private void OnDrawGizmosSelected()
    {
        Vector2 size = useColliderSize && cachedBox != null ? cachedBox.size : boxCheckSize;
        var mr = moveRoot ? moveRoot : transform;

        Gizmos.color = new Color(1f, 0.6f, 0.2f, 0.35f);
        Matrix4x4 old = Gizmos.matrix;
        Gizmos.matrix = Matrix4x4.TRS(mr.position, mr.rotation, Vector3.one);
        Gizmos.DrawWireCube(Vector3.zero, new Vector3(size.x, size.y, 0.01f));
        Gizmos.matrix = old;
    }

    // ===== Health / Death / Visuals =====
    public void TakeDamage()
    {
        if (isDying) return;

        enemyHealth -= 1;
        if (enemyHealth <= 0)
        {
            isDying = true;
            StartCoroutine(DeathSequence());
        }
    }

    public void OnDeathAnimationComplete() => deathAnimationComplete = true;

    private IEnumerator DeathSequence()
    {
        if (animator) animator.SetTrigger("Death");
        yield return new WaitUntil(() => deathAnimationComplete);
        gameObject.SetActive(false);
    }

    private void HandleSpriteFlipping(Dir direction)
    {
        if (!spriteRenderer) return;

        switch (direction)
        {
            case Dir.Left: spriteRenderer.flipX = true; break;
            case Dir.Right: spriteRenderer.flipX = false; break;
                // Up and Down keep current flip
        }
    }

   

}
