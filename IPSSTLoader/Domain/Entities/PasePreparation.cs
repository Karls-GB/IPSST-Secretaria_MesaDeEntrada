using System;
using System.Collections.Generic;
using System.Text;

namespace IPSST.Domain.Entities;

public class PasePreparation
{
    public string? Causante { get; set; }
    public int FolioActual { get; set; }
    public string? ExpId { get; set; }

    // Si no esta en la cola de Pases pero tiene un pase hecho desde esta oficina
    public PasePendiente? PasePendiente { get; set; }

    // Si el expediente existe pero no se puede hacer el pase (otra oficina, pase de otra oficina, etc.)
    public string? MotivoNoDisponible { get; set; }
}
