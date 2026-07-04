using IPSSTLoader.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace IPSSTLoader.Domain.Interface;

public interface IAutomationRecepcion
{
    Task<bool> AdmitSingleAsync(string nroExpediente);
    Task<BulkAdmitResult> AdmitBulkAsync(string oficina, List<string> nroExpediente);
}
