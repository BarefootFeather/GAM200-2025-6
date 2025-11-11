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
* @file	   BeatBus.cs
* @brief	BeatBus is like a "relay station" for beat timing.
/// - It listens to an Interval UnityEvent (from BPMController).
/// - Every time the Interval event fires, it raises its own C# event called Beat.
/// - Other scripts can subscribe to Beat to know "which beat number" we are currently on.
/// - Also supports animation triggers that fire before action beats for perfect sync.
/// - Includes reset functionality for timing desynchronization recovery.
*/
using System;
using UnityEngine;


public class BeatBus : MonoBehaviour
{
    // A static event that any script can subscribe to.
    // When fired, it passes along an int: the current beat index (0, 1, 2, ...).
    // Example: Beat += MyFunction; → MyFunction will run whenever a beat happens.
    public static event Action<int> Beat;   

    // Animation trigger event that fires before action beats
    // Parameters: (beatIndex, fireEveryN, fireOffset, beatsBeforeAction)
    public static event Action<int, int, int, float> AnimationTrigger;

    // Reset event - fired when timing system is reset
    public static event Action<bool> OnTimingReset; // bool parameter: should destroy projectiles

    // A static counter that keeps track of how many beats have passed.
    // Starts at -1 so the first call increments it to 0.
    private static int _beatIndex = -1;
    
    // Timing validation variables
    private static float _lastBeatTime = -1f;
    private static int _consecutiveValidBeats = 0;
    private static int _consecutiveInvalidBeats = 0; // Track consecutive invalid beats
    private static bool _timingIsValid = true;
    private static float _lastLoopTime = -1f; // Track when we last detected a potential loop
    private static bool _possibleLoopDetected = false; // Flag to handle loop recovery
    
    // Static configuration - accessed through singleton pattern
    private static BeatBus _instance;
    
    [Header("Beat Timing Validation")]
    [SerializeField] private bool enableTimingValidation = true;
    [Tooltip("Enable automatic detection and recovery from timing desync")]
    
    [SerializeField] private float beatTolerancePercent = 20f; // Increased from 15% for better loop handling
    [Tooltip("Allowed deviation from expected beat timing (percentage)")]
    
    [SerializeField] private int resetAfterInvalidBeats = 5; // Increased from 3 for better loop tolerance
    [Tooltip("Reset timing after this many consecutive invalid beats")]
    
    [SerializeField] private bool debugTimingIssues = true;
    [Tooltip("Log timing validation issues for debugging")]

    [Header("Animation System")]
    [SerializeField] private bool allowAnimationsDuringInvalidTiming = true;
    [Tooltip("Allow animation triggers even when timing validation fails (prevents animation death on loops)")]
    
    [SerializeField] private float loopDetectionThreshold = 0.5f;
    [Tooltip("If beat timing is off by more than this many seconds, assume it might be a loop")]

    [Header("Projectile Reset Behavior")]
    [SerializeField] private bool destroyProjectilesOnReset = true;
    [Tooltip("Destroy all projectiles when timing resets (prevents stuck bullets during loops)")]
    
    [SerializeField] private bool enableHalfBeatRecovery = true;
    [Tooltip("Enable special recovery logic for half-beat animation bullets during timing resets")]

    private void Awake()
    {
        _instance = this;
    }

    // This function will be called by Unity's Event system (Inspector wiring).
    // DO NOT rename it: it's the method that the BPMController will call each interval.
    public void OnIntervalReached()
    {
        float currentTime = Time.time;
        
        // Validate beat timing if enabled
        if (enableTimingValidation && _beatIndex >= 0)
        {
            ValidateBeatTiming(currentTime);
        }

        _beatIndex++;                // Move to the next beat count (e.g., -1→0, 0→1, 1→2, ...).
        _lastBeatTime = currentTime;
        
        // If something is subscribed to Beat, invoke it and pass the beat index.
        // (Beat? checks for null before calling).
        Beat?.Invoke(_beatIndex);    
    }

    /// <summary>
    /// Validates if the current beat timing is within acceptable bounds
    /// </summary>
    private void ValidateBeatTiming(float currentTime)
    {
        if (_lastBeatTime < 0) return; // First beat, nothing to validate

        // Get expected beat duration from BPM system
        var bpmController = FindFirstObjectByType<BPMController>();
        if (bpmController == null) return;

        float expectedBeatDuration = 60f / bpmController.GetCurrentBPM();
        float actualDuration = currentTime - _lastBeatTime;
        float deviationPercent = Mathf.Abs(actualDuration - expectedBeatDuration) / expectedBeatDuration * 100f;
        float deviationSeconds = Mathf.Abs(actualDuration - expectedBeatDuration);

        bool isValidBeat = deviationPercent <= beatTolerancePercent;
        
        // Special case: Check if this might be a song loop
        bool isPossibleLoop = deviationSeconds > loopDetectionThreshold;
        if (isPossibleLoop && !_possibleLoopDetected)
        {
            _possibleLoopDetected = true;
            _lastLoopTime = currentTime;
            if (debugTimingIssues)
                Debug.Log($"[BeatBus] Possible song loop detected - timing off by {deviationSeconds:F3}s. Giving grace period for recovery...");
        }

        // If we're in a potential loop recovery period, be more lenient
        if (_possibleLoopDetected && (currentTime - _lastLoopTime) < 2f) // 2 second grace period
        {
            // During loop recovery, use more lenient validation
            isValidBeat = deviationPercent <= (beatTolerancePercent * 2f); // Double tolerance
            if (isValidBeat && debugTimingIssues)
                Debug.Log($"[BeatBus] Beat recovered during loop grace period (deviation: {deviationPercent:F1}%)");
        }

        if (isValidBeat)
        {
            _consecutiveValidBeats++;
            _consecutiveInvalidBeats = 0; // Reset invalid counter
            
            // Clear loop detection if we have multiple valid beats
            if (_consecutiveValidBeats >= 3)
            {
                _possibleLoopDetected = false;
            }
            
            if (!_timingIsValid && _consecutiveValidBeats >= 2)
            {
                // Timing has recovered
                _timingIsValid = true;
                if (debugTimingIssues)
                    Debug.Log($"[BeatBus] Timing recovered after {_consecutiveValidBeats} valid beats");
            }
        }
        else
        {
            _consecutiveValidBeats = 0;
            _consecutiveInvalidBeats++; // Increment invalid counter
            _timingIsValid = false;
            
            if (debugTimingIssues)
            {
                Debug.LogWarning($"[BeatBus] Beat timing off by {deviationPercent:F1}%! Expected: {expectedBeatDuration:F3}s, Actual: {actualDuration:F3}s (Invalid beats: {_consecutiveInvalidBeats}) {(_possibleLoopDetected ? "[LOOP RECOVERY]" : "")}");
            }

            // Only reset if we're not in a loop recovery period AND we have too many invalid beats
            bool shouldReset = _consecutiveInvalidBeats >= resetAfterInvalidBeats && 
                              (!_possibleLoopDetected || (currentTime - _lastLoopTime) > 3f);
            
            if (shouldReset || deviationPercent > beatTolerancePercent * 3f) // Very severe deviation
            {
                if (debugTimingIssues)
                    Debug.LogWarning($"[BeatBus] Timing severely off ({deviationPercent:F1}%) or too many invalid beats ({_consecutiveInvalidBeats}), triggering reset!");
                ResetTiming();
            }
        }
    }

    /// <summary>
    /// Manually reset the beat timing system
    /// </summary>
    [ContextMenu("Reset Beat Timing")]
    public static void ResetTiming()
    {
        Debug.Log("[BeatBus] Resetting beat timing system...");
        
        // Store the old beat index for half-beat animation recovery
        int oldBeatIndex = _beatIndex;
        
        _beatIndex = -1;
        _lastBeatTime = -1f;
        _consecutiveValidBeats = 0;
        _consecutiveInvalidBeats = 0; // Reset invalid counter too
        _timingIsValid = true;
        _possibleLoopDetected = false; // Reset loop detection
        _lastLoopTime = -1f;
        
        // Notify all systems that timing was reset
        bool shouldDestroyProjectiles = _instance?.destroyProjectilesOnReset ?? true;
        
        // Special handling for half-beat animation bullets
        // If we're not destroying projectiles and had a valid beat index, 
        // give bullets a chance to recover instead of getting stuck
        if (!shouldDestroyProjectiles && oldBeatIndex >= 0)
        {
            if (_instance != null && _instance.debugTimingIssues)
                Debug.Log($"[BeatBus] Timing reset from beat {oldBeatIndex} - allowing bullet recovery");
        }
        
        OnTimingReset?.Invoke(shouldDestroyProjectiles);
    }

    /// <summary>
    /// Force set the beat index (for external synchronization)
    /// </summary>
    public static void SetBeatIndex(int newBeatIndex)
    {
        Debug.Log($"[BeatBus] Beat index manually set to {newBeatIndex}");
        _beatIndex = newBeatIndex;
        _lastBeatTime = Time.time;
        _consecutiveValidBeats = 0;
        _possibleLoopDetected = false; // Reset loop detection on manual sync
    }

    /// <summary>
    /// Get current beat index
    /// </summary>
    public static int GetCurrentBeat() => _beatIndex;

    /// <summary>
    /// Check if timing system is currently valid
    /// </summary>
    public static bool IsTimingValid() => _timingIsValid;

    // Called by BPMController to trigger animation events with precise timing
    // This allows for fractional beat timing for animation triggers
    public static void TriggerAnimationEvent(float currentBeat, int fireEveryN, int fireOffset, float beatsBeforeAction)
    {
        // NEW: More lenient animation blocking logic to prevent animation death
        bool shouldBlockAnimations = false;
        
        if (_instance != null && _instance.enableTimingValidation)
        {
            if (_instance.allowAnimationsDuringInvalidTiming)
            {
                // Only block animations if timing is severely broken (many consecutive invalid beats)
                // This prevents single hiccups (like song loops) from killing all animations
                shouldBlockAnimations = _consecutiveInvalidBeats >= (_instance.resetAfterInvalidBeats * 2) && !_possibleLoopDetected;
            }
            else
            {
                // Original behavior - block on any invalid timing
                shouldBlockAnimations = !_timingIsValid;
            }
        }
        
        if (shouldBlockAnimations)
        {
            if (_instance != null && _instance.debugTimingIssues)
                Debug.Log($"[BeatBus] Blocking animation trigger due to severely invalid timing (consecutive invalid: {_consecutiveInvalidBeats})");
            return;
        }
        
        // Always trigger the animation event - let the turrets decide if they should respond
        AnimationTrigger?.Invoke(Mathf.FloorToInt(currentBeat), fireEveryN, fireOffset, beatsBeforeAction);
    }

    /// <summary>
    /// Detect if a song loop just occurred by checking for sudden timing discontinuity
    /// Call this when you suspect a loop happened (e.g., from audio system)
    /// </summary>
    public static void NotifyPossibleLoop()
    {
        _possibleLoopDetected = true;
        _lastLoopTime = Time.time;
        
        // Reset consecutive invalid beats since the loop explains the timing issue
        _consecutiveInvalidBeats = 0;
        
        if (_instance != null && _instance.debugTimingIssues)
            Debug.Log("[BeatBus] External loop notification received - entering recovery mode");
    }

    /// <summary>
    /// Unity-version-safe way to find one object of type T
    /// </summary>
    static new T FindFirstObjectByType<T>() where T : UnityEngine.Object
    {
#if UNITY_2023_1_OR_NEWER || UNITY_2022_3_OR_NEWER
        return UnityEngine.Object.FindFirstObjectByType<T>();
#else
        return UnityEngine.Object.FindObjectOfType<T>();
#endif
    }
}