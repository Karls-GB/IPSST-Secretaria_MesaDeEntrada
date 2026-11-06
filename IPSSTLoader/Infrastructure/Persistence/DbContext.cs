using IPSST.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;

namespace IPSST.Infrastructure.Persistence;

public class AppDbContext : DbContext
{
    public DbSet<UploadJob> UploadJobs { get; set; }

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }
}
