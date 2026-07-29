using System;
using System.Collections.Generic;
using System.Text;

namespace IPSST.Domain.Entities;

public class ResPreparation
{
    public string? Causante { get; set; }
    public int FolioActual { get; set; }
    public string ExpId { get; set; } = string.Empty;
    public List<string?> ResolucionesAnteriores { get; set; } = new ();
}
