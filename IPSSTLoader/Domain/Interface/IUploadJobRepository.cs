using System;
using System.Collections.Generic;
using System.Text;
using IPSSTLoader.Domain.Entities;

namespace IPSSTLoader.Domain.Interface;

public interface IUploadJobRepository
{
    Task AddAsync(UploadJob job);
    Task UpdateAsync(UploadJob job);
    Task<List<UploadJob>> GetFailedAsync();
    Task<UploadJob?> GetByIdAsync(Guid id);
}
