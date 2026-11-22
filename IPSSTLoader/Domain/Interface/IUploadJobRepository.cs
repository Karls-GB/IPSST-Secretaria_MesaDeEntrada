using System;
using System.Collections.Generic;
using System.Text;
using IPSST.Domain.Entities;
using IPSST.Domain.Enums;

namespace IPSST.Domain.Interface;

public interface IUploadJobRepository
{
    Task AddAsync(UploadJob job);
    Task UpdateAsync(UploadJob job);
    Task<List<UploadJob>> GetFailedAsync();
    Task<UploadJob?> GetByIdAsync(Guid id);

    // Mas nuevos primero. desdeUtc null = todos
    Task<List<UploadJob>> GetHistorialAsync(DateTime? desdeUtc, IReadOnlyCollection<TipoAccion> tipos);
}
