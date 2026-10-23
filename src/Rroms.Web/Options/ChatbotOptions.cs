namespace Rroms.Web.Options;

/// <summary>Only ApiKey is secret. Store it with user-secrets or an environment variable.</summary>
public class ChatbotOptions
{
    public const string SectionName = "Chatbot";
    public const int MaxQuestionLength = 500;
    public string ApiKey { get; set; } = "";
    // meta/llama-3.1-8b-instruct was retired by NVIDIA on 26 Aug 2026 (HTTP 410), so every AI answer fell back.
    public string Model { get; set; } = "meta/llama-3.2-11b-vision-instruct";
    // The free NVIDIA service often takes 2 to 5 seconds; the page itself waits 18 seconds.
    public int TimeoutSeconds { get; set; } = 12;
}
