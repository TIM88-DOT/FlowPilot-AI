using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.Features;

namespace FlowPilot.Api.Middleware;

/// <summary>
/// Catches unhandled exceptions and writes a consistent RFC 7807 ProblemDetails response.
/// Malformed-input exceptions (bad JSON body, unparseable route/body values surfaced as
/// <see cref="BadHttpRequestException"/>) map to their carried status code — almost always 400 —
/// instead of leaking a 500. Everything else falls through to a generic 500 without exposing
/// internal details.
/// </summary>
public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly IProblemDetailsService _problemDetailsService;
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(
        IProblemDetailsService problemDetailsService,
        ILogger<GlobalExceptionHandler> logger)
    {
        _problemDetailsService = problemDetailsService;
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        // BadHttpRequestException is thrown by minimal-API parameter binding on malformed
        // input (bad JSON, invalid GUID in body, etc.) and carries the intended status (400).
        int statusCode = exception is BadHttpRequestException badRequest
            ? badRequest.StatusCode
            : StatusCodes.Status500InternalServerError;

        bool isClientError = statusCode is >= 400 and < 500;

        if (isClientError)
            _logger.LogInformation(exception, "Request rejected with {StatusCode}: {Message}", statusCode, exception.Message);
        else
            _logger.LogError(exception, "Unhandled exception processing {Path}", httpContext.Request.Path);

        httpContext.Response.StatusCode = statusCode;

        return await _problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails =
            {
                Status = statusCode,
                Title = isClientError ? "Bad Request" : "An unexpected error occurred.",
                // Only echo the message for client errors — never leak internals on a 500.
                Detail = isClientError ? exception.Message : null,
            },
        });
    }
}
