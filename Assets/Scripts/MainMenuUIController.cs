using UnityEngine;
using UnityEngine.SceneManagement;

public class MainMenuUIController : MonoBehaviour
{
    [SerializeField] private string gameplaySceneName = "Game";

    public void StartGame()
    {
        if (string.IsNullOrWhiteSpace(gameplaySceneName))
        {
            Debug.LogError("Gameplay scene name is not configured on MainMenuUIController.");
            return;
        }

        GameplaySessionState.ResetSessionStartOverrides();
        SceneManager.LoadScene(gameplaySceneName);
    }
}
