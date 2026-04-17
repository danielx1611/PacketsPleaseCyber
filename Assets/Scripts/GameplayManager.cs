using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class GameplayManager : MonoBehaviour
{
    public enum GameState
    {
        Idle,
        Dialogue,
        PlayerAction,
        AwaitingTrigger,
        LevelComplete
    }

    public enum DifficultyPreset
    {
        Easy,
        Normal,
        Hard,
        Custom
    }

    public enum RequiredDecision
    {
        None,
        Accept,
        Reject,
        Either
    }

    [Serializable]
    public class WinConditions
    {
        [Min(0)]
        public int requiredInspectionsComplete;

        public bool requiresDecision = true;
        public RequiredDecision requiredDecision = RequiredDecision.Either;
    }

    [Serializable]
    public class LevelConfig
    {
        [Min(0)]
        public int levelId;

        public DifficultyPreset difficultyPreset = DifficultyPreset.Normal;

        [Range(0.1f, 5f)]
        public float customDifficultyScalar = 1f;

        [Tooltip("Manual page indexes to unlock at level start.")]
        public List<int> manualPagesToUnlock = new();

        [Header("Dialogue Flow")]
        public LevelDialoguePlan dialoguePlan;

        [Header("Advance Conditions")]
        public WinConditions winConditions = new();
        [Tooltip("Trigger id raised when this level is considered complete.")]
        public string levelCompletionTriggerId = "level_complete";

        [Header("Train Waves")]
        public List<TrainWaveConfig> trainWaves = new();
    }

    [Serializable]
    public class TrainCompartmentWaveData
    {
        public TrainInspectable compartment;
        public string payload;
    }

    [Serializable]
    public class TrainWaveConfig
    {
        [Tooltip("Trigger id required to run this wave. Leave empty to run as a default sequential wave.")]
        public string triggerId;
        [Tooltip("Marks this entire wave as suspicious. Accepting this wave will log a warning.")]
        public bool isSuspicious;
        [Tooltip("If enabled, this wave will trigger the very next configured wave when resolved.")]
        public bool triggerFollowingWaveInList;
        public List<int> trainIndices = new();
        public bool startAllSelectedTrainsAtOnce = true;
        [Min(0f)] public float delayBeforeWaveStarts;
        public bool snapSelectedTrainsToOffScreenLeftBeforeEntry = true;
        public List<TrainCompartmentWaveData> compartmentData = new();
    }

    [Header("Core References")]
    [SerializeField] private GameplayManual gameplayManual;
    [SerializeField] private GameplayDecisionUI gameplayDecisionUI;
    [SerializeField] private CharacterDialogueUI characterDialogueUI;

    [Header("Optional External Controllers")]
    [SerializeField] private TrainController trainController;
    [SerializeField] private MonoBehaviour inspectionController;
    [SerializeField] private CyberAIPacketJudge cyberAIPacketJudge;

    [Header("Progression Configuration")]
    [SerializeField] private bool autoStartFirstLevelOnStart = true;
    [SerializeField] private int initialLevelIndex;

    [Tooltip("Global multiplier applied on top of each level difficulty.")]
    [SerializeField] private float globalDifficultyMultiplier = 1f;

    [Header("Scene Flow")]
    [SerializeField] private string winSceneName = "YouWinScreen";
    [SerializeField] private string loseSceneName = "YouLoseScreen";

    [Header("Player Health")]
    [SerializeField] private int maxLives = 3;

    [SerializeField] private List<LevelConfig> levels = new();

    public GameState CurrentState { get; private set; } = GameState.Idle;
    public int CurrentLevelIndex { get; private set; } = -1;
    public float CurrentDifficultyScalar { get; private set; } = 1f;

    private readonly Dictionary<string, LevelDialoguePlan.DialogueBlock> blocksById = new(StringComparer.Ordinal);
    private readonly HashSet<string> completedBlocks = new(StringComparer.Ordinal);
    private readonly HashSet<string> startedBlocks = new(StringComparer.Ordinal);
    private readonly HashSet<string> activeTutorialAllowedInteractions = new(StringComparer.Ordinal);
    private readonly HashSet<string> completedTutorialInteractions = new(StringComparer.Ordinal);

    private int completedInspections;
    private bool decisionReceived;
    private RequiredDecision latestDecision = RequiredDecision.None;
    private bool levelWinConditionsMet;
    private string activeDialogueBlockId;
    private LevelDialoguePlan activeDialoguePlan;
    private InspectButton[] inspectButtons = Array.Empty<InspectButton>();
    private InspectionPopupUI[] inspectionPopups = Array.Empty<InspectionPopupUI>();
    private bool tutorialInteractionLockActive;
    private int tutorialInteractionsRequired;
    private int tutorialInteractionProgress;
    private Coroutine pendingWaveAdvanceCoroutine;
    private int currentTrainWaveIndex;
    private TrainWaveConfig activeTrainWave;
    private bool skipIntroDialogueForCurrentLevel;
    private bool startAtFirstFollowingWaveForCurrentLevel;
    private int currentLives;

    private void OnEnable()
    {
        cyberAIPacketJudge ??= FindObjectOfType<CyberAIPacketJudge>();
        CacheInspectButtons();
        CacheInspectionPopups();
        SubscribeToEvents();
    }

    private void Start()
    {
        if (!autoStartFirstLevelOnStart)
        {
            return;
        }

        if (GameplaySessionState.ConsumeRequestedStartLevel(
                out var requestedLevelIndex,
                out var skipFirstLevelTutorial,
                out var startAtFirstFollowingWave))
        {
            skipIntroDialogueForCurrentLevel = skipFirstLevelTutorial;
            startAtFirstFollowingWaveForCurrentLevel = startAtFirstFollowingWave;
            StartLevel(requestedLevelIndex);
            return;
        }

        skipIntroDialogueForCurrentLevel = false;
        startAtFirstFollowingWaveForCurrentLevel = false;
        StartLevel(initialLevelIndex);
    }

    private void Update()
    {
        if (CurrentState != GameState.Dialogue || characterDialogueUI == null)
        {
            return;
        }

        if (characterDialogueUI.IsVisible || characterDialogueUI.IsRevealing || characterDialogueUI.IsWaitingForTutorialInteraction)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(activeDialogueBlockId))
        {
            OnDialogueBlockCompleted(activeDialogueBlockId);
        }
    }

    private void OnDisable()
    {
        UnsubscribeFromEvents();
    }

    public void StartLevel(int index)
    {
        if (!TryGetLevel(index, out var config))
        {
            Debug.LogWarning($"{nameof(GameplayManager)} could not start level index {index}. Check level list configuration.", this);
            return;
        }

        CurrentLevelIndex = index;
        GameplaySessionState.SetLastKnownLevelIndex(CurrentLevelIndex);
        ResetLevelProgressTracking();

        ApplyDifficultySettings(config);
        ApplyManualUnlocks(config);
        InitializeDialoguePlan(config.dialoguePlan);
        ClearTutorialInteractionLock();
        trainController?.ResetForLevelStart();
        currentTrainWaveIndex = 0;
        activeTrainWave = null;
        var startAtFirstFollowingWave = startAtFirstFollowingWaveForCurrentLevel && CurrentLevelIndex == initialLevelIndex;
        startAtFirstFollowingWaveForCurrentLevel = false;
        if (startAtFirstFollowingWave)
        {
            if (!TryStartFirstTriggerFollowingTrainWave())
            {
                TryStartNextTrainWaveForTrigger(string.Empty);
            }
        }
        else
        {
            TryStartNextTrainWaveForTrigger(string.Empty);
        }

        var shouldSkipIntroDialogue = skipIntroDialogueForCurrentLevel && CurrentLevelIndex == initialLevelIndex;
        skipIntroDialogueForCurrentLevel = false;

        if (shouldSkipIntroDialogue || !TryStartAutoDialogueForTrigger(string.Empty))
        {
            SetState(GameState.PlayerAction);
        }
    }

    public void AdvanceToNextLevel()
    {
        var nextIndex = CurrentLevelIndex + 1;
        if (!TryGetLevel(nextIndex, out _))
        {
            SetState(GameState.LevelComplete);
            Debug.Log("No additional levels configured. Campaign complete.", this);
            return;
        }

        StartLevel(nextIndex);
    }

    public void RestartLevel()
    {
        if (CurrentLevelIndex < 0)
        {
            StartLevel(initialLevelIndex);
            return;
        }

        StartLevel(CurrentLevelIndex);
    }

    public void NotifyInspectionCompleted()
    {
        if (CurrentState != GameState.PlayerAction && CurrentState != GameState.AwaitingTrigger)
        {
            return;
        }

        completedInspections++;
        EvaluateLevelProgression();
    }

    public void NotifyExternalTriggerSatisfied()
    {
        if (CurrentState != GameState.AwaitingTrigger)
        {
            return;
        }

        EvaluateLevelProgression();
    }

    public void StartDialogueBlock(string blockId)
    {
        if (CurrentState == GameState.Dialogue)
        {
            return;
        }

        if (characterDialogueUI == null)
        {
            Debug.LogWarning($"{nameof(GameplayManager)} cannot start dialogue block without {nameof(characterDialogueUI)}.", this);
            return;
        }

        if (!TryGetDialogueBlock(blockId, out var block))
        {
            Debug.LogWarning($"{nameof(GameplayManager)} could not find dialogue block '{blockId}'.", this);
            return;
        }

        if (block.sequence == null)
        {
            Debug.LogWarning($"Dialogue block '{blockId}' has no {nameof(DialogueSequence)} assigned.", this);
            return;
        }

        if (completedBlocks.Contains(blockId))
        {
            return;
        }

        startedBlocks.Add(blockId);
        activeDialogueBlockId = blockId;
        SetState(GameState.Dialogue);
        characterDialogueUI.ShowDialogue(block.sequence, block.startingLineIndex);
    }

    public void OnDialogueBlockCompleted(string blockId)
    {
        if (!string.Equals(activeDialogueBlockId, blockId, StringComparison.Ordinal))
        {
            return;
        }

        activeDialogueBlockId = null;
        completedBlocks.Add(blockId);
        ClearTutorialInteractionLock();

        var completionTriggerId = GetDialogueBlockCompletionTrigger(blockId);
        if (!string.IsNullOrWhiteSpace(completionTriggerId))
        {
            SetState(GameState.PlayerAction);
            ResumeDialogueAfterTrigger(completionTriggerId);
            return;
        }

        if (levelWinConditionsMet)
        {
            var levelCompletionTriggerId = GetCurrentLevelCompletionTriggerId();
            if (!TryStartAutoDialogueForTrigger(levelCompletionTriggerId))
            {
                SetState(GameState.LevelComplete);
            }

            return;
        }

        SetState(GameState.PlayerAction);
    }

    public void ResumeDialogueAfterTrigger(string triggerId)
    {
        var normalizedTriggerId = triggerId?.Trim() ?? string.Empty;
        TryStartNextTrainWaveForTrigger(normalizedTriggerId);

        if (TryStartAutoDialogueForTrigger(normalizedTriggerId))
        {
            return;
        }

        if (CurrentState == GameState.AwaitingTrigger)
        {
            EvaluateLevelProgression();
            return;
        }

        if (levelWinConditionsMet && string.Equals(normalizedTriggerId, GetCurrentLevelCompletionTriggerId(), StringComparison.Ordinal))
        {
            SetState(GameState.LevelComplete);
            return;
        }

        if (CurrentState == GameState.Dialogue)
        {
            SetState(GameState.PlayerAction);
        }
    }

    private void SubscribeToEvents()
    {
        if (characterDialogueUI != null)
        {
            characterDialogueUI.OnTutorialInteractionRequested += HandleTutorialInteractionRequested;
            characterDialogueUI.OnTutorialInteractionCompleted += HandleTutorialInteractionCompleted;
        }

        if (gameplayDecisionUI == null)
        {
            SubscribeToInspectButtons();
            return;
        }

        gameplayDecisionUI.OnAcceptTrainRequested += HandleAcceptTrainRequested;
        gameplayDecisionUI.OnRejectTrainRequested += HandleRejectTrainRequested;
        gameplayDecisionUI.OnTutorialInteractionPerformed += HandleTutorialInteractionPerformed;
        SubscribeToInspectButtons();
    }

    private void UnsubscribeFromEvents()
    {
        if (characterDialogueUI != null)
        {
            characterDialogueUI.OnTutorialInteractionRequested -= HandleTutorialInteractionRequested;
            characterDialogueUI.OnTutorialInteractionCompleted -= HandleTutorialInteractionCompleted;
        }

        UnsubscribeFromInspectButtons();

        if (gameplayDecisionUI == null)
        {
            return;
        }

        gameplayDecisionUI.OnAcceptTrainRequested -= HandleAcceptTrainRequested;
        gameplayDecisionUI.OnRejectTrainRequested -= HandleRejectTrainRequested;
        gameplayDecisionUI.OnTutorialInteractionPerformed -= HandleTutorialInteractionPerformed;
    }

    private void HandleAcceptTrainRequested(IReadOnlyList<int> selectedTrainIndices)
    {
        if (!CanHandleDecisionRequest())
        {
            return;
        }

        if (selectedTrainIndices == null || selectedTrainIndices.Count == 0)
        {
            return;
        }

        trainController?.SendAcceptedTrainsOffScreen(selectedTrainIndices);
        gameplayDecisionUI?.ResolveSelectedTrains(selectedTrainIndices);
        decisionReceived = true;
        latestDecision = RequiredDecision.Accept;

        if (activeTrainWave != null && activeTrainWave.isSuspicious)
        {
            Debug.LogWarning("A suspicious train wave was accepted.", this);
        }

        var isCorrectDecision = activeTrainWave == null || !activeTrainWave.isSuspicious;
        ShowWrongDecisionFeedbackIfNeeded(playerAccepted: true, isCorrectDecision);
        if (!HandleDecisionHealthResult(isCorrectDecision))
        {
            return;
        }

        if (CurrentState == GameState.PlayerAction)
        {
            SetState(GameState.AwaitingTrigger);
        }

        QueueWaveAdvanceAfterTrainMovement();

        EvaluateLevelProgression();
    }

    private void HandleRejectTrainRequested(IReadOnlyList<int> selectedTrainIndices)
    {
        if (!CanHandleDecisionRequest())
        {
            return;
        }

        if (selectedTrainIndices == null || selectedTrainIndices.Count == 0)
        {
            return;
        }

        trainController?.SendRejectedTrainsOffScreen(selectedTrainIndices);
        gameplayDecisionUI?.ResolveSelectedTrains(selectedTrainIndices);
        decisionReceived = true;
        latestDecision = RequiredDecision.Reject;

        var isCorrectDecision = activeTrainWave != null && activeTrainWave.isSuspicious;
        ShowWrongDecisionFeedbackIfNeeded(playerAccepted: false, isCorrectDecision);
        if (!HandleDecisionHealthResult(isCorrectDecision))
        {
            return;
        }

        if (CurrentState == GameState.PlayerAction)
        {
            SetState(GameState.AwaitingTrigger);
        }

        QueueWaveAdvanceAfterTrainMovement();

        EvaluateLevelProgression();
    }

    private void QueueWaveAdvanceAfterTrainMovement()
    {
        if (pendingWaveAdvanceCoroutine != null)
        {
            StopCoroutine(pendingWaveAdvanceCoroutine);
        }

        pendingWaveAdvanceCoroutine = StartCoroutine(AdvanceWaveAfterTrainMovement());
    }

    private System.Collections.IEnumerator AdvanceWaveAfterTrainMovement()
    {
        yield return null;

        while (trainController != null && trainController.IsTrainMoving)
        {
            yield return null;
        }

        var startedNextWave = activeTrainWave != null && activeTrainWave.triggerFollowingWaveInList
            ? TryStartFollowingTrainWave()
            : TryStartNextTrainWaveForTrigger(string.Empty);

        if (!startedNextWave && !HasRemainingTrainWaves())
        {
            levelWinConditionsMet = true;
            ResumeDialogueAfterTrigger(GetCurrentLevelCompletionTriggerId());
        }

        pendingWaveAdvanceCoroutine = null;
    }

    private bool CanHandleDecisionRequest()
    {
        if (CurrentState == GameState.PlayerAction)
        {
            return true;
        }

        return CurrentState == GameState.Dialogue &&
               characterDialogueUI != null &&
               characterDialogueUI.IsWaitingForTutorialInteraction;
    }

    private void EvaluateLevelProgression()
    {
        if (!TryGetLevel(CurrentLevelIndex, out var config))
        {
            return;
        }

        if (!AreWinConditionsMet(config.winConditions))
        {
            if (CurrentState == GameState.AwaitingTrigger)
            {
                SetState(GameState.PlayerAction);
            }

            return;
        }

        levelWinConditionsMet = true;
        ResumeDialogueAfterTrigger(GetCurrentLevelCompletionTriggerId());
    }

    private bool AreWinConditionsMet(WinConditions conditions)
    {
        if (conditions == null)
        {
            return true;
        }

        if (completedInspections < conditions.requiredInspectionsComplete)
        {
            return false;
        }

        if (!conditions.requiresDecision)
        {
            return true;
        }

        if (!decisionReceived)
        {
            return false;
        }

        return conditions.requiredDecision switch
        {
            RequiredDecision.None => true,
            RequiredDecision.Either => latestDecision == RequiredDecision.Accept || latestDecision == RequiredDecision.Reject,
            _ => latestDecision == conditions.requiredDecision
        };
    }

    private void InitializeDialoguePlan(LevelDialoguePlan plan)
    {
        activeDialoguePlan = plan;
        blocksById.Clear();
        completedBlocks.Clear();
        startedBlocks.Clear();
        activeDialogueBlockId = null;

        if (activeDialoguePlan == null)
        {
            return;
        }

        foreach (var block in activeDialoguePlan.GetAllBlocks())
        {
            if (block == null || string.IsNullOrWhiteSpace(block.blockId))
            {
                continue;
            }

            if (blocksById.ContainsKey(block.blockId))
            {
                Debug.LogWarning($"Duplicate dialogue block id '{block.blockId}' in {activeDialoguePlan.name}.", activeDialoguePlan);
                continue;
            }

            blocksById.Add(block.blockId, block);
        }
    }

    private bool TryStartAutoDialogueForTrigger(string triggerId)
    {
        if (CurrentState == GameState.Dialogue || activeDialoguePlan == null)
        {
            return false;
        }

        var normalizedTriggerId = triggerId?.Trim() ?? string.Empty;
        var useEndingBlocks = string.Equals(normalizedTriggerId, GetCurrentLevelCompletionTriggerId(), StringComparison.Ordinal);
        var blocks = activeDialoguePlan.GetBlocksForPhase(useEndingBlocks);

        if (blocks == null)
        {
            return false;
        }

        for (var i = 0; i < blocks.Count; i++)
        {
            var block = blocks[i];
            if (block == null || !block.autoStart || string.IsNullOrWhiteSpace(block.blockId))
            {
                continue;
            }

            if (startedBlocks.Contains(block.blockId) || completedBlocks.Contains(block.blockId))
            {
                continue;
            }

            var blockTrigger = block.triggerId?.Trim() ?? string.Empty;
            if (!string.Equals(blockTrigger, normalizedTriggerId, StringComparison.Ordinal))
            {
                continue;
            }

            StartDialogueBlock(block.blockId);
            return true;
        }

        return false;
    }

    private bool TryGetDialogueBlock(string blockId, out LevelDialoguePlan.DialogueBlock block)
    {
        block = null;
        if (string.IsNullOrWhiteSpace(blockId))
        {
            return false;
        }

        return blocksById.TryGetValue(blockId, out block);
    }

    private string GetDialogueBlockCompletionTrigger(string blockId)
    {
        if (!TryGetDialogueBlock(blockId, out var block))
        {
            return string.Empty;
        }

        return block?.completionTriggerId?.Trim() ?? string.Empty;
    }

    private void ResetLevelProgressTracking()
    {
        completedInspections = 0;
        decisionReceived = false;
        latestDecision = RequiredDecision.None;
        levelWinConditionsMet = false;
        activeDialogueBlockId = null;
        ClearTutorialInteractionLock();
        currentLives = Mathf.Max(1, maxLives);
        gameplayDecisionUI?.RefreshLives(currentLives);
    }

    private bool HandleDecisionHealthResult(bool isCorrectDecision)
    {
        if (isCorrectDecision)
        {
            return true;
        }

        currentLives = Mathf.Max(0, currentLives - 1);
        gameplayDecisionUI?.RefreshLives(currentLives);

        if (currentLives > 0)
        {
            return true;
        }

        LoadLoseScene();
        return false;
    }

    private void ShowWrongDecisionFeedbackIfNeeded(bool playerAccepted, bool isCorrectDecision)
    {
        if (isCorrectDecision)
        {
            gameplayDecisionUI?.HideDecisionFeedbackPopup();
            return;
        }

        gameplayDecisionUI?.ShowDecisionFeedbackLoading();

        if (cyberAIPacketJudge == null)
        {
            ShowFallbackWrongDecisionFeedback(playerAccepted);
            return;
        }

        var payload = BuildActiveWavePayloadSummary();
        StartCoroutine(cyberAIPacketJudge.RequestDecisionFeedback(
            payload,
            playerAccepted,
            activeTrainWave != null && activeTrainWave.isSuspicious,
            feedback =>
            {
                if (gameplayDecisionUI == null || feedback == null)
                {
                    return;
                }

                gameplayDecisionUI.ShowDecisionFeedback(feedback.wrongReason);
            }));
    }

    private string BuildActiveWavePayloadSummary()
    {
        if (activeTrainWave?.compartmentData == null || activeTrainWave.compartmentData.Count == 0)
        {
            return "No packet payload provided";
        }

        var payloadParts = new List<string>();
        for (var i = 0; i < activeTrainWave.compartmentData.Count; i++)
        {
            var data = activeTrainWave.compartmentData[i];
            if (data == null || string.IsNullOrWhiteSpace(data.payload))
            {
                continue;
            }

            payloadParts.Add(data.payload.Trim());
        }

        return payloadParts.Count > 0
            ? string.Join(" | ", payloadParts)
            : "No packet payload provided";
    }

    private void ShowFallbackWrongDecisionFeedback(bool playerAccepted)
    {
        if (gameplayDecisionUI == null)
        {
            return;
        }

        var actuallySuspicious = activeTrainWave != null && activeTrainWave.isSuspicious;
        if (playerAccepted && actuallySuspicious)
        {
            gameplayDecisionUI.ShowDecisionFeedback(
                "You accepted traffic that matched suspicious indicators.");
            return;
        }

        gameplayDecisionUI.ShowDecisionFeedback(
            "You rejected traffic that lacked suspicious indicators.");
    }

    private void ApplyDifficultySettings(LevelConfig config)
    {
        var levelScalar = config.difficultyPreset switch
        {
            DifficultyPreset.Easy => 0.8f,
            DifficultyPreset.Normal => 1f,
            DifficultyPreset.Hard => 1.35f,
            DifficultyPreset.Custom => config.customDifficultyScalar,
            _ => 1f
        };

        CurrentDifficultyScalar = Mathf.Max(0.1f, levelScalar * Mathf.Max(0.1f, globalDifficultyMultiplier));

        Debug.Log($"Starting level {config.levelId} (index {CurrentLevelIndex}) with difficulty scalar {CurrentDifficultyScalar:0.00}.", this);
    }

    private void ApplyManualUnlocks(LevelConfig config)
    {
        if (gameplayManual == null)
        {
            return;
        }

        gameplayManual.SetUnlockedPages(config.manualPagesToUnlock, config.levelId);
    }

    private bool TryStartNextTrainWaveForTrigger(string triggerId)
    {
        if (trainController == null || !TryGetLevel(CurrentLevelIndex, out var config))
        {
            gameplayDecisionUI?.ConfigureTrainSelection(trainController != null ? trainController.GetTrainCount() : 0);
            return false;
        }

        var waves = config.trainWaves;
        var normalizedTriggerId = triggerId?.Trim() ?? string.Empty;
        if (waves == null || waves.Count == 0)
        {
            var allTrainIndices = GetAllTrainIndices(trainController.GetTrainCount());
            trainController.SetVisibleTrains(allTrainIndices, hideUnselected: false);
            gameplayDecisionUI?.ConfigureTrainSelection(trainController.GetTrainCount(), allTrainIndices);
            return false;
        }

        for (var i = currentTrainWaveIndex; i < waves.Count; i++)
        {
            var wave = waves[i];
            if (wave == null)
            {
                continue;
            }

            var waveTriggerId = wave.triggerId?.Trim() ?? string.Empty;
            if (!string.Equals(waveTriggerId, normalizedTriggerId, StringComparison.Ordinal))
            {
                continue;
            }

            currentTrainWaveIndex = i + 1;
            ApplyTrainWave(wave);
            return true;
        }

        return false;
    }

    private void ApplyTrainWave(TrainWaveConfig wave)
    {
        activeTrainWave = wave;
        var trainCount = trainController != null ? trainController.GetTrainCount() : 0;
        var selectedIndices = GetValidTrainIndices(wave.trainIndices, trainCount);
        if (selectedIndices.Count == 0)
        {
            selectedIndices = GetAllTrainIndices(trainCount);
        }

        ApplyWaveCompartmentData(wave);
        trainController.SetVisibleTrains(selectedIndices);
        gameplayDecisionUI?.ConfigureTrainSelection(trainCount, selectedIndices);
        StartCoroutine(RunTrainWaveAfterDelay(wave, selectedIndices));
    }

    private bool TryStartFollowingTrainWave()
    {
        if (trainController == null || !TryGetLevel(CurrentLevelIndex, out var config))
        {
            return false;
        }

        var waves = config.trainWaves;
        if (waves == null || waves.Count == 0)
        {
            return false;
        }

        for (var i = currentTrainWaveIndex; i < waves.Count; i++)
        {
            var wave = waves[i];
            if (wave == null)
            {
                continue;
            }

            currentTrainWaveIndex = i + 1;
            ApplyTrainWave(wave);
            return true;
        }

        return false;
    }

    private bool TryStartFirstTriggerFollowingTrainWave()
    {
        if (trainController == null || !TryGetLevel(CurrentLevelIndex, out var config))
        {
            gameplayDecisionUI?.ConfigureTrainSelection(trainController != null ? trainController.GetTrainCount() : 0);
            return false;
        }

        var waves = config.trainWaves;
        if (waves == null || waves.Count == 0)
        {
            return false;
        }

        for (var i = 0; i < waves.Count; i++)
        {
            var wave = waves[i];
            if (wave == null || !wave.triggerFollowingWaveInList)
            {
                continue;
            }

            currentTrainWaveIndex = i + 1;
            ApplyTrainWave(wave);
            return true;
        }

        return false;
    }

    private bool HasRemainingTrainWaves()
    {
        if (!TryGetLevel(CurrentLevelIndex, out var config))
        {
            return false;
        }

        var waves = config.trainWaves;
        if (waves == null || waves.Count == 0)
        {
            return false;
        }

        for (var i = currentTrainWaveIndex; i < waves.Count; i++)
        {
            if (waves[i] != null)
            {
                return true;
            }
        }

        return false;
    }

    private System.Collections.IEnumerator RunTrainWaveAfterDelay(TrainWaveConfig wave, List<int> selectedIndices)
    {
        if (wave != null && wave.delayBeforeWaveStarts > 0f)
        {
            yield return new WaitForSeconds(wave.delayBeforeWaveStarts);
        }

        trainController?.BeginTrainEntry(
            selectedIndices,
            resetActiveMask: true,
            moveAllAtOnce: wave != null && wave.startAllSelectedTrainsAtOnce,
            snapToLeftBeforeMove: wave == null || wave.snapSelectedTrainsToOffScreenLeftBeforeEntry);
    }

    private void ApplyWaveCompartmentData(TrainWaveConfig wave)
    {
        if (wave?.compartmentData == null)
        {
            return;
        }

        for (var i = 0; i < wave.compartmentData.Count; i++)
        {
            var data = wave.compartmentData[i];
            data?.compartment?.SetPayload(data.payload);
        }
    }

    private static List<int> GetValidTrainIndices(IReadOnlyList<int> indices, int trainCount)
    {
        var validIndices = new List<int>();
        if (indices == null)
        {
            return validIndices;
        }

        for (var i = 0; i < indices.Count; i++)
        {
            var index = indices[i];
            if (index < 0 || index >= trainCount || validIndices.Contains(index))
            {
                continue;
            }

            validIndices.Add(index);
        }

        return validIndices;
    }

    private static List<int> GetAllTrainIndices(int trainCount)
    {
        var indices = new List<int>(trainCount);
        for (var i = 0; i < trainCount; i++)
        {
            indices.Add(i);
        }

        return indices;
    }

    private bool TryGetLevel(int index, out LevelConfig config)
    {
        config = null;
        if (levels == null || index < 0 || index >= levels.Count)
        {
            return false;
        }

        config = levels[index];
        return config != null;
    }

    private string GetCurrentLevelCompletionTriggerId()
    {
        if (!TryGetLevel(CurrentLevelIndex, out var config))
        {
            return "level_complete";
        }

        if (string.IsNullOrWhiteSpace(config.levelCompletionTriggerId))
        {
            return "level_complete";
        }

        return config.levelCompletionTriggerId.Trim();
    }

    private void SetState(GameState nextState)
    {
        CurrentState = nextState;

        if (nextState == GameState.LevelComplete && !HasAnotherLevelAfterCurrent())
        {
            LoadWinScene();
        }
    }

    private bool HasAnotherLevelAfterCurrent()
    {
        if (levels == null)
        {
            return false;
        }

        var nextLevelIndex = CurrentLevelIndex + 1;
        return nextLevelIndex >= 0 && nextLevelIndex < levels.Count && levels[nextLevelIndex] != null;
    }

    private void LoadWinScene()
    {
        if (string.IsNullOrWhiteSpace(winSceneName))
        {
            Debug.LogWarning($"{nameof(GameplayManager)} could not load the win scene because no scene name is configured.", this);
            return;
        }

        var activeScene = SceneManager.GetActiveScene();
        if (string.Equals(activeScene.name, winSceneName, StringComparison.Ordinal))
        {
            return;
        }

        SceneManager.LoadScene(winSceneName);
    }

    private void LoadLoseScene()
    {
        if (string.IsNullOrWhiteSpace(loseSceneName))
        {
            Debug.LogWarning($"{nameof(GameplayManager)} could not load the lose scene because no scene name is configured.", this);
            return;
        }

        var activeScene = SceneManager.GetActiveScene();
        if (string.Equals(activeScene.name, loseSceneName, StringComparison.Ordinal))
        {
            return;
        }

        SceneManager.LoadScene(loseSceneName);
    }

    private void HandleTutorialInteractionRequested(CharacterDialogueUI.TutorialInteractionRequest request)
    {
        activeTutorialAllowedInteractions.Clear();
        completedTutorialInteractions.Clear();
        tutorialInteractionProgress = 0;
        tutorialInteractionsRequired = request != null ? Mathf.Max(1, request.RequiredInteractionCount) : 1;

        if (request?.AllowedInteractionIds != null)
        {
            for (var i = 0; i < request.AllowedInteractionIds.Count; i++)
            {
                var interactionId = request.AllowedInteractionIds[i];
                if (!string.IsNullOrWhiteSpace(interactionId))
                {
                    activeTutorialAllowedInteractions.Add(interactionId.Trim());
                }
            }
        }

        tutorialInteractionLockActive = activeTutorialAllowedInteractions.Count > 0;
        ApplyTutorialInteractionLockState();

        if (!tutorialInteractionLockActive)
        {
            CompleteTutorialInteractionStep();
        }
    }

    private void HandleTutorialInteractionCompleted()
    {
        ClearTutorialInteractionLock();
    }

    private void HandleTutorialInteractionPerformed(string interactionId)
    {
        if (!tutorialInteractionLockActive || string.IsNullOrWhiteSpace(interactionId))
        {
            return;
        }

        if (!activeTutorialAllowedInteractions.Contains(interactionId))
        {
            return;
        }

        if (completedTutorialInteractions.Add(interactionId))
        {
            tutorialInteractionProgress++;
        }

        if (tutorialInteractionProgress >= tutorialInteractionsRequired)
        {
            CompleteTutorialInteractionStep();
        }
    }

    private void CompleteTutorialInteractionStep()
    {
        characterDialogueUI?.ResumeAfterTutorialInteraction();
    }

    private void ApplyTutorialInteractionLockState()
    {
        gameplayDecisionUI?.SetTutorialInteractionLock(tutorialInteractionLockActive, activeTutorialAllowedInteractions);

        for (var i = 0; i < inspectButtons.Length; i++)
        {
            var inspectButton = inspectButtons[i];
            if (inspectButton == null)
            {
                continue;
            }

            var isAllowed = !tutorialInteractionLockActive ||
                            activeTutorialAllowedInteractions.Contains(inspectButton.TutorialInteractionId);
            inspectButton.SetTutorialInteractionAllowed(tutorialInteractionLockActive, isAllowed);
        }
    }

    private void ClearTutorialInteractionLock()
    {
        tutorialInteractionLockActive = false;
        tutorialInteractionsRequired = 0;
        tutorialInteractionProgress = 0;
        activeTutorialAllowedInteractions.Clear();
        completedTutorialInteractions.Clear();
        ApplyTutorialInteractionLockState();
    }

    private void CacheInspectButtons()
    {
        inspectButtons = FindObjectsByType<InspectButton>(FindObjectsSortMode.None);
    }

    private void SubscribeToInspectButtons()
    {
        for (var i = 0; i < inspectButtons.Length; i++)
        {
            if (inspectButtons[i] != null)
            {
                inspectButtons[i].OnTutorialInteractionPerformed += HandleTutorialInteractionPerformed;
            }
        }

        for (var i = 0; i < inspectionPopups.Length; i++)
        {
            if (inspectionPopups[i] != null)
            {
                inspectionPopups[i].OnTutorialInteractionPerformed += HandleTutorialInteractionPerformed;
            }
        }
    }

    private void UnsubscribeFromInspectButtons()
    {
        for (var i = 0; i < inspectButtons.Length; i++)
        {
            if (inspectButtons[i] != null)
            {
                inspectButtons[i].OnTutorialInteractionPerformed -= HandleTutorialInteractionPerformed;
            }
        }

        for (var i = 0; i < inspectionPopups.Length; i++)
        {
            if (inspectionPopups[i] != null)
            {
                inspectionPopups[i].OnTutorialInteractionPerformed -= HandleTutorialInteractionPerformed;
            }
        }
    }

    private void CacheInspectionPopups()
    {
        inspectionPopups = FindObjectsByType<InspectionPopupUI>(FindObjectsSortMode.None);
    }
}
