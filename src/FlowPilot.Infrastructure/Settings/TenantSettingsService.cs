using System.Text.Json;
using FlowPilot.Application.Settings;
using FlowPilot.Domain.Entities;
using FlowPilot.Infrastructure.Persistence;
using FlowPilot.Shared;
using FlowPilot.Shared.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace FlowPilot.Infrastructure.Settings;

/// <summary>
/// Reads and updates the combined Tenant + TenantSettings for the current tenant.
/// </summary>
public sealed class TenantSettingsService : ITenantSettingsService
{
    private readonly AppDbContext _db;
    private readonly ICurrentTenant _currentTenant;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public TenantSettingsService(AppDbContext db, ICurrentTenant currentTenant)
    {
        _db = db;
        _currentTenant = currentTenant;
    }

    /// <inheritdoc />
    public async Task<Result<TenantSettingsDto>> GetAsync(CancellationToken cancellationToken = default)
    {
        Tenant? tenant = await _db.Tenants
            .AsNoTracking()
            .Include(t => t.Settings)
            .FirstOrDefaultAsync(t => t.Id == _currentTenant.TenantId, cancellationToken);

        if (tenant is null)
            return Result.Failure<TenantSettingsDto>(Error.NotFound("Tenant", _currentTenant.TenantId));

        return Result.Success(ToDto(tenant, tenant.Settings));
    }

    /// <inheritdoc />
    public async Task<Result<TenantSettingsDto>> UpdateAsync(UpdateTenantSettingsRequest request, CancellationToken cancellationToken = default)
    {
        Tenant? tenant = await _db.Tenants
            .Include(t => t.Settings)
            .FirstOrDefaultAsync(t => t.Id == _currentTenant.TenantId, cancellationToken);

        if (tenant is null)
            return Result.Failure<TenantSettingsDto>(Error.NotFound("Tenant", _currentTenant.TenantId));

        // Update Tenant fields
        if (request.BusinessName is not null)
            tenant.BusinessName = request.BusinessName.Trim();
        if (request.BusinessPhone is not null)
            tenant.BusinessPhone = request.BusinessPhone.Trim();
        if (request.BusinessEmail is not null)
            tenant.BusinessEmail = request.BusinessEmail.Trim();
        if (request.Address is not null)
            tenant.Address = request.Address.Trim();
        if (request.Timezone is not null)
            tenant.Timezone = request.Timezone;
        if (request.DefaultLanguage is not null)
            tenant.DefaultLanguage = request.DefaultLanguage;
        if (request.Currency is not null)
            tenant.Currency = request.Currency;

        // Ensure TenantSettings exists
        TenantSettings settings = tenant.Settings ?? new TenantSettings
        {
            OwnerTenantId = tenant.Id,
            TenantId = tenant.TenantId
        };

        if (tenant.Settings is null)
        {
            _db.TenantSettings.Add(settings);
            tenant.Settings = settings;
        }

        // Update TenantSettings fields
        if (request.DefaultSenderPhone is not null)
            settings.DefaultSenderPhone = request.DefaultSenderPhone.Trim();
        if (request.ReminderLeadTimeMinutes.HasValue)
            settings.ReminderLeadTimeMinutes = request.ReminderLeadTimeMinutes.Value;
        if (request.GooglePlaceId is not null)
            settings.GooglePlaceId = request.GooglePlaceId.Trim();
        if (request.FacebookPageUrl is not null)
            settings.FacebookPageUrl = request.FacebookPageUrl.Trim();
        if (request.TrustpilotUrl is not null)
            settings.TrustpilotUrl = request.TrustpilotUrl.Trim();

        // Update JSON sub-sections
        if (request.BusinessHours is not null)
            settings.BusinessHoursJson = JsonSerializer.Serialize(request.BusinessHours, JsonOptions);
        if (request.Notifications is not null)
            settings.NotificationSettingsJson = JsonSerializer.Serialize(request.Notifications, JsonOptions);
        if (request.ReviewSettings is not null)
            settings.ReviewSettingsJson = JsonSerializer.Serialize(request.ReviewSettings, JsonOptions);
        if (request.BookingSettings is not null)
            settings.BookingSettingsJson = JsonSerializer.Serialize(request.BookingSettings, JsonOptions);

        await _db.SaveChangesAsync(cancellationToken);

        return Result.Success(ToDto(tenant, settings));
    }

    private static TenantSettingsDto ToDto(Tenant tenant, TenantSettings? settings)
    {
        BusinessHoursDto? businessHours = Deserialize<BusinessHoursDto>(settings?.BusinessHoursJson);
        NotificationSettingsDto? notifications = Deserialize<NotificationSettingsDto>(settings?.NotificationSettingsJson);
        ReviewSettingsDto? reviewSettings = Deserialize<ReviewSettingsDto>(settings?.ReviewSettingsJson);
        BookingSettingsDto? bookingSettings = Deserialize<BookingSettingsDto>(settings?.BookingSettingsJson);

        return new TenantSettingsDto(
            BusinessName: tenant.BusinessName,
            BusinessPhone: tenant.BusinessPhone,
            BusinessEmail: tenant.BusinessEmail,
            Address: tenant.Address,
            Timezone: tenant.Timezone,
            DefaultLanguage: tenant.DefaultLanguage,
            Currency: tenant.Currency,
            DefaultSenderPhone: settings?.DefaultSenderPhone,
            ReminderLeadTimeMinutes: settings?.ReminderLeadTimeMinutes ?? 120,
            GooglePlaceId: settings?.GooglePlaceId,
            FacebookPageUrl: settings?.FacebookPageUrl,
            TrustpilotUrl: settings?.TrustpilotUrl,
            BusinessHours: businessHours,
            Notifications: notifications,
            ReviewSettings: reviewSettings,
            BookingSettings: bookingSettings);
    }

    private static T? Deserialize<T>(string? json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
