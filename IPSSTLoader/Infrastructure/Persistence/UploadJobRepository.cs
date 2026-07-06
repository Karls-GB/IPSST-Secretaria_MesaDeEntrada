using IPSSTLoader.Domain.Entities;
using IPSSTLoader.Domain.Enums;
using IPSSTLoader.Domain.Interface;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;

namespace IPSSTLoader.Infrastructure.Persistence;

public class UploadJobRepository : IUploadJobRepository
{
    private readonly AppDbContext _context;

    public UploadJobRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task AddAsync(UploadJob job)
    {
        _context.UploadJobs.Add(job);
        await _context.SaveChangesAsync();
    }

    public async Task UpdateAsync(UploadJob job)
    {
        _context.UploadJobs.Update(job);
        await _context.SaveChangesAsync();
    }

    public async Task<List<UploadJob>> GetFailedAsync()
    {
        return await _context.UploadJobs.Where(j => j.Status == UploadStatus.Failed).ToListAsync();
    }

    public async Task<UploadJob?> GetByIdAsync(Guid id)
    {
        return await _context.UploadJobs.FindAsync(id);
    }
}
