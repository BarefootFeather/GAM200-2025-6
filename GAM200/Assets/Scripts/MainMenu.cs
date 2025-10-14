using UnityEngine;
using UnityEngine.SceneManagement;

public class MainMenu : MonoBehaviour
{
    public void PlayGame()
    {
        SceneManager.LoadSceneAsync(1);
    }

    public void PlayGame2()
    {
        SceneManager.LoadSceneAsync(2);
        if (Time.timeScale == 0f)
        {
            Time.timeScale = 1f;
        }
    }

    public void PlayGame3()
    {
        SceneManager.LoadSceneAsync(3);
        if (Time.timeScale == 0f)
        {
            Time.timeScale = 1f;
        }
    }

    public void BackToMenu()
    {
        SceneManager.LoadSceneAsync(0);
    }

    public void QuitGame()
    {
        Application.Quit();
    }
}
