using System;
using UnityEngine;

/// <summary>
/// A relay that converts your Interval UnityEvent into a C# event for runtime listeners.
/// Wire BPMController → Interval → On Interval Reached → BeatBus.OnIntervalReached in the Inspector.
/// </summary>
public class BeatBus : MonoBehaviour
{
    public static event Action<int> Beat;   // broadcasts the beat index to all listeners
    private static int _beatIndex = -1;

    // This is the target of the UnityEvent. Do NOT rename (keep it public).
    public void OnIntervalReached()
    {
        _beatIndex++;
        Beat?.Invoke(_beatIndex);
    }
}
