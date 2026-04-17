using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public class CyberAIPacketJudge : MonoBehaviour
{
    [Serializable]
    public class CyberDecisionFeedback
    {
        public bool requestSucceeded;
        public string wrongReason;
        public string rawResponse;
    }

    [Serializable]
    private class ChatCompletionRequest
    {
        public string model;
        public ChatMessage[] messages;
        public float temperature;
    }

    [Serializable]
    private class ChatMessage
    {
        public string role;
        public string content;
    }

    [Serializable]
    private class ChatCompletionResponse
    {
        public Choice[] choices;
    }

    [Serializable]
    private class Choice
    {
        public ChatMessage message;
    }

    [Header("API Settings")]
    [SerializeField] private bool enableRemoteJudge;
    [SerializeField] private string apiUrl = "https://api.openai.com/v1/chat/completions";
    [SerializeField] private string apiKey;
    [SerializeField] private string modelName = "gpt-4o-mini";

    [Header("Model Context (Do Not Change In Functions)")]
    [SerializeField] private TextAsset modelContextAsset;
    [SerializeField, TextArea(8, 18)] private string modelContext =
        "You are a cybersecurity packet inspector tutor. Keep answers concise and educational.";

    public bool CanQueryRemoteJudge =>
        enableRemoteJudge &&
        !string.IsNullOrWhiteSpace(apiUrl) &&
        !string.IsNullOrWhiteSpace(apiKey);

    public IEnumerator RequestDecisionFeedback(
        string packetPayload,
        bool playerAccepted,
        bool actuallySuspicious,
        Action<CyberDecisionFeedback> onCompleted)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            onCompleted?.Invoke(new CyberDecisionFeedback
            {
                requestSucceeded = false,
                wrongReason = "AI explanation disabled for this build."
            });
            yield break;
        }

        var fallback = BuildFallbackFeedback(playerAccepted, actuallySuspicious);
        if (!CanQueryRemoteJudge)
        {
            onCompleted?.Invoke(fallback);
            yield break;
        }

        var playerDecision = playerAccepted ? "accepted" : "rejected";
        var groundTruth = actuallySuspicious ? "suspicious" : "safe";
        var userPrompt =
            $"Packet payload: {packetPayload}\n" +
            $"Player decision: {playerDecision}\n" +
            $"Ground truth: {groundTruth}\n\n" +
            "Return JSON only with key wrongReason.";

        var requestPayload = new ChatCompletionRequest
        {
            model = modelName,
            temperature = 0.2f,
            messages = new[]
            {
                new ChatMessage { role = "system", content = ResolveModelContext() },
                new ChatMessage { role = "user", content = userPrompt }
            }
        };

        var json = JsonUtility.ToJson(requestPayload);
        using var request = new UnityWebRequest(apiUrl, UnityWebRequest.kHttpVerbPOST);
        request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("Authorization", $"Bearer {apiKey}");
        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogWarning($"Cyber AI request failed: {request.error}", this);
            onCompleted?.Invoke(fallback);
            yield break;
        }

        var responseBody = request.downloadHandler?.text;
        var parsed = ParseResponse(responseBody, fallback);
        onCompleted?.Invoke(parsed);
    }

    private CyberDecisionFeedback BuildFallbackFeedback(bool playerAccepted, bool actuallySuspicious)
    {
        var acceptedSuspicious = playerAccepted && actuallySuspicious;
        var rejectedSafe = !playerAccepted && !actuallySuspicious;

        if (acceptedSuspicious)
        {
            return new CyberDecisionFeedback
            {
                requestSucceeded = false,
                wrongReason = "You accepted a packet that matched suspicious traffic patterns."
            };
        }

        if (rejectedSafe)
        {
            return new CyberDecisionFeedback
            {
                requestSucceeded = false,
                wrongReason = "You rejected a packet that did not show threat indicators."
            };
        }

        return new CyberDecisionFeedback
        {
            requestSucceeded = false,
            wrongReason = "The decision was mismatched with packet risk."
        };
    }

    private CyberDecisionFeedback ParseResponse(string responseBody, CyberDecisionFeedback fallback)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return fallback;
        }

        ChatCompletionResponse parsedResponse;
        try
        {
            parsedResponse = JsonUtility.FromJson<ChatCompletionResponse>(responseBody);
        }
        catch (ArgumentException)
        {
            return fallback;
        }

        var content = parsedResponse?.choices != null && parsedResponse.choices.Length > 0
            ? parsedResponse.choices[0]?.message?.content
            : null;

        if (string.IsNullOrWhiteSpace(content))
        {
            return fallback;
        }

        try
        {
            var feedback = JsonUtility.FromJson<CyberDecisionFeedback>(content);
            if (feedback == null)
            {
                return fallback;
            }

            feedback.requestSucceeded = true;
            feedback.rawResponse = responseBody;
            if (string.IsNullOrWhiteSpace(feedback.wrongReason))
            {
                return fallback;
            }

            return feedback;
        }
        catch (ArgumentException)
        {
            return fallback;
        }
    }

    private string ResolveModelContext()
    {
        if (modelContextAsset != null && !string.IsNullOrWhiteSpace(modelContextAsset.text))
        {
            return modelContextAsset.text;
        }

        return modelContext;
    }
}
