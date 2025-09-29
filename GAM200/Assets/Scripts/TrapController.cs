using TMPro;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.UI;

public class TrapController : MonoBehaviour
{
    [Header("Player Reference")]
    [SerializeField] private GameObject player;

    [Header("Tilemap Configuration")]
    [SerializeField] private Tilemap targetTilemap;
    [SerializeField] private Sprite inactiveTile;
    [SerializeField] private Sprite activeTile;

    [Header("Damage Settings")]
    [Tooltip("Amount of damage to deal to the player.")]
    [SerializeField] private int damageAmount = 1;

    [Tooltip("Log collision events for debugging.")]
    [SerializeField] private bool debugCollisions = false;

    public GameObject GetPlayer()
    {
        return player;
    }

    public void ToggleTraps()
    {
        Debug.Log("Toggling traps...");

        foreach (TrapScript trapscript in this.GetComponentsInChildren<TrapScript>())
        {
            if (trapscript != null)
            {
                trapscript.ToggleTrap();
            }
        }
    }

    public Grid GetGrid()
    {
        return targetTilemap.layoutGrid;
    }
    public Sprite GetActiveTile()
    {
        return activeTile;
    }
    public Sprite GetInactiveTile()
    {
        return inactiveTile;
    }
    public int GetDamageAmount()
    {
        return damageAmount;
    }
    public bool GetDebugCollisions()
    {
        return debugCollisions;
    }
}
