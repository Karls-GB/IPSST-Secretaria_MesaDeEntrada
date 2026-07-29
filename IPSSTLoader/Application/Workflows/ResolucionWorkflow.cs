using IPSST.Domain.Entities;
using IPSSTLoader.Domain.Entities;
using IPSSTLoader.Domain.Enums;
using IPSSTLoader.Domain.Interface;
using IPSSTLoader.Domain.Validation;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

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

    public async Task<ResPreparation?> PrepararResolucionAsync(string nroExpediente)
    {
        return await _automationResolucion.PrepararResolucionAsync(nroExpediente);
    }

    public async Task ExecuteAsync(Expediente expediente, string expId, bool isRetry = false, int maxRetries = 3)
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

        //Crear Trabajo
        var job = new UploadJob
        {
            Id = Guid.NewGuid(),
            ExpedienteId = expediente.Id,
            NroExpediente = expediente.NroExpediente,
            Status = UploadStatus.Pending,
            CreatedAt = DateTime.UtcNow,
            ResolucionDataJson = JsonSerializer.Serialize(expediente.Resolucion)
        };

        if (isRetry)
        {
            job.RetryCount++;
        }

        await _uploadJobRepository.AddAsync(job);

        //Parte 1: Cargar Resolucion
        int attempt = 0;
        bool resolucionSuccess = false;

        while(attempt < maxRetries && !resolucionSuccess)
        {
            job.Status = UploadStatus.InProgress;
            job.StartedAt = DateTime.UtcNow;
            await _uploadJobRepository.UpdateAsync(job);

            try
            {
                resolucionSuccess = await _automationResolucion.ConfirmarResAsync(
                                                    expediente.Resolucion!.NroResolucion,
                                                    expediente.Resolucion.FechaResolucion,
                                                    expediente.Resolucion.Observaciones ?? string.Empty);

                if (!resolucionSuccess)
                {
                    job.RetryCount++;
                    attempt++;
                    job.LastError = "Fallo de Resolucion en Playwright";

                    if(attempt < maxRetries)
                    {
                        await Task.Delay(5000);
                    }
                }
            }
            catch (Exception ex)
            {
                job.RetryCount++;
                attempt++;
                job.LastError = ex.Message;

                if(attempt >= maxRetries)
                {
                    job.Status = UploadStatus.Failed;
                    await _uploadJobRepository.UpdateAsync(job);
                    throw;
                }

                await Task.Delay(5000);
            }
        }

        if (!resolucionSuccess)
        {
            job.Status = UploadStatus.Failed;
            job.LastError = "Se Alcanzaron los Intentos Maximos de Resolucion";
            await _uploadJobRepository.UpdateAsync(job);
            return;
        }

        //Resolucion Exitosa
        job.Status = UploadStatus.ResolucionCompleted;
        job.RetryCount = 0;
        await _uploadJobRepository.UpdateAsync(job);
        
        //Ejecutar Pase
        await _paseWorkflow.ExecuteAsync(expediente, expId, maxRetries: maxRetries, existingJob: job);
    }
}
