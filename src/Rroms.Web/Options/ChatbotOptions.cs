namespace Rroms.Web.Options;

/// <summary>Only ApiKey is secret. Store it with user-secrets or an environment variable.</summary>
public class ChatbotOptions
{
    public const string SectionName = "Chatbot";
    public const int MaxQuestionLength = 500;
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "meta/llama-3.1-8b-instruct";
    public int TimeoutSeconds { get; set; } = 8;
}
