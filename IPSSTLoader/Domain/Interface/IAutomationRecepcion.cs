using IPSST.Domain.Entities;
using IPSSTLoader.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace IPSSTLoader.Domain.Interface;

public interface IAutomationRecepcion
{
    Task<RecepcionItem?> PrepararRecepcionAsync(string nroExpediente);
    Task<bool> ConfirmarRecepcionAsync(string nroExpediente);
    Task<List<RecepcionItem>> BuscarPorOficinaAsync(string oficina);
    Task<BulkAdmitResult> AdmitBulkAsync(string oficina, List<string> nroExpedientes);
}
