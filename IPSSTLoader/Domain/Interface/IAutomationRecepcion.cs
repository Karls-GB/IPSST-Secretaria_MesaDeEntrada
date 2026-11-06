using IPSST.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace IPSST.Domain.Interface;

public interface IAutomationRecepcion
{
    Task<RecepcionItem?> PrepararRecepcionAsync(string nroExpediente);
    Task<bool> ConfirmarRecepcionAsync(string nroExpediente);
    Task<ListadoRecepcion> BuscarPorOficinaAsync(string oficina);
    Task<BulkAdmitResult> AdmitBulkAsync(string oficina, List<string> nroExpedientes);
}
