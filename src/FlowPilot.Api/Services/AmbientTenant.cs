namespace FlowPilot.Api.Services;

/// <summary>
/// Ambient (async-flowed) tenant override used when code runs outside an HTTP request — e.g. the
/// <see cref="FlowPilot.Api.BackgroundEvents.BackgroundEventProcessor"/> replaying a domain event on
/// a background scope. <see cref="HttpCurrentTenant"/> consults this first; it is null on the normal
/// request path, so HTTP behaviour is unchanged.
/// </summary>
public static class AmbientTenant
{
    private static readonly AsyncLocal<TenantContext?> Holder = new();

    public static TenantContext? Current
    {
        get => Holder.Value;
        set => Holder.Value = value;
    }

    public sealed record TenantContext(Guid TenantId, Guid UserId, string UserRole);
}
