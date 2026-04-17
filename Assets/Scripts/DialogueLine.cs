using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class DialogueLine
{
    [System.Serializable]
    public class InteractionRequirement
    {
        [Tooltip("If enabled, dialogue pauses after this line until the required tutorial interactions are performed.")]
        public bool enabled;

        [Tooltip("Interaction ids allowed while this requirement is active (for example: 'manual_toggle' or 'train_compartment').")]
        public List<string> allowedInteractionIds = new();

        [Min(1)]
        [Tooltip("How many valid interactions are required before dialogue continues.")]
        public int requiredInteractionCount = 1;
    }

    [TextArea]
    public string text;

    public string speakerName;
    public Sprite portrait;

    [Tooltip("Enable to override the dialogue UI's default reveal speed for this line.")]
    public bool useCustomRevealSpeed;

    [Min(0f)]
    [Tooltip("Characters revealed per second when using a custom reveal speed.")]
    public float revealLettersPerSecond = 30f;

    [Header("Tutorial Interaction")]
    public InteractionRequirement interactionRequirement = new();

    public DialogueLine(string text, Sprite portrait = null, string speakerName = null)
    {
        this.text = text;
        this.portrait = portrait;
        this.speakerName = speakerName;
        useCustomRevealSpeed = false;
        revealLettersPerSecond = 30f;
    }

    public DialogueLine(
        string text,
        Sprite portrait,
        string speakerName,
        bool useCustomRevealSpeed,
        float revealLettersPerSecond)
    {
        this.text = text;
        this.portrait = portrait;
        this.speakerName = speakerName;
        this.useCustomRevealSpeed = useCustomRevealSpeed;
        this.revealLettersPerSecond = revealLettersPerSecond;
    }
}
