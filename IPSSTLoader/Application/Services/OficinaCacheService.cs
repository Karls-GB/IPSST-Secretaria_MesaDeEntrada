using IPSST.Application.Configuration;
using IPSST.Domain.Entities;
using IPSST.Domain.Interface;

namespace IPSST.Application.Services;

public class OficinaCacheService
{
    private readonly IAutomationPase _automationPase;
    private readonly OficinasFrecuentesConfig _frecuentes;

    public List<OficinaOption> Oficinas { get; private set; } = new();

    public OficinaCacheService(IAutomationPase automationPase, OficinasFrecuentesConfig frecuentes)
    {
        _automationPase = automationPase;
        _frecuentes = frecuentes;
    }

    public async Task InitializeAsync()
    {
        Oficinas = await _automationPase.GetOficinasDestinoAsync();
    }

    /// <summary>
    /// Oficinas que contienen el texto. Las frecuentes van primero (en el orden configurado);
    /// el resto mantiene el orden del sitio (OrderBy es estable).
    /// </summary>
    public List<OficinaOption> Filtrar(string texto) =>
        Oficinas
            .Where(o => o.Nombre.Contains(texto, StringComparison.OrdinalIgnoreCase))
            .OrderBy(o => PosicionFrecuente(o.Nombre))
            .ToList();

    private int PosicionFrecuente(string nombre)
    {
        for (int i = 0; i < _frecuentes.Nombres.Count; i++)
        {
            if (string.Equals(_frecuentes.Nombres[i], nombre.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }
        return int.MaxValue;
    }
}
