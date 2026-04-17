using System;
using System.Collections.Generic;
using UnityEngine;

public class TrainController : MonoBehaviour
{
    [Serializable]
    public class TrainConfig
    {
        [SerializeField] private Transform trainRoot;
        [SerializeField] private float offScreenLeftX = -15f;
        [SerializeField] private float onScreenX = 0f;
        [SerializeField] private float offScreenRightX = 15f;

        public Transform TrainRoot => trainRoot;
        public float OffScreenLeftX => offScreenLeftX;
        public float OnScreenX => onScreenX;
        public float OffScreenRightX => offScreenRightX;

        public static TrainConfig CreateDefault(Transform root)
        {
            return new TrainConfig
            {
                trainRoot = root
            };
        }
    }

    private enum TrainDestination
    {
        OnScreen,
        OffScreenLeft,
        OffScreenRight
    }

    [Serializable]
    private class TrainSpeedProfile
    {
        [Header("Distance Breakpoints (units from target)")]
        [SerializeField] private float farRange = 10f;
        [SerializeField] private float nearRange = 3f;

        [Header("Speed At Breakpoints (units / second)")]
        [SerializeField] private float farSpeed = 9f;
        [SerializeField] private float midSpeed = 5f;
        [SerializeField] private float nearSpeed = 2f;

        public float GetSpeed(float distanceToTarget)
        {
            var clampedNearRange = Mathf.Max(0.01f, nearRange);
            var clampedFarRange = Mathf.Max(clampedNearRange, farRange);
            var clampedNearSpeed = Mathf.Max(0.01f, nearSpeed);
            var clampedMidSpeed = Mathf.Max(clampedNearSpeed, midSpeed);
            var clampedFarSpeed = Mathf.Max(clampedMidSpeed, farSpeed);

            if (distanceToTarget >= clampedFarRange)
            {
                return clampedFarSpeed;
            }

            if (distanceToTarget >= clampedNearRange)
            {
                var t = Mathf.InverseLerp(clampedNearRange, clampedFarRange, distanceToTarget);
                return Mathf.Lerp(clampedMidSpeed, clampedFarSpeed, t);
            }

            var nearT = Mathf.InverseLerp(0f, clampedNearRange, distanceToTarget);
            return Mathf.Lerp(clampedNearSpeed, clampedMidSpeed, nearT);
        }
    }

    [Header("Train Configuration")]
    [SerializeField] private List<TrainConfig> trains = new();
    [SerializeField] private TrainSpeedProfile speedProfile = new();
    [SerializeField] private float delayBetweenTrains = 0.15f;
    [SerializeField] private bool autoRunOnStart;

    public bool IsTrainMoving { get; private set; }

    public event Action<bool> OnTrainMovementChanged;

    private int activeTrainMovements;
    private bool[] activeTrainMask = Array.Empty<bool>();

    private void Awake()
    {
        EnsureDefaultTrainConfig();
        ResetForLevelStart();
    }

    private void Start()
    {
        if (autoRunOnStart)
        {
            BeginTrainEntry();
        }
    }

    public void ResetForLevelStart()
    {
        StopAllCoroutines();
        activeTrainMovements = 0;
        SetTrainMoving(false);
        EnsureDefaultTrainConfig();
        SnapAllToOffScreenLeft();
        ResetActiveTrainMask(false);
    }

    public void SetVisibleTrains(IReadOnlyList<int> visibleTrainIndices, bool hideUnselected = true)
    {
        EnsureDefaultTrainConfig();
        var visibleIndexSet = new HashSet<int>(GetValidTrainIndices(visibleTrainIndices));
        var hasExplicitSelection = visibleIndexSet.Count > 0;

        for (var i = 0; i < trains.Count; i++)
        {
            var root = trains[i]?.TrainRoot;
            if (root == null)
            {
                continue;
            }

            var shouldBeVisible = !hideUnselected || !hasExplicitSelection || visibleIndexSet.Contains(i);
            if (root.gameObject.activeSelf != shouldBeVisible)
            {
                root.gameObject.SetActive(shouldBeVisible);
            }
        }
    }

    public void BeginTrainEntry()
    {
        BeginTrainEntry(GetAllTrainIndices(), resetActiveMask: true, moveAllAtOnce: false);
    }

    public void BeginTrainEntry(
        IReadOnlyList<int> selectedTrainIndices,
        bool resetActiveMask = false,
        bool moveAllAtOnce = false,
        bool snapToLeftBeforeMove = true)
    {
        EnsureDefaultTrainConfig();
        StopAllCoroutines();
        activeTrainMovements = 0;
        SetTrainMoving(false);

        if (resetActiveMask)
        {
            ResetActiveTrainMask(false);
        }

        MarkTrainsAsActive(selectedTrainIndices);
        StartCoroutine(MoveTrainsWithDelay(
            TrainDestination.OnScreen,
            snapToLeftBeforeMove: snapToLeftBeforeMove,
            selectedTrainIndices: selectedTrainIndices,
            moveAllAtOnce: moveAllAtOnce));
    }

    public void SendAcceptedTrainsOffScreen()
    {
        SendAcceptedTrainsOffScreen(GetAllActiveTrainIndices());
    }

    public void SendAcceptedTrainsOffScreen(IReadOnlyList<int> selectedTrainIndices)
    {
        EnsureDefaultTrainConfig();
        StopAllCoroutines();
        activeTrainMovements = 0;
        SetTrainMoving(false);
        StartCoroutine(MoveTrainsWithDelay(
            TrainDestination.OffScreenRight,
            snapToLeftBeforeMove: false,
            accelerateFromStart: true,
            selectedTrainIndices: selectedTrainIndices,
            moveAllAtOnce: false));
    }

    public void SendRejectedTrainsOffScreen()
    {
        SendRejectedTrainsOffScreen(GetAllActiveTrainIndices());
    }

    public void SendRejectedTrainsOffScreen(IReadOnlyList<int> selectedTrainIndices)
    {
        EnsureDefaultTrainConfig();
        StopAllCoroutines();
        activeTrainMovements = 0;
        SetTrainMoving(false);
        StartCoroutine(MoveTrainsWithDelay(
            TrainDestination.OffScreenLeft,
            snapToLeftBeforeMove: false,
            accelerateFromStart: true,
            selectedTrainIndices: selectedTrainIndices,
            moveAllAtOnce: false));
    }

    public int GetTrainCount()
    {
        EnsureDefaultTrainConfig();
        return trains?.Count ?? 0;
    }

    private System.Collections.IEnumerator MoveTrainsWithDelay(
        TrainDestination destination,
        bool snapToLeftBeforeMove,
        bool accelerateFromStart = false,
        IReadOnlyList<int> selectedTrainIndices = null,
        bool moveAllAtOnce = false)
    {
        if (trains == null || trains.Count == 0)
        {
            yield break;
        }

        var selectedIndices = selectedTrainIndices?.Count > 0
            ? new HashSet<int>(selectedTrainIndices)
            : null;

        for (var i = 0; i < trains.Count; i++)
        {
            if (selectedIndices != null && !selectedIndices.Contains(i))
            {
                continue;
            }

            if (activeTrainMask.Length > i && !activeTrainMask[i])
            {
                continue;
            }

            var train = trains[i];
            if (train == null || train.TrainRoot == null || !train.TrainRoot.gameObject.activeInHierarchy)
            {
                continue;
            }

            if (snapToLeftBeforeMove)
            {
                SnapTrainToX(train, train.OffScreenLeftX);
            }

            StartCoroutine(MoveSingleTrain(i, train, destination, accelerateFromStart));

            if (!moveAllAtOnce && delayBetweenTrains > 0f)
            {
                yield return new WaitForSeconds(delayBetweenTrains);
            }
        }
    }

    private System.Collections.IEnumerator MoveSingleTrain(
        int trainIndex,
        TrainConfig train,
        TrainDestination destination,
        bool accelerateFromStart)
    {
        if (train == null || train.TrainRoot == null)
        {
            yield break;
        }

        activeTrainMovements++;
        SetTrainMoving(true);

        var targetX = GetTargetX(train, destination);
        var startX = train.TrainRoot.localPosition.x;
        var totalDistance = Mathf.Abs(targetX - startX);
        while (!Mathf.Approximately(train.TrainRoot.localPosition.x, targetX))
        {
            var position = train.TrainRoot.localPosition;
            var distanceToTarget = Mathf.Abs(targetX - position.x);
            var distanceForSpeedProfile = accelerateFromStart
                ? Mathf.Max(0f, totalDistance - distanceToTarget)
                : distanceToTarget;
            var movementSpeed = (speedProfile ??= new TrainSpeedProfile()).GetSpeed(distanceForSpeedProfile);
            var nextX = Mathf.MoveTowards(position.x, targetX, movementSpeed * Time.deltaTime);
            train.TrainRoot.localPosition = new Vector3(nextX, position.y, position.z);
            yield return null;
        }

        if ((destination == TrainDestination.OffScreenLeft || destination == TrainDestination.OffScreenRight) &&
            activeTrainMask.Length > trainIndex)
        {
            activeTrainMask[trainIndex] = false;
        }

        activeTrainMovements = Mathf.Max(0, activeTrainMovements - 1);
        if (activeTrainMovements == 0)
        {
            SetTrainMoving(false);
        }
    }

    private void EnsureDefaultTrainConfig()
    {
        if (trains != null && trains.Count > 0)
        {
            return;
        }

        trains = new List<TrainConfig>
        {
            TrainConfig.CreateDefault(transform)
        };
    }

    private void SnapAllToOffScreenLeft()
    {
        if (trains == null)
        {
            return;
        }

        for (var i = 0; i < trains.Count; i++)
        {
            var train = trains[i];
            if (train == null || train.TrainRoot == null)
            {
                continue;
            }

            SnapTrainToX(train, train.OffScreenLeftX);
        }
    }

    private void SnapTrainToX(TrainConfig train, float x)
    {
        var position = train.TrainRoot.localPosition;
        train.TrainRoot.localPosition = new Vector3(x, position.y, position.z);
    }

    private float GetTargetX(TrainConfig train, TrainDestination destination)
    {
        return destination switch
        {
            TrainDestination.OnScreen => train.OnScreenX,
            TrainDestination.OffScreenLeft => train.OffScreenLeftX,
            TrainDestination.OffScreenRight => train.OffScreenRightX,
            _ => train.OnScreenX
        };
    }

    private void ResetActiveTrainMask(bool defaultValue)
    {
        var count = trains?.Count ?? 0;
        activeTrainMask = new bool[count];
        for (var i = 0; i < count; i++)
        {
            activeTrainMask[i] = defaultValue;
        }
    }

    private void MarkTrainsAsActive(IReadOnlyList<int> indices)
    {
        if (activeTrainMask == null || activeTrainMask.Length != (trains?.Count ?? 0))
        {
            ResetActiveTrainMask(false);
        }

        var validIndices = GetValidTrainIndices(indices);
        if (validIndices.Count == 0)
        {
            for (var i = 0; i < activeTrainMask.Length; i++)
            {
                activeTrainMask[i] = true;
            }

            return;
        }

        for (var i = 0; i < validIndices.Count; i++)
        {
            activeTrainMask[validIndices[i]] = true;
        }
    }

    private List<int> GetValidTrainIndices(IReadOnlyList<int> indices)
    {
        var validIndices = new List<int>();
        var maxCount = trains?.Count ?? 0;
        if (indices == null)
        {
            return validIndices;
        }

        for (var i = 0; i < indices.Count; i++)
        {
            var index = indices[i];
            if (index < 0 || index >= maxCount || validIndices.Contains(index))
            {
                continue;
            }

            validIndices.Add(index);
        }

        return validIndices;
    }

    private List<int> GetAllTrainIndices()
    {
        var count = trains?.Count ?? 0;
        var indices = new List<int>(count);
        for (var i = 0; i < count; i++)
        {
            indices.Add(i);
        }

        return indices;
    }

    private List<int> GetAllActiveTrainIndices()
    {
        var activeIndices = new List<int>();
        for (var i = 0; i < activeTrainMask.Length; i++)
        {
            if (activeTrainMask[i])
            {
                activeIndices.Add(i);
            }
        }

        return activeIndices;
    }

    private void SetTrainMoving(bool isMoving)
    {
        if (IsTrainMoving == isMoving)
        {
            return;
        }

        IsTrainMoving = isMoving;
        OnTrainMovementChanged?.Invoke(IsTrainMoving);
    }
}
