using CentreSoutien.Application.Abstractions;
using CentreSoutien.Domain.Calculations;
using CentreSoutien.Domain.Entities;
using CentreSoutien.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CentreSoutien.Infrastructure.Services;

/// <summary>Generic CRUD for flat reference entities (no owned collections are saved).</summary>
public class CrudService<T>(IDbContextFactory<AppDbContext> factory) : ICrudService<T> where T : Entity
{
    protected IDbContextFactory<AppDbContext> Factory => factory;

    protected virtual IQueryable<T> Query(AppDbContext db) => db.Set<T>().AsNoTracking();
    protected virtual IEnumerable<T> Order(IEnumerable<T> items) => items.OrderBy(x => x.Id);
    protected virtual void Validate(T entity) { }
    protected virtual Task BeforeDeleteAsync(AppDbContext db, int id, CancellationToken ct) => Task.CompletedTask;

    public async Task<List<T>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return Order(await Query(db).ToListAsync(ct)).ToList();
    }

    public async Task<T?> GetAsync(int id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await Query(db).FirstOrDefaultAsync(x => x.Id == id, ct);
    }

    public async Task<T> SaveAsync(T entity, CancellationToken ct = default)
    {
        Validate(entity);
        await using var db = await factory.CreateDbContextAsync(ct);
        db.Entry(entity).State = entity.Id == 0 ? EntityState.Added : EntityState.Modified;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("UNIQUE") == true)
        {
            throw new BusinessException("Un élément portant ce nom existe déjà.");
        }
        return entity;
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await BeforeDeleteAsync(db, id, ct);
        var entity = await db.Set<T>().FindAsync([id], ct);
        if (entity is null) return;
        db.Remove(entity);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            throw new BusinessException("Suppression impossible : cet élément est encore utilisé ailleurs.");
        }
    }
}

public sealed class SubjectService(IDbContextFactory<AppDbContext> f) : CrudService<Subject>(f)
{
    protected override IEnumerable<Subject> Order(IEnumerable<Subject> items) => items.OrderBy(x => x.Name);
    protected override void Validate(Subject e)
    {
        if (string.IsNullOrWhiteSpace(e.Name)) throw new BusinessException("Le nom de la matière est obligatoire.");
        e.Name = e.Name.Trim();
    }
    protected override async Task BeforeDeleteAsync(AppDbContext db, int id, CancellationToken ct)
    {
        if (await db.Courses.AnyAsync(c => c.SubjectId == id, ct))
            throw new BusinessException("Cette matière est utilisée par des cours. Supprimez d'abord ces cours.");
    }
}

public sealed class RoomService(IDbContextFactory<AppDbContext> f) : CrudService<Room>(f)
{
    protected override IEnumerable<Room> Order(IEnumerable<Room> items) => items.OrderBy(x => x.Name);
    protected override void Validate(Room e)
    {
        if (string.IsNullOrWhiteSpace(e.Name)) throw new BusinessException("Le nom de la salle est obligatoire.");
        if (e.Capacity <= 0) throw new BusinessException("La capacité doit être positive.");
        e.Name = e.Name.Trim();
    }
}

public sealed class DiscountService(IDbContextFactory<AppDbContext> f) : CrudService<Discount>(f)
{
    protected override IEnumerable<Discount> Order(IEnumerable<Discount> items) => items.OrderBy(x => x.Name);
    protected override void Validate(Discount e)
    {
        if (string.IsNullOrWhiteSpace(e.Name)) throw new BusinessException("Le nom de la remise est obligatoire.");
        if (e.Value < 0 || (e.Type == Domain.Enums.DiscountType.Percent && e.Value > 100))
            throw new BusinessException("Valeur de remise invalide.");
    }
}

public sealed class ExpenseService(IDbContextFactory<AppDbContext> f) : CrudService<Expense>(f)
{
    protected override IEnumerable<Expense> Order(IEnumerable<Expense> items) => items.OrderByDescending(x => x.Date).ThenByDescending(x => x.Id);
    protected override void Validate(Expense e)
    {
        if (e.Amount <= 0) throw new BusinessException("Le montant doit être positif.");
        if (string.IsNullOrWhiteSpace(e.Category)) throw new BusinessException("La catégorie est obligatoire.");
    }
}

public sealed class ParentService(IDbContextFactory<AppDbContext> factory) : IParentService
{
    public async Task<List<Parent>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var list = await db.Parents.AsNoTracking().Include(p => p.Children).ToListAsync(ct);
        return list.OrderBy(p => p.FullName).ToList();
    }

    public async Task<Parent?> GetAsync(int id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Parents.AsNoTracking()
            .Include(p => p.Children).ThenInclude(s => s.Enrollments).ThenInclude(e => e.Group).ThenInclude(g => g!.Course).ThenInclude(c => c!.Subject)
            .Include(p => p.Children).ThenInclude(s => s.Payments)
            .Include(p => p.Children).ThenInclude(s => s.Discount)
            .AsSplitQuery()
            .FirstOrDefaultAsync(p => p.Id == id, ct);
    }

    public async Task<Parent> SaveAsync(Parent parent, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(parent.FullName)) throw new BusinessException("Le nom du parent est obligatoire.");
        await using var db = await factory.CreateDbContextAsync(ct);
        var children = parent.Children;
        parent.Children = [];
        db.Entry(parent).State = parent.Id == 0 ? EntityState.Added : EntityState.Modified;
        await db.SaveChangesAsync(ct);
        parent.Children = children;
        return parent;
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var p = await db.Parents.FindAsync([id], ct);
        if (p is null) return;
        db.Parents.Remove(p);
        await db.SaveChangesAsync(ct);
    }
}

public sealed class SettingsService(IDbContextFactory<AppDbContext> factory) : ISettingsService
{
    public async Task<CenterSettings> GetAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var s = await db.Settings.AsNoTracking().FirstAsync(ct);
        Money.Currency = s.Currency;
        return s;
    }

    public async Task<CenterSettings> SaveAsync(CenterSettings settings, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(settings.CenterName)) throw new BusinessException("Le nom du centre est obligatoire.");
        if (settings.AutoLockMinutes < 0 || settings.AutoLockMinutes > 240) throw new BusinessException("Délai de verrouillage invalide (0 à 240 minutes).");
        if (settings.PaymentDueDay is < 1 or > 28) throw new BusinessException("Le jour d'échéance doit être entre 1 et 28.");
        if (settings.GradeScale <= 0) throw new BusinessException("Le barème doit être positif.");
        await using var db = await factory.CreateDbContextAsync(ct);
        db.Settings.Update(settings);
        await db.SaveChangesAsync(ct);
        Money.Currency = settings.Currency;
        return settings;
    }
}
