using IPSST.Domain.Entities;
using IPSST.Domain.Enums;
using IPSST.Domain.Interface;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;

namespace IPSST.Infrastructure.Persistence;

public class UploadJobRepository : IUploadJobRepository
{
    // Un contexto por operacion: evita errores de concurrencia si dos trabajos corren a la vez
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public UploadJobRepository(IDbContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task AddAsync(UploadJob job)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        context.UploadJobs.Add(job);
        await context.SaveChangesAsync();
    }

    public async Task UpdateAsync(UploadJob job)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        context.UploadJobs.Update(job);
        await context.SaveChangesAsync();
    }

    public async Task<List<UploadJob>> GetFailedAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.UploadJobs.Where(j => j.Status == UploadStatus.Failed).ToListAsync();
    }

    public async Task<UploadJob?> GetByIdAsync(Guid id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.UploadJobs.FindAsync(id);
    }

    public async Task<List<UploadJob>> GetHistorialAsync(DateTime? desdeUtc, IReadOnlyCollection<TipoAccion> tipos)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var query = context.UploadJobs.AsNoTracking().Where(j => tipos.Contains(j.TipoAccion));

        if (desdeUtc != null)
        {
            query = query.Where(j => j.CreatedAt >= desdeUtc);
        }

        return await query.OrderByDescending(j => j.CreatedAt).ToListAsync();
    }
}
