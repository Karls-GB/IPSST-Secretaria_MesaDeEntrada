using System;
using System.Collections.Generic;
using System.Text;

namespace IPSSTLoader.Domain.Entities;

public class Expediente
{
    public Guid Id { get; set; }
    public string NroExpediente { get; set; } = string.Empty;
    public string? ExpedienteIdWeb { get; set; }
    public ResolucionData? Resolucion { get; set; }
    public PaseData? Pase {  get; set; }
    public DateTime CreatedAt { get; set; }

}
