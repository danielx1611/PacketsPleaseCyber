using UnityEngine;

public class GameplayTriggerRelay : MonoBehaviour
{
    [SerializeField] private GameplayManager gameplayManager;
    [SerializeField] private string triggerId;

    public void RaiseConfiguredTrigger()
    {
        if (string.IsNullOrWhiteSpace(triggerId))
        {
            Debug.LogWarning($"{nameof(GameplayTriggerRelay)} on '{name}' has no configured trigger id.", this);
            return;
        }

        RaiseTrigger(triggerId);
    }

    public void RaiseTrigger(string eventTriggerId)
    {
        if (gameplayManager == null)
        {
            Debug.LogWarning($"{nameof(GameplayTriggerRelay)} on '{name}' is missing {nameof(gameplayManager)} reference.", this);
            return;
        }

        gameplayManager.ResumeDialogueAfterTrigger(eventTriggerId);
    }
}
