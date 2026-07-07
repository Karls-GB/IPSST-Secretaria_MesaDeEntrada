using System;
using System.Collections.Generic;
using System.Text;

namespace IPSSTLoader.Domain.Entities;

public class ResultadoBusqueda
{
    public string NroExpediente { get; set; } = string.Empty;
    public string? ExpedienteIdWeb { get; set; }
    public DateTime? FechaAlta { get; set; }
    public int? Folios { get; set; }
    public string? Motivo { get; set; }
    public string? Asunto { get; set; }
    public string? Causante { get; set; }
    public string? CuitCuil { get; set; }
    public string? Estado { get; set; }
    public string? Oficina { get; set; }
    public string? Sucursal { get; set; }
    public string? TrabajadoPor { get; set; }
    public List<ObservacionItem> Observaciones { get; set; } = new();
    public string? UsuarioPase { get; set; }
    public string? FechaPase { get; set; }
}
