using System;
using System.Collections.Generic;
using System.Text;

namespace IPSSTLoader.Domain.Entities;

public class PaseData
{
    public int Folios { get; set; }
    public string OficinaDestino { get; set; } = string.Empty;
    public string? Observaciones { get; set; }
}
