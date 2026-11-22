namespace IPSST.Domain.Entities;

// Fila de la pestaña Historial (solo para mostrar)
public class HistorialEntry
{
    public DateTime Fecha { get; set; }
    public string Accion { get; set; } = string.Empty;
    public string NroExpediente { get; set; } = string.Empty;
    public string? Causante { get; set; }
    public string Resultado { get; set; } = string.Empty;
    public string? Detalle { get; set; }
    public bool EsBusqueda { get; set; }
    public bool Fallido { get; set; }
}
