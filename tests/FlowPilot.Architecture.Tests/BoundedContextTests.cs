using ArchUnitNET.Domain;
using ArchUnitNET.Fluent;
using ArchUnitNET.Loader;
using ArchUnitNET.xUnit;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace FlowPilot.Architecture.Tests;

/// <summary>
/// Enforces bounded context isolation: Infrastructure modules must not directly
/// reference another module's DbContext or service. Cross-module communication
/// must go through domain events (MediatR) or integration events (Service Bus).
/// </summary>
public class BoundedContextTests
{
    private static readonly ArchUnitNET.Domain.Architecture Architecture =
        new ArchLoader()
            .LoadAssemblies(
                typeof(FlowPilot.Infrastructure.Persistence.AppDbContext).Assembly)
            .Build();

    [Theory]
    [InlineData("FlowPilot.Infrastructure.Appointments", "FlowPilot.Infrastructure.Customers")]
    [InlineData("FlowPilot.Infrastructure.Appointments", "FlowPilot.Infrastructure.Messaging")]
    [InlineData("FlowPilot.Infrastructure.Appointments", "FlowPilot.Infrastructure.Templates")]
    [InlineData("FlowPilot.Infrastructure.Customers", "FlowPilot.Infrastructure.Appointments")]
    [InlineData("FlowPilot.Infrastructure.Customers", "FlowPilot.Infrastructure.Messaging")]
    [InlineData("FlowPilot.Infrastructure.Customers", "FlowPilot.Infrastructure.Templates")]
    [InlineData("FlowPilot.Infrastructure.Messaging", "FlowPilot.Infrastructure.Appointments")]
    [InlineData("FlowPilot.Infrastructure.Messaging", "FlowPilot.Infrastructure.Customers")]
    [InlineData("FlowPilot.Infrastructure.Services", "FlowPilot.Infrastructure.Appointments")]
    [InlineData("FlowPilot.Infrastructure.Services", "FlowPilot.Infrastructure.Customers")]
    [InlineData("FlowPilot.Infrastructure.Services", "FlowPilot.Infrastructure.Messaging")]
    [InlineData("FlowPilot.Infrastructure.Settings", "FlowPilot.Infrastructure.Appointments")]
    [InlineData("FlowPilot.Infrastructure.Settings", "FlowPilot.Infrastructure.Customers")]
    [InlineData("FlowPilot.Infrastructure.Settings", "FlowPilot.Infrastructure.Messaging")]
    public void InfrastructureModule_ShouldNotDirectlyReference_AnotherModule(string sourceNs, string targetNs)
    {
        IObjectProvider<IType> source = Types().That()
            .ResideInNamespace(sourceNs, useRegularExpressions: false)
            .As($"{sourceNs}");

        IObjectProvider<IType> target = Types().That()
            .ResideInNamespace(targetNs, useRegularExpressions: false)
            .As($"{targetNs}");

        IArchRule rule = Types().That().Are(source)
            .Should().NotDependOnAny(target)
            .Because("bounded contexts must communicate via domain events, not direct references");

        rule.Check(Architecture);
    }
}
