namespace FlowPilot.Application.Agents;

/// <summary>
/// Configuration settings for Azure OpenAI connection.
/// </summary>
public sealed class AzureOpenAISettings
{
    public const string SectionName = "AzureOpenAI";

    /// <summary>Azure OpenAI endpoint URL (e.g. "https://my-resource.openai.azure.com/").</summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>API key for Azure OpenAI.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Deployment name for the chat model (e.g. "gpt-4o").</summary>
    public string DeploymentName { get; set; } = "gpt-4o";

    /// <summary>Maximum tool call iterations to prevent infinite loops.</summary>
    public int MaxToolCallIterations { get; set; } = 10;
}
