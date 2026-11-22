using IPSST.Domain.Entities;
using IPSST.Domain.Enums;
using IPSST.Domain.Interface;
using IPSST.Domain.Validation;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace IPSST.Application.Workflows;

public class ResolucionWorkflow
{
    private readonly IAutomationResolucion _automationResolucion;
    private readonly PaseWorkflow _paseWorkflow;
    private readonly IUploadJobRepository _uploadJobRepository;
    private readonly ExpValidation _expValidation;
    private readonly ILogger<ResolucionWorkflow> _logger;

    public ResolucionWorkflow(
        IAutomationResolucion automationResolucion,
        PaseWorkflow paseWorkflow,
        IUploadJobRepository uploadJobRepository,
        ExpValidation expValidation,
        ILogger<ResolucionWorkflow> logger)
    {
        _automationResolucion = automationResolucion;
        _paseWorkflow = paseWorkflow;
        _uploadJobRepository = uploadJobRepository;
        _expValidation = expValidation;
        _logger = logger;
    }

    public async Task<ResPreparation?> PrepararResolucionAsync(string nroExpediente)
    {
        _logger.LogInformation("Preparando Resolucion de {NroExpediente}", nroExpediente);

        try
        {
            return await _automationResolucion.PrepararResolucionAsync(nroExpediente);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al preparar la Resolucion de {NroExpediente}", nroExpediente);
            throw;
        }
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
            _logger.LogWarning("Validacion fallida para la Resolucion de {NroExpediente}: {Errores}", expediente.NroExpediente, string.Join(", ", resolucionValidation.Errors));
            throw new ArgumentException(string.Join(", ", resolucionValidation.Errors));
        }

        //Validacion de Pase
        var paseValidation = _expValidation.Validate(expediente, ExpValidationContext.Pase);
        if (!paseValidation.IsValid)
        {
            _logger.LogWarning("Validacion fallida para el Pase de la Resolucion de {NroExpediente}: {Errores}", expediente.NroExpediente, string.Join(", ", paseValidation.Errors));
            throw new ArgumentException(string.Join(", ", paseValidation.Errors));
        }

        _logger.LogInformation("Iniciando Resolucion {NroResolucion} del {FechaResolucion:dd/MM/yyyy} en {NroExpediente}",
            expediente.Resolucion!.NroResolucion, expediente.Resolucion.FechaResolucion, expediente.NroExpediente);

        //Crear Trabajo
        var job = new UploadJob
        {
            Id = Guid.NewGuid(),
            ExpedienteId = expediente.Id,
            NroExpediente = expediente.NroExpediente,
            TipoAccion = TipoAccion.Resolucion,
            Causante = expediente.Causante,
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
            // En un reintento: el intento anterior pudo cargar la resolucion sin que se confirmara.
            // Nunca se carga dos veces la misma resolucion
            if (attempt > 0 && await ResolucionYaCargadaAsync(expediente))
            {
                _logger.LogInformation("La Resolucion de {NroExpediente} ya figura cargada: no se reenvia", expediente.NroExpediente);
                resolucionSuccess = true;
                break;
            }

            job.Status = UploadStatus.InProgress;
            job.StartedAt = DateTime.UtcNow;
            await _uploadJobRepository.UpdateAsync(job);

            try
            {
                resolucionSuccess = await _automationResolucion.ConfirmarResAsync(
                                                    expediente.NroExpediente,
                                                    expediente.Resolucion!.NroResolucion,
                                                    expediente.Resolucion.FechaResolucion,
                                                    expediente.Resolucion.Observaciones ?? string.Empty);

                if (!resolucionSuccess)
                {
                    job.LastError = "Fallo de Resolucion en Playwright";
                    _logger.LogWarning("Intento {Intento}/{Maximo} de Resolucion de {NroExpediente} fallido: el sistema no confirmo",
                        attempt + 1, maxRetries, expediente.NroExpediente);
                }
            }
            catch (Exception ex)
            {
                job.LastError = ex.Message;
                _logger.LogWarning(ex, "Intento {Intento}/{Maximo} de Resolucion de {NroExpediente} fallido",
                    attempt + 1, maxRetries, expediente.NroExpediente);
            }

            if (!resolucionSuccess)
            {
                job.RetryCount++;
                attempt++;
                await _uploadJobRepository.UpdateAsync(job);

                if (attempt < maxRetries)
                {
                    await Task.Delay(5000);
                }
            }
        }

        // El ultimo intento tambien pudo haber cargado la resolucion
        if (!resolucionSuccess && await ResolucionYaCargadaAsync(expediente))
        {
            _logger.LogInformation("La Resolucion de {NroExpediente} figura cargada tras el ultimo intento: se da por realizada", expediente.NroExpediente);
            resolucionSuccess = true;
        }

        if (!resolucionSuccess)
        {
            var ultimoError = job.LastError;
            _logger.LogError("Resolucion de {NroExpediente} fallida tras {Maximo} intentos (no se hace el Pase). Ultimo error: {Error}",
                expediente.NroExpediente, maxRetries, ultimoError);
            job.Status = UploadStatus.Failed;
            job.LastError = $"Se Alcanzaron los Intentos Maximos de Resolucion. Ultimo error: {ultimoError}";
            await _uploadJobRepository.UpdateAsync(job);

            // Se lanza para que la UI no informe la Resolucion como exitosa
            throw new InvalidOperationException(job.LastError);
        }

        //Resolucion Exitosa
        _logger.LogInformation("Resolucion de {NroExpediente} cargada, sigue el Pase", expediente.NroExpediente);
        job.Status = UploadStatus.ResolucionCompleted;
        job.RetryCount = 0;
        await _uploadJobRepository.UpdateAsync(job);
        
        //Ejecutar Pase
        await _paseWorkflow.ExecuteAsync(expediente, expId, maxRetries: maxRetries, existingJob: job);
    }

    private async Task<bool> ResolucionYaCargadaAsync(Expediente expediente)
    {
        try
        {
            return await _automationResolucion.ExisteResolucionAsync(
                expediente.NroExpediente,
                expediente.Resolucion!.NroResolucion,
                expediente.Resolucion.FechaResolucion);
        }
        catch (Exception ex)
        {
            // Si la verificacion falla se sigue con el reintento normal
            _logger.LogWarning(ex, "No se pudo verificar si la Resolucion de {NroExpediente} ya estaba cargada", expediente.NroExpediente);
            return false;
        }
    }
}
