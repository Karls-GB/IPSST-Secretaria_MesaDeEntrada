using IPSSTLoader.Domain.Entities;
using IPSSTLoader.Domain.Interface;
using System;
using System.Collections.Generic;
using System.Text;

namespace IPSSTLoader.Infrastructure.Automation;

public class PlaywrightRecepcion : IAutomationRecepcion
{
    public Task<bool> AdmitSingleAsync(string nroExpediente)
    {
        throw new NotImplementedException("Recepcion Aun no Implementado");
    }

    public Task<BulkAdmitResult> AdmitBulkAsync(string oficina, List<string> nroExpedientes)
    {
        throw new NotImplementedException("Recepcion Aun no Implementado");
    }
}
