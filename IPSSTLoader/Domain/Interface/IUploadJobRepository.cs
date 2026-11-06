using System;
using System.Collections.Generic;
using System.Text;
using IPSST.Domain.Entities;

namespace IPSST.Domain.Interface;

public interface IUploadJobRepository
{
    Task AddAsync(UploadJob job);
    Task UpdateAsync(UploadJob job);
    Task<List<UploadJob>> GetFailedAsync();
    Task<UploadJob?> GetByIdAsync(Guid id);
}
