using System;
using System.Collections.Generic;
using System.Text;

namespace IPSSTLoader.Domain.Interface;

public interface IAutomateRecepcion
{
    Task<bool> AdmitSingleAsync(string nroExpediente);
    Task<bool> AdmitBulkAsync(string oficina, List<string> nroExpediente);
}
