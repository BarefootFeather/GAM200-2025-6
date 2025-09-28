using TMPro;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.UI;

public class TrapController : MonoBehaviour
{
    [Header("Player Reference")]
    [SerializeField] private GameObject player;

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
}
