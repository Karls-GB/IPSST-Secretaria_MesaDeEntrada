using System;
using System.Collections.Generic;
using System.Text;

namespace IPSST.Domain.Entities;

// Pase ya hecho desde esta oficina que todavia no fue recibido, y se puede cancelar
public class PasePendiente
{
    public string NroExpediente { get; set; } = string.Empty;
    public string ExpId { get; set; } = string.Empty;
    public string OficinaDestino { get; set; } = string.Empty;
    public string? Causante { get; set; }

    // Folios con los que se hizo el pase pendiente
    public int FoliosPase { get; set; }

    // Folios del expediente sin el pase pendiente (los agregados en el pase se pierden al cancelarlo)
    public int FoliosExpediente { get; set; }
}
