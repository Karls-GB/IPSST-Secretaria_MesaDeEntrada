using IPSSTLoader.Domain.Entities;
using IPSSTLoader.Domain.Interface;
using IPSSTLoader.Domain.Validation;
using System;
using System.Collections.Generic;
using System.Text;

namespace IPSSTLoader.Application.Services;

public class BusquedaService
{
    private readonly IAutomateBusqueda _automationBusqueda;
    private readonly ExpValidation _expValidation;
    public BusquedaService(IAutomateBusqueda automationBusqueda, ExpValidation expValidation)
    {
        _automationBusqueda = automationBusqueda;
        _expValidation = expValidation;
    }
    public async Task<Expediente?> SearchAsync(string nroExpediente)
    {
        //Validacion de Numero de Expediente
        var tempExpediente = new Expediente { NroExpediente = nroExpediente };
        var validationResult = _expValidation.Validate(tempExpediente, ExpValidationContext.Pase);

        if (!validationResult.IsValid)
        {
            throw new ArgumentException(string.Join(", ", validationResult.Errors));
        }
        return await _automationBusqueda.SearchAsync(nroExpediente);
    }
}
