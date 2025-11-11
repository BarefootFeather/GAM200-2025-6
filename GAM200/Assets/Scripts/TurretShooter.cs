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
*   Beat-synced turret that automatically fires BeatProjectile prefabs.
*   - Subscribes to BeatBus and shoots every N beats with an optional offset.
*   - Configures projectile movement, damage, and lifetime via BeatProjectile.Config.
*   - Allows independent control of shoot direction (logic) and prefab rotation (visual).
*   - Auto-finds references to PlayerController and Tilemap if not set.
*/

using System.Collections;
using UnityEngine;
using UnityEngine.Tilemaps;

public class TurretShooter : MonoBehaviour
{
    // Cardinal directions for both movement and visual prefab rotation.
    public enum CardinalDir { Right, Left, Up, Down }

    [Header("Beat Scheduling")]
    [SerializeField] int fireEveryN = 6;  // Fire once every N beats.
    [SerializeField] int fireOffset = 0;  // Offset to desync with other turrets.

    [Header("Projectile")]
    [SerializeField] GameObject projectilePrefab;     // Prefab to instantiate (must have BeatProjectile).
    [SerializeField] Transform muzzle;                // Optional specific spawn point.
    [SerializeField] int projectileDamage = 1;
    [SerializeField] int projectileMaxSteps = 12;
    [SerializeField] float projectileStepDistance = 1f;

    [Header("Direction (Movement)")]
    [SerializeField] CardinalDir shootDirection = CardinalDir.Right; // Which way the projectile travels.

    [Header("Rotation (Visual Prefab)")]
    [SerializeField] CardinalDir prefabRotation = CardinalDir.Right; // How the prefab sprite is rotated visually.

    [Header("Collisions")]
    [SerializeField] PlayerController player; // Target player reference for projectile damage.
    [SerializeField] Tilemap gridTilemap;     // Tilemap used for grid snapping.

    [Header("Timing")]
    [Tooltip("What fraction of the beat duration to use for animation buildup (only used if 'Use Full Beat' is false)")]
    [Header("Animation Speed Sync")]
    [SerializeField] bool autoSyncAnimationSpeed = true;
    [Tooltip("Automatically adjust animation speed to match current BPM")]
    [SerializeField] string fireAnimationStateName = "Fire";
    [Tooltip("Name of the fire animation state in the animator")]
    [SerializeField] float baseAnimationLength = 0.5f;
    [Tooltip("Original length of the fire animation at speed 1.0 (in seconds). Set this once and leave it.")]
    [Header("Animation Timing")]
    [SerializeField] bool useAnimationEvents = false;
    [Tooltip("If true, bullet spawns when animation event is triggered. If false, bullet spawns immediately on beat.")]
    [SerializeField] bool delayBulletSpawn = true;
    [Tooltip("Delay bullet spawn by this fraction of beat duration to sync with animation")]
    [SerializeField, Range(0.0f, 0.9f)] float bulletSpawnDelay = 0.3f;
    [Header("Half-Beat Animation System")]
    [SerializeField] bool useHalfBeatAnimation = true;
    [Tooltip("Animation starts 0.5 beats before bullet fires, ends 0.5 beats after (total: 1 beat duration)")]

    [Header("Half-Beat Animation Validation")]
    [SerializeField] bool validateAnimationTiming = true;
    [Tooltip("Show detailed debug info about animation timing calculations")]

    [Header("Debug")]
    [SerializeField] bool debugLogs = false;

    [SerializeField] private Animator animator;

    int _beatCount = -1; // Tracks current beat index.
    private BPMController bpmController; // Cached reference
    private bool pendingFire = false; // Track if we're waiting to fire
    private Coroutine fireDelayCoroutine; // For delayed firing

    void Awake()
    {
        // Auto-assign references if not manually set.
        if (!player) player = FindOne<PlayerController>();
        if (!gridTilemap) gridTilemap = FindOne<Tilemap>();
        
        // Cache BPM controller reference
        bpmController = FindFirstObjectByType<BPMController>();
        
        // Auto-calculate base animation length if not set and auto-sync is enabled
        if (autoSyncAnimationSpeed && baseAnimationLength <= 0f)
        {
            CalculateBaseAnimationLength();
        }
    }

    // Subscribe/unsubscribe to beat events when enabled/disabled.
    void OnEnable() 
    { 
        BeatBus.Beat += OnBeat;
        BeatBus.AnimationTrigger += OnAnimationTrigger;
        BeatBus.OnTimingReset += OnTimingSystemReset;
        
        if (debugLogs)
            Debug.Log($"[TurretShooter] Subscribed to BeatBus events for turret with timing {fireEveryN}/{fireOffset}");
    }
    
    void OnDisable() 
    { 
        BeatBus.Beat -= OnBeat;
        BeatBus.AnimationTrigger -= OnAnimationTrigger;
        BeatBus.OnTimingReset -= OnTimingSystemReset;
        
        if (debugLogs)
            Debug.Log($"[TurretShooter] Unsubscribed from BeatBus events");
    }

    /// <summary>
    /// Called every beat from BeatBus. Checks if this beat is a scheduled fire tick.
    /// </summary>
    void OnBeat(int beatIndex)
    {
        _beatCount = beatIndex;
        
        // Check if THIS beat is a fire beat
        if ((_beatCount - fireOffset) % Mathf.Max(1, fireEveryN) == 0)
        {
            if (useHalfBeatAnimation)
            {
                // In half-beat mode: spawn bullet on current beat, but don't move yet
                // Animation will start 0.5 beats before the NEXT action beat
                SpawnProjectile();
                
                if (debugLogs)
                    Debug.Log($"[TurretShooter] Bullet spawned on beat {_beatCount} (half-beat animation mode) - will move on next action beat ({_beatCount + fireEveryN})");
            }
            else if (delayBulletSpawn)
            {
                // Start delayed firing sequence
                StartDelayedFire();
            }
            else
            {
                // Fire immediately (old behavior)
                SpawnProjectile();
                
                if (debugLogs)
                    Debug.Log($"[TurretShooter] Bullet fired immediately on beat {_beatCount}!");
            }
        }
    }

    /// <summary>
    /// Start a delayed firing sequence to sync with animation
    /// </summary>
    void StartDelayedFire()
    {
        if (fireDelayCoroutine != null)
        {
            StopCoroutine(fireDelayCoroutine);
        }
        
        fireDelayCoroutine = StartCoroutine(DelayedFireSequence());
    }

    /// <summary>
    /// Coroutine that waits for animation timing then fires bullet
    /// </summary>
    IEnumerator DelayedFireSequence()
    {
        if (debugLogs)
            Debug.Log($"[TurretShooter] Starting delayed fire sequence on beat {_beatCount}");

        // Start animation immediately
        StartFireAnimation();
        
        // Wait for the animation delay
        float beatDuration = GetBeatDuration();
        float delay = beatDuration * bulletSpawnDelay;
        
        if (debugLogs)
            Debug.Log($"[TurretShooter] Waiting {delay:F3}s before firing bullet");
            
        yield return new WaitForSeconds(delay);
        
        // Now fire the bullet
        SpawnProjectile();
        
        if (debugLogs)
            Debug.Log($"[TurretShooter] Bullet fired after {delay:F3}s delay!");
            
        fireDelayCoroutine = null;
    }

    /// <summary>
    /// Called by animation events to trigger bullet spawn (alternative to delayed firing)
    /// </summary>
    public void OnFireAnimationEvent()
    {
        if (useAnimationEvents && pendingFire)
        {
            SpawnProjectile();
            pendingFire = false;
            
            if (debugLogs)
                Debug.Log($"[TurretShooter] Bullet fired by animation event!");
        }
    }

    /// <summary>
    /// Called by BeatBus when an animation should trigger based on BPMController's animation timing
    /// </summary>
    void OnAnimationTrigger(int currentBeat, int triggerFireEveryN, int triggerFireOffset, float triggerBeatsBeforeAction)
    {
        if (debugLogs)
            Debug.Log($"[TurretShooter] OnAnimationTrigger received: currentBeat={currentBeat}, triggerFireEveryN={triggerFireEveryN}, triggerFireOffset={triggerFireOffset}, beatsBeforeAction={triggerBeatsBeforeAction}, myFireEveryN={fireEveryN}, myFireOffset={fireOffset}");
            
        // Check if this animation trigger is meant for this turret
        if (triggerFireEveryN == fireEveryN && triggerFireOffset == fireOffset)
        {
            // This animation trigger matches our turret's timing configuration
            if (useHalfBeatAnimation)
            {
                StartHalfBeatAnimation(triggerBeatsBeforeAction);
            }
            else
            {
                StartFireAnimation();
            }
            
            // If using animation events, mark that we're pending a fire
            if (useAnimationEvents)
            {
                pendingFire = true;
            }
            
            if (debugLogs)
                Debug.Log($"[TurretShooter] Animation triggered and matched! UseHalfBeat={useHalfBeatAnimation}, UseAnimEvents={useAnimationEvents}");
        }
        else if (debugLogs)
        {
            Debug.Log($"[TurretShooter] Animation trigger didn't match our timing configuration.");
        }
    }

    /// <summary>
    /// Starts half-beat animation system - animation spans 1 full beat with bullet firing at midpoint
    /// </summary>
    void StartHalfBeatAnimation(float beatsBeforeAction)
    {
        if (autoSyncAnimationSpeed)
        {
            SyncHalfBeatAnimationSpeed();
        }

        // Start fire animation
        if (animator != null)
        {
            animator.SetTrigger("Fire");
        }
        
        if (debugLogs)
            Debug.Log($"[TurretShooter] Half-beat fire animation started, spans 1 full beat with bullet firing at midpoint (triggered {beatsBeforeAction:F2} beats before action)");
    }

    /// <summary>
    /// Syncs animation speed for half-beat system - animation spans exactly 1 beat with bullet at midpoint
    /// </summary>
    void SyncHalfBeatAnimationSpeed()
    {
        if (animator == null) return;

        float currentBeatDuration = GetBeatDuration();
        
        // For half-beat animation system:
        // - Animation starts 0.5 beats before bullet fires
        // - Animation ends 0.5 beats after bullet fires  
        // - Total animation duration = exactly 1 beat
        // - Bullet fires at animation midpoint (50% through the animation)
        
        float targetAnimationDuration = currentBeatDuration; // Exactly 1 beat
        float targetSpeed = baseAnimationLength / targetAnimationDuration;
        
        // Validation: Check if timing makes sense
        if (validateAnimationTiming || debugLogs)
        {
            float animationMidpointTime = targetAnimationDuration * 0.5f; // When bullet should fire within animation
            float halfBeatDuration = currentBeatDuration * 0.5f; // 0.5 beats in seconds
            
            if (validateAnimationTiming)
            {
                Debug.Log($"[TurretShooter] Half-Beat Animation Validation:");
                Debug.Log($"  • Current BPM: {bpmController?.GetCurrentBPM() ?? 120}");
                Debug.Log($"  • Beat Duration: {currentBeatDuration:F3}s");
                Debug.Log($"  • Half-Beat Duration: {halfBeatDuration:F3}s");
                Debug.Log($"  • Base Animation Length: {baseAnimationLength:F3}s");
                Debug.Log($"  • Target Animation Duration: {targetAnimationDuration:F3}s (1 beat)");
                Debug.Log($"  • Animation Speed Multiplier: {targetSpeed:F2}x");
                Debug.Log($"  • Bullet fires at: {animationMidpointTime:F3}s (animation midpoint)");
                Debug.Log($"  • Timeline: Animation starts 0.5 beats early, bullet fires on beat, animation ends 0.5 beats later");
            }
            
            // Warning if speed is extreme
            if (targetSpeed < 0.1f || targetSpeed > 10f)
            {
                Debug.LogWarning($"[TurretShooter] Extreme animation speed detected ({targetSpeed:F2}x)! Check baseAnimationLength ({baseAnimationLength:F3}s) vs beat duration ({currentBeatDuration:F3}s)");
            }
        }
        
        // Apply the speed to the animator
        animator.speed = targetSpeed;
        
        if (debugLogs)
        {
            int currentBPM = bpmController?.GetCurrentBPM() ?? 120;
            Debug.Log($"[TurretShooter] Half-beat animation speed: BPM={currentBPM}, BeatDuration={currentBeatDuration:F3}s, BaseAnimLength={baseAnimationLength:F3}s, TargetAnimDuration={targetAnimationDuration:F3}s, NewSpeed={targetSpeed:F2}x");
            Debug.Log($"[TurretShooter] Animation timing: Starts 0.5 beats before bullet, ends 0.5 beats after bullet, bullet fires at animation midpoint");
        }
    }

    /// <summary>
    /// Starts the fire animation with BPM-synced speed
    /// </summary>
    void StartFireAnimation()
    {
        if (autoSyncAnimationSpeed)
        {
            SyncAnimationSpeedToBPM();
        }

        // Start fire animation
        if (animator != null)
        {
            animator.SetTrigger("Fire");
        }
        
        if (debugLogs)
            Debug.Log($"[TurretShooter] Fire animation started");
    }

    /// <summary>
    /// Automatically adjusts animation speed to match current BPM
    /// </summary>
    void SyncAnimationSpeedToBPM()
    {
        if (animator == null) return;

        float currentBeatDuration = GetBeatDuration();
        
        // Calculate the speed multiplier needed to make animation fit exactly one beat
        // Speed = Original Length / Desired Length
        float targetSpeed = baseAnimationLength / currentBeatDuration;
        
        // Apply the speed to the animator
        animator.speed = targetSpeed;
        
        if (debugLogs)
        {
            int currentBPM = bpmController?.GetCurrentBPM() ?? 120;
            Debug.Log($"[TurretShooter] Animation speed sync: BPM={currentBPM}, BeatDuration={currentBeatDuration:F3}s, BaseAnimLength={baseAnimationLength:F3}s, NewSpeed={targetSpeed:F2}x");
        }
    }

    /// <summary>
    /// Auto-calculates the base animation length from the animator (call this once to set baseAnimationLength)
    /// </summary>
    [ContextMenu("Calculate Base Animation Length")]
    void CalculateBaseAnimationLength()
    {
        if (animator == null)
        {
            Debug.LogWarning("[TurretShooter] No animator assigned!");
            return;
        }

        // Find the animation clip
        AnimationClip fireClip = GetAnimationClip(fireAnimationStateName);
        if (fireClip != null)
        {
            baseAnimationLength = fireClip.length;
            Debug.Log($"[TurretShooter] Base animation length calculated: {baseAnimationLength:F3}s for '{fireAnimationStateName}'");
            
            // Validate the length makes sense
            if (baseAnimationLength <= 0f)
            {
                Debug.LogWarning($"[TurretShooter] Animation length is {baseAnimationLength}, this seems wrong!");
                baseAnimationLength = 0.5f; // Safe fallback
            }
            else if (baseAnimationLength > 5f)
            {
                Debug.LogWarning($"[TurretShooter] Animation length is {baseAnimationLength}s, this seems very long for a fire animation!");
            }
        }
        else
        {
            Debug.LogWarning($"[TurretShooter] Could not find animation state '{fireAnimationStateName}'. Available clips:");
            if (animator.runtimeAnimatorController != null)
            {
                foreach (AnimationClip clip in animator.runtimeAnimatorController.animationClips)
                {
                    Debug.Log($"  - {clip.name} ({clip.length:F3}s)");
                }
            }
        }
    }

    /// <summary>
    /// Test the half-beat animation timing with current settings (editor/debug use)
    /// </summary>
    [ContextMenu("Test Half-Beat Animation Timing")]
    void TestHalfBeatAnimationTiming()
    {
        if (!useHalfBeatAnimation)
        {
            Debug.LogWarning("[TurretShooter] Half-beat animation is disabled!");
            return;
        }

        Debug.Log("[TurretShooter] === Half-Beat Animation Timing Test ===");
        
        // Simulate animation trigger
        bool originalValidation = validateAnimationTiming;
        validateAnimationTiming = true; // Force validation output
        
        SyncHalfBeatAnimationSpeed();
        
        validateAnimationTiming = originalValidation; // Restore original setting
        
        Debug.Log("[TurretShooter] === End Test ===");
    }

    /// <summary>
    /// Gets an animation clip by state name from the animator
    /// </summary>
    AnimationClip GetAnimationClip(string stateName)
    {
        if (animator?.runtimeAnimatorController == null) return null;

        foreach (AnimationClip clip in animator.runtimeAnimatorController.animationClips)
        {
            if (clip.name.Contains(stateName) || clip.name == stateName)
            {
                return clip;
            }
        }
        return null;
    }

    /// <summary>
    /// Gets the current beat duration from BPM system
    /// </summary>
    float GetBeatDuration()
    {
        if (bpmController != null)
        {
            int currentBPM = bpmController.GetCurrentBPM();
            return 60f / currentBPM; // Duration of one beat in seconds
        }
        return 0.5f; // Fallback: 120 BPM
    }

    /// <summary>
    /// Spawns one projectile, configures it, and initializes its movement + damage.
    /// </summary>
    void SpawnProjectile()
    {
        // Safety: require prefab, player, and tilemap references.
        if (!projectilePrefab || !player || !gridTilemap)
        {
            if (debugLogs)
                Debug.LogWarning("[TurretShooter] Missing prefab/player/tilemap reference.");
            return;
        }

        // Decide projectile movement direction.
        Vector2 dir = Vector2.right;
        switch (shootDirection)
        {
            case CardinalDir.Right: dir = Vector2.right; break;
            case CardinalDir.Left:  dir = Vector2.left;  break;
            case CardinalDir.Up:    dir = Vector2.up;    break;
            case CardinalDir.Down:  dir = Vector2.down;  break;
        }

        // Decide prefab's visual rotation (independent from movement).
        float zDeg = 0f;
        switch (prefabRotation)
        {
            case CardinalDir.Up:    zDeg = 0f;   break;
            case CardinalDir.Right: zDeg = 90f;  break;
            case CardinalDir.Down:  zDeg = 180f; break;
            case CardinalDir.Left:  zDeg = -90f; break;
        }

        // Calculate spawn position: Start from turret center, move to muzzle position
        Vector3 turretCenter;
        Vector3 spawnPosition;
        Vector3 spawnOffset = Vector3.zero;
        
        if (muzzle)
        {
            // Use muzzle position directly
            spawnPosition = muzzle.position;
            turretCenter = transform.position; // For debug logging
        }
        else
        {
            // Get turret's grid position center
            Vector3Int turretGridPos = gridTilemap.WorldToCell(transform.position);
            turretCenter = gridTilemap.GetCellCenterWorld(turretGridPos);
            
            // Calculate spawn position based on direction:
            // - 1 tile ahead for horizontal directions
            // - At turret level for vertical directions to simulate emerging from turret
            switch (shootDirection)
            {
                case CardinalDir.Left:
                case CardinalDir.Right:
                    // Horizontal: spawn 1 tile ahead
                    spawnOffset = (Vector3)dir * projectileStepDistance;
                    break;
                case CardinalDir.Up:
                    // Up: spawn 1 tile ahead (above turret)
                    spawnOffset = (Vector3)dir * projectileStepDistance;
                    break;
                case CardinalDir.Down:
                    // Down: spawn at turret level (bullet emerges from turret)
                    spawnOffset = Vector3.zero;
                    break;
                default:
                    spawnOffset = (Vector3)dir * projectileStepDistance;
                    break;
            }
            
            spawnPosition = turretCenter + spawnOffset;
        }
        
        // Snap final position to grid center
        Vector3Int spawnGridPos = gridTilemap.WorldToCell(spawnPosition);
        Vector3 finalSpawnPos = gridTilemap.GetCellCenterWorld(spawnGridPos);

        if (debugLogs)
            Debug.Log($"[TurretShooter] Spawn calculation: turretCenter={turretCenter}, spawnOffset={spawnOffset}, finalSpawn={finalSpawnPos}");

        Quaternion spawnRot = Quaternion.Euler(0f, 0f, zDeg);

        // Instantiate projectile prefab at the offset position
        var go = Instantiate(projectilePrefab, finalSpawnPos, spawnRot);

        // Ensure it has a BeatProjectile component.
        var proj = go.GetComponent<BeatProjectile>();
        if (!proj) { Destroy(go); return; }

        // Calculate delayed movement beat for half-beat animation
        int delayMovementUntilBeat = -1; // Default: move immediately
        if (useHalfBeatAnimation)
        {
            // Delay movement until next action beat (current beat + fireEveryN)
            delayMovementUntilBeat = _beatCount + fireEveryN;
        }

        // Build config struct for projectile.
        var cfg = new BeatProjectile.Config
        {
            direction = dir,
            stepDistance = projectileStepDistance,
            snapToGrid = true,
            smoothStep = true,
            moveLerpFractionOfInterval = 0.35f,
            bpmForSmoothing = 120,
            intervalBeatsForSmoothing = 1f,
            gridTilemap = gridTilemap,
            maxSteps = projectileMaxSteps,
            damage = projectileDamage,
            player = player,
            delayMovementUntilBeat = delayMovementUntilBeat
        };

        // Initialize projectile with config.
        proj.Initialize(cfg);

        if (debugLogs)
        {
            if (useHalfBeatAnimation)
            {
                Debug.Log($"[TurretShooter] Bullet spawned at {finalSpawnPos}, dir={dir}, prefabRot={prefabRotation} ({zDeg}°) - movement delayed until beat {delayMovementUntilBeat}");
            }
            else
            {
                Debug.Log($"[TurretShooter] Bullet spawned at {finalSpawnPos}, dir={dir}, prefabRot={prefabRotation} ({zDeg}°) - immediate movement");
            }
        }
    }

    /// <summary>
    /// Called when the timing system is reset
    /// </summary>
    void OnTimingSystemReset(bool shouldDestroyProjectiles)
    {
        // Stop any ongoing fire sequences
        if (fireDelayCoroutine != null)
        {
            StopCoroutine(fireDelayCoroutine);
            fireDelayCoroutine = null;
        }

        // Reset animation speed to normal
        ResetAnimationSpeed();
        
        // Clear pending fire state
        pendingFire = false;
        
        // Reset beat counter
        _beatCount = -1;
        
        // If we were in the middle of an animation, stop the animator to prevent stuck states
        if (animator != null)
        {
            animator.SetTrigger("Reset"); // Optional: add a "Reset" trigger in your animator
            // Alternatively, you can play an idle state:
            // animator.Play("Idle", 0, 0f);
        }
        
        if (debugLogs)
            Debug.Log($"[TurretShooter] Timing reset - cleared all pending actions and reset animator (destroyProjectiles: {shouldDestroyProjectiles})");
    }

    /// <summary>
    /// Resets animation speed to normal (1.0)
    /// </summary>
    public void ResetAnimationSpeed()
    {
        if (animator != null)
        {
            animator.speed = 1.0f;
        }
    }

    /// <summary>
    /// Unity-version-safe way to find one object of type T.
    /// </summary>
    static T FindOne<T>() where T : Object
    {
#if UNITY_2023_1_OR_NEWER || UNITY_2022_3_OR_NEWER
        return Object.FindFirstObjectByType<T>(FindObjectsInactive.Exclude);
#else
        return Object.FindObjectOfType<T>();
#endif
    }
}
