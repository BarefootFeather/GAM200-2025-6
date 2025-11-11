/*
* Copyright (c) 2025 Infernumb. All rights reserved.
*
* This code is part of the hellmaker prototype project.
* Unauthorized copying of this file, via any medium, is strictly prohibited.
* Proprietary and confidential.
*
* Author:
*   - Lin Xin
*   - Contribution Level (100%)
* @file
* @brief
*   Beat-synced, grid-aligned enemy that follows a looping path of steps (direction + count).
*   - Moves on your beat/interval (hook OnIntervalReached).
*   - Supports optional grid snap (Tilemap) and smooth gliding between cells.
*   - Optional ping-pong (reverseMovement) retraces the path exactly.
*   - Logs prospective collisions (physics/tilemap) for debugging.
*   - Can damage the player when sharing the same grid cell.
*/

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

    [Header("Movement Options")]
    [SerializeField] private bool reverseMovement = false; // When true, ping-pong (retraces exactly).
    
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

    AudioManager audioManager;

    private bool isDying = false;                 // Prevents re-triggering death.
    private bool deathAnimationComplete = false;  // Set by animation event.

    // Ping-pong state for reverseMovement
    private bool movingForward = true; // Only used when reverseMovement == true

    // ---------------- Internals ----------------
    private int intervalCounter = -1;     // Counts beats that have passed (used for scheduling).
    private Coroutine moveCo;             // Active smooth movement coroutine (if any).
    private Rigidbody2D rb2d;             // Optional physics movement for smoother collisions.
    private BoxCollider2D cachedBox;      // For size auto-detection in OverlapBox logging.

    // Helper to choose which transform actually moves.
    private Transform M => moveRoot != null ? moveRoot : transform;

    // Directions used by the path authoring.
    public enum Dir { Up, Right, Down, Left }

    [System.Serializable]
    public struct Step
    {
        public Dir direction;     // Which way to move.
        [Min(0)] public int count; // How many steps to take in this direction.
        public Step(Dir d, int c) { direction = d; count = c; }
    }

    // Runtime counters for walking through the path list.
    private int pathIndex = 0;          // Which Step we are on.
    private int stepsTakenInCurrent = 0; // How many sub-steps completed in current Step.

    private void Awake()
    {
        if (moveRoot == null) moveRoot = transform;

        rb2d = moveRoot.GetComponent<Rigidbody2D>();
        cachedBox = moveRoot.GetComponent<BoxCollider2D>();

        // Snap initial spawn to the center of the current grid cell (if tilemap present).
        Vector3Int cellPosition = gridTilemap.WorldToCell(transform.position);
        Vector3 centeredWorldPos = gridTilemap.GetCellCenterWorld(cellPosition);
        transform.position = centeredWorldPos;

        // Clean up the path (clamp counts, keep indices valid).
        SanitizePath();
    }

    private static Dir Opposite(Dir d)
    {
        // Utility to invert direction (used for reverseMovement retracing).
        switch (d)
        {
            case Dir.Up:    return Dir.Down;
            case Dir.Right: return Dir.Left;
            case Dir.Down:  return Dir.Up;
            default:        return Dir.Right; // Left → Right
        }
    }

    private void OnValidate()
    {
        // Keep inspector values in a safe range and indices consistent in edit-time.
        moveEveryNIntervals = Mathf.Max(1, moveEveryNIntervals);
        SanitizePath();
    }

    private void Update()
    {
        // Damage player when occupying the same grid cell.
        if (canDamagePlayer && player != null && gridTilemap != null)
            CheckPlayerCollision();
    }

    /// <summary>
    /// Ensure path counts are non-negative and indices are clamped.
    /// Avoids edge cases if author leaves empty slots or zero counts.
    /// </summary>
    private void SanitizePath()
    {
        if (path == null) path = new List<Step>();
        for (int i = 0; i < path.Count; ++i)
            path[i] = new Step(path[i].direction, Mathf.Max(0, path[i].count));

        if (path.Count == 0)
        {
            // Stay idle if no steps are authored.
            pathIndex = 0;
            stepsTakenInCurrent = 0;
        }
        else
        {
            pathIndex %= Mathf.Max(1, path.Count);
            stepsTakenInCurrent = Mathf.Clamp(stepsTakenInCurrent, 0, path[pathIndex].count);
        }
    }

    /// <summary>
    /// Spawn helper (unused by default—kept for explicit spawn-at-grid use cases).
    /// </summary>
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

    /// <summary>
    /// Damages the player if both share the same tile cell (simple grid-based contact).
    /// </summary>
    private void CheckPlayerCollision()
    {
        Vector3Int playerGridPos = gridTilemap.WorldToCell(player.transform.position);
        Vector3Int enemyGridPos = gridTilemap.WorldToCell(M.position);

        if (playerGridPos == enemyGridPos && (player.isShieldActive || !player.IsInvulnerable()))
        {
            player.TakeDamage(damageAmount);
            if (debugLogs)
                Debug.Log($"[EnemyScript] Player and enemy both at grid {playerGridPos}, dealt {damageAmount} damage");
        }
    }

    /// <summary>Call this from your beat/interval system (e.g., BeatBus) each interval.</summary>
    public void OnIntervalReached()
    {
        intervalCounter++;
        // Only move when our schedule says so (every N beats with an optional offset).
        if (IsScheduled(intervalCounter, moveEveryNIntervals, moveOffset))
            DoMoveTick();
    }

    private static bool IsScheduled(int count, int everyN, int offset) => ((count - offset) % everyN) == 0;

    /// <summary>
    /// Performs one move “tick”: advances along the current Step, handles snapping and visual flipping,
    /// logs potential collisions (for debugging), and handles path advancement / ping-pong logic.
    /// </summary>
    private void DoMoveTick()
    {
        if (isDying) return;
        if (path == null || path.Count == 0) return;

        // Skip zero-count steps (safety for authors).
        int guard = 0;
        while (path[pathIndex].count == 0 && guard++ < 16)
            AdvancePath();

        var current = path[pathIndex];

        // If we’re in reverse mode and traversing backward, use the opposite direction to retrace.
        var effectiveDir = (reverseMovement && !movingForward)
            ? Opposite(current.direction)
            : current.direction;

        Vector2 dir = DirToVector(effectiveDir);

        // Flip sprite visuals according to the EFFECTIVE direction.
        HandleSpriteFlipping(effectiveDir);

        // Compute target position for this single step.
        Vector2 from = M.position;
        Vector2 to = from + dir * stepDistance;

        // Snap to tile center if tilemap exists; else optional snap by cell size.
        if (gridTilemap)
        {
            Vector3Int gridPos = gridTilemap.WorldToCell(to);
            to = gridTilemap.GetCellCenterWorld(gridPos);
        }
        else if (snapToGrid)
        {
            to = Snap(to, stepDistance);
        }

        // Debug-only “what would we hit?” logging.
        LogPotentialCollisions(from, to);

        if (debugLogs)
            Debug.Log($"[EnemyScript] Moving {current.direction} ({stepsTakenInCurrent + 1}/{current.count}) → {to}");

        // Move (instant or smooth).
        MoveTo(to);

        // Track sub-steps and advance to next step entry when count is reached.
        stepsTakenInCurrent++;
        if (stepsTakenInCurrent >= current.count)
        {
            stepsTakenInCurrent = 0;
            AdvancePath();
        }
    }

    /// <summary>
    /// Advances the path index, supporting simple loop or ping-pong back-and-forth retracing.
    /// In reverse mode: when you hit an end, flip direction but stay on the same segment
    /// so you retrace it before moving to the next.
    /// </summary>
    private void AdvancePath()
    {
        if (path == null || path.Count == 0) return;

        if (!reverseMovement)
        {
            // Simple loop around.
            pathIndex = (pathIndex + 1) % path.Count;
            return;
        }

        // Ping-pong retrace mode
        if (movingForward)
        {
            if (pathIndex >= path.Count - 1)
            {
                movingForward = false; // flip at end
                // stay on same segment to retrace
            }
            else
            {
                pathIndex++;
            }
        }
        else
        {
            if (pathIndex <= 0)
            {
                movingForward = true; // flip at start
                // stay on same segment to replay forward
            }
            else
            {
                pathIndex--;
            }
        }
    }

    /// <summary>
    /// Logs potential physics/tile collisions at the destination (debug assist only).
    /// </summary>
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

    /// <summary>
    /// Applies a move: instant or smooth glide based on smoothStep.
    /// Uses Rigidbody2D when available for fixed-timestep lerp.
    /// </summary>
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

    /// <summary>
    /// Computes smoothing duration from BPM and fraction-of-interval settings.
    /// </summary>
    private float CalcLerpDuration()
    {
        float intervalSec = (60f / Mathf.Max(1, bpmForSmoothing)) * Mathf.Max(0.0001f, intervalBeatsForSmoothing);
        return Mathf.Max(0.01f, intervalSec * moveLerpFractionOfInterval);
    }

    /// <summary>
    /// Smoothly interpolates position from 'from' to 'to' over 'duration'.
    /// Uses FixedUpdate timing for Rigidbody2D; otherwise uses regular Update.
    /// </summary>
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
        // Converts enum direction into a Vector2 step.
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
        // Rounds a position to the nearest cell multiple (used when no Tilemap center is available).
        if (cell <= 0f) return pos;
        float x = Mathf.Round(pos.x / cell) * cell;
        float y = Mathf.Round(pos.y / cell) * cell;
        return new Vector2(x, y);
    }

    private void OnDrawGizmosSelected()
    {
        // Draws a wire box at the destination for visual debugging.
        Vector2 size = useColliderSize && cachedBox != null ? cachedBox.size : boxCheckSize;
        var mr = moveRoot ? moveRoot : transform;

        Gizmos.color = new Color(1f, 0.6f, 0.2f, 0.35f);
        Matrix4x4 old = Gizmos.matrix;
        Gizmos.matrix = Matrix4x4.TRS(mr.position, mr.rotation, Vector3.one);
        Gizmos.DrawWireCube(Vector3.zero, new Vector3(size.x, size.y, 0.01f));
        Gizmos.matrix = old;
    }

    // ===== Health / Death / Visuals =====

    /// <summary>Deals 1 damage; triggers death sequence when health <= 0.</summary>
    public void TakeDamage()
    {
        if (isDying) return;

        enemyHealth -= 1;
        if (enemyHealth <= 0)
        {
            isDying = true;
            StartCoroutine(DeathSequence());
            audioManager.PlaySFX(audioManager.enemydeath);
        }
    }

    /// <summary>Animation event hook to signal the death animation finished.</summary>
    public void OnDeathAnimationComplete() => deathAnimationComplete = true;

    /// <summary>Plays the death animation and disables the GameObject afterward.</summary>
    private IEnumerator DeathSequence()
    {
        if (animator) animator.SetTrigger("Death");
        yield return new WaitUntil(() => deathAnimationComplete);
        gameObject.SetActive(false);
    }

    /// <summary>Flips the sprite when moving left/right (keeps flip state for up/down).</summary>
    private void HandleSpriteFlipping(Dir direction)
    {
        if (!spriteRenderer) return;

        switch (direction)
        {
            case Dir.Left:  spriteRenderer.flipX = true;  break;
            case Dir.Right: spriteRenderer.flipX = false; break;
            // Up and Down keep current flip
        }
    }
}
