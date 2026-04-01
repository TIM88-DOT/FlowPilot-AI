using System.Security.Cryptography;
using System.Text;

namespace FlowPilot.Api.Filters;

/// <summary>
/// Endpoint filter that validates the X-Twilio-Signature header on inbound webhooks.
/// Rejects spoofed requests with 403. Skipped in Development environment.
/// See: https://www.twilio.com/docs/usage/security#validating-requests
/// </summary>
public sealed class TwilioSignatureFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        HttpContext httpContext = context.HttpContext;
        IConfiguration config = httpContext.RequestServices.GetRequiredService<IConfiguration>();
        IHostEnvironment env = httpContext.RequestServices.GetRequiredService<IHostEnvironment>();

        // Skip validation in Development so ngrok/local testing works without friction
        if (env.IsDevelopment())
            return await next(context);

        string? authToken = config["Twilio:AuthToken"];
        if (string.IsNullOrEmpty(authToken))
        {
            // If Twilio isn't configured, reject all webhook calls
            return Results.Problem("Twilio:AuthToken is not configured.", statusCode: 500);
        }

        string? signature = httpContext.Request.Headers["X-Twilio-Signature"].ToString();
        if (string.IsNullOrEmpty(signature))
            return Results.Json(new { error = "Missing X-Twilio-Signature header." }, statusCode: 403);

        // Build the full URL Twilio used to sign (scheme + host + path)
        string url = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}{httpContext.Request.Path}";

        // Read form data (Twilio sends application/x-www-form-urlencoded)
        httpContext.Request.EnableBuffering();
        IFormCollection form = await httpContext.Request.ReadFormAsync();

        // Build signed data: URL + sorted key-value pairs concatenated
        var data = new StringBuilder(url);
        foreach (string key in form.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            data.Append(key);
            data.Append(form[key].ToString());
        }

        // Compute HMAC-SHA1
        byte[] keyBytes = Encoding.UTF8.GetBytes(authToken);
        byte[] dataBytes = Encoding.UTF8.GetBytes(data.ToString());
        using HMACSHA1 hmac = new(keyBytes);
        byte[] hash = hmac.ComputeHash(dataBytes);
        string expectedSignature = Convert.ToBase64String(hash);

        if (!string.Equals(signature, expectedSignature, StringComparison.Ordinal))
        {
            ILogger<TwilioSignatureFilter> logger = httpContext.RequestServices
                .GetRequiredService<ILogger<TwilioSignatureFilter>>();
            logger.LogWarning("Twilio signature validation failed. Expected={Expected}, Got={Got}, URL={Url}",
                expectedSignature, signature, url);

            return Results.Json(new { error = "Invalid Twilio signature." }, statusCode: 403);
        }

        return await next(context);
    }
}
