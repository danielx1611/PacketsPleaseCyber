using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class CharacterDialogueUI : MonoBehaviour
{
    public class TutorialInteractionRequest
    {
        public IReadOnlyList<string> AllowedInteractionIds { get; }
        public int RequiredInteractionCount { get; }

        public TutorialInteractionRequest(IReadOnlyList<string> allowedInteractionIds, int requiredInteractionCount)
        {
            AllowedInteractionIds = allowedInteractionIds;
            RequiredInteractionCount = Mathf.Max(1, requiredInteractionCount);
        }
    }

    [Header("UI References")]
    [SerializeField] private GameObject rootContainer;
    [SerializeField] private Image portraitImage;
    [SerializeField] private TMP_Text speakerNameText;
    [SerializeField] private TMP_Text dialogueText;
    [SerializeField] private TMP_Text continueHintText;

    [Header("Reveal Timing")]
    [Min(0f)]
    [SerializeField] private float lettersPerSecond = 30f;
    [Min(0f)]
    [SerializeField] private float acceleratedLettersPerSecond = 120f;

    private Coroutine revealCoroutine;
    private bool hasAcceleratedReveal;
    private bool skipToEndRequested;
    private string currentFullText;
    private float currentLettersPerSecond;
    private DialogueSequence activeSequence;
    private int activeSequenceLineIndex;
    private DialogueLine activeLine;
    private bool isWaitingForTutorialInteraction;

    public event System.Action<TutorialInteractionRequest> OnTutorialInteractionRequested;
    public event System.Action OnTutorialInteractionCompleted;

    public bool IsRevealing { get; private set; }
    public bool IsVisible => rootContainer != null && rootContainer.activeSelf;
    public bool IsAcceleratingReveal => IsRevealing && hasAcceleratedReveal;
    public bool IsWaitingForTutorialInteraction => isWaitingForTutorialInteraction;

    private void Awake()
    {
        if (!ValidateCoreReferences())
        {
            return;
        }

        HideDialogue();
    }

    private void Update()
    {
        if (!IsVisible)
        {
            return;
        }

        if (!Input.GetMouseButtonDown(0))
        {
            return;
        }

        if (IsRevealing)
        {
            if (!hasAcceleratedReveal)
            {
                hasAcceleratedReveal = true;
                return;
            }

            skipToEndRequested = true;
            return;
        }

        if (isWaitingForTutorialInteraction)
        {
            HideDialogueForPendingInteraction();
            return;
        }

        AdvanceDialogue();
    }

    public void ShowDialogue(DialogueLine line)
    {
        ResetSequenceProgress();
        ShowDialogueLine(line);
    }

    public void ShowDialogue(DialogueSequence sequence, int lineIndex = 0)
    {
        if (sequence == null)
        {
            ResetSequenceProgress();
            ShowDialogue(string.Empty);
            return;
        }

        activeSequence = sequence;
        activeSequenceLineIndex = Mathf.Max(0, lineIndex);
        ShowDialogueLineFromActiveSequence();
    }

    public void ShowDialogue(string text, Sprite portrait = null, string speakerName = null)
    {
        ResetSequenceProgress();
        activeLine = null;
        ShowDialogue(text, portrait, speakerName, false, lettersPerSecond);
    }

    public void ResumeAfterTutorialInteraction()
    {
        if (!isWaitingForTutorialInteraction)
        {
            return;
        }

        isWaitingForTutorialInteraction = false;
        OnTutorialInteractionCompleted?.Invoke();
        AdvanceDialogue();
    }

    private void ShowDialogue(string text, Sprite portrait, string speakerName, bool useCustomRevealSpeed, float lineRevealSpeed)
    {
        if (!ValidateCoreReferences())
        {
            return;
        }

        if (revealCoroutine != null)
        {
            StopCoroutine(revealCoroutine);
            revealCoroutine = null;
        }

        rootContainer.SetActive(true);
        isWaitingForTutorialInteraction = false;

        if (portraitImage != null)
        {
            portraitImage.sprite = portrait;
            portraitImage.enabled = portrait != null;
        }

        SetSpeakerName(speakerName);
        currentFullText = text ?? string.Empty;
        currentLettersPerSecond = useCustomRevealSpeed ? Mathf.Max(0f, lineRevealSpeed) : lettersPerSecond;
        dialogueText.text = string.Empty;
        hasAcceleratedReveal = false;
        skipToEndRequested = false;
        SetContinueHintVisible(false);

        revealCoroutine = StartCoroutine(RevealTextCoroutine());
    }

    public void HideDialogue()
    {
        if (revealCoroutine != null)
        {
            StopCoroutine(revealCoroutine);
            revealCoroutine = null;
        }

        IsRevealing = false;
        hasAcceleratedReveal = false;
        skipToEndRequested = false;
        isWaitingForTutorialInteraction = false;
        currentFullText = string.Empty;
        currentLettersPerSecond = lettersPerSecond;
        ResetSequenceProgress();
        SetContinueHintVisible(false);
        SetSpeakerName(null);

        if (dialogueText != null)
        {
            dialogueText.text = string.Empty;
        }

        if (rootContainer != null)
        {
            rootContainer.SetActive(false);
        }
    }

    private IEnumerator RevealTextCoroutine()
    {
        IsRevealing = true;

        for (var i = 0; i < currentFullText.Length; i++)
        {
            if (skipToEndRequested)
            {
                dialogueText.text = currentFullText;
                break;
            }

            dialogueText.text += currentFullText[i];

            var revealSpeed = hasAcceleratedReveal ? acceleratedLettersPerSecond : currentLettersPerSecond;
            if (revealSpeed <= 0f)
            {
                continue;
            }

            yield return new WaitForSeconds(1f / revealSpeed);
        }

        dialogueText.text = currentFullText;
        IsRevealing = false;
        hasAcceleratedReveal = false;
        skipToEndRequested = false;
        revealCoroutine = null;

        if (TryRequestTutorialInteraction())
        {
            SetContinueHintVisible(false);
            yield break;
        }

        SetContinueHintVisible(true);
    }

    private bool ValidateCoreReferences()
    {
        if (rootContainer == null)
        {
            Debug.LogWarning($"{nameof(CharacterDialogueUI)} on '{name}' is missing {nameof(rootContainer)} reference.", this);
            return false;
        }

        if (dialogueText == null)
        {
            Debug.LogWarning($"{nameof(CharacterDialogueUI)} on '{name}' is missing {nameof(dialogueText)} reference.", this);
            return false;
        }

        return true;
    }

    private void AdvanceDialogue()
    {
        if (activeSequence == null)
        {
            HideDialogue();
            return;
        }

        activeSequenceLineIndex++;
        ShowDialogueLineFromActiveSequence();
    }

    private void ShowDialogueLineFromActiveSequence()
    {
        if (activeSequence == null)
        {
            HideDialogue();
            return;
        }

        var line = activeSequence.GetLine(activeSequenceLineIndex);
        ShowDialogueLine(line);
    }

    private void ResetSequenceProgress()
    {
        activeSequence = null;
        activeSequenceLineIndex = 0;
    }

    private void ShowDialogueLine(DialogueLine line)
    {
        activeLine = line;
        if (line == null)
        {
            HideDialogue();
            return;
        }

        ShowDialogue(line.text, line.portrait, line.speakerName, line.useCustomRevealSpeed, line.revealLettersPerSecond);
    }

    private bool TryRequestTutorialInteraction()
    {
        var requirement = activeLine?.interactionRequirement;
        if (requirement == null || !requirement.enabled)
        {
            isWaitingForTutorialInteraction = false;
            return false;
        }

        isWaitingForTutorialInteraction = true;
        OnTutorialInteractionRequested?.Invoke(new TutorialInteractionRequest(requirement.allowedInteractionIds, requirement.requiredInteractionCount));
        return true;
    }

    private void HideDialogueForPendingInteraction()
    {
        if (rootContainer == null || !rootContainer.activeSelf)
        {
            return;
        }

        SetContinueHintVisible(false);
        rootContainer.SetActive(false);
    }

    private void SetContinueHintVisible(bool isVisible)
    {
        if (continueHintText == null)
        {
            return;
        }

        continueHintText.text = "Press left click to continue";
        continueHintText.gameObject.SetActive(isVisible);
    }

    private void SetSpeakerName(string speakerName)
    {
        if (speakerNameText == null)
        {
            return;
        }

        var hasSpeaker = !string.IsNullOrWhiteSpace(speakerName);
        speakerNameText.text = hasSpeaker ? $"{speakerName}:" : string.Empty;
        speakerNameText.gameObject.SetActive(hasSpeaker);
    }
}
