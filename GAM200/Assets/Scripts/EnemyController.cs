using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Grid-style enemy that moves in a repeating pattern of steps.
/// By default uses the legacy 5-count (Up/Right/Down/Left/Up) pattern,
/// but if the new Path list is non-empty, that will override the legacy fields.
/// Movement is synchronized with beats from the BPM system.
/// </summary>
public class EnemyController : MonoBehaviour
{
    [Header("Grid Alignment")]
    [SerializeField] private Tilemap gridTilemap;
    [SerializeField] private Vector2Int spawnGridCoords;

    [Header("Move Root")]
    [SerializeField] private Transform moveRoot;

    [Header("Beat Scheduling")]
    [SerializeField] private int moveEveryNIntervals = 1;
    [SerializeField] private int moveOffset = 0;

    [Header("Movement")]
    [SerializeField] private float stepDistance = 1.0f;
    [SerializeField] private bool snapToGrid = false;

    [Header("Smooth Step (optional)")]
    [SerializeField] private bool smoothStep = false;
    [SerializeField, Range(0.05f, 0.9f)] private float moveLerpFractionOfInterval = 0.35f;
    [SerializeField] private int bpmForSmoothing = 120;
    [SerializeField] private float intervalBeatsForSmoothing = 1f;

    [Header("Path (new, overrides legacy if non-empty)")]
    [Tooltip("Flexible path: sequence of direction + step counts. If empty, falls back to legacy 5-count fields.")]
    [SerializeField] private List<Step> path = new List<Step>();

    [Header("Cardinal Counts (legacy; used only if Path is empty)")]
    [Min(0)] public int stepsUpStart = 3;
    [Min(0)] public int stepsRight = 6;
    [Min(0)] public int stepsDown = 6;
    [Min(0)] public int stepsLeft = 6;
    [Min(0)] public int stepsUpEnd = 3;

    [Header("Collision LOGGING (does NOT block)")]
    [SerializeField] private bool logPhysicsCollisions = true;
    [SerializeField] private bool useColliderSize = true;
    [SerializeField] private Vector2 boxCheckSize = new Vector2(0.8f, 0.8f);
    [SerializeField] private bool includeTriggers = false;
    [SerializeField] private LayerMask physicsMask = ~0;
    [SerializeField] private bool logTilemapCollisions = false;
    [SerializeField] private Tilemap collisionTilemap;

    [Header("Player Damage")]
    [SerializeField] private PlayerController player;
    [SerializeField] private int damageAmount = 1;
    [SerializeField] private bool canDamagePlayer = true;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = false;

    // ---------------- Internals ----------------
    private int intervalCounter = -1;
    private Coroutine moveCo;
    private Rigidbody2D rb2d;
    private BoxCollider2D cachedBox;

    private Transform M => moveRoot != null ? moveRoot : transform;

    public enum Dir { Up, Right, Down, Left }

    [System.Serializable]
    public struct Step
    {
        public Dir direction;
        [Min(0)] public int count;
        public Step(Dir d, int c) { direction = d; count = c; }
    }

    private struct Segment { public Dir d; public int count; public Segment(Dir d, int c) { this.d = d; this.count = c; } }

    private Segment[] segments;
    private int segIndex = 0;
    private int segStepTaken = 0;

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
        moveEveryNIntervals = Mathf.Max(1, moveEveryNIntervals);
        stepsUpStart = Mathf.Max(0, stepsUpStart);
        stepsRight = Mathf.Max(0, stepsRight);
        stepsDown = Mathf.Max(0, stepsDown);
        stepsLeft = Mathf.Max(0, stepsLeft);
        stepsUpEnd = Mathf.Max(0, stepsUpEnd);

        RebuildSegments();
        ClampIndices();
    }

    private void Update()
    {
        if (canDamagePlayer && player != null && gridTilemap != null)
        {
            CheckPlayerCollision();
        }
    }

    // Build the movement segments
    private void RebuildSegments()
    {
        var tmp = new List<Segment>();

        if (path != null && path.Count > 0)
        {
            foreach (var s in path)
            {
                int c = Mathf.Max(0, s.count);
                if (c > 0) tmp.Add(new Segment(s.direction, c));
            }
        }
        else
        {
            if (stepsUpStart > 0) tmp.Add(new Segment(Dir.Up, stepsUpStart));
            if (stepsRight > 0) tmp.Add(new Segment(Dir.Right, stepsRight));
            if (stepsDown > 0) tmp.Add(new Segment(Dir.Down, stepsDown));
            if (stepsLeft > 0) tmp.Add(new Segment(Dir.Left, stepsLeft));
            if (stepsUpEnd > 0) tmp.Add(new Segment(Dir.Up, stepsUpEnd));
        }

        segments = tmp.Count > 0 ? tmp.ToArray() : new Segment[] { new Segment(Dir.Up, 0) };
    }

    private void ClampIndices()
    {
        segIndex = Mathf.Clamp(segIndex, 0, Mathf.Max(0, segments.Length - 1));
        segStepTaken = Mathf.Clamp(
            segStepTaken,
            0,
            (segments.Length > 0 ? Mathf.Max(0, segments[segIndex].count - 1) : 0)
        );
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
        {
            Debug.Log($"[EnemyController] Spawned at grid {gridCoords} → world {worldPos}");
        }
    }

    private void CheckPlayerCollision()
    {
        Vector3Int playerGridPos = gridTilemap.WorldToCell(player.transform.position);
        Vector3Int enemyGridPos = gridTilemap.WorldToCell(M.position);

        if (playerGridPos == enemyGridPos && !player.IsInvulnerable())
        {
            player.TakeDamage(damageAmount);

            if (debugLogs)
            {
                Debug.Log($"[EnemyController] Player and enemy both at grid {playerGridPos}, dealt {damageAmount} damage");
            }
        }
    }

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

        if (segments == null || segments.Length == 0) return;

        int guard = 0;
        while (segments[segIndex].count == 0 && guard++ < 16)
            AdvanceSegment();

        var seg = segments[segIndex];
        Vector2 dir = DirToVector(seg.d);

        HandleSpriteFlipping(seg.d);

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
            Debug.Log($"[EnemyController] Moving {seg.d} ({segStepTaken + 1}/{seg.count}) → {to}");

        MoveTo(to);

        segStepTaken++;
        if (segStepTaken >= seg.count)
        {
            segStepTaken = 0;
            AdvanceSegment();
        }
    }

    private void LogPotentialCollisions(Vector2 from, Vector2 to)
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

                Debug.Log($"[EnemyController] Would collide with '{h.name}' at {to}");
            }
        }

        if (logTilemapCollisions && collisionTilemap != null)
        {
            Vector3Int cell = collisionTilemap.WorldToCell(to);
            //if (collisionTilemap.HasTile(cell))
            //    Debug.Log($"[EnemyController] Would collide with Tilemap at cell {cell} (world {to})");
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

    private void AdvanceSegment()
    {
        segIndex++;
        if (segIndex >= segments.Length) segIndex = 0;
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
}
