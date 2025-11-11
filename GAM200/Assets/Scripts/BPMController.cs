using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class BPMController : MonoBehaviour
{
    public AudioSource GetBGMPlayer() => BGM_Player;
    public AudioSource GetMetronomePlayer() => Metronome_SFX_Player;
    public static bool isPaused;

    [Header("-------------- Assigning BPM --------------")]
    [SerializeField] private int bpm = 120; // Beats per minute
    [SerializeField] private Interval interval;

    [Header("-------------- Animation Pre-Beat System --------------")]
    [SerializeField] private List<AnimationTiming> animationTimings = new List<AnimationTiming>();
    [Tooltip("Configure animation triggers that fire BEFORE the main beat to allow for perfect sync")]

    [Header("-------------- Timing Adjustments --------------")]
    [SerializeField] private float musicStartOffset = 0.1f; // Adjust this value

    [Header("--------------- Music Clips ---------------")]
    [SerializeField] private AudioClip BGM; // Reference to the music clip
    [SerializeField] private AudioClip Metronome_SFX; // Reference to the sound effect clip

    [Header("-------------- Audio Players --------------")]
    [SerializeField] private AudioSource BGM_Player; // Reference to the AudioSource component
    [SerializeField] private AudioSource Metronome_SFX_Player; // Reference to the AudioSource component for sound effects

    [Header("-------------- Timing Recovery System --------------")]
    [SerializeField] private bool enableAutoRecovery = true;
    [Tooltip("Automatically recover from audio timing issues")]
    
    [SerializeField] private KeyCode resetTimingKey = KeyCode.R;
    [Tooltip("Key to manually reset timing system (for debugging)")]

    [Header("-------------- Loop Detection --------------")]
    [SerializeField] private bool enableLoopDetection = true;
    [Tooltip("Automatically detect when audio loops and notify timing system")]
    
    [SerializeField] private float loopDetectionThreshold = 1.0f;
    [Tooltip("Time difference threshold to detect audio loops (seconds)")]

    // Public method to get current BPM for other scripts
    public int GetCurrentBPM() => bpm;

    // System to track which beats have been processed for animation timing
    private HashSet<int> processedAnimationBeats = new HashSet<int>();
    
    // Loop detection variables
    private float lastAudioTime = 0f;
    private bool wasLooping = false;

    [System.Serializable]
    public class AnimationTiming
    {
        [Header("Timing Configuration")]
        [Tooltip("How many beats before the action beat to trigger animation")]
        [Range(0.1f, 4.0f)] public float beatsBeforeAction = 0.5f;
        
        [Tooltip("Fire animation every N beats (same as your action timing)")]
        public int fireEveryN = 6;
        
        [Tooltip("Offset to sync with your action timing")]
        public int fireOffset = 0;

        [Header("Event System")]
        [Tooltip("This event fires when animation should start (UnityEvent system - for manual wiring)")]
        public UnityEvent OnAnimationTrigger;
        
        [Tooltip("Also broadcast to BeatBus system for automatic turret management")]
        public bool useBeatBusSystem = true;

        [Header("Half-Beat Animation")]
        [Tooltip("Use half-beat animation system (animation starts 0.5 beats early, bullet fires on beat)")]
        public bool useHalfBeatSystem = true;

        [Header("Debug")]
        public string debugName = "Animation Timing";
        
        [Tooltip("Enable debug logs for this timing configuration")]
        public bool enableDebugLogs = false;
    }
        
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        BGM_Player.clip = BGM; // Assign the music clip to the AudioSource
        BGM_Player.Play(); // Start playing the music

        // Start the metronome sound effect
        Metronome_SFX_Player.clip = Metronome_SFX;
        
        // Initialize loop detection
        lastAudioTime = BGM_Player.time;
        wasLooping = BGM_Player.loop;
    }

    // Update is called once per frame
    private void Update()
    {
        // Manual reset key for debugging
        if (enableAutoRecovery && Input.GetKeyDown(resetTimingKey))
        {
            Debug.Log("[BPMController] Manual timing reset triggered!");
            ResetTimingSystem();
            return;
        }

        // Check for audio loops if enabled
        if (enableLoopDetection)
        {
            CheckForAudioLoop();
        }

        // Check if audio is still playing
        if (enableAutoRecovery && !BGM_Player.isPlaying && BGM_Player.clip != null)
        {
            Debug.LogWarning("[BPMController] BGM stopped playing, attempting to restart...");
            RestartAudio();
            return;
        }

        float frequency = isPaused ? 0 : BGM.frequency;
        // Calculate the current time in terms of intervals
        float offsetInSamples = musicStartOffset * frequency;

        // Adjust the timeSamples by the offset
        float adjustedSamples = BGM_Player.timeSamples + offsetInSamples;

        // Calculate the current time in terms of intervals
        float sampledTime = adjustedSamples / (frequency * interval.GetIntervalLength(bpm));

        // Check if a new interval has been reached
        interval.CheckForNewInterval(sampledTime);

        // Check animation timing triggers (supports both UnityEvent and BeatBus systems)
        CheckAnimationTimings(sampledTime);
    }

    /// <summary>
    /// Detects when audio loops and notifies the timing system
    /// </summary>
    private void CheckForAudioLoop()
    {
        if (!BGM_Player.isPlaying || BGM_Player.clip == null) return;

        float currentAudioTime = BGM_Player.time;
        
        // Detect if audio time jumped backwards significantly (indicating a loop)
        float timeDifference = currentAudioTime - lastAudioTime;
        bool loopDetected = false;
        
        if (BGM_Player.loop)
        {
            // For looping audio, detect backwards time jumps
            if (timeDifference < -loopDetectionThreshold)
            {
                loopDetected = true;
                Debug.Log($"[BPMController] Audio loop detected! Time jumped from {lastAudioTime:F3}s to {currentAudioTime:F3}s (delta: {timeDifference:F3}s)");
            }
        }
        else if (wasLooping && !BGM_Player.loop)
        {
            // Audio was looping but now isn't - this might indicate a restart
            loopDetected = true;
            Debug.Log("[BPMController] Audio loop state changed - possible restart detected");
        }

        // Also detect if we're near the end and suddenly jump to beginning
        if (BGM_Player.clip != null && lastAudioTime > (BGM_Player.clip.length - 1f) && currentAudioTime < 1f)
        {
            loopDetected = true;
            Debug.Log($"[BPMController] End-to-beginning loop detected! Last: {lastAudioTime:F3}s, Current: {currentAudioTime:F3}s, Clip Length: {BGM_Player.clip.length:F3}s");
        }

        if (loopDetected)
        {
            // Notify BeatBus that a loop occurred
            BeatBus.NotifyPossibleLoop();
            
            // Clear processed animation beats to allow them to retrigger
            processedAnimationBeats.Clear();
            
            Debug.Log("[BPMController] Notified timing systems about audio loop and cleared animation tracking");
        }

        lastAudioTime = currentAudioTime;
        wasLooping = BGM_Player.loop;
    }

    /// <summary>
    /// Checks if any animation timings should trigger based on current musical position
    /// Supports both UnityEvent and BeatBus systems
    /// </summary>
    private void CheckAnimationTimings(float currentInterval)
    {
        foreach (var timing in animationTimings)
        {
            // Calculate when this timing should trigger
            // We need to predict future beats and trigger animation early
            
            float currentBeat = currentInterval;
            int currentBeatInt = Mathf.FloorToInt(currentBeat);
            
            // Calculate the next action beat
            int nextActionBeat = GetNextActionBeat(currentBeatInt, timing.fireEveryN, timing.fireOffset);
            
            // Calculate when animation should start (beatsBeforeAction before the action)
            float animationTriggerBeat = nextActionBeat - timing.beatsBeforeAction;
            
            // Create unique key for this timing to prevent duplicate triggers
            int timingKey = nextActionBeat * 1000 + timing.fireEveryN * 100 + timing.fireOffset;
            
            // Check if we've crossed the animation trigger point
            if (currentBeat >= animationTriggerBeat && !processedAnimationBeats.Contains(timingKey))
            {
                // Mark this beat as processed to prevent multiple triggers
                processedAnimationBeats.Add(timingKey);
                
                // Clean up old processed beats to prevent memory buildup
                if (processedAnimationBeats.Count > 100)
                {
                    var toRemove = new List<int>();
                    foreach (int beat in processedAnimationBeats)
                    {
                        if (beat < (currentBeatInt - 50) * 1000) toRemove.Add(beat);
                    }
                    foreach (int beat in toRemove)
                    {
                        processedAnimationBeats.Remove(beat);
                    }
                }
                
                // Trigger UnityEvent system (for manual object wiring)
                timing.OnAnimationTrigger?.Invoke();
                
                // Trigger BeatBus system (for automatic turret management)
                if (timing.useBeatBusSystem)
                {
                    Debug.Log($"[BPMController] Calling BeatBus.TriggerAnimationEvent for '{timing.debugName}' at beat {currentBeat:F2}");
                    BeatBus.TriggerAnimationEvent(currentBeat, timing.fireEveryN, timing.fireOffset, timing.beatsBeforeAction);
                }
                
                if (timing.enableDebugLogs)
                {
                    Debug.Log($"[BPMController] Animation '{timing.debugName}' triggered at beat {currentBeat:F2} for action at beat {nextActionBeat}");
                }
            }
        }
    }

    /// <summary>
    /// Calculate the next beat when an action should occur based on timing parameters
    /// </summary>
    private int GetNextActionBeat(int currentBeat, int fireEveryN, int fireOffset)
    {
        // Find the next beat that matches the firing pattern
        for (int testBeat = currentBeat; testBeat <= currentBeat + fireEveryN + 1; testBeat++)
        {
            if ((testBeat - fireOffset) % fireEveryN == 0)
            {
                return testBeat;
            }
        }
        return currentBeat + fireEveryN; // Fallback
    }

    // Call this method to play the metronome sound effect
    public void PlayMetronomeSFX()
    {
        // Play the metronome sound effect over the existing sound to ensure its on beat
        Metronome_SFX_Player.PlayOneShot(Metronome_SFX);
    }

    /// <summary>
    /// Reset the entire timing system
    /// </summary>
    [ContextMenu("Reset Timing System")]
    public void ResetTimingSystem()
    {
        Debug.Log("[BPMController] Resetting timing system...");
        
        // Reset BeatBus timing
        BeatBus.ResetTiming();
        
        // Reset our own interval tracking
        if (interval != null)
        {
            // Access the private field via reflection or add a public reset method
            interval.ResetTiming();
        }
        
        // Clear processed animation beats
        processedAnimationBeats.Clear();
        
        // Reset loop detection
        lastAudioTime = BGM_Player.time;
        
        // Restart audio from current position to resync
        if (BGM_Player.clip != null)
        {
            float currentTime = BGM_Player.time;
            BGM_Player.Stop();
            BGM_Player.time = currentTime;
            BGM_Player.Play();
        }
    }

    /// <summary>
    /// Restart audio playback
    /// </summary>
    public void RestartAudio()
    {
        if (BGM_Player.clip != null)
        {
            Debug.Log("[BPMController] Restarting audio playback...");
            BGM_Player.Stop();
            BGM_Player.Play();
            ResetTimingSystem();
        }
    }

    /// <summary>
    /// Pause/resume the timing system
    /// </summary>
    public void SetPaused(bool paused)
    {
        isPaused = paused;
        if (paused)
        {
            BGM_Player.Pause();
        }
        else
        {
            BGM_Player.UnPause();
        }
        Debug.Log($"[BPMController] Timing system {(paused ? "paused" : "resumed")}");
    }
}

[System.Serializable]
public class Interval
{
    public float steps;
    [SerializeField] private UnityEvent onIntervalReached;

    // To keep track of the last interval checked
    private int old_interval;

    // Calculate the length of one interval in seconds based on BPM and steps
    public float GetIntervalLength(int bpm)
    {
        return 60f / bpm  * steps;
    }

    // Check if a new interval has been reached and invoke the event if so
    public void CheckForNewInterval(float interval)
    {
        // If the floored interval has changed, invoke the event
        if (Mathf.FloorToInt(interval) != old_interval)
        {
            old_interval = Mathf.FloorToInt(interval);
            onIntervalReached.Invoke();
        }
    }

    // Reset the interval tracking for timing recovery
    public void ResetTiming()
    {
        old_interval = -1;
        Debug.Log("[Interval] Timing reset");
    }
}