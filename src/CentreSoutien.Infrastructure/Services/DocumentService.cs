using CentreSoutien.Application.Abstractions;
using CentreSoutien.Domain.Entities;
using CentreSoutien.Domain.Enums;
using CentreSoutien.Infrastructure.Data;
using CentreSoutien.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace CentreSoutien.Infrastructure.Services;

public sealed class DocumentService(IDbContextFactory<AppDbContext> factory, IFileStorage storage) : IDocumentService
{
    public const long MaxSizeBytes = 50L * 1024 * 1024;

    public async Task<List<Document>> ListAsync(DocumentOwnerType? ownerType = null, int? ownerId = null, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var q = db.Documents.AsNoTracking();
        if (ownerType is not null) q = q.Where(d => d.OwnerType == ownerType);
        if (ownerId is not null) q = q.Where(d => d.OwnerId == ownerId);
        return (await q.ToListAsync(ct)).OrderByDescending(d => d.CreatedAt).ToList();
    }

    public async Task<Document> AddAsync(string sourcePath, string title, string? category, DocumentOwnerType ownerType, int? ownerId, CancellationToken ct = default)
    {
        var info = new FileInfo(sourcePath);
        if (!info.Exists) throw new BusinessException("Fichier introuvable.");
        if (info.Length > MaxSizeBytes) throw new BusinessException("Le fichier dépasse 50 Mo.");
        var stored = storage.Import(sourcePath, StorageAreas.Documents);
        var doc = new Document
        {
            Title = string.IsNullOrWhiteSpace(title) ? Path.GetFileNameWithoutExtension(info.Name) : title.Trim(),
            Category = category, StoredFile = stored, OriginalName = info.Name, SizeBytes = info.Length, OwnerType = ownerType, OwnerId = ownerId,
        };
        await using var db = await factory.CreateDbContextAsync(ct);
        db.Documents.Add(doc);
        await db.SaveChangesAsync(ct);
        return doc;
    }

    public async Task<Document> SaveAsync(Document document, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(document.Title)) throw new BusinessException("Le titre est obligatoire.");
        await using var db = await factory.CreateDbContextAsync(ct);
        var entity = await db.Documents.FindAsync([document.Id], ct) ?? throw new BusinessException("Document introuvable.");
        entity.Title = document.Title.Trim();
        entity.Category = document.Category;
        entity.Notes = document.Notes;
        entity.OwnerType = document.OwnerType;
        entity.OwnerId = document.OwnerId;
        await db.SaveChangesAsync(ct);
        return entity;
    }

    public string GetFullPath(Document document) => storage.GetPath(document.StoredFile, StorageAreas.Documents);

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var d = await db.Documents.FindAsync([id], ct);
        if (d is null) return;
        db.Documents.Remove(d);
        await db.SaveChangesAsync(ct);
        storage.Delete(d.StoredFile, StorageAreas.Documents);
    }
}
