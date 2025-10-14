using UnityEngine;
using UnityEngine.SceneManagement; // Required for scene management

public class DoorTransition : MonoBehaviour
{
    [SerializeField] private string sceneToLoad; // Name of the scene to load
    [SerializeField] private GameObject doorUIPanel; // Reference to the UI Panel


    private void OnTriggerEnter2D(Collider2D other)
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


    }

}