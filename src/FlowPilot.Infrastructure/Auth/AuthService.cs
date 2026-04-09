using System.Text.RegularExpressions;
using FlowPilot.Application.Auth;
using FlowPilot.Domain.Entities;
using FlowPilot.Domain.Enums;
using FlowPilot.Infrastructure.Persistence;
using FlowPilot.Shared;
using Microsoft.EntityFrameworkCore;

namespace FlowPilot.Infrastructure.Auth;

/// <summary>
/// Implements authentication: register (with full tenant provisioning), login, refresh, and logout.
/// </summary>
public sealed class AuthService : IAuthService
{
    private readonly AppDbContext _db;
    private readonly JwtTokenService _tokenService;

    public AuthService(AppDbContext db, JwtTokenService tokenService)
    {
        _db = db;
        _tokenService = tokenService;
    }

    /// <inheritdoc />
    public async Task<Result<AuthResponse>> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default)
    {
        // Check for duplicate email across all tenants (ignore query filters for global uniqueness)
        bool emailExists = await _db.Users
            .IgnoreQueryFilters() // Global uniqueness check — email must be unique across all tenants
            .AnyAsync(u => u.Email == request.Email && !u.IsDeleted, cancellationToken);

        if (emailExists)
            return Result.Failure<AuthResponse>(Error.Conflict("Auth.EmailTaken", "A user with this email already exists."));

        // Create tenant
        var tenantId = Guid.NewGuid();
        string slug = await GenerateUniqueSlugAsync(request.BusinessName, cancellationToken);

        var tenant = new Tenant
        {
            Id = tenantId,
            TenantId = tenantId, // Self-referencing: tenant owns itself
            BusinessName = request.BusinessName,
            Slug = slug,
            BusinessPhone = request.BusinessPhone,
            Timezone = request.Timezone ?? "Africa/Casablanca",
            DefaultLanguage = request.DefaultLanguage
        };

        // Create default plan (Free tier)
        var plan = new Plan
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = "Free",
            MaxSmsPerMonth = 100,
            MaxCustomers = 50,
            MaxAgentRunsPerMonth = 50,
            FeatureFlags = """{"reviewRecovery": false, "campaigns": false, "multiStaff": false}"""
        };

        // Create tenant settings with sensible defaults so public booking works out of the box
        var settings = new TenantSettings
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OwnerTenantId = tenantId,
            ReminderLeadTimeMinutes = 120,
            BusinessHoursJson = """
                {"monday":{"enabled":true,"open":"09:00","close":"18:00"},"tuesday":{"enabled":true,"open":"09:00","close":"18:00"},"wednesday":{"enabled":true,"open":"09:00","close":"18:00"},"thursday":{"enabled":true,"open":"09:00","close":"18:00"},"friday":{"enabled":true,"open":"09:00","close":"18:00"},"saturday":{"enabled":true,"open":"09:00","close":"13:00"},"sunday":{"enabled":false,"open":"09:00","close":"18:00"}}
                """.Trim(),
            BookingSettingsJson = """
                {"bufferMinutes":10,"maxAdvanceDays":60,"minAdvanceHours":0,"allowCancel":true,"cancelBeforeHours":24,"allowReschedule":true,"rescheduleBeforeHours":24}
                """.Trim(),
            NotificationSettingsJson = """
                {"reminderTimingHours":24,"secondReminder":true,"secondReminderTimingHours":2,"confirmationEnabled":true,"noShowFollowUp":true,"smsSignature":null}
                """.Trim(),
            ReviewSettingsJson = """
                {"reviewDelayHours":2,"reviewCooldownDays":30,"autoSend":true}
                """.Trim()
        };

        // Create owner user
        var user = new User
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Email = request.Email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            FirstName = request.FirstName,
            LastName = request.LastName,
            Role = UserRole.Owner
        };

        // Seed system templates with fr + ar locale variants
        List<Template> templates = CreateSystemTemplates(tenantId);

        // Save tenant first — Plan, TenantSettings, User, and Templates all have FK to Tenant
        _db.Tenants.Add(tenant);
        await _db.SaveChangesAsync(cancellationToken);

        _db.Plans.Add(plan);
        _db.TenantSettings.Add(settings);
        _db.Users.Add(user);
        _db.Templates.AddRange(templates);
        await _db.SaveChangesAsync(cancellationToken);

        // Generate tokens
        (string accessToken, DateTime expiresAt) = _tokenService.GenerateAccessToken(user);
        (string refreshToken, DateTime refreshExpiresAt) = _tokenService.GenerateRefreshToken();

        user.RefreshToken = BCrypt.Net.BCrypt.HashPassword(refreshToken);
        user.RefreshTokenExpiresAt = refreshExpiresAt;
        await _db.SaveChangesAsync(cancellationToken);

        return Result.Success(new AuthResponse(
            accessToken,
            expiresAt,
            ToUserInfo(user, tenant.BusinessName)) { RawRefreshToken = refreshToken });
    }

    /// <inheritdoc />
    public async Task<Result<AuthResponse>> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        User? user = await _db.Users
            .IgnoreQueryFilters() // Login is pre-auth — no tenant context yet
            .Include(u => u.Tenant)
            .FirstOrDefaultAsync(u => u.Email == request.Email && !u.IsDeleted, cancellationToken);

        if (user is null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            return Result.Failure<AuthResponse>(Error.Unauthorized("Invalid email or password."));

        (string accessToken, DateTime expiresAt) = _tokenService.GenerateAccessToken(user);
        (string refreshToken, DateTime refreshExpiresAt) = _tokenService.GenerateRefreshToken();

        user.RefreshToken = BCrypt.Net.BCrypt.HashPassword(refreshToken);
        user.RefreshTokenExpiresAt = refreshExpiresAt;
        await _db.SaveChangesAsync(cancellationToken);

        return Result.Success(new AuthResponse(
            accessToken,
            expiresAt,
            ToUserInfo(user, user.Tenant.BusinessName)) { RawRefreshToken = refreshToken });
    }

    /// <inheritdoc />
    public async Task<Result<AuthResponse>> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        // Find users with non-expired refresh tokens
        List<User> candidates = await _db.Users
            .IgnoreQueryFilters() // Refresh is pre-auth — no tenant context yet
            .Include(u => u.Tenant)
            .Where(u => !u.IsDeleted && u.RefreshToken != null && u.RefreshTokenExpiresAt > DateTime.UtcNow)
            .ToListAsync(cancellationToken);

        User? user = candidates.FirstOrDefault(u =>
            u.RefreshToken is not null && BCrypt.Net.BCrypt.Verify(refreshToken, u.RefreshToken));

        if (user is null)
            return Result.Failure<AuthResponse>(Error.Unauthorized("Invalid or expired refresh token."));

        // Rotate tokens
        (string newAccessToken, DateTime expiresAt) = _tokenService.GenerateAccessToken(user);
        (string newRefreshToken, DateTime refreshExpiresAt) = _tokenService.GenerateRefreshToken();

        user.RefreshToken = BCrypt.Net.BCrypt.HashPassword(newRefreshToken);
        user.RefreshTokenExpiresAt = refreshExpiresAt;
        await _db.SaveChangesAsync(cancellationToken);

        return Result.Success(new AuthResponse(
            newAccessToken,
            expiresAt,
            ToUserInfo(user, user.Tenant.BusinessName)) { RawRefreshToken = newRefreshToken });
    }

    /// <inheritdoc />
    public async Task<Result> LogoutAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        User? user = await _db.Users
            .IgnoreQueryFilters() // Logout must work even if tenant filter doesn't match
            .FirstOrDefaultAsync(u => u.Id == userId && !u.IsDeleted, cancellationToken);

        if (user is null)
            return Result.Failure(Error.NotFound("User", userId));

        user.RefreshToken = null;
        user.RefreshTokenExpiresAt = null;
        await _db.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    /// <summary>
    /// Creates system templates with French and Arabic locale variants for a new tenant.
    /// </summary>
    private static List<Template> CreateSystemTemplates(Guid tenantId)
    {
        var templates = new List<Template>();

        // Appointment reminder template
        var reminder = new Template
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = "appointment_reminder",
            Description = "Default appointment reminder SMS",
            Category = "reminder",
            IsSystem = true
        };
        reminder.LocaleVariants = new List<TemplateLocaleVariant>
        {
            new()
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                TemplateId = reminder.Id,
                Locale = "fr",
                Body = "Bonjour {{customer_name}}, rappel de votre RDV le {{appointment_date}} à {{appointment_time}}. Répondez OUI pour confirmer ou NON pour annuler."
            },
            new()
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                TemplateId = reminder.Id,
                Locale = "ar",
                Body = "مرحباً {{customer_name}}، تذكير بموعدك يوم {{appointment_date}} الساعة {{appointment_time}}. أجب بنعم للتأكيد أو لا للإلغاء."
            }
        };
        templates.Add(reminder);

        // Appointment confirmation template
        var confirmation = new Template
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = "appointment_confirmed",
            Description = "Confirmation acknowledgement SMS",
            Category = "confirmation",
            IsSystem = true
        };
        confirmation.LocaleVariants = new List<TemplateLocaleVariant>
        {
            new()
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                TemplateId = confirmation.Id,
                Locale = "fr",
                Body = "Merci {{customer_name}} ! Votre RDV du {{appointment_date}} à {{appointment_time}} est confirmé. À bientôt !"
            },
            new()
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                TemplateId = confirmation.Id,
                Locale = "ar",
                Body = "شكراً {{customer_name}}! تم تأكيد موعدك يوم {{appointment_date}} الساعة {{appointment_time}}. نراك قريباً!"
            }
        };
        templates.Add(confirmation);

        // Review request template
        var review = new Template
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = "review_request",
            Description = "Post-appointment review request SMS",
            Category = "review",
            IsSystem = true
        };
        review.LocaleVariants = new List<TemplateLocaleVariant>
        {
            new()
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                TemplateId = review.Id,
                Locale = "fr",
                Body = "Bonjour {{customer_name}}, merci pour votre visite ! Nous serions ravis d'avoir votre avis : {{review_link}}"
            },
            new()
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                TemplateId = review.Id,
                Locale = "ar",
                Body = "مرحباً {{customer_name}}، شكراً لزيارتك! سنكون سعداء بمعرفة رأيك: {{review_link}}"
            }
        };
        templates.Add(review);

        // Cancellation notice template
        var cancellation = new Template
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = "appointment_cancelled",
            Description = "Appointment cancellation notice SMS",
            Category = "cancellation",
            IsSystem = true
        };
        cancellation.LocaleVariants = new List<TemplateLocaleVariant>
        {
            new()
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                TemplateId = cancellation.Id,
                Locale = "fr",
                Body = "Bonjour {{customer_name}}, votre RDV du {{appointment_date}} a été annulé. Contactez-nous pour reprogrammer."
            },
            new()
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                TemplateId = cancellation.Id,
                Locale = "ar",
                Body = "مرحباً {{customer_name}}، تم إلغاء موعدك يوم {{appointment_date}}. تواصل معنا لإعادة الجدولة."
            }
        };
        templates.Add(cancellation);

        return templates;
    }

    /// <summary>
    /// Generates a URL-safe slug from the business name, ensuring global uniqueness.
    /// </summary>
    private async Task<string> GenerateUniqueSlugAsync(string businessName, CancellationToken ct)
    {
        string baseSlug = Regex.Replace(businessName.Trim().ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
        if (string.IsNullOrEmpty(baseSlug)) baseSlug = "business";

        string slug = baseSlug;
        int suffix = 1;
        while (await _db.Tenants
            .IgnoreQueryFilters() // Slug uniqueness must be checked globally across all tenants
            .AnyAsync(t => t.Slug == slug && !t.IsDeleted, ct))
        {
            slug = $"{baseSlug}-{suffix++}";
        }
        return slug;
    }

    private static UserInfo ToUserInfo(User user, string businessName) =>
        new(user.Id, user.TenantId, user.Email, user.FirstName, user.LastName, user.Role.ToString(), businessName);
}
