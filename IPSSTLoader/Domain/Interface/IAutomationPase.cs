using IPSST.Domain.Entities;
using IPSSTLoader.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace IPSSTLoader.Domain.Interface;

public interface IAutomationPase
{
    Task<bool> SubmitAsync(Expediente expediente);
    Task<List<OficinaOption>> GetOficinasDestinoAsync();
}
