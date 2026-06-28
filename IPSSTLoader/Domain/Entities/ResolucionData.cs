using System;
using System.Collections.Generic;
using System.Text;

namespace IPSSTLoader.Domain.Entities;

public class ResolucionData
{
    public string NroResolucion { get; set; } = string.Empty;
    public DateOnly? FechaResolucion { get; set; }
    public string? Observaciones { get; set; }
}
