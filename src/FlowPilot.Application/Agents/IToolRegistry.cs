namespace FlowPilot.Application.Agents;

/// <summary>
/// Registry of all available agent tools. Provides tool lookup and
/// generates the function-calling schema list for Azure OpenAI.
/// </summary>
public interface IToolRegistry
{
    /// <summary>Registers a tool in the registry.</summary>
    void Register(IAgentTool tool);

    /// <summary>Gets a tool by name, or null if not found.</summary>
    IAgentTool? Get(string name);

    /// <summary>Executes a tool by name with the given JSON input.</summary>
    Task<string> ExecuteToolAsync(string name, string inputJson, CancellationToken cancellationToken = default);

    /// <summary>Returns all registered tool names.</summary>
    IReadOnlyList<string> GetRegisteredToolNames();

    /// <summary>Returns a subset of tools filtered by name.</summary>
    IReadOnlyList<IAgentTool> GetTools(IEnumerable<string> toolNames);
}
