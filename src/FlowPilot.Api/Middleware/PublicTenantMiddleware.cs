using FlowPilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FlowPilot.Api.Middleware;

/// <summary>
/// Resolves TenantId from URL slug for public booking endpoints.
/// Sets HttpContext.Items["PublicTenantId"] so that <see cref="Services.HttpCurrentTenant"/>
/// can fall back to it when no JWT claims are present.
/// </summary>
public class PublicTenantMiddleware
{
    private readonly RequestDelegate _next;

    public PublicTenantMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, AppDbContext db)
    {
        if (context.Request.Path.StartsWithSegments("/api/v1/public/book", out PathString remaining)
            && remaining.HasValue)
        {
            // Extract slug: remaining = "/{slug}" or "/{slug}/slots"
            string[] segments = remaining.Value!.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length >= 1)
            {
                string slug = segments[0];
                Guid? tenantId = await db.Tenants
                    .IgnoreQueryFilters() // Public tenant lookup — no JWT context available
                    .AsNoTracking()
                    .Where(t => t.Slug == slug && !t.IsDeleted)
                    .Select(t => (Guid?)t.Id)
                    .FirstOrDefaultAsync();

                if (tenantId is null)
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    await context.Response.WriteAsJsonAsync(new { detail = "Business not found." });
                    return;
                }

                context.Items["PublicTenantId"] = tenantId.Value;
            }
        }

        await _next(context);
    }
}
