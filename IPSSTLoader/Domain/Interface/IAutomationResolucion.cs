using IPSST.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace IPSST.Domain.Interface;

public interface IAutomationResolucion
{
    Task<ResPreparation?> PrepararResolucionAsync(string nroExpediente);
    Task<bool> ConfirmarResAsync(string nroExpediente, string nroResolucion, DateTime fechaResolucion, string observacionesRes);
}
