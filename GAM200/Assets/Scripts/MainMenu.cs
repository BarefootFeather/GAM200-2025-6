using UnityEngine;
using UnityEngine.SceneManagement;

public class MainMenu : MonoBehaviour
{
    public void PauseMenu()
    {
        SceneManager.LoadSceneAsync(1);
    }

    public void PlayGame()
    {
        SceneManager.LoadSceneAsync(2);
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
