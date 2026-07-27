using IPSST.Domain.Entities;
using IPSSTLoader.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace IPSSTLoader.Domain.Interface;

public interface IAutomationResolucion
{
    Task<ResPreparation?> PrepararResolucionAsync(string nroExpediente);
    Task<bool> ConfirmarResAsync(string nroResolucion, DateTime fechaResolucion, string observacionesRes);
}
