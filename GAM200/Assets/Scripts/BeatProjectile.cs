/*
* Copyright (c) 2025 Infernumb. All rights reserved.
*
* This code is part of the hellmaker prototype project.
* Unauthorized copying of this file, via any medium, is strictly prohibited.
* Proprietary and confidential.
*
* Author:
*	- Lin Xin
*	- Contribution Level (100%)
* @file
* @brief
*  Beat-synced projectile that moves one “step” each musical interval (from BeatBus).
*  - Subscribes to BeatBus.Beat and advances exactly once per beat index.
*  - Supports grid snapping to a Tilemap, optional smooth lerp over a fraction of the interval,
*    max step lifetime, and player damage on contact (cell-based collision).
*  - All runtime behavior is configured via the nested Config struct and Initialize(Config).
*/

using System.Collections;
using UnityEngine;
using UnityEngine.Tilemaps;

public class BeatProjectile : MonoBehaviour
{
    /// <summary>
    /// Runtime configuration payload for the projectile.
    /// Supply this via <see cref="Initialize(Config)"/> right after instantiation.
    /// </summary>
    public struct Config
    {
        public Vector2 direction;                     // Movement direction; normalized internally (defaults to +X if zero).
        public float stepDistance;                    // World-units moved per beat (before snapping).
        public bool snapToGrid;                       // If true, final destination snaps to tile center each step.
        public bool smoothStep;                       // If true, lerp movement over a portion of the beat interval.
        public float moveLerpFractionOfInterval;      // 0..1 portion of the interval used for lerping (e.g., 0.35f).
        public int bpmForSmoothing;                   // BPM used to compute interval seconds for smoothing.
        public float intervalBeatsForSmoothing;       // How many beats does one move span (often 1).
        public Tilemap gridTilemap;                   // Tilemap to convert between world ↔ cell and to find cell centers.
        public int maxSteps;                          // Lifetime in steps; destroy when reached.
        public int damage;                            // Damage applied to Player on contact.
        public PlayerController player;               // Player reference for collision & damage.
        public int delayMovementUntilBeat;            // If > -1, don't start moving until this beat index (for half-beat animation)
    }

    [SerializeField] private bool debugLogs = false;  // Optional verbose logs (editor toggle).

    // ----- movement / smoothing state -----
    Vector2 _dir = Vector2.right;            // Current direction (normalized).
    float _step = 1f;                        // Units per beat.
    float _lerpFrac = 0.35f;                 // Fraction of interval used for lerp (if smoothing).
    float _intervalBeatsForSmoothing = 1f;   // Beats per move (for smoothing timing).
    bool _snap;                              // Whether to snap to grid centers.
    bool _smooth;                            // Whether to smooth using coroutine lerp.
    int _bpmForSmoothing = 120;              // BPM used to compute interval seconds.

    // ----- references & damage -----
    Tilemap _tilemap;                        // Grid for snapping & cell math.
    PlayerController _player;                // Player target for collision/damage.
    int _damageToPlayer = 1;                 // Damage applied on hit.

    // ----- step/lifetime state -----
    int _maxSteps = 16;                      // Max steps before auto-destroy.
    int _stepsTaken = 0;                     // Steps performed so far.
    int _lastProcessedBeat = int.MinValue;   // Guard to ensure exactly one StepOnce per beat index.
    Coroutine _moveCo;                       // Active lerp coroutine, if any.
    int _delayMovementUntilBeat = -1;        // If > -1, don't move until this beat index
    bool _movementEnabled = true;            // Whether movement is currently enabled

    AudioManager audioManager;

    /// <summary>
    /// Configure the projectile at runtime. Call immediately after spawning.
    /// Applies clamping and sane defaults; never changes behavior outside the provided values.
    /// </summary>
    public void Initialize(Config c)
    {
        // Pick a valid direction: if zero-length, default to +X to avoid NaNs.
        _dir = c.direction.sqrMagnitude > 0 ? c.direction.normalized : Vector2.right;

        // Defensive clamps for robustness (no zero/negative step or weird lerp fractions).
        _step  = Mathf.Max(0.01f, c.stepDistance);
        _snap  = c.snapToGrid;
        _smooth = c.smoothStep;
        _lerpFrac = Mathf.Clamp(c.moveLerpFractionOfInterval, 0.05f, 0.9f);
        _bpmForSmoothing = Mathf.Max(1, c.bpmForSmoothing);
        _intervalBeatsForSmoothing = Mathf.Max(0.0001f, c.intervalBeatsForSmoothing);

        // External refs and lifetime/damage guards.
        _tilemap = c.gridTilemap;
        _maxSteps = Mathf.Max(1, c.maxSteps);
        _damageToPlayer = Mathf.Max(0, c.damage);
        _player = c.player;
        
        // Handle delayed movement
        _delayMovementUntilBeat = c.delayMovementUntilBeat;
        _movementEnabled = _delayMovementUntilBeat <= -1; // If no delay specified, movement starts immediately
        
        if (debugLogs && _delayMovementUntilBeat > -1)
        {
            Debug.Log($"[BeatProjectile] Movement delayed until beat {_delayMovementUntilBeat}");
        }
    }

    // Subscribe/unsubscribe to beat events when this projectile is enabled/disabled.
    void OnEnable()  
    { 
        BeatBus.Beat += OnBeat;
        BeatBus.OnTimingReset += OnTimingReset;
    }
    
    void OnDisable() 
    { 
        BeatBus.Beat -= OnBeat;
        BeatBus.OnTimingReset -= OnTimingReset;
    }

    /// <summary>
    /// Called every time BeatBus announces a new beat index.
    /// Ensures we react once per unique index (avoids duplicate moves within one frame).
    /// </summary>
    void OnBeat(int beatIndex)
    {
        if (beatIndex == _lastProcessedBeat) return; // Already handled this beat index.
        _lastProcessedBeat = beatIndex;
        
        // Safety mechanism: If we're waiting for delayed movement but the beat index is getting very high
        // compared to our expected delay, something went wrong (likely a timing reset), so enable movement
        if (!_movementEnabled && _delayMovementUntilBeat > -1)
        {
            // If we're way past when we should have started moving, enable movement immediately
            // This handles cases where timing resets break the delay calculation
            bool shouldForceEnable = beatIndex >= _delayMovementUntilBeat + 10; // Allow some tolerance
            
            if (beatIndex >= _delayMovementUntilBeat || shouldForceEnable)
            {
                _movementEnabled = true;
                if (debugLogs)
                {
                    if (shouldForceEnable)
                        Debug.Log($"[BeatProjectile] Force-enabled movement on beat {beatIndex} (was waiting for {_delayMovementUntilBeat}) - likely timing reset recovery");
                    else
                        Debug.Log($"[BeatProjectile] Movement enabled on beat {beatIndex}");
                }
            }
        }
        
        // Only step if movement is enabled
        if (_movementEnabled)
        {
            StepOnce();
        }
        else if (debugLogs)
        {
            Debug.Log($"[BeatProjectile] Beat {beatIndex} - movement still delayed until beat {_delayMovementUntilBeat}");
        }
    }

    /// <summary>
    /// Perform one logical step:
    /// - Lifetime check → destroy if exceeded.
    /// - Cell-based collision with player (current or destination cell).
    /// - Move to next position (snap or raw), optionally smoothed by lerp.
    /// </summary>
    void StepOnce()
    {
        // Lifetime end → self-destruct.
        if (_stepsTaken >= _maxSteps) { Destroy(gameObject); return; }

        // Hard dependencies must exist.
        if (!_tilemap || !_player) return;

        // Don't move yet if waiting for a delayed start.
        if (_delayMovementUntilBeat > -1 && _lastProcessedBeat < _delayMovementUntilBeat) return;

        // Compute “from” and “to” positions in world space.
        Vector2 from = transform.position;
        Vector2 to   = from + _dir * _step;

        // Figure out destination cell & its center for snapping.
        Vector3Int destCell   = _tilemap.WorldToCell(to);
        Vector2 snappedTo     = _tilemap.GetCellCenterWorld(destCell);

        // Legacy fallback snap (kept for parity): if snap is requested but tilemap is null.
        // Note: condition is unreachable because we early-return when !_tilemap above.
        if (_snap && !_tilemap) snappedTo = Snap(to, _step);

        // Cell-level collision check:
        // If we are currently in the same cell as the player OR
        // if our destination cell matches the player's cell, apply damage and destroy.
        Vector3Int currCell   = _tilemap.WorldToCell(transform.position);
        Vector3Int playerCell = _tilemap.WorldToCell(_player.transform.position);

        if ((currCell == playerCell || destCell == playerCell) && (_player.isShieldActive || !_player.IsInvulnerable()))
        {
            _player.TakeDamage(_damageToPlayer);
            if (debugLogs)
                Debug.Log($"[BeatProjectile] Player and projectile both at grid {playerCell}, dealt {_damageToPlayer} damage");
            Destroy(gameObject);
            return;
        }

        // Perform the actual movement (snapped to tile center) with optional smoothing.
        MoveTo(snappedTo);
        _stepsTaken++;
    }

    /// <summary>
    /// Move instantly or smoothly toward the next position.
    /// If smoothing is enabled, lerp over a fraction of the musical interval.
    /// </summary>
    void MoveTo(Vector2 nextPos)
    {
        if (!_smooth) {                      // No smoothing → teleport to position.
            transform.position = nextPos;
            return;
        }

        // Compute the duration from BPM & beats-per-interval (e.g., 120 BPM, 1 beat per step).
        float intervalSec = (60f / _bpmForSmoothing) * _intervalBeatsForSmoothing;
        float dur = Mathf.Max(0.01f, intervalSec * _lerpFrac);

        // Cancel any previous lerp and start a fresh one to avoid overlap.
        if (_moveCo != null) StopCoroutine(_moveCo);
        _moveCo = StartCoroutine(LerpTo(nextPos, dur));
    }

    /// <summary>
    /// Smoothly interpolate position from current to target over the given duration.
    /// Uses unscaled delta time (game time) so it respects timescale.
    /// </summary>
    IEnumerator LerpTo(Vector2 target, float dur)
    {
        Vector2 start = transform.position;
        float t = 0f;

        while (t < 1f)
        {
            t += Time.deltaTime / dur;                                      // Normalized time 0→1 over 'dur' seconds.
            transform.position = Vector2.Lerp(start, target, Mathf.Clamp01(t));
            yield return null;                                              // Continue next frame.
        }
    }

    /// <summary>
    /// Fallback grid snap if we only know a cell size (not using Tilemap centers).
    /// Rounds to the nearest multiple of 'cell'.
    /// </summary>
    static Vector2 Snap(Vector2 p, float cell)
    {
        if (cell <= 0f) return p;
        return new Vector2(Mathf.Round(p.x / cell) * cell,
                           Mathf.Round(p.y / cell) * cell);
    }

    /// <summary>
    /// Called when BeatBus timing system resets (e.g., song loops)
    /// Clean up the projectile to prevent stuck bullets
    /// </summary>
    void OnTimingReset(bool shouldDestroyProjectiles)
    {
        if (debugLogs)
            Debug.Log($"[BeatProjectile] Timing reset detected - shouldDestroy: {shouldDestroyProjectiles}");
            
        // Stop any active movement coroutine
        if (_moveCo != null)
        {
            StopCoroutine(_moveCo);
            _moveCo = null;
        }
        
        if (shouldDestroyProjectiles)
        {
            // Destroy the bullet to prevent stuck projectiles during song loops
            if (debugLogs)
                Debug.Log($"[BeatProjectile] Destroying bullet at {transform.position}");
            Destroy(gameObject);
        }
        else
        {
            // Reset bullet state but keep it alive
            if (debugLogs)
                Debug.Log($"[BeatProjectile] Resetting bullet timing at {transform.position}");
                
            _lastProcessedBeat = int.MinValue;
            _stepsTaken = 0; // Reset step counter for fresh movement
            
            // IMPORTANT: Fix for half-beat animation system
            // If this bullet was waiting for delayed movement, enable movement immediately
            // since the beat counting system has been reset
            if (_delayMovementUntilBeat > -1 && !_movementEnabled)
            {
                if (debugLogs)
                    Debug.Log($"[BeatProjectile] Half-beat bullet was waiting for beat {_delayMovementUntilBeat} - enabling movement immediately due to reset");
                _movementEnabled = true;
                _delayMovementUntilBeat = -1; // Clear the delay since timing is reset
            }
            else
            {
                _movementEnabled = _delayMovementUntilBeat <= -1; // Reset movement state normally
            }
        }
    }
}
