using IPSST.Domain.Entities;
using IPSST.Domain.Enums;
using IPSST.Domain.Interface;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace IPSST.Application.Services;

public enum FiltroAccion { Todas, Busqueda, Pase, Resolucion, Cancelacion }

public enum FiltroFecha { Hoy, UltimosSieteDias, Todo }

public class HistorialService
{
    private readonly IUploadJobRepository _uploadJobRepository;

    // Las busquedas no se guardan en la base: solo duran la sesion
    private readonly List<HistorialEntry> _busquedas = new();

    private readonly ILogger<HistorialService> _logger;

    public HistorialService(IUploadJobRepository uploadJobRepository, ILogger<HistorialService> logger)
    {
        _uploadJobRepository = uploadJobRepository;
        _logger = logger;
    }

    public void RegistrarBusqueda(ResultadoBusqueda resultado)
    {
        // Un expediente buscado varias veces aparece una sola vez, con la fecha mas reciente
        _busquedas.RemoveAll(b => b.NroExpediente == resultado.NroExpediente);

        var ubicacion = string.Join(" en ", new[] { resultado.Estado, resultado.Oficina }
            .Where(s => !string.IsNullOrWhiteSpace(s)));

        _busquedas.Add(new HistorialEntry
        {
            Fecha = DateTime.Now,
            Accion = "Busqueda",
            NroExpediente = resultado.NroExpediente,
            Causante = resultado.Causante,
            Resultado = "Encontrado",
            Detalle = ubicacion,
            EsBusqueda = true
        });
    }

    public async Task<List<HistorialEntry>> ObtenerAsync(FiltroAccion accion, FiltroFecha fecha)
    {
        DateTime? desde = fecha switch
        {
            FiltroFecha.Hoy => DateTime.Today,
            FiltroFecha.UltimosSieteDias => DateTime.Today.AddDays(-6),
            _ => null
        };

        var entradas = new List<HistorialEntry>();

        if (accion is FiltroAccion.Todas or FiltroAccion.Busqueda)
        {
            entradas.AddRange(_busquedas.Where(b => desde == null || b.Fecha >= desde));
        }

        var tipos = accion switch
        {
            FiltroAccion.Todas => new[] { TipoAccion.Pase, TipoAccion.CorreccionPase, TipoAccion.Resolucion, TipoAccion.Cancelacion },
            FiltroAccion.Pase => new[] { TipoAccion.Pase, TipoAccion.CorreccionPase },
            FiltroAccion.Resolucion => new[] { TipoAccion.Resolucion },
            FiltroAccion.Cancelacion => new[] { TipoAccion.Cancelacion },
            _ => Array.Empty<TipoAccion>()
        };

        if (tipos.Length > 0)
        {
            try
            {
                var jobs = await _uploadJobRepository.GetHistorialAsync(desde?.ToUniversalTime(), tipos);
                entradas.AddRange(jobs.Select(Mapear));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al leer el historial de la base (filtro {Accion}, {Fecha})", accion, fecha);
                throw;
            }
        }

        return entradas.OrderByDescending(e => e.Fecha).ToList();
    }

    private HistorialEntry Mapear(UploadJob job)
    {
        var pase = Deserializar<PaseData>(job.PaseDataJson, job);
        var resolucion = Deserializar<ResolucionData>(job.ResolucionDataJson, job);

        var entrada = new HistorialEntry
        {
            Fecha = DateTime.SpecifyKind(job.CreatedAt, DateTimeKind.Utc).ToLocalTime(),
            NroExpediente = job.NroExpediente,
            Causante = job.Causante,
            Fallido = job.Status == UploadStatus.Failed,
            Resultado = job.Status switch
            {
                UploadStatus.Completed => "Exitoso",
                UploadStatus.Failed => "Fallido",
                UploadStatus.Cancelled => "Cancelado",
                // Una fase hecha y la otra no termino (ej. se cerro el programa en el medio)
                UploadStatus.ResolucionCompleted or UploadStatus.CancelacionCompleted or UploadStatus.PaseCompleted => "Incompleto",
                _ => "En curso"
            }
        };

        var destino = string.IsNullOrWhiteSpace(pase?.OficinaDestino) ? null : $"a {pase.OficinaDestino}, {pase.Folios} fojas";

        (entrada.Accion, entrada.Detalle) = job.TipoAccion switch
        {
            TipoAccion.CorreccionPase => ("Pase", $"{destino} (corrección, antes a {job.OficinaAnterior ?? "?"})"),
            TipoAccion.Cancelacion => ("Cancelacion", $"Pase cancelado (iba a {job.OficinaAnterior ?? "?"})"),
            TipoAccion.Resolucion => ("Resolucion",
                $"Res. {resolucion?.NroResolucion} del {resolucion?.FechaResolucion:dd/MM/yyyy}" + (destino != null ? $", pase {destino}" : "")),
            _ => ("Pase", destino)
        };

        if (entrada.Fallido && !string.IsNullOrWhiteSpace(job.LastError))
        {
            entrada.Detalle = $"{entrada.Detalle} | Error: {job.LastError}";
        }

        return entrada;
    }

    private T? Deserializar<T>(string? json, UploadJob job) where T : class
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json);
        }
        catch (JsonException ex)
        {
            // La fila se muestra igual, sin esos datos
            _logger.LogWarning(ex, "Historial: datos de {Tipo} ilegibles en el trabajo {JobId} ({NroExpediente})", typeof(T).Name, job.Id, job.NroExpediente);
            return null;
        }
    }
}
