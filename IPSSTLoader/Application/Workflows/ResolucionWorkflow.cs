using IPSSTLoader.Domain.Entities;
using IPSSTLoader.Domain.Interface;
using IPSSTLoader.Domain.Validation;
using System;
using System.Collections.Generic;
using System.Text;

namespace IPSSTLoader.Application.Workflows;

public class ResolucionWorkflow
{
    private readonly IAutomationResolucion _automationResolucion;
    private readonly PaseWorkflow _paseWorkflow;
    private readonly IUploadJobRepository _uploadJobRepository;
    private readonly ExpValidation _expValidation;

    public ResolucionWorkflow(
        IAutomationResolucion automationResolucion,
        PaseWorkflow paseWorkflow,
        IUploadJobRepository uploadJobRepository,
        ExpValidation expValidation)
    {
        _automationResolucion = automationResolucion;
        _paseWorkflow = paseWorkflow;
        _uploadJobRepository = uploadJobRepository;
        _expValidation = expValidation;
    }

    public async Task ExecuteAsync(Expediente expediente, bool isRetry = false, int maxRetries = 3)
    {
        //Asignar Id si no lo tiene
        if (expediente.Id == default)
        {
            expediente.Id = Guid.NewGuid();
        }

        //Validacion de Resolucion
        var resolucionValidation = _expValidation.Validate(expediente, ExpValidationContext.Resolucion);
        if (!resolucionValidation.IsValid)
        {
            throw new ArgumentException(string.Join(", ", resolucionValidation.Errors));
        }

        //Validacion de Pase
        var paseValidation = _expValidation.Validate(expediente, ExpValidationContext.Pase);
        if (!paseValidation.IsValid)
        {
            throw new ArgumentException(string.Join(", ", paseValidation.Errors));
        }

        //Parte 1: Cargar Resolucion
        int attempt = 0;
        bool resolucionSuccess = false;

        while(attempt < maxRetries && !resolucionSuccess)
        {
            try
            {
                resolucionSuccess = await _automationResolucion.SubmitAsync(expediente);

                if (!resolucionSuccess)
                {
                    attempt++;

                    if(attempt < maxRetries)
                    {
                        await Task.Delay(5000);
                    }
                }
            }
            catch (Exception ex)
            {
                attempt++;

                if(attempt >= maxRetries)
                {
                    throw;
                }

                await Task.Delay(5000);
            }
        }

        if (!resolucionSuccess)
        {
            throw new Exception("Se Alcanzaron los Intentos Maximos de Resolucion")
        }
    }
}
