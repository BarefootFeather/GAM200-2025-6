using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Grid-style enemy that moves in a repeating Up/Right/Down/Left pattern,
/// stepping only on certain musical intervals (hooked from your BPM system).
/// Movement never gets blocked; if it would hit the target, we just log it.
/// </summary>
public class EnemyController : MonoBehaviour
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

    [Header("Cardinal Counts (looped pattern)")]
    [Tooltip("Go Up this many steps first.")]
    [Min(0)] public int stepsUpStart = 3;
    [Tooltip("Then go Right this many steps.")]
    [Min(0)] public int stepsRight = 6;
    [Tooltip("Then go Down this many steps.")]
    [Min(0)] public int stepsDown = 6;
    [Tooltip("Then go Left this many steps.")]
    [Min(0)] public int stepsLeft = 6;
    [Tooltip("Finally go Up this many steps, then loop back to the start.")]
    [Min(0)] public int stepsUpEnd = 3;

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
    [SerializeField] private int enemyHealth;


    private bool isDying = false; // Prevent multiple death calls

    // ---------------- Internals ----------------

    private int intervalCounter = -1;   // counts how many beats have passed
    private Coroutine moveCo;           // handle to the glide coroutine
    private Rigidbody2D rb2d;           // optional, if attached to moveRoot
    private BoxCollider2D cachedBox;    // optional, used to auto-size the OverlapBox


    // convenient accessor: which transform actually moves
    private Transform M => moveRoot != null ? moveRoot : transform;

    // Simple direction enum + segment structure for the pattern list
    private enum Dir { Up, Right, Down, Left }
    private struct Segment { public Dir d; public int count; public Segment(Dir d, int c) { this.d = d; this.count = c; } }

    private Segment[] segments; // the looped movement pattern
    private int segIndex = 0;   // which segment we are on (e.g., "Right")
    private int segStepTaken = 0; // how many steps already taken in the current segment

    private void Awake()
    {
        if (moveRoot == null) moveRoot = transform;

        rb2d = moveRoot.GetComponent<Rigidbody2D>();
        cachedBox = moveRoot.GetComponent<BoxCollider2D>();

        RebuildSegments();
        ClampIndices();

        SpawnAtGridPosition(spawnGridCoords);
    }


    private void OnValidate()
    {
        // keep input sane in the Inspector
        moveEveryNIntervals = Mathf.Max(1, moveEveryNIntervals);
        stepsUpStart = Mathf.Max(0, stepsUpStart);
        stepsRight = Mathf.Max(0, stepsRight);
        stepsDown = Mathf.Max(0, stepsDown);
        stepsLeft = Mathf.Max(0, stepsLeft);
        stepsUpEnd = Mathf.Max(0, stepsUpEnd);

        // whenever values change in Inspector, rebuild the pattern safely
        RebuildSegments();
        ClampIndices();
    }

    private void Update()
    {
        // Check for player collision every frame when damage is enabled
        if (canDamagePlayer && player != null && gridTilemap != null)
        {
            CheckPlayerCollision();
        }
    }

    // Turn the five counts into a simple list we can loop over forever
    private void RebuildSegments()
    {
        var tmp = new List<Segment>(5);
        if (stepsUpStart > 0) tmp.Add(new Segment(Dir.Up, stepsUpStart));
        if (stepsRight > 0) tmp.Add(new Segment(Dir.Right, stepsRight));
        if (stepsDown > 0) tmp.Add(new Segment(Dir.Down, stepsDown));
        if (stepsLeft > 0) tmp.Add(new Segment(Dir.Left, stepsLeft));
        if (stepsUpEnd > 0) tmp.Add(new Segment(Dir.Up, stepsUpEnd));

        // if everything is zero, at least have an empty Up segment so we don't crash
        segments = tmp.Count > 0 ? tmp.ToArray() : new Segment[] { new Segment(Dir.Up, 0) };
    }

    // Keep our current indices inside safe ranges
    private void ClampIndices()
    {
        segIndex = Mathf.Clamp(segIndex, 0, Mathf.Max(0, segments.Length - 1));
        segStepTaken = Mathf.Clamp(
            segStepTaken,
            0,
            (segments.Length > 0 ? Mathf.Max(0, segments[segIndex].count - 1) : 0)
        );
    }

    /// <summary>
    /// Spawns the enemy at the specified grid coordinates.
    /// Uses the gridTilemap to convert grid coordinates to world position.
    /// </summary>
    /// <param name="gridCoords">Grid coordinates to spawn at</param>
    private void SpawnAtGridPosition(Vector2Int gridCoords)
    {
        Vector3 worldPos;
        
        if (gridTilemap != null)
        {
            // Convert grid coordinates to world position using the tilemap
            Vector3Int cellPos = new Vector3Int(gridCoords.x, gridCoords.y, 0);
            worldPos = gridTilemap.GetCellCenterWorld(cellPos);
        }
        else
        {
            // Fallback: use step distance as grid size
            worldPos = new Vector3(gridCoords.x * stepDistance, gridCoords.y * stepDistance, 0);
        }

        // Position the move root (or this transform if no move root)
        if (rb2d)
        {
            rb2d.MovePosition(worldPos);
        }
        else
        {
            M.position = worldPos;
        }

        if (debugLogs)
        {
            Debug.Log($"[EnemyController] Spawned at grid {gridCoords} → world {worldPos}");
        }
    }

    /// <summary>
    /// Checks if the player and enemy are on the same grid position and deals damage if so.
    /// </summary>
    private void CheckPlayerCollision()
    {
        // Get both player and enemy grid positions
        Vector3Int playerGridPos = gridTilemap.WorldToCell(player.transform.position);
        Vector3Int enemyGridPos = gridTilemap.WorldToCell(M.position);
        
        // If they're on the same grid cell and player isn't invulnerable, deal damage
        if (playerGridPos == enemyGridPos && !player.IsInvulnerable())
        {
            player.TakeDamage(damageAmount);
            
            if (debugLogs)
            {
                Debug.Log($"[EnemyController] Player and enemy both at grid {playerGridPos}, dealt {damageAmount} damage");
            }
        }
    }

    /// <summary>
    /// Call this from your BPM system when an interval/beat happens.
    /// Example: hook to BPMController.Interval.onIntervalReached in the Inspector.
    /// </summary>
    public void OnIntervalReached()
    {
        intervalCounter++;

        // Only move on scheduled beats (e.g., every 2nd beat, with optional offset)
        if (IsScheduled(intervalCounter, moveEveryNIntervals, moveOffset))
            DoMoveTick();
    }

    // Simple beat scheduling helper
    private static bool IsScheduled(int count, int everyN, int offset) => ((count - offset) % everyN) == 0;

    // This runs once per scheduled move (i.e., on the beat you chose)
    private void DoMoveTick()
    {
        if (segments == null || segments.Length == 0) return;

        // If we somehow have an empty segment, skip forward safely
        int guard = 0;
        while (segments[segIndex].count == 0 && guard++ < 16)
            AdvanceSegment();

        var seg = segments[segIndex];
        Vector2 dir = DirToVector(seg.d);
        Vector2 from = M.position;
        Vector2 to = from + dir * stepDistance;

        // MODIFIED: Use tilemap grid instead of simple snapping
        if (gridTilemap)
        {
            Vector3Int gridPos = gridTilemap.WorldToCell(to);
            to = gridTilemap.GetCellCenterWorld(gridPos);
        }
        else if (snapToGrid)
        {
            to = Snap(to, stepDistance);
        }

        // Log what we would collide with (colliders/tilemap), but NEVER block
        LogPotentialCollisions(from, to);


        if (debugLogs)
            Debug.Log($"[EnemyController] Moving {seg.d} ({segStepTaken + 1}/{seg.count}) → {to}");

        // Actually move (teleport or glide)
        MoveTo(to);

        // Update how many steps we've taken in this segment
        segStepTaken++;
        if (segStepTaken >= seg.count)
        {
            segStepTaken = 0;
            AdvanceSegment(); // move to the next segment (and loop when needed)
        }
    }

    private void LogPotentialCollisions(Vector2 from, Vector2 to)
    {
        // Physics: OverlapBox at the DESTINATION position (logging only, no damage)
        if (logPhysicsCollisions)
        {
            Vector2 size = useColliderSize && cachedBox != null ? cachedBox.size : boxCheckSize;
            float angle = moveRoot ? moveRoot.eulerAngles.z : transform.eulerAngles.z;

            var hits = Physics2D.OverlapBoxAll(to, size, angle, physicsMask);
            foreach (var h in hits)
            {
                if (!h) continue;
                if (!includeTriggers && h.isTrigger) continue;
                if (h.transform == (moveRoot ? moveRoot : transform)) continue; // skip self
                
                //Debug.Log($"[EnemyController] Would collide with '{h.name}' at {to}");
            }
        }

        // Tilemap: log if the destination cell has a tile
        if (logTilemapCollisions && collisionTilemap != null)
        {
            Vector3Int cell = collisionTilemap.WorldToCell(to);
            //if (collisionTilemap.HasTile(cell))
            //    Debug.Log($"[EnemyController] Would collide with Tilemap at cell {cell} (world {to})");
        }
    }


    // Move to the next cell (teleport or smooth glide)
    private void MoveTo(Vector2 nextPos)
    {
        if (!smoothStep)
        {
            // Instant move each scheduled step
            if (rb2d) rb2d.MovePosition(nextPos);
            else M.position = nextPos;
            return;
        }

        // Smooth glide between cells
        float dur = CalcLerpDuration();
        if (moveCo != null) StopCoroutine(moveCo);
        moveCo = StartCoroutine(StepLerp2D(rb2d, M.position, nextPos, dur));
    }

    // Convert BPM + interval fraction into a time in seconds
    private float CalcLerpDuration()
    {
        float intervalSec = (60f / Mathf.Max(1, bpmForSmoothing)) * Mathf.Max(0.0001f, intervalBeatsForSmoothing);
        return Mathf.Max(0.01f, intervalSec * moveLerpFractionOfInterval);
    }

    // Coroutine that moves from A to B over "duration" seconds
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
            // If using physics, move on FixedUpdate timing
            while (t < 1f)
            {
                t += Time.fixedDeltaTime / duration;
                body.MovePosition(Vector2.Lerp(from, to, Mathf.Clamp01(t)));
                yield return new WaitForFixedUpdate();
            }
        }
        else
        {
            // Otherwise, use normal frame timing
            while (t < 1f)
            {
                t += Time.deltaTime / duration;
                M.position = Vector2.Lerp(from, to, Mathf.Clamp01(t));
                yield return null;
            }
        }
    }

    // Advance to the next segment in our loop (wrap around at the end)
    private void AdvanceSegment()
    {
        segIndex++;
        if (segIndex >= segments.Length) segIndex = 0; // loop forever
    }

    // Helper: convert our direction enum into a Vector2
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

    // Helper: snap a position to a grid so we stay aligned
    private static Vector2 Snap(Vector2 pos, float cell)
    {
        if (cell <= 0f) return pos;
        float x = Mathf.Round(pos.x / cell) * cell;
        float y = Mathf.Round(pos.y / cell) * cell;
        return new Vector2(x, y);
    }

    // Visualize the planned path and our cast radius when selected in Scene view
    private void OnDrawGizmosSelected()
    {
        // draw current OverlapBox size preview at mover
        Vector2 size = useColliderSize && cachedBox != null ? cachedBox.size : boxCheckSize;
        var mr = moveRoot ? moveRoot : transform;

        Gizmos.color = new Color(1f, 0.6f, 0.2f, 0.35f);
        Matrix4x4 old = Gizmos.matrix;
        Gizmos.matrix = Matrix4x4.TRS(mr.position, mr.rotation, Vector3.one);
        Gizmos.DrawWireCube(Vector3.zero, new Vector3(size.x, size.y, 0.01f));
        Gizmos.matrix = old;

    }

    public void TakeDamage()
    {
        if (isDying) return;

        enemyHealth -= 1;

        if (enemyHealth <= 0)
        {
            isDying = true;

            this.gameObject.SetActive(false);

            // Play death animation
            //animator.SetTrigger("Death");

            // Start coroutine to destroy after animation
            //StartCoroutine(DeathSequence());
        }
    }

}
