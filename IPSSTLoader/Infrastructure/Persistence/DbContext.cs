using IPSSTLoader.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;

namespace IPSSTLoader.Infrastructure.Persistence;

public class AppDbContext : DbContext
{
    public DbSet<UploadJob> UploadJobs { get; set; }

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }
}
