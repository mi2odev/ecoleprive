using CentreSoutien.Application.Abstractions;
using CentreSoutien.Domain.Entities;
using CentreSoutien.Domain.Enums;
using CentreSoutien.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CentreSoutien.Infrastructure.Services;

/// <summary>Builds activity journal entries; services add them to their own unit of work so they are saved together.</summary>
internal static class Audit
{
    public static AuditEntry Entry(TimeProvider clock, AuditCategory category, string action, string? details = null) => new()
    {
        At = clock.GetLocalNow().DateTime,
        Category = category,
        Action = action.Length > 300 ? action[..300] : action,
        Details = details is { Length: > 1000 } ? details[..1000] : details,
    };
}

public sealed class AuditService(IDbContextFactory<AppDbContext> factory, TimeProvider clock) : IAuditService
{
    public async Task AddAsync(AuditCategory category, string action, string? details = null, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        db.AuditLog.Add(Audit.Entry(clock, category, action, details));
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<AuditEntry>> ListAsync(AuditCategory? category = null, DateTime? from = null, int take = 500, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var q = db.AuditLog.AsNoTracking();
        if (category is not null) q = q.Where(x => x.Category == category);
        if (from is not null) q = q.Where(x => x.At >= from);
        return await q.OrderByDescending(x => x.At).ThenByDescending(x => x.Id).Take(take).ToListAsync(ct);
    }
}
