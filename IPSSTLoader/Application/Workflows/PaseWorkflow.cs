using IPSST.Application.Configuration;
using IPSST.Domain.Entities;
using IPSST.Domain.Enums;
using IPSST.Domain.Interface;
using IPSST.Domain.Validation;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace IPSST.Application.Workflows;

public class PaseWorkflow
{
    private readonly IAutomationPase _automationPase;
    private readonly IAutomationBusqueda _automationBusqueda;
    private readonly IUploadJobRepository _uploadJobRepository;
    private readonly ExpValidation _expValidation;
    private readonly OficinaConfig _oficinaConfig;

    public PaseWorkflow(IAutomationPase automationPase,
        IAutomationBusqueda automationBusqueda,
        IUploadJobRepository uploadJobRepository,
        ExpValidation expValidation,
        OficinaConfig oficinaConfig)
    {
        _automationPase = automationPase;
        _automationBusqueda = automationBusqueda;
        _uploadJobRepository = uploadJobRepository;
        _expValidation = expValidation;
        _oficinaConfig = oficinaConfig;
    }

    // Devuelve null si el expediente no existe
    public async Task<PasePreparation?> PrepararAsync(string nroExpediente)
    {
        // Caso mas comun: el expediente esta en la cola de Pases. Se va directo, sin busqueda previa
        var preparacion = await _automationPase.PrepararPaseAsync(nroExpediente);
        if (preparacion != null)
        {
            return preparacion;
        }

        // No esta en la cola: busqueda rapida para saber donde esta
        var estado = await _automationBusqueda.ConsultarEstadoAsync(nroExpediente);
        if (estado == null)
        {
            return null;
        }

        if (estado.EstaEnOficina(_oficinaConfig.Nombre))
        {
            // En esta oficina y en transito: tiene un pase hecho desde aca, se puede cancelar y rehacer
            if (estado.EnTransito)
            {
                return await PrepararPasePendienteAsync(nroExpediente, estado);
            }

            return new PasePreparation
            {
                MotivoNoDisponible = $"El expediente está en {_oficinaConfig.Nombre} ({estado.Descripcion}) pero no aparece en la cola de Pases."
            };
        }

        // Otra oficina (o fuera de la institucion): se informa donde esta
        return new PasePreparation { MotivoNoDisponible = estado.DescribirUbicacion() };
    }

    // El expediente esta en transito: solo se puede cancelar si sigue figurando en esta oficina
    public async Task<PasePreparation> PrepararPasePendienteAsync(string nroExpediente, EstadoExpediente estado)
    {
        if (!estado.EstaEnOficina(_oficinaConfig.Nombre))
        {
            // No se carga la lista de Cancelacion (lenta): el pase no se puede cancelar desde aca
            return new PasePreparation
            {
                MotivoNoDisponible = $"El expediente está {estado.Descripcion} desde {estado.Oficina}. " +
                                     $"Solo se pueden cancelar pases de expedientes que están en {_oficinaConfig.Nombre}."
            };
        }

        var pendiente = await _automationPase.PrepararCancelarPaseAsync(nroExpediente);
        if (pendiente == null)
        {
            return new PasePreparation
            {
                MotivoNoDisponible = $"El expediente está {estado.Descripcion}, pero el pase no fue hecho desde esta oficina."
            };
        }

        return new PasePreparation
        {
            Causante = pendiente.Causante,
            FolioActual = pendiente.FoliosExpediente,
            ExpId = pendiente.ExpId,
            PasePendiente = pendiente
        };
    }

    public void ValidateExp(string nroExpediente)
    {
        var tempExpediente = new Expediente { NroExpediente = nroExpediente };
        var validationResult = _expValidation.Validate(tempExpediente, ExpValidationContext.Busqueda);

        if (!validationResult.IsValid)
        {
            throw new ArgumentException(string.Join(", ", validationResult.Errors));
        }
    }

    public async Task ExecuteAsync(Expediente expediente, string expId, bool isRetry = false, int maxRetries = 3, UploadJob? existingJob = null)
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

        //Crear Trabajo de subida O Usar Uno Existente
        UploadJob job;

        if (existingJob != null)
        {
            job = existingJob;
            job.PaseDataJson = JsonSerializer.Serialize(expediente.Pase);
        }
        else
        {
            job = new UploadJob
            {
                Id = Guid.NewGuid(),
                ExpedienteId = expediente.Id,
                NroExpediente = expediente.NroExpediente,
                Status = UploadStatus.Pending,
                CreatedAt = DateTime.UtcNow,
                PaseDataJson = JsonSerializer.Serialize(expediente.Pase)
            };

            await _uploadJobRepository.AddAsync(job);
        }

        //Comprobar si se esta reintentando la subida
        if (isRetry)
        {
            job.RetryCount++;
        }

        await _uploadJobRepository.UpdateAsync(job);

        //Ejecucion y Logica de Reintento
        int attempt = 0;

        while(attempt < maxRetries)
        {
            job.Status = UploadStatus.InProgress;
            job.StartedAt = DateTime.UtcNow;
            await _uploadJobRepository.UpdateAsync(job);

            try
            {
                var success = await _automationPase.ConfirmarPaseAsync(
                    expediente.Pase!.OficinaDestino,
                    expediente.Pase.Folios,
                    expediente.Pase.Observaciones ?? string.Empty,
                    expId);

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

        // Se lanza para que la UI no informe el Pase como exitoso
        throw new InvalidOperationException(job.LastError);
    }
}
