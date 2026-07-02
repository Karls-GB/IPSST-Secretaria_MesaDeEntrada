using IPSSTLoader.Domain.Entities;
using IPSSTLoader.Domain.Enums;
using IPSSTLoader.Domain.Interface;
using IPSSTLoader.Domain.Validation;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace IPSSTLoader.Application.Workflows;

public class PaseWorkflow
{
    private readonly IAutomationPase _automationPase;
    private readonly IUploadJobRepository _uploadJobRepository;
    private readonly ExpValidation _expValidation;

    public PaseWorkflow(IAutomationPase automationPase,
        IUploadJobRepository uploadJobRepository,
        ExpValidation expValidation)
    {
        _automationPase = automationPase;
        _uploadJobRepository = uploadJobRepository;
        _expValidation = expValidation;
    }

    public async Task ExecuteAsync(Expediente expediente, bool isRetry = false, int maxRetries = 3)
    {
        //Asignar Id si no lo tiene
        if(expediente.Id == default)
        {
            expediente.Id = Guid.NewGuid();
        }
        
        //Validacion
        var validationResult = _expValidation.Validate(expediente, ExpValidationContext.Pase);
        if (!validationResult.IsValid)
        {
            throw new ArgumentException(string.Join(", ", validationResult.Errors));
        }

        //Crear Trabajo de subida
        var job = new UploadJob
        {
            Id = Guid.NewGuid(),
            ExpedienteId = expediente.Id,
            NroExpediente = expediente.NroExpediente,
            Status = UploadStatus.Pending,
            CreatedAt = DateTime.UtcNow,
            PaseDataJson = JsonSerializer.Serialize(expediente.Pase)
        };

        //Comprobar si se esta reintentando la subida
        if (isRetry)
        {
            job.RetryCount++;
        }

        await _uploadJobRepository.AddAsync(job);

        //Ejecucion y Logica de Reintento
        int attempt = 0;

        while(attempt < maxRetries)
        {
            job.Status = UploadStatus.InProgress;
            job.StartedAt = DateTime.UtcNow;
            await _uploadJobRepository.UpdateAsync(job);

            try
            {
                var success = await _automationPase.SubmitAsync(expediente);

                if (success)
                {
                    job.Status = UploadStatus.Completed;
                    job.CompletedAt = DateTime.UtcNow;
                    await _uploadJobRepository.UpdateAsync(job);
                    return;
                }
                else
                {
                    job.RetryCount++;
                    attempt++;
                    job.LastError = "Fallo de Pase en Playwright";

                    if(attempt < maxRetries)
                    {
                        await Task.Delay(5000);
                    }
                }
            }
            catch(Exception ex) 
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

        //Intentos Agotados
        job.Status = UploadStatus.Failed;
        job.LastError = "Se Alcanzaron los Intentos Maximos de Pase";
        await _uploadJobRepository.UpdateAsync(job);
    }
}
