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
*/
using System;
using UnityEngine;


public class BeatBus : MonoBehaviour
{
    // A static event that any script can subscribe to.
    // When fired, it passes along an int: the current beat index (0, 1, 2, ...).
    // Example: Beat += MyFunction; → MyFunction will run whenever a beat happens.
    public static event Action<int> Beat;   

    // A static counter that keeps track of how many beats have passed.
    // Starts at -1 so the first call increments it to 0.
    private static int _beatIndex = -1;

    // This function will be called by Unity’s Event system (Inspector wiring).
    // DO NOT rename it: it’s the method that the BPMController will call each interval.
    public void OnIntervalReached()
    {
        _beatIndex++;                // Move to the next beat count (e.g., -1→0, 0→1, 1→2, ...).
        
        // If something is subscribed to Beat, invoke it and pass the beat index.
        // (Beat? checks for null before calling).
        Beat?.Invoke(_beatIndex);    
    }
}