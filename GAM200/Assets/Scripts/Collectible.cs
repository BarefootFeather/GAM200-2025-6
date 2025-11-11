using TMPro;
using UnityEngine;

public class Collectible : MonoBehaviour
{
    public int collectibleCount;
    public TMP_Text collectibleText;
    public GameObject door;

    private bool hasGivenShield = false; // Prevents giving shield multiple times

    void Update()
    {
        if (collectibleText != null)
        {
            collectibleText.text = "Completed Maps: " + collectibleCount.ToString();
        }
    }

    public void OnCollect(PlayerController player)
    {
        collectibleCount++;

        // Give shield ability the first time a collectible is picked up
        if (!hasGivenShield && player != null)
        {
            player.EnableShieldAbility();
            hasGivenShield = true;
            Debug.Log("Player has unlocked the Shield Ability!");
        }
    }
}
