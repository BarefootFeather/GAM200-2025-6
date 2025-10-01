using UnityEngine;
using UnityEngine.SceneManagement;

public class PauseMenu : MonoBehaviour
{
    public GameObject pauseMenuUI; // Assign your Pause Menu Canvas
    private bool isPaused = false;

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Tab)) // Toggle with Tab
        {
            if (isPaused)
                Resume();
            else
                Pause();
        }
    }

    void Pause()
    {
        pauseMenuUI.SetActive(true);
        Time.timeScale = 0f;
        isPaused = true;
    }

    void Resume()
    {
        pauseMenuUI.SetActive(false);
        Time.timeScale = 1f;
        isPaused = false;
    }

}
