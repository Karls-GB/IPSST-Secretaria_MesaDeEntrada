using System;
using System.Collections.Generic;
using System.Text;

namespace IPSST.Domain.Entities;

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
    // Oficina hacia la que tiene un pase (solo si esta en transito)
    public string? OficinaDestino { get; set; }
    public bool EnTransito => Estado?.Contains("TRANSITO", StringComparison.OrdinalIgnoreCase) == true;
    public string? Sucursal { get; set; }
    // Se consulta solo si el usuario lo pide (carga la cola de Pases, que es lenta)
    public string? TrabajadoPor { get; set; }
    // Solo tiene sentido si esta en la oficina del usuario y no en transito
    public bool PuedeConsultarTrabajadoPor { get; set; }

    // Solo la primera pagina de la tabla (las mas recientes)
    public List<ObservacionItem> Observaciones { get; set; } = new();
    // La primera que se agrego: ultima fila de la ultima pagina
    public ObservacionItem? PrimeraObservacion { get; set; }
    // La tabla tiene mas paginas que se pueden cargar a pedido
    public bool HayMasObservaciones { get; set; }
    // Observaciones que el sistema informo pero no terminaron de cargar
    public int ObservacionesFaltantes { get; set; }
    public string? UsuarioPase { get; set; }
    public string? FechaPase { get; set; }
}
