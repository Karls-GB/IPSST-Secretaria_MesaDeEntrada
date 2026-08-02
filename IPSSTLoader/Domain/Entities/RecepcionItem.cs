using System;
using System.Collections.Generic;
using System.Text;

namespace IPSST.Domain.Entities;

public class RecepcionItem
{
    public string NroExpediente { get; set; } = string.Empty;
    public string? Asunto { get; set; }
    public string? Causante { get; set; }
    public int? Folios { get; set; }
    public string? OficinaOrigen { get; set; }
    public bool Seleccionado { get; set; }
}
