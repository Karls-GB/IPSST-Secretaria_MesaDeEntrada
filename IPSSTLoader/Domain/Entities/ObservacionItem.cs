using System;
using System.Collections.Generic;
using System.Text;

namespace IPSSTLoader.Domain.Entities;

public class ObservacionItem
{
    public string? FechaHora { get; set; }
    public string? Descripcion { get; set; }
    public string? Usuario { get; set; }
    public string? Oficina { get; set; }
}
