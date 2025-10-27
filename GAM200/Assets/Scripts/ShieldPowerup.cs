using UnityEngine;

public class ShieldPowerup : MonoBehaviour
{
    // Optional particle or sound effect when collected
    [SerializeField] private AudioClip pickupSound;
    [SerializeField] private GameObject pickupEffect;


    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag("Player"))
        {
            // Get PlayerController from the player
            PlayerController player = other.GetComponent<PlayerController>();

            if (player != null)
            {
                // Grant shield ability
                player.EnableShieldAbility();

                // Play effects
                if (pickupSound)
                    AudioSource.PlayClipAtPoint(pickupSound, transform.position);

                if (pickupEffect)
                    Instantiate(pickupEffect, transform.position, Quaternion.identity);

                // Destroy collectible object
                Destroy(gameObject);
            }
        }
    }
}
