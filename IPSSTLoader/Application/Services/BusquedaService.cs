using IPSSTLoader.Domain.Entities;
using IPSSTLoader.Domain.Interface;
using IPSSTLoader.Domain.Validation;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text;

namespace IPSSTLoader.Application.Services;

public class BusquedaService
{
    private readonly IAutomationBusqueda _automationBusqueda;
    private readonly ExpValidation _expValidation;
    private readonly ILogger<BusquedaService> _logger;
    public BusquedaService(IAutomationBusqueda automationBusqueda, ExpValidation expValidation, ILogger<BusquedaService> logger)
    {
        _automationBusqueda = automationBusqueda;
        _expValidation = expValidation;
        _logger = logger;
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
                _logger.LogInformation("Expediente {NroExpediente} encontrado (Estado: {Estado}, Oficina: {Oficina})", nroExpediente, result.Estado, result.Oficina);
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
