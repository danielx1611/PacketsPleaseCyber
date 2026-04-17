using System;
using UnityEngine;

[Obsolete("Use GameplayManager with LevelDialoguePlan dialogue blocks instead of GameOpeningDialogueBootstrap.")]
public class GameOpeningDialogueBootstrap : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private CharacterDialogueUI characterDialogueUI;

    [Header("Opening Dialogue")]
    [SerializeField] private DialogueSequence openingSequence;
    [SerializeField] private int openingSequenceLineIndex;
    [TextArea]
    [SerializeField] private string fallbackOpeningText = "Welcome aboard. Let's begin inspections.";

    private void Start()
    {
        if (characterDialogueUI == null)
        {
            Debug.LogWarning($"{nameof(GameOpeningDialogueBootstrap)} on '{name}' is missing {nameof(characterDialogueUI)} reference.", this);
            return;
        }

        if (openingSequence != null)
        {
            characterDialogueUI.ShowDialogue(openingSequence, openingSequenceLineIndex);
            return;
        }

        characterDialogueUI.ShowDialogue(fallbackOpeningText);
    }
}
