using UnityEngine;
using UnityEngine.SceneManagement; // Required for scene management

public class DoorTransition : MonoBehaviour
{
    [SerializeField] private string sceneToLoad; // Name of the scene to load

    private void OnTriggerEnter(Collider other)
    {
        // Check if the entering object is the player
        if (other.CompareTag("Player"))
        {
            // Load the specified scene
            SceneManager.LoadScene(sceneToLoad);
        }
    }
}