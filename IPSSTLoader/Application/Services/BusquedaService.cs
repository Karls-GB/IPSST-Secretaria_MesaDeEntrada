using IPSST.Application.Configuration;
using IPSST.Domain.Entities;
using IPSST.Domain.Interface;
using IPSST.Domain.Validation;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace IPSST.Application.Services;

public class BusquedaService
{
    private readonly IAutomationBusqueda _automationBusqueda;
    private readonly ExpValidation _expValidation;
    private readonly ILogger<BusquedaService> _logger;
    private readonly OficinaConfig _oficinaConfig;

    public BusquedaService(IAutomationBusqueda automationBusqueda, ExpValidation expValidation, ILogger<BusquedaService> logger, OficinaConfig oficinaConfig)
    {
        _automationBusqueda = automationBusqueda;
        _expValidation = expValidation;
        _logger = logger;
        _oficinaConfig = oficinaConfig;
    }

    public async Task<(List<ObservacionItem> Observaciones, int Faltantes)> ObtenerTodasObservacionesAsync(string nroExpediente)
    {
        _logger.LogInformation("Cargando todas las observaciones de {NroExpediente}", nroExpediente);

        try
        {
            var resultado = await _automationBusqueda.ObtenerTodasObservacionesAsync(nroExpediente);

            if (resultado.Faltantes > 0)
            {
                _logger.LogWarning("Observaciones de {NroExpediente}: {Faltantes} no terminaron de cargar", nroExpediente, resultado.Faltantes);
            }

            return resultado;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al cargar todas las observaciones de {NroExpediente}", nroExpediente);
            throw;
        }
    }

    public async Task<string?> ObtenerTrabajadoPorAsync(string nroExpediente)
    {
        try
        {
            return await _automationBusqueda.ObtenerTrabajadoPorAsync(nroExpediente);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al consultar 'Trabajado por' de {NroExpediente}", nroExpediente);
            throw;
        }
    }

    // "Trabajado por" sale de la cola de Pases: solo aplica si esta en la oficina del usuario y no en transito
    private bool PuedeConsultarTrabajadoPor(ResultadoBusqueda result)
    {
        static string Normalizar(string? s) => Regex.Replace((s ?? string.Empty).Trim(), @"\s+", " ");
        return !result.EnTransito &&
               string.Equals(Normalizar(result.Oficina), Normalizar(_oficinaConfig.Nombre), StringComparison.OrdinalIgnoreCase);
    }
    public async Task<ResultadoBusqueda?> SearchAsync(string nroExpediente)
    {
        //Validacion de Numero de Expediente
        var tempExpediente = new Expediente { NroExpediente = nroExpediente };
        var validationResult = _expValidation.Validate(tempExpediente, ExpValidationContext.Busqueda);

        if (!validationResult.IsValid)
        {
            _logger.LogWarning("Validacion fallida para NroExpediente {NroExpediente}: {Errors}", nroExpediente, string.Join(", ", validationResult.Errors));
            throw new ArgumentException(string.Join(", ", validationResult.Errors));
        }

        _logger.LogInformation("Buscando expediente {NroExpediente}", nroExpediente);

        try
        {
            var result = await _automationBusqueda.SearchAsync(nroExpediente);

            if(result == null)
            {
                _logger.LogInformation("No se encontró expediente {NroExpediente}", nroExpediente);
            }
            else
            {
                result.PuedeConsultarTrabajadoPor = PuedeConsultarTrabajadoPor(result);
                _logger.LogInformation("Expediente {NroExpediente} encontrado (Estado: {Estado} {Destino}, Oficina: {Oficina})", nroExpediente, result.Estado, result.OficinaDestino, result.Oficina);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Excepcion al buscar expediente {NroExpediente}", nroExpediente);
            throw;
        }
    }
}
