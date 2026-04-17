using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class GameplayDecisionUI : MonoBehaviour
{
    private const string OpenManualInteractionId = "open_manual";
    private const string CloseManualInteractionId = "close_manual";
    private const string AcceptTrainInteractionId = "accept_train";
    private const string RejectTrainInteractionId = "reject_train";

    [Header("Manual UI")]
    [SerializeField] private GameObject manualMenuRoot;
    [SerializeField] private Button manualToggleButton;
    [SerializeField] private Button manualCloseButton;

    [Header("Decision Buttons")]
    [SerializeField] private Button acceptTrainButton;
    [SerializeField] private Button rejectTrainButton;
    [SerializeField] private Button readyToDecideButton;
    [SerializeField] private TMP_Text readyToDecideLabel;

    [Header("Lives UI")]
    [SerializeField] private Image[] lifeHeartIcons = Array.Empty<Image>();
    [SerializeField] private Sprite heartFullIcon;
    [SerializeField] private Sprite heartEmptyIcon;

    [Header("Decision Feedback Popup")]
    [SerializeField] private GameObject decisionFeedbackPopupRoot;
    [SerializeField] private TMP_Text decisionFeedbackTitleText;
    [SerializeField] private TMP_Text decisionFeedbackBodyText;
    [SerializeField] private TMP_Text decisionFeedbackLoadingText;
    [SerializeField] private Button decisionFeedbackCloseButton;

    public event Action<IReadOnlyList<int>> OnAcceptTrainRequested;
    public event Action<IReadOnlyList<int>> OnRejectTrainRequested;
    public event Action<bool> OnManualVisibilityChanged;
    public event Action<string> OnTutorialInteractionPerformed;

    private readonly List<bool> resolvedTrainStates = new();
    private bool isReadyToDecide;
    private bool hasTutorialInteractionLock;
    private readonly HashSet<string> allowedTutorialInteractions = new();

    private void Awake()
    {
        EnsureDecisionFeedbackPopupReferences();
        HideManual();
        HideDecisionFeedbackPopup();
        UpdateReadyToDecideLabel();
        RefreshDecisionButtonState();
        RefreshLives(0);
    }

    private void OnEnable()
    {
        if (manualToggleButton != null)
        {
            manualToggleButton.onClick.AddListener(ToggleManual);
        }

        if (manualCloseButton != null)
        {
            manualCloseButton.onClick.AddListener(HideManualFromCloseButton);
        }

        if (acceptTrainButton != null)
        {
            acceptTrainButton.onClick.AddListener(AcceptCurrentTrain);
        }

        if (rejectTrainButton != null)
        {
            rejectTrainButton.onClick.AddListener(RejectCurrentTrain);
        }

        if (readyToDecideButton != null)
        {
            readyToDecideButton.onClick.AddListener(ToggleReadyToDecide);
        }

        if (decisionFeedbackCloseButton != null)
        {
            decisionFeedbackCloseButton.onClick.AddListener(HideDecisionFeedbackPopup);
        }

        RefreshDecisionButtonState();
    }

    private void OnDisable()
    {
        if (manualToggleButton != null)
        {
            manualToggleButton.onClick.RemoveListener(ToggleManual);
        }

        if (manualCloseButton != null)
        {
            manualCloseButton.onClick.RemoveListener(HideManualFromCloseButton);
        }

        if (acceptTrainButton != null)
        {
            acceptTrainButton.onClick.RemoveListener(AcceptCurrentTrain);
        }

        if (rejectTrainButton != null)
        {
            rejectTrainButton.onClick.RemoveListener(RejectCurrentTrain);
        }

        if (readyToDecideButton != null)
        {
            readyToDecideButton.onClick.RemoveListener(ToggleReadyToDecide);
        }

        if (decisionFeedbackCloseButton != null)
        {
            decisionFeedbackCloseButton.onClick.RemoveListener(HideDecisionFeedbackPopup);
        }
    }

    public void ShowDecisionFeedbackLoading()
    {
        EnsureDecisionFeedbackPopupReferences();
        if (decisionFeedbackPopupRoot == null)
        {
            return;
        }

        decisionFeedbackPopupRoot.SetActive(true);
        if (decisionFeedbackTitleText != null)
        {
            decisionFeedbackTitleText.text = "Analyzing decision...";
        }

        if (decisionFeedbackBodyText != null)
        {
            decisionFeedbackBodyText.text = string.Empty;
        }

        if (decisionFeedbackLoadingText != null)
        {
            decisionFeedbackLoadingText.gameObject.SetActive(true);
            decisionFeedbackLoadingText.text = "Loading analysis...";
        }
    }

    public void ShowDecisionFeedback(string wrongReason)
    {
        EnsureDecisionFeedbackPopupReferences();
        if (decisionFeedbackPopupRoot == null)
        {
            return;
        }

        decisionFeedbackPopupRoot.SetActive(true);
        if (decisionFeedbackTitleText != null)
        {
            decisionFeedbackTitleText.text = "Incorrect Decision";
        }

        if (decisionFeedbackBodyText != null)
        {
            decisionFeedbackBodyText.text = $"Why this is wrong:\n{wrongReason}";
        }

        if (decisionFeedbackLoadingText != null)
        {
            decisionFeedbackLoadingText.gameObject.SetActive(false);
        }
    }

    public void HideDecisionFeedbackPopup()
    {
        if (decisionFeedbackPopupRoot != null)
        {
            decisionFeedbackPopupRoot.SetActive(false);
        }
    }

    public void ToggleManual()
    {
        if (manualMenuRoot == null)
        {
            return;
        }

        if (manualMenuRoot.activeSelf)
        {
            if (!IsTutorialInteractionAllowed(CloseManualInteractionId))
            {
                return;
            }

            HideManual();
            OnTutorialInteractionPerformed?.Invoke(CloseManualInteractionId);
            return;
        }

        if (!IsTutorialInteractionAllowed(OpenManualInteractionId))
        {
            return;
        }

        ShowManual();
        OnTutorialInteractionPerformed?.Invoke(OpenManualInteractionId);
    }

    public void ShowManual()
    {
        if (!IsTutorialInteractionAllowed(OpenManualInteractionId))
        {
            return;
        }

        if (manualMenuRoot == null)
        {
            return;
        }

        manualMenuRoot.SetActive(true);
        OnManualVisibilityChanged?.Invoke(true);
    }

    public void HideManual()
    {
        if (!IsTutorialInteractionAllowed(CloseManualInteractionId))
        {
            return;
        }

        if (manualMenuRoot == null)
        {
            return;
        }

        manualMenuRoot.SetActive(false);
        OnManualVisibilityChanged?.Invoke(false);
    }

    public void AcceptCurrentTrain()
    {
        if (!IsTutorialInteractionAllowed(AcceptTrainInteractionId))
        {
            return;
        }

        if (!IsReadyToSubmitDecision())
        {
            return;
        }

        var selectedIndices = GetUnresolvedTrainIndices();
        if (selectedIndices.Count == 0)
        {
            return;
        }

        Debug.Log("Accept train requested.");
        OnAcceptTrainRequested?.Invoke(selectedIndices);
        OnTutorialInteractionPerformed?.Invoke(AcceptTrainInteractionId);
    }

    public void RejectCurrentTrain()
    {
        if (!IsTutorialInteractionAllowed(RejectTrainInteractionId))
        {
            return;
        }

        if (!IsReadyToSubmitDecision())
        {
            return;
        }

        var selectedIndices = GetUnresolvedTrainIndices();
        if (selectedIndices.Count == 0)
        {
            return;
        }

        Debug.Log("Reject train requested.");
        OnRejectTrainRequested?.Invoke(selectedIndices);
        OnTutorialInteractionPerformed?.Invoke(RejectTrainInteractionId);
    }

    private void HideManualFromCloseButton()
    {
        if (!IsTutorialInteractionAllowed(CloseManualInteractionId))
        {
            return;
        }

        HideManual();
        OnTutorialInteractionPerformed?.Invoke(CloseManualInteractionId);
    }

    public void SetTutorialInteractionLock(bool isLocked, IReadOnlyCollection<string> allowedInteractionIds)
    {
        hasTutorialInteractionLock = isLocked;
        allowedTutorialInteractions.Clear();

        if (isLocked && allowedInteractionIds != null)
        {
            foreach (var interactionId in allowedInteractionIds)
            {
                if (!string.IsNullOrWhiteSpace(interactionId))
                {
                    allowedTutorialInteractions.Add(interactionId.Trim());
                }
            }
        }

        RefreshTutorialLockState();
    }

    public void ConfigureTrainSelection(int trainCount)
    {
        resolvedTrainStates.Clear();

        for (var i = 0; i < trainCount; i++)
        {
            resolvedTrainStates.Add(false);
        }

        ResetDecisionGate();
    }

    public void ConfigureTrainSelection(int trainCount, IReadOnlyList<int> selectableTrainIndices)
    {
        resolvedTrainStates.Clear();

        for (var i = 0; i < trainCount; i++)
        {
            resolvedTrainStates.Add(true);
        }

        if (selectableTrainIndices == null || selectableTrainIndices.Count == 0)
        {
            for (var i = 0; i < trainCount; i++)
            {
                resolvedTrainStates[i] = false;
            }
        }
        else
        {
            for (var i = 0; i < selectableTrainIndices.Count; i++)
            {
                var index = selectableTrainIndices[i];
                if (index >= 0 && index < trainCount)
                {
                    resolvedTrainStates[index] = false;
                }
            }
        }

        ResetDecisionGate();
    }

    public void ResolveSelectedTrains(IReadOnlyList<int> resolvedTrainIndices)
    {
        if (resolvedTrainIndices != null)
        {
            for (var i = 0; i < resolvedTrainIndices.Count; i++)
            {
                var index = resolvedTrainIndices[i];
                if (index >= 0 && index < resolvedTrainStates.Count)
                {
                    resolvedTrainStates[index] = true;
                }
            }
        }

        ResetDecisionGate();
    }

    private List<int> GetUnresolvedTrainIndices()
    {
        var unresolved = new List<int>();

        for (var i = 0; i < resolvedTrainStates.Count; i++)
        {
            if (!resolvedTrainStates[i])
            {
                unresolved.Add(i);
            }
        }

        return unresolved;
    }

    private bool IsReadyToSubmitDecision()
    {
        if (readyToDecideButton == null)
        {
            return true;
        }

        if (!hasTutorialInteractionLock)
        {
            return isReadyToDecide;
        }

        var tutorialAllowsDirectDecision =
            IsTutorialInteractionAllowed(AcceptTrainInteractionId) ||
            IsTutorialInteractionAllowed(RejectTrainInteractionId);

        return tutorialAllowsDirectDecision || isReadyToDecide;
    }

    private void ToggleReadyToDecide()
    {
        isReadyToDecide = !isReadyToDecide;
        UpdateReadyToDecideLabel();
        RefreshDecisionButtonState();
    }

    private void ResetDecisionGate()
    {
        isReadyToDecide = false;
        UpdateReadyToDecideLabel();
        RefreshDecisionButtonState();
    }

    private void RefreshDecisionButtonState()
    {
        var canDecide = IsReadyToSubmitDecision();
        if (acceptTrainButton != null)
        {
            acceptTrainButton.interactable = canDecide && IsTutorialInteractionAllowed(AcceptTrainInteractionId);
        }

        if (rejectTrainButton != null)
        {
            rejectTrainButton.interactable = canDecide && IsTutorialInteractionAllowed(RejectTrainInteractionId);
        }
    }

    private void RefreshTutorialLockState()
    {
        SetButtonTutorialInteractable(manualToggleButton, OpenManualInteractionId);
        SetButtonTutorialInteractable(manualCloseButton, CloseManualInteractionId);
        SetButtonTutorialInteractable(acceptTrainButton, AcceptTrainInteractionId);
        SetButtonTutorialInteractable(rejectTrainButton, RejectTrainInteractionId);
        RefreshDecisionButtonState();

        if (readyToDecideButton != null)
        {
            readyToDecideButton.interactable = !hasTutorialInteractionLock || IsTutorialInteractionAllowed("ready_to_decide");
        }
    }

    private void SetButtonTutorialInteractable(Button button, string interactionId)
    {
        if (button == null)
        {
            return;
        }

        if (hasTutorialInteractionLock && !IsTutorialInteractionAllowed(interactionId))
        {
            button.interactable = false;
            return;
        }

        if (button == acceptTrainButton || button == rejectTrainButton)
        {
            return;
        }

        button.interactable = true;
    }

    private bool IsTutorialInteractionAllowed(string interactionId)
    {
        return !hasTutorialInteractionLock || (interactionId != null && allowedTutorialInteractions.Contains(interactionId));
    }

    private void UpdateReadyToDecideLabel()
    {
        if (readyToDecideLabel == null)
        {
            return;
        }

        readyToDecideLabel.text = $"{(isReadyToDecide ? "Set Not" : "Set")} Ready To Decide";
    }

    public void RefreshLives(int livesRemaining)
    {
        if (lifeHeartIcons == null || lifeHeartIcons.Length == 0)
        {
            return;
        }

        var clampedLives = Mathf.Max(0, livesRemaining);
        for (var i = 0; i < lifeHeartIcons.Length; i++)
        {
            var heartIcon = lifeHeartIcons[i];
            if (heartIcon == null)
            {
                continue;
            }

            heartIcon.sprite = i < clampedLives ? heartFullIcon : heartEmptyIcon;
            heartIcon.enabled = heartIcon.sprite != null;
        }
    }

    private void EnsureDecisionFeedbackPopupReferences()
    {
        if (decisionFeedbackPopupRoot != null)
        {
            return;
        }

        var popupRoot = new GameObject("DecisionFeedbackPopup", typeof(RectTransform), typeof(Image));
        popupRoot.transform.SetParent(transform, false);

        var popupRect = popupRoot.GetComponent<RectTransform>();
        popupRect.anchorMin = new Vector2(0.5f, 0.5f);
        popupRect.anchorMax = new Vector2(0.5f, 0.5f);
        popupRect.sizeDelta = new Vector2(560f, 340f);

        var popupImage = popupRoot.GetComponent<Image>();
        popupImage.color = new Color(0f, 0f, 0f, 0.88f);

        decisionFeedbackPopupRoot = popupRoot;
        decisionFeedbackTitleText = CreatePopupText("Title", popupRoot.transform, new Vector2(0f, 130f), 34, TextAlignmentOptions.Center);
        decisionFeedbackBodyText = CreatePopupText("Body", popupRoot.transform, new Vector2(0f, 18f), 24, TextAlignmentOptions.TopLeft);
        decisionFeedbackBodyText.rectTransform.sizeDelta = new Vector2(500f, 190f);
        decisionFeedbackLoadingText = CreatePopupText("Loading", popupRoot.transform, new Vector2(0f, -120f), 24, TextAlignmentOptions.Center);

        var closeButtonObject = new GameObject("CloseButton", typeof(RectTransform), typeof(Image), typeof(Button));
        closeButtonObject.transform.SetParent(popupRoot.transform, false);
        var closeButtonRect = closeButtonObject.GetComponent<RectTransform>();
        closeButtonRect.anchoredPosition = new Vector2(0f, -145f);
        closeButtonRect.sizeDelta = new Vector2(200f, 44f);
        closeButtonObject.GetComponent<Image>().color = new Color(0.22f, 0.22f, 0.22f, 1f);
        decisionFeedbackCloseButton = closeButtonObject.GetComponent<Button>();

        var closeLabel = CreatePopupText("CloseLabel", closeButtonObject.transform, Vector2.zero, 24, TextAlignmentOptions.Center);
        closeLabel.text = "Close";
    }

    private static TMP_Text CreatePopupText(
        string objectName,
        Transform parent,
        Vector2 anchoredPosition,
        int fontSize,
        TextAlignmentOptions alignment)
    {
        var textObject = new GameObject(objectName, typeof(RectTransform), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(parent, false);
        var rect = textObject.GetComponent<RectTransform>();
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = new Vector2(500f, 80f);

        var text = textObject.GetComponent<TextMeshProUGUI>();
        text.fontSize = fontSize;
        text.alignment = alignment;
        text.color = Color.white;
        text.text = string.Empty;
        text.enableWordWrapping = true;
        return text;
    }
}
