using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "DialogueSequence", menuName = "Dialogue/Sequence")]
public class DialogueSequence : ScriptableObject
{
    [SerializeField] private List<DialogueLine> lines = new();

    public IReadOnlyList<DialogueLine> Lines => lines;

    public DialogueLine GetLine(int index)
    {
        if (index < 0 || index >= lines.Count)
        {
            return null;
        }

        return lines[index];
    }
}
