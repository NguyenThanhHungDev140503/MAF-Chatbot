namespace ErpChatbot.Core.Models;

public class LlmOptions
{
    public const string SectionName = "Llm";

    public string Provider { get; set; } = "ollama";
    public string Model { get; set; } = "llama3.2";
    public float Temperature { get; set; } = 0.2f;
    public int MaxOutputTokens { get; set; } = 500;
    public OllamaOptions Ollama { get; set; } = new();
}

public class OllamaOptions
{
    public string Endpoint { get; set; } = "http://localhost:11434";
}
