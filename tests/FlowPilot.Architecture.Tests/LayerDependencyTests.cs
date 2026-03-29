using ArchUnitNET.Domain;
using ArchUnitNET.Fluent;
using ArchUnitNET.Loader;
using ArchUnitNET.xUnit;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace FlowPilot.Architecture.Tests;

/// <summary>
/// Enforces the dependency direction: Domain → Shared, Application → Domain+Shared,
/// Infrastructure → Application+Domain+Shared, Api → all.
/// Domain and Application must NEVER reference Infrastructure.
/// </summary>
public class LayerDependencyTests
{
    private static readonly ArchUnitNET.Domain.Architecture Architecture =
        new ArchLoader()
            .LoadAssemblies(
                typeof(FlowPilot.Domain.Common.BaseEntity).Assembly,
                typeof(FlowPilot.Shared.Result).Assembly,
                typeof(FlowPilot.Application.Appointments.IAppointmentService).Assembly,
                typeof(FlowPilot.Infrastructure.Persistence.AppDbContext).Assembly,
                typeof(Program).Assembly)
            .Build();

    private readonly IObjectProvider<IType> _domainLayer =
        Types().That().ResideInNamespace("FlowPilot.Domain", useRegularExpressions: false)
            .As("Domain Layer");

    private readonly IObjectProvider<IType> _applicationLayer =
        Types().That().ResideInNamespace("FlowPilot.Application", useRegularExpressions: false)
            .As("Application Layer");

    private readonly IObjectProvider<IType> _infrastructureLayer =
        Types().That().ResideInNamespace("FlowPilot.Infrastructure", useRegularExpressions: false)
            .As("Infrastructure Layer");

    private readonly IObjectProvider<IType> _sharedLayer =
        Types().That().ResideInNamespace("FlowPilot.Shared", useRegularExpressions: false)
            .As("Shared Layer");

    [Fact]
    public void Domain_ShouldNotDependOn_Application()
    {
        IArchRule rule = Types().That().Are(_domainLayer)
            .Should().NotDependOnAny(_applicationLayer);

        rule.Check(Architecture);
    }

    [Fact]
    public void Domain_ShouldNotDependOn_Infrastructure()
    {
        IArchRule rule = Types().That().Are(_domainLayer)
            .Should().NotDependOnAny(_infrastructureLayer);

        rule.Check(Architecture);
    }

    [Fact]
    public void Application_ShouldNotDependOn_Infrastructure()
    {
        IArchRule rule = Types().That().Are(_applicationLayer)
            .Should().NotDependOnAny(_infrastructureLayer);

        rule.Check(Architecture);
    }

    [Fact]
    public void Shared_ShouldNotDependOn_Domain()
    {
        IArchRule rule = Types().That().Are(_sharedLayer)
            .Should().NotDependOnAny(_domainLayer);

        rule.Check(Architecture);
    }

    [Fact]
    public void Shared_ShouldNotDependOn_Application()
    {
        IArchRule rule = Types().That().Are(_sharedLayer)
            .Should().NotDependOnAny(_applicationLayer);

        rule.Check(Architecture);
    }

    [Fact]
    public void Shared_ShouldNotDependOn_Infrastructure()
    {
        IArchRule rule = Types().That().Are(_sharedLayer)
            .Should().NotDependOnAny(_infrastructureLayer);

        rule.Check(Architecture);
    }
}
