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

    [Header("-------------- Timing Adjustments --------------")]
    [SerializeField] private float musicStartOffset = 0.1f; // Adjust this value

    [Header("--------------- Music Clips ---------------")]
    [SerializeField] private AudioClip BGM; // Reference to the music clip
    [SerializeField] private AudioClip Metronome_SFX; // Reference to the sound effect clip

    [Header("-------------- Audio Players --------------")]
    [SerializeField] private AudioSource BGM_Player; // Reference to the AudioSource component
    [SerializeField] private AudioSource Metronome_SFX_Player; // Reference to the AudioSource component for sound effects

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        BGM_Player.clip = BGM; // Assign the music clip to the AudioSource
        BGM_Player.Play(); // Start playing the music

        // Start the metronome sound effect
        Metronome_SFX_Player.clip = Metronome_SFX;
    }

    // Update is called once per frame
    private void Update()
    {
        float frequency = isPaused ? 0 : BGM.frequency;
        // Calculate the current time in terms of intervals
        float offsetInSamples = musicStartOffset * frequency;

        // Adjust the timeSamples by the offset
        float adjustedSamples = BGM_Player.timeSamples + offsetInSamples;

        // Calculate the current time in terms of intervals
        float sampledTime = adjustedSamples / (frequency * interval.GetIntervalLength(bpm));

        // Check if a new interval has been reached
        interval.CheckForNewInterval(sampledTime);
    }

    // Call this method to play the metronome sound effect
    public void PlayMetronomeSFX()
    {
        // Play the metronome sound effect over the existing sound to ensure its on beat
        Metronome_SFX_Player.PlayOneShot(Metronome_SFX);
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
}