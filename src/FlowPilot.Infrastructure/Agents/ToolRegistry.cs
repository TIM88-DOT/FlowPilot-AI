using FlowPilot.Application.Agents;

namespace FlowPilot.Infrastructure.Agents;

/// <summary>
/// In-memory registry of all agent tools. Singleton — tools are registered at startup.
/// </summary>
public sealed class ToolRegistry : IToolRegistry
{
    private readonly Dictionary<string, IAgentTool> _tools = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public void Register(IAgentTool tool)
    {
        _tools[tool.Name] = tool;
    }

    /// <inheritdoc />
    public IAgentTool? Get(string name)
    {
        _tools.TryGetValue(name, out IAgentTool? tool);
        return tool;
    }

    /// <inheritdoc />
    public async Task<string> ExecuteToolAsync(string name, string inputJson, CancellationToken cancellationToken = default)
    {
        IAgentTool? tool = Get(name);
        if (tool is null)
            return $"{{\"error\": \"Tool '{name}' not found.\"}}";

        return await tool.ExecuteAsync(inputJson, cancellationToken);
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetRegisteredToolNames()
    {
        return _tools.Keys.ToList().AsReadOnly();
    }

    /// <inheritdoc />
    public IReadOnlyList<IAgentTool> GetTools(IEnumerable<string> toolNames)
    {
        return toolNames
            .Select(name => Get(name))
            .Where(tool => tool is not null)
            .Cast<IAgentTool>()
            .ToList()
            .AsReadOnly();
    }
}
