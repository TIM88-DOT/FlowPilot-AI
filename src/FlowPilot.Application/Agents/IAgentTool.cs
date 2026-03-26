namespace FlowPilot.Application.Agents;

/// <summary>
/// A single tool that an AI agent can invoke during its execution loop.
/// Each tool has a name, description, JSON schema for inputs, and an execute method.
/// </summary>
public interface IAgentTool
{
    /// <summary>Tool name used in function-calling (e.g. "get_customer_history").</summary>
    string Name { get; }

    /// <summary>Human-readable description sent to the LLM so it knows when to use this tool.</summary>
    string Description { get; }

    /// <summary>JSON Schema describing the expected input parameters.</summary>
    BinaryData InputSchema { get; }

    /// <summary>
    /// Executes the tool with the given JSON input and returns a JSON string result.
    /// </summary>
    Task<string> ExecuteAsync(string inputJson, CancellationToken cancellationToken = default);
}
