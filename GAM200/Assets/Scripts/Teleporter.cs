using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Teleporter : MonoBehaviour
{
    [SerializeField] private Transform destination;
    [SerializeField] private PolygonCollider2D destinationMapBoundary; // Optional: camera bounds for destination area

    public Transform GetDestination()
    {
        return destination;
    }

    public PolygonCollider2D GetDestinationBoundary()
    {
        return destinationMapBoundary;
    }
}
