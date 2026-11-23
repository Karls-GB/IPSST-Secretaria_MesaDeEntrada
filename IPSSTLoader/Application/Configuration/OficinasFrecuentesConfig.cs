namespace IPSST.Application.Configuration;

/// <summary>
/// Oficinas que aparecen primero en el autocompletado (clave "OficinasFrecuentes" de appsettings.json),
/// en el orden configurado. Se comparan sin distinguir mayusculas.
/// </summary>
public sealed class OficinasFrecuentesConfig
{
    public IReadOnlyList<string> Nombres { get; }

    public OficinasFrecuentesConfig(IEnumerable<string>? nombres)
    {
        Nombres = (nombres ?? [])
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n.Trim())
            .ToList();
    }
}
