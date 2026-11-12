namespace Rroms.Web.Options;

/// <summary>Only ApiKey is secret. Store it with user-secrets or an environment variable.</summary>
public class ChatbotOptions
{
    public const string SectionName = "Chatbot";
    public const int MaxQuestionLength = 500;
    public string ApiKey { get; set; } = "";
    // NVIDIA retired meta/llama-3.1-8b-instruct (26 Aug 2026) and then meta/llama-3.2-11b-vision-instruct (HTTP 410),
    // so every AI answer fell back. nvidia/nemotron-3.5-bolt-e4b answered in about 1 second when checked on 12 Nov 2026.
    public string Model { get; set; } = "nvidia/nemotron-3.5-bolt-e4b";
    // The free NVIDIA service often takes 2 to 5 seconds; the page itself waits 18 seconds.
    public int TimeoutSeconds { get; set; } = 12;
}
