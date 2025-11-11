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
    [SerializeField] private bool smoothStep = true;
    [Tooltip("How long the glide lasts, as a fraction of one interval.")]
    [SerializeField, Range(0.05f, 0.9f)] private float moveLerpFractionOfInterval = 0.35f;
    [Tooltip("Reference to BPMController to get current BPM for accurate timing")]
    [SerializeField] private BPMController bpmController;
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
    [Header("Animation Settings")]
    [Tooltip("Name of the move trigger parameter in the animator (leave empty to disable move animations)")]
    [SerializeField] private string moveTriggerName = "Move";
    [Tooltip("Whether to play move animations when the enemy moves")]
    [SerializeField] private bool useMoveAnimations = true;
    [Tooltip("Wait for move animation to complete before starting movement. If false, animation and movement run simultaneously.")]
    [SerializeField] private bool waitForMoveAnimation = false;
    [Tooltip("Use animation events to control movement timing")]
    [SerializeField] private bool useAnimationEvents = false;
    [Header("Dynamic BPM Settings")]
    [Tooltip("If true, recalculate movement duration when BPM changes during movement")]
    [SerializeField] private bool adaptToBPMChanges = true;

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
    private Vector2 logicalPosition;      // Track the intended grid position separately from visual position

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

        // Auto-find BPMController if not assigned
        if (bpmController == null)
            bpmController = FindFirstObjectByType<BPMController>();

        // Snap initial spawn to the center of the current grid cell (if tilemap present).
        if (gridTilemap)
        {
            Vector3Int cellPosition = gridTilemap.WorldToCell(transform.position);
            Vector3 centeredWorldPos = gridTilemap.GetCellCenterWorld(cellPosition);
            transform.position = centeredWorldPos;
            
            // Also update the moveRoot position if it's different from transform
            if (moveRoot != transform)
                moveRoot.position = centeredWorldPos;
                
            // Initialize logical position
            logicalPosition = centeredWorldPos;
        }
        else
        {
            // Initialize logical position to current position if no tilemap
            logicalPosition = M.position;
        }

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
    /// Damages the player if both share the same tile cell (simple grid-based contact).
    /// </summary>
    private void CheckPlayerCollision()
    {
        // Don't damage if dying
        if (isDying) return;

        Vector3Int playerGridPos = gridTilemap.WorldToCell(player.transform.position);
        Vector3Int enemyGridPos = gridTilemap.WorldToCell(logicalPosition);

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
    /// Performs one move "tick": advances along the current Step, handles snapping and visual flipping,
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

        // If we're in reverse mode and traversing backward, use the opposite direction to retrace.
        var effectiveDir = (reverseMovement && !movingForward)
            ? Opposite(current.direction)
            : current.direction;

        Vector2 dir = DirToVector(effectiveDir);

        // Flip sprite visuals according to the EFFECTIVE direction.
        HandleSpriteFlipping(effectiveDir);

        // Use logical position (intended position) instead of current animated position
        Vector2 from = logicalPosition;
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

        // Update logical position to the target position
        logicalPosition = to;

        // Debug-only "what would we hit?" logging.
        LogPotentialCollisions(from, to);

        if (debugLogs)
            Debug.Log($"[EnemyScript] Moving {current.direction} ({stepsTakenInCurrent + 1}/{current.count}) from {from} → {to} (distance: {Vector2.Distance(from, to)})");

        // Move (instant or smooth) - pass the original starting position
        MoveTo(from, to);

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
    private void MoveTo(Vector2 fromPos, Vector2 nextPos)
    {
        if (useAnimationEvents)
        {
            // Store movement data for animation event callback
            pendingMovement = new PendingMovement { from = fromPos, to = nextPos };
            
            // Start animation and wait for animation event to trigger actual movement
            StartCoroutine(MoveAnimation());
        }
        else
        {
            // Original behavior: start animation and movement together
            StartCoroutine(MoveAnimation());
            
            if (!smoothStep)
            {
                if (rb2d) rb2d.MovePosition(nextPos);
                else M.position = nextPos;
                // Ensure logical position is updated for instant movement
                logicalPosition = nextPos;
                return;
            }

            // Stop any existing movement coroutine first
            if (moveCo != null) 
            {
                StopCoroutine(moveCo);
                moveCo = null;
            }

            float dur = CalcLerpDuration();
            
            if (debugLogs)
                Debug.Log($"[EnemyScript] Starting smooth move from {fromPos} to {nextPos}, duration: {dur}");
                
            moveCo = StartCoroutine(StepLerp2D(rb2d, fromPos, nextPos, dur));
        }
    }
    
    // Data structure for pending movement
    private struct PendingMovement
    {
        public Vector2 from;
        public Vector2 to;
    }
    private PendingMovement? pendingMovement = null;
    
    /// <summary>Called by animation event to trigger the actual movement</summary>
    public void OnMoveAnimationEvent()
    {
        if (pendingMovement.HasValue)
        {
            var movement = pendingMovement.Value;
            pendingMovement = null;
            
            if (debugLogs)
                Debug.Log($"[EnemyScript] Animation event triggered movement from {movement.from} to {movement.to}");
            
            if (!smoothStep)
            {
                if (rb2d) rb2d.MovePosition(movement.to);
                else M.position = movement.to;
                logicalPosition = movement.to;
                return;
            }

            // Stop any existing movement coroutine first
            if (moveCo != null) 
            {
                StopCoroutine(moveCo);
                moveCo = null;
            }

            float dur = CalcLerpDuration();
            moveCo = StartCoroutine(StepLerp2D(rb2d, movement.from, movement.to, dur));
        }
    }

    /// <summary>
    /// Computes smoothing duration from BPM and fraction-of-interval settings.
    /// Uses current BPM from BPMController if available, falls back to default.
    /// </summary>
    private float CalcLerpDuration()
    {
        // Get current BPM dynamically if BPMController is assigned
        int currentBPM = bpmController != null ? bpmController.GetCurrentBPM() : 120;
        
        float intervalSec = (60f / Mathf.Max(1, currentBPM)) * Mathf.Max(0.0001f, intervalBeatsForSmoothing);
        return Mathf.Max(0.01f, intervalSec * moveLerpFractionOfInterval);
    }

    /// <summary>
    /// Smoothly interpolates position from 'from' to 'to' over 'duration'.
    /// Uses FixedUpdate timing for Rigidbody2D; otherwise uses regular Update.
    /// Can adapt to BPM changes during movement if enabled.
    /// </summary>
    private IEnumerator StepLerp2D(Rigidbody2D body, Vector2 from, Vector2 to, float duration)
    {
        if (debugLogs)
            Debug.Log($"[EnemyScript] StepLerp2D started: from={from}, to={to}, duration={duration}");

        if (duration <= 0f)
        {
            if (debugLogs)
                Debug.Log($"[EnemyScript] Duration <= 0, teleporting to {to}");
            if (body) body.MovePosition(to);
            else M.position = to;
            yield break;
        }

        float t = 0f;
        float originalDuration = duration;
        float startTime = Time.time;

        if (body)
        {
            while (t < 1f)
            {
                // Adapt to BPM changes during movement if enabled
                if (adaptToBPMChanges)
                {
                    float newDuration = CalcLerpDuration();
                    if (Mathf.Abs(newDuration - originalDuration) > 0.01f)
                    {
                        // Recalculate t based on elapsed time and new duration
                        float elapsed = Time.time - startTime;
                        t = elapsed / newDuration;
                        originalDuration = newDuration;
                        
                        if (debugLogs)
                            Debug.Log($"[EnemyScript] BPM changed, adjusted duration to {newDuration}");
                    }
                    else
                    {
                        t += Time.fixedDeltaTime / duration;
                    }
                }
                else
                {
                    t += Time.fixedDeltaTime / duration;
                }
                
                Vector2 currentPos = Vector2.Lerp(from, to, Mathf.Clamp01(t));
                body.MovePosition(currentPos);
                yield return new WaitForFixedUpdate();
            }
            // Ensure we end exactly at the target position
            body.MovePosition(to);
        }
        else
        {
            while (t < 1f)
            {
                // Adapt to BPM changes during movement if enabled
                if (adaptToBPMChanges)
                {
                    float newDuration = CalcLerpDuration();
                    if (Mathf.Abs(newDuration - originalDuration) > 0.01f)
                    {
                        // Recalculate t based on elapsed time and new duration
                        float elapsed = Time.time - startTime;
                        t = elapsed / newDuration;
                        originalDuration = newDuration;
                        
                        if (debugLogs)
                            Debug.Log($"[EnemyScript] BPM changed, adjusted duration to {newDuration}");
                    }
                    else
                    {
                        t += Time.deltaTime / duration;
                    }
                }
                else
                {
                    t += Time.deltaTime / duration;
                }
                
                Vector2 currentPos = Vector2.Lerp(from, to, Mathf.Clamp01(t));
                M.position = currentPos;
                yield return null;
            }
            // Ensure we end exactly at the target position
            M.position = to;
        }

        if (debugLogs)
            Debug.Log($"[EnemyScript] StepLerp2D completed, final position: {(body ? body.position : (Vector2)M.position)}");
            
        moveCo = null;
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

    private IEnumerator MoveAnimation()
    {
        // Only trigger move animation if enabled and all conditions are met
        if (useMoveAnimations && animator != null && !string.IsNullOrEmpty(moveTriggerName))
        {
            // Check if the parameter exists before trying to set it
            if (HasParameter(animator, moveTriggerName))
            {
                animator.SetTrigger(moveTriggerName);
                
                if (waitForMoveAnimation)
                {
                    // Wait until the animation state is no longer the move animation
                    yield return new WaitUntil(() => !IsPlayingMoveAnimation());
                }
                else
                {
                    // Don't wait, just trigger and continue immediately
                    yield return null;
                }
            }
            else if (debugLogs)
            {
                Debug.LogWarning($"[EnemyScript] Animator parameter '{moveTriggerName}' not found on {gameObject.name}");
                yield return null;
            }
        }
        else
        {
            // If not using move animations, yield immediately
            yield return null;
        }
    }
    
    /// <summary>Check if the move animation is currently playing</summary>
    private bool IsPlayingMoveAnimation()
    {
        if (animator == null) return false;
        
        AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(0);
        
        // Check if we're in a move-related animation state
        // You'll need to adjust these state names to match your animator
        return stateInfo.IsName("Move") || 
               stateInfo.IsName("Walk") || 
               stateInfo.IsName("Step") ||
               stateInfo.IsTag("Moving"); // Alternative: use tags instead of state names
    }

    /// <summary>Helper method to check if animator has a specific parameter</summary>
    private bool HasParameter(Animator anim, string parameterName)
    {
        if (anim == null) return false;
        
        foreach (AnimatorControllerParameter parameter in anim.parameters)
        {
            if (parameter.name == parameterName)
                return true;
        }
        return false;
    }

    /// <summary>Flips the sprite when moving left/right (keeps flip state for up/down).</summary>
    private void HandleSpriteFlipping(Dir direction)
    {
        if (!spriteRenderer) return;

        switch (direction)
        {
            case Dir.Right:  spriteRenderer.flipX = true;  break;
            case Dir.Left: spriteRenderer.flipX = false; break;
            // Up and Down keep current flip
        }
    }
}
