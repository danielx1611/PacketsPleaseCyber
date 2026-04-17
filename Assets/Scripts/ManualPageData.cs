using UnityEngine;
using UnityEngine.Serialization;

[CreateAssetMenu(fileName = "ManualPageData", menuName = "Gameplay/Manual Page Data")]
public class ManualPageData : ScriptableObject
{
    [Min(0)]
    [SerializeField] private int pageIndex;

    [SerializeField] private string pageTitle;

    [TextArea(4, 10)]
    [FormerlySerializedAs("pageBody")]
    [SerializeField] private string leftPageBody;

    [TextArea(4, 10)]
    [SerializeField] private string rightPageBody;

    [Tooltip("Optional label used by designers for filtering/grouping manual pages by difficulty.")]
    [SerializeField] private string difficultyTag;

    [Tooltip("Optional unlock threshold for game systems that unlock manual pages progressively.")]
    [Min(0)]
    [SerializeField] private int unlockThreshold;

    public int PageIndex => pageIndex;
    public string DifficultyTag => difficultyTag;
    public int UnlockThreshold => unlockThreshold;
    public string PageTitle => pageTitle;

    public string LeftPageBody => leftPageBody;

    public string RightPageBody => rightPageBody;
}
