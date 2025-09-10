using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Grid-style enemy that moves in a repeating Up/Right/Down/Left pattern,
/// stepping only on certain musical intervals (hooked from your BPM system).
/// Movement never gets blocked; if it would hit the target, we just log it.
/// </summary>
public class EnemyController : MonoBehaviour
{
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

    [Header("Target-only collision logging")]
    [Tooltip("We only check if we would hit THIS object. Movement never stops; we just log.")]
    [SerializeField] private Transform target;
    [SerializeField, Tooltip("Approx radius for our sweep/overlap checks.")]
    private float castRadius = 0.3f;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = false;

    // ---------------- Internals ----------------

    private int intervalCounter = -1;   // counts how many beats have passed
    private Coroutine moveCo;           // handle to the glide coroutine
    private Rigidbody2D rb2d;           // optional, if attached to moveRoot
    private Collider2D targetCol;       // cached collider of the target

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
        // if not assigned, move this object directly
        if (moveRoot == null) moveRoot = transform;

        // try to use Rigidbody2D if present (optional)
        rb2d = moveRoot.GetComponent<Rigidbody2D>();

        // cache the collider of the current target (if any)
        ResolveTargetCollider();

        // build the movement pattern from the counts above
        RebuildSegments();

        // make sure our indices are valid
        ClampIndices();
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

    // Find and store the target's Collider2D (if any)
    private void ResolveTargetCollider()
    {
        targetCol = null;
        if (target) targetCol = target.GetComponent<Collider2D>();
    }

    // Allow setting the target at runtime
    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
        ResolveTargetCollider();
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
        if (snapToGrid) to = Snap(to, stepDistance);

        // Check if our move would hit the target; DO NOT block, just log it.
        if (WillHitTarget(from, to))
            Debug.Log("[EnemyController] Collision with target detected this step.");

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

    // Returns true if our movement this step would touch the target's collider
    private bool WillHitTarget(Vector2 from, Vector2 to)
    {
        if (!targetCol) return false;

        Vector2 dir = to - from;
        float dist = dir.magnitude;
        if (dist <= 1e-6f) return false;
        dir /= dist;

        // Preferred: sweep our Rigidbody2D shape forward if we have one
        RaycastHit2D[] results = new RaycastHit2D[8];
        int hits = 0;

        if (rb2d != null) // note: rb2d.simulated can also be checked if needed
        {
            var filter = new ContactFilter2D
            {
                useLayerMask = false, // we'll compare collider instances directly
                useTriggers = true
            };
            hits = rb2d.Cast(dir, filter, results, dist);
        }
        else
        {
            // Fallback: a simple circle cast if we don't have a Rigidbody2D
            RaycastHit2D hit = Physics2D.CircleCast(from, castRadius, dir, dist, ~0);
            if (hit.collider) { results[0] = hit; hits = 1; }
        }

        for (int i = 0; i < hits; i++)
        {
            var col = results[i].collider;
            if (!col) continue;
            if (col == targetCol) return true;
        }

        // Extra safety: also check if we end up overlapping at the destination
        Collider2D overlap = Physics2D.OverlapCircle(to, castRadius, ~0);
        if (overlap && overlap == targetCol) return true;

        return false;
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
        if (!Application.isPlaying) RebuildSegments();
        if (segments == null || segments.Length == 0) return;

        Gizmos.color = new Color(0.2f, 1f, 0.7f, 0.6f);
        Vector3 cur = (moveRoot ? moveRoot : transform).position;

        foreach (var seg in segments)
        {
            Vector2 dir = DirToVector(seg.d);
            for (int s = 0; s < seg.count; s++)
            {
                Vector3 next = cur + (Vector3)(dir * stepDistance);
                Gizmos.DrawLine(cur, next);
                Gizmos.DrawWireSphere(next, 0.05f);
                cur = next;
            }
        }

        // visualize cast radius at current position
        Gizmos.color = new Color(1f, 0.6f, 0.2f, 0.35f);
        Gizmos.DrawWireSphere((moveRoot ? moveRoot : transform).position, castRadius);
    }
}
