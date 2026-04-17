using UnityEngine;

public class TrainInspectable : MonoBehaviour
{
    [Header("Display")]
    public string componentName;
    [TextArea(3, 6)]
    public string description;

    [Header("Gameplay")]
    public string payload;

    public void SetPayloadData(string newComponentName, string newDescription)
    {
        componentName = newComponentName;
        description = newDescription;
    }

    public void SetPayload(string newPayload)
    {
        payload = newPayload;
    }

    public string GetInspectionText()
    {
        var payloadText = string.IsNullOrWhiteSpace(payload)
            ? string.Empty
            : $"Payload: {payload}\n\n";

        return payloadText + description;
    }
}
