using UnityEngine;
using UnityEngine.SceneManagement;

public class EndScreenUIController : MonoBehaviour
{
    [SerializeField] private string mainMenuSceneName = "MainMenu";
    [SerializeField] private string gameplaySceneName = "Game";

    public void ReturnToMainMenu()
    {
        GameplaySessionState.ResetSessionStartOverrides();
        SceneManager.LoadScene(mainMenuSceneName);
    }

    public void RetryCurrentLevel()
    {
        var retryLevelIndex = GameplaySessionState.LastKnownLevelIndex;
        if (retryLevelIndex < 0)
        {
            retryLevelIndex = 0;
        }

        var isRetryingTutorialLevel = retryLevelIndex == 0;
        GameplaySessionState.RequestStartLevel(
            retryLevelIndex,
            skipFirstLevelTutorial: isRetryingTutorialLevel,
            startFirstLevelAtFirstFollowingWave: isRetryingTutorialLevel);
        SceneManager.LoadScene(gameplaySceneName);
    }
}
