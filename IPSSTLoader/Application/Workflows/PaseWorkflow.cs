using IPSST.Application.Configuration;
using IPSST.Domain.Entities;
using IPSST.Domain.Exceptions;
using IPSST.Domain.Enums;
using IPSST.Domain.Interface;
using IPSST.Domain.Validation;
using Microsoft.Extensions.Logging;
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
    private readonly ILogger<PaseWorkflow> _logger;

    public PaseWorkflow(IAutomationPase automationPase,
        IAutomationBusqueda automationBusqueda,
        IUploadJobRepository uploadJobRepository,
        ExpValidation expValidation,
        OficinaConfig oficinaConfig,
        ILogger<PaseWorkflow> logger)
    {
        _automationPase = automationPase;
        _automationBusqueda = automationBusqueda;
        _uploadJobRepository = uploadJobRepository;
        _expValidation = expValidation;
        _oficinaConfig = oficinaConfig;
        _logger = logger;
    }

    // Devuelve null si el expediente no existe
    public async Task<PasePreparation?> PrepararAsync(string nroExpediente)
    {
        _logger.LogInformation("Preparando Pase de {NroExpediente}", nroExpediente);

        try
        {
            var preparacion = await PrepararInternoAsync(nroExpediente);

            if (preparacion == null)
            {
                _logger.LogWarning("Pase: expediente {NroExpediente} no encontrado", nroExpediente);
            }
            else if (preparacion.MotivoNoDisponible != null)
            {
                _logger.LogWarning("Pase de {NroExpediente} no disponible: {Motivo}", nroExpediente, preparacion.MotivoNoDisponible);
            }
            else if (preparacion.PasePendiente != null)
            {
                _logger.LogInformation("Pase de {NroExpediente}: tiene un pase pendiente hacia {OficinaDestino}, se ofrece corregirlo",
                    nroExpediente, preparacion.PasePendiente.OficinaDestino);
            }

            return preparacion;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al preparar el Pase de {NroExpediente}", nroExpediente);
            throw;
        }
    }

    private async Task<PasePreparation?> PrepararInternoAsync(string nroExpediente)
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
            _logger.LogWarning("Validacion fallida para el Pase de {NroExpediente}: {Errores}", expediente.NroExpediente, string.Join(", ", validationResult.Errors));
            throw new ArgumentException(string.Join(", ", validationResult.Errors));
        }

        _logger.LogInformation("Iniciando Pase de {NroExpediente} hacia {OficinaDestino} con {Folios} folios",
            expediente.NroExpediente, expediente.Pase!.OficinaDestino, expediente.Pase.Folios);

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
                TipoAccion = TipoAccion.Pase,
                Causante = expediente.Causante,
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
            // En un reintento: el intento anterior pudo llegar al sitio sin que se confirmara
            // (respuesta lenta, corte de red). Si el pase ya figura, no se vuelve a enviar
            if (attempt > 0 && await PaseYaRealizadoAsync(expediente))
            {
                _logger.LogInformation("El Pase de {NroExpediente} ya figura en transito hacia {OficinaDestino}: no se reenvia",
                    expediente.NroExpediente, expediente.Pase!.OficinaDestino);
                await MarcarCompletadoAsync(job);
                return;
            }

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
                    _logger.LogInformation("Pase de {NroExpediente} hacia {OficinaDestino} realizado", expediente.NroExpediente, expediente.Pase.OficinaDestino);
                    await MarcarCompletadoAsync(job);
                    return;
                }

                job.LastError = "Fallo de Pase en Playwright";
                _logger.LogWarning("Intento {Intento}/{Maximo} de Pase de {NroExpediente} fallido: el sistema no confirmo",
                    attempt + 1, maxRetries, expediente.NroExpediente);
            }
            catch (OperacionRechazadaException ex)
            {
                // El sitio rechazo el pase: se corta sin reintentar y se muestra su motivo
                _logger.LogWarning("Pase de {NroExpediente} rechazado por el sistema, no se reintenta: {Motivo}", expediente.NroExpediente, ex.Message);
                job.Status = UploadStatus.Failed;
                job.LastError = ex.Message;
                await _uploadJobRepository.UpdateAsync(job);
                throw;
            }
            catch(Exception ex)
            {
                job.LastError = ex.Message;
                _logger.LogWarning(ex, "Intento {Intento}/{Maximo} de Pase de {NroExpediente} fallido",
                    attempt + 1, maxRetries, expediente.NroExpediente);
            }

            job.RetryCount++;
            attempt++;
            await _uploadJobRepository.UpdateAsync(job);

            if (attempt < maxRetries)
            {
                await Task.Delay(5000);
            }
        }

        // El ultimo intento tambien pudo haber llegado al sitio
        if (await PaseYaRealizadoAsync(expediente))
        {
            _logger.LogInformation("El Pase de {NroExpediente} figura en transito hacia {OficinaDestino} tras el ultimo intento: se da por realizado",
                expediente.NroExpediente, expediente.Pase!.OficinaDestino);
            await MarcarCompletadoAsync(job);
            return;
        }

        //Intentos Agotados
        var ultimoError = job.LastError;
        _logger.LogError("Pase de {NroExpediente} fallido tras {Maximo} intentos. Ultimo error: {Error}",
            expediente.NroExpediente, maxRetries, ultimoError);
        job.Status = UploadStatus.Failed;
        job.LastError = $"Se Alcanzaron los Intentos Maximos de Pase. Ultimo error: {ultimoError}";
        await _uploadJobRepository.UpdateAsync(job);

        // Se lanza para que la UI no informe el Pase como exitoso
        throw new InvalidOperationException(job.LastError);
    }

    // Busqueda rapida: si ya esta en transito hacia la oficina destino, el pase se hizo
    private async Task<bool> PaseYaRealizadoAsync(Expediente expediente)
    {
        try
        {
            var estado = await _automationBusqueda.ConsultarEstadoAsync(expediente.NroExpediente);
            return estado?.EnTransitoHacia(expediente.Pase!.OficinaDestino) == true;
        }
        catch (Exception ex)
        {
            // Si la verificacion falla se sigue con el reintento normal
            _logger.LogWarning(ex, "No se pudo verificar si el Pase de {NroExpediente} ya estaba hecho", expediente.NroExpediente);
            return false;
        }
    }

    private async Task MarcarCompletadoAsync(UploadJob job)
    {
        job.Status = UploadStatus.Completed;
        job.CompletedAt = DateTime.UtcNow;
        await _uploadJobRepository.UpdateAsync(job);
    }
}
