using IPSST.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace IPSST.Domain.Interface;

public interface IAutomationPase
{
    Task<PasePreparation?> PrepararPaseAsync(string nroExpediente);
    Task<bool> ConfirmarPaseAsync(string oficinaDestino, int foliosTotal, string observaciones, string expId);
    Task<List<OficinaOption>> GetOficinasDestinoAsync();
    Task<PasePendiente?> PrepararCancelarPaseAsync(string nroExpediente);
    Task<bool> ConfirmarCancelarPaseAsync(string expId);
}
