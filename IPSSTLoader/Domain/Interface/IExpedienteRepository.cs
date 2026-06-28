using System;
using System.Collections.Generic;
using System.Text;
using IPSSTLoader.Domain.Entities;

namespace IPSSTLoader.Domain.Interface;

public interface IExpedienteRepository
{
    Task AddAsync(Expediente expediente);
    Task<Expediente?> GetByIdAsync(Guid id);
}
