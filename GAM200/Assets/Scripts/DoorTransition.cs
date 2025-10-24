using UnityEngine;
using UnityEngine.SceneManagement; // Required for scene management

public class DoorTransition : MonoBehaviour
{
    [SerializeField] private string sceneToLoad; // Name of the scene to load
    [SerializeField] private GameObject doorUIPanel; // Reference to the UI Panel
    [SerializeField] private Collectible collectible;     // Reference to the Collectible script
    [SerializeField] private bool requiresCollectible = true; // Whether this door needs collectibles to unlock


    private Collider2D doorCollider;

    private void Start()
    {
        doorCollider = GetComponent<Collider2D>();

        // Ensure the door is solid at start if it requires collectible(s)
        if (requiresCollectible && collectible.collectibleCount == 0)
        {
            SetDoorLocked(true);
        }
        else
        {
            SetDoorLocked(false);
        }
    }

    private void Update()
    {
        // Dynamically check if collectibles have been collected
        if (requiresCollectible)
        {
            if (collectible.collectibleCount > 0)
                SetDoorLocked(false);
            else
                SetDoorLocked(true);
        }
    }


    private void OnTriggerEnter2D(Collider2D other)
    {
        // If it's the player and the door is unlocked
        if (other.CompareTag("Player") && !doorCollider.isTrigger)
        {
            return; // Still locked, acts as wall
        }

        // If door is unlocked
        if (other.CompareTag("Player") && CompareTag("CompletedLvl"))
        {
            doorUIPanel.SetActive(true);
        }

        if (other.CompareTag("Player") && CompareTag("ToSecondLvl"))
        {
            SceneManager.LoadScene(sceneToLoad);
        }
    }

    private void SetDoorLocked(bool locked)
    {
        // If locked, make collider solid; otherwise, make it a trigger
        if (doorCollider != null)
        {
            doorCollider.isTrigger = !locked;
        }
    }

    /*private void OnTriggerEnter2D(Collider2D other)
    {
        //Check if the entering object is the player
        if (other.CompareTag("Player") && CompareTag("CompletedLvl"))
        {
            doorUIPanel.SetActive(true);
        }
        if (other.CompareTag("Player") && CompareTag("ToSecondLvl"))
        {
            SceneManager.LoadScene(sceneToLoad);
        }


    }*/

}