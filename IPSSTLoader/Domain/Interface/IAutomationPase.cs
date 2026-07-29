using IPSST.Domain.Entities;
using IPSSTLoader.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace IPSSTLoader.Domain.Interface;

public interface IAutomationPase
{
    Task<PasePreparation?> PrepararPaseAsync(string nroExpediente);
    Task<bool> ConfirmarPaseAsync(string oficinaDestino, int foliosTotal, string observaciones, string expId);
    Task<List<OficinaOption>> GetOficinasDestinoAsync();
}
