using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class GameplayManual : MonoBehaviour
{
    [SerializeField] private TMP_Text pageTitleText;
    [SerializeField] private TMP_Text leftPageText;
    [SerializeField] private TMP_Text rightPageText;
    [SerializeField] private List<Button> tabButtons = new();
    [SerializeField] private List<ManualPageData> manualPages = new();
    [SerializeField] private int startingPageIndex;

    private readonly List<UnityEngine.Events.UnityAction> tabClickHandlers = new();
    private readonly Dictionary<int, ManualPageData> manualPagesByIndex = new();

    private int currentPageIndex = -1;

    private void OnEnable()
    {
        RebuildManualPageLookup();
        BindTabButtons();
        ShowFirstAvailablePage();
    }

    private void OnDisable()
    {
        UnbindTabButtons();
    }

    private void OnValidate()
    {
        RebuildManualPageLookup();
    }

    public void SetPageUnlocked(int pageIndex, bool unlocked)
    {
        if (!IsValidPageIndex(pageIndex))
        {
            Debug.LogWarning($"{nameof(GameplayManual)} invalid page index in {nameof(SetPageUnlocked)}. {BuildWarningContext(pageIndex)}", this);
            return;
        }

        tabButtons[pageIndex].interactable = unlocked;

        if (currentPageIndex == pageIndex && !unlocked)
        {
            ShowFirstAvailablePage();
        }
    }

    public void SetUnlockedPages(IEnumerable<int> pageIndices, int levelId = -1)
    {
        if (tabButtons.Count == 0)
        {
            return;
        }

        var unlockedIndexSet = new HashSet<int>();
        if (pageIndices != null)
        {
            foreach (var pageIndex in pageIndices)
            {
                if (!IsValidPageIndex(pageIndex))
                {
                    Debug.LogWarning($"{nameof(GameplayManual)} invalid unlock index. {BuildWarningContext(pageIndex, levelId)}", this);
                    continue;
                }

                unlockedIndexSet.Add(pageIndex);
            }
        }
        else
        {
            Debug.LogWarning($"{nameof(GameplayManual)} received null page unlock list. {BuildWarningContext(levelId: levelId)}", this);
        }

        for (var index = 0; index < tabButtons.Count; index++)
        {
            var tabButton = tabButtons[index];
            if (tabButton == null)
            {
                Debug.LogWarning($"{nameof(GameplayManual)} missing tab button reference. {BuildWarningContext(index, levelId)}", this);
                continue;
            }

            tabButton.interactable = unlockedIndexSet.Contains(index);
        }

        if (currentPageIndex < 0 || !IsValidPageIndex(currentPageIndex) || tabButtons[currentPageIndex] == null || !tabButtons[currentPageIndex].interactable)
        {
            ShowFirstAvailablePage();
        }
    }

    public void ShowPage(int pageIndex)
    {
        if (!IsValidPageIndex(pageIndex) || !tabButtons[pageIndex].interactable)
        {
            return;
        }

        currentPageIndex = pageIndex;

        if (manualPagesByIndex.TryGetValue(pageIndex, out var pageData) && pageData != null)
        {
            SetDisplayedPageContent(pageData);
            return;
        }

        Debug.LogWarning($"{nameof(GameplayManual)} has no manual page data. {BuildWarningContext(pageIndex)}", this);
        SetDisplayedPageContent("Section not found.", string.Empty, string.Empty);
    }

    private void BindTabButtons()
    {
        UnbindTabButtons();

        for (var index = 0; index < tabButtons.Count; index++)
        {
            var tabButton = tabButtons[index];
            if (tabButton == null)
            {
                tabClickHandlers.Add(null);
                continue;
            }

            var capturedIndex = index;
            UnityEngine.Events.UnityAction clickHandler = () => ShowPage(capturedIndex);
            tabClickHandlers.Add(clickHandler);
            tabButton.onClick.AddListener(clickHandler);
        }
    }

    private void UnbindTabButtons()
    {
        for (var index = 0; index < tabButtons.Count && index < tabClickHandlers.Count; index++)
        {
            var tabButton = tabButtons[index];
            var clickHandler = tabClickHandlers[index];
            if (tabButton == null || clickHandler == null)
            {
                continue;
            }

            tabButton.onClick.RemoveListener(clickHandler);
        }

        tabClickHandlers.Clear();
    }

    private void ShowFirstAvailablePage()
    {
        if (tabButtons.Count == 0)
        {
            SetDisplayedPageContent("No pages available.", string.Empty, string.Empty);
            currentPageIndex = -1;
            return;
        }

        var clampedStartingIndex = Mathf.Clamp(startingPageIndex, 0, tabButtons.Count - 1);
        if (tabButtons[clampedStartingIndex] != null && tabButtons[clampedStartingIndex].interactable)
        {
            ShowPage(clampedStartingIndex);
            return;
        }

        for (var index = 0; index < tabButtons.Count; index++)
        {
            var tabButton = tabButtons[index];
            if (tabButton != null && tabButton.interactable)
            {
                ShowPage(index);
                return;
            }
        }

        SetDisplayedPageContent("No pages unlocked.", string.Empty, string.Empty);
        currentPageIndex = -1;
    }

    private void SetDisplayedPageContent(ManualPageData pageData)
    {
        SetDisplayedPageContent(pageData.PageTitle, pageData.LeftPageBody, pageData.RightPageBody);
    }

    private void SetDisplayedPageContent(string title, string leftBody, string rightBody)
    {
        if (pageTitleText != null)
        {
            pageTitleText.text = title;
        }

        if (leftPageText != null)
        {
            leftPageText.text = leftBody;
        }

        if (rightPageText != null)
        {
            rightPageText.text = rightBody;
        }
    }

    private void RebuildManualPageLookup()
    {
        manualPagesByIndex.Clear();

        for (var listIndex = 0; listIndex < manualPages.Count; listIndex++)
        {
            var pageData = manualPages[listIndex];
            if (pageData == null)
            {
                Debug.LogWarning($"{nameof(GameplayManual)} null page data entry at list index {listIndex}. {BuildWarningContext()}" , this);
                continue;
            }

            var pageIndex = pageData.PageIndex;
            if (manualPagesByIndex.ContainsKey(pageIndex))
            {
                Debug.LogWarning($"{nameof(GameplayManual)} duplicate page data found. {BuildWarningContext(pageIndex)}", this);
                continue;
            }

            manualPagesByIndex.Add(pageIndex, pageData);
        }

        ValidatePageDataAgainstTabs();
    }

    private void ValidatePageDataAgainstTabs()
    {
        if (tabButtons.Count == 0 && manualPagesByIndex.Count > 0)
        {
            Debug.LogWarning($"{nameof(GameplayManual)} manual page data configured but no tab buttons. {BuildWarningContext()}", this);
            return;
        }

        for (var tabIndex = 0; tabIndex < tabButtons.Count; tabIndex++)
        {
            if (!manualPagesByIndex.ContainsKey(tabIndex))
            {
                Debug.LogWarning($"{nameof(GameplayManual)} missing manual data for tab. {BuildWarningContext(tabIndex)}", this);
            }
        }

        foreach (var configuredPageId in manualPagesByIndex.Keys)
        {
            if (!IsValidPageIndex(configuredPageId))
            {
                Debug.LogWarning($"{nameof(GameplayManual)} manual page data has no matching tab button. {BuildWarningContext(configuredPageId)}", this);
            }
        }
    }

    private static string BuildWarningContext(int pageId = -1, int levelId = -1)
    {
        return $"levelId={levelId}, pageId={pageId}";
    }

    private bool IsValidPageIndex(int pageIndex)
    {
        return pageIndex >= 0 && pageIndex < tabButtons.Count;
    }
}
