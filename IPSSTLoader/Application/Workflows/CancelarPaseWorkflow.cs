using IPSST.Application.Configuration;
using IPSST.Domain.Entities;
using IPSST.Domain.Enums;
using IPSST.Domain.Interface;
using IPSST.Domain.Validation;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text;

namespace IPSST.Application.Workflows;

public class CancelarPaseWorkflow
{
    private readonly IAutomationPase _automationPase;
    private readonly IAutomationBusqueda _automationBusqueda;
    private readonly IUploadJobRepository _uploadJobRepository;
    private readonly ExpValidation _expValidation;
    private readonly PaseWorkflow _paseWorkflow;
    private readonly OficinaConfig _oficinaConfig;
    private readonly ILogger<CancelarPaseWorkflow> _logger;

    public CancelarPaseWorkflow(
        IAutomationPase automationPase,
        IAutomationBusqueda automationBusqueda,
        IUploadJobRepository uploadJobRepository,
        ExpValidation expValidation,
        PaseWorkflow paseWorkflow,
        OficinaConfig oficinaConfig,
        ILogger<CancelarPaseWorkflow> logger)
    {
        _logger = logger;
        _automationPase = automationPase;
        _automationBusqueda = automationBusqueda;
        _uploadJobRepository = uploadJobRepository;
        _expValidation = expValidation;
        _paseWorkflow = paseWorkflow;
        _oficinaConfig = oficinaConfig;
    }

    // Devuelve null si el expediente no existe
    public async Task<PasePreparation?> PrepararAsync(string nroExpediente)
    {
        _logger.LogInformation("Preparando Cancelacion de Pase de {NroExpediente}", nroExpediente);

        try
        {
            var preparacion = await PrepararInternoAsync(nroExpediente);

            if (preparacion == null)
            {
                _logger.LogWarning("Cancelacion: expediente {NroExpediente} no encontrado", nroExpediente);
            }
            else if (preparacion.MotivoNoDisponible != null)
            {
                _logger.LogWarning("Cancelacion de Pase de {NroExpediente} no disponible: {Motivo}", nroExpediente, preparacion.MotivoNoDisponible);
            }

            return preparacion;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al preparar la Cancelacion de Pase de {NroExpediente}", nroExpediente);
            throw;
        }
    }

    private async Task<PasePreparation?> PrepararInternoAsync(string nroExpediente)
    {
        // Caso mas comun: si el usuario quiere cancelar es porque hay un pase hecho.
        // Se va directo a la lista de Cancelacion (solo muestra pases de esta oficina)
        var pendiente = await _automationPase.PrepararCancelarPaseAsync(nroExpediente);
        if (pendiente != null)
        {
            return new PasePreparation
            {
                Causante = pendiente.Causante,
                FolioActual = pendiente.FoliosExpediente,
                ExpId = pendiente.ExpId,
                PasePendiente = pendiente
            };
        }

        // No esta en la lista: busqueda rapida para informar donde esta
        var estado = await _automationBusqueda.ConsultarEstadoAsync(nroExpediente);
        if (estado == null)
        {
            return null;
        }

        if (estado.EstaEnOficina(_oficinaConfig.Nombre))
        {
            return new PasePreparation
            {
                MotivoNoDisponible = estado.EnTransito
                    ? $"El expediente está {estado.Descripcion} desde {_oficinaConfig.Nombre}, pero no aparece en la lista de Cancelación de Pases."
                    : $"El expediente no tiene un pase pendiente: está en {_oficinaConfig.Nombre} ({estado.Descripcion})."
            };
        }

        return new PasePreparation { MotivoNoDisponible = estado.DescribirUbicacion() };
    }

    // Cancelacion sola
    public async Task CancelarAsync(string nroExpediente, string expId, string? oficinaAnterior = null, string? causante = null, int maxRetries = 3)
    {
        var validationResult = _expValidation.Validate(new Expediente { NroExpediente = nroExpediente }, ExpValidationContext.Busqueda);
        if (!validationResult.IsValid)
        {
            _logger.LogWarning("Validacion fallida para la Cancelacion de {NroExpediente}: {Errores}", nroExpediente, string.Join(", ", validationResult.Errors));
            throw new ArgumentException(string.Join(", ", validationResult.Errors));
        }

        _logger.LogInformation("Iniciando Cancelacion del Pase de {NroExpediente} (iba a {OficinaAnterior})", nroExpediente, oficinaAnterior);

        var job = await CrearJobAsync(nroExpediente, TipoAccion.Cancelacion, oficinaAnterior, causante);
        await EjecutarCancelacionAsync(job, nroExpediente, expId, maxRetries);

        _logger.LogInformation("Pase de {NroExpediente} cancelado", nroExpediente);

        job.Status = UploadStatus.Completed;
        job.CompletedAt = DateTime.UtcNow;
        await _uploadJobRepository.UpdateAsync(job);
    }

    // Cancela el pase pendiente y hace el pase corregido. Ambas fases comparten un UploadJob:
    // si la cancelacion salio bien y el pase falla, solo se reintenta el pase
    public async Task CancelarYRehacerAsync(Expediente expediente, string expId, string? oficinaAnterior = null, int maxRetries = 3)
    {
        // Se valida el pase antes de cancelar, para no cancelar si el pase nuevo no se va a poder hacer
        var validationResult = _expValidation.Validate(expediente, ExpValidationContext.Pase);
        if (!validationResult.IsValid)
        {
            _logger.LogWarning("Validacion fallida para la Correccion de Pase de {NroExpediente}: {Errores}", expediente.NroExpediente, string.Join(", ", validationResult.Errors));
            throw new ArgumentException(string.Join(", ", validationResult.Errors));
        }

        _logger.LogInformation("Iniciando Correccion de Pase de {NroExpediente}: se cancela el pase a {OficinaAnterior} y se rehace a {OficinaDestino}",
            expediente.NroExpediente, oficinaAnterior, expediente.Pase!.OficinaDestino);

        var job = await CrearJobAsync(expediente.NroExpediente, TipoAccion.CorreccionPase, oficinaAnterior, expediente.Causante);
        await EjecutarCancelacionAsync(job, expediente.NroExpediente, expId, maxRetries);

        _logger.LogInformation("Pase de {NroExpediente} cancelado, sigue el Pase corregido", expediente.NroExpediente);

        job.Status = UploadStatus.CancelacionCompleted;
        job.RetryCount = 0;
        await _uploadJobRepository.UpdateAsync(job);

        await _paseWorkflow.ExecuteAsync(expediente, expId, maxRetries: maxRetries, existingJob: job);
    }

    private async Task<UploadJob> CrearJobAsync(string nroExpediente, TipoAccion tipo, string? oficinaAnterior, string? causante)
    {
        var job = new UploadJob
        {
            Id = Guid.NewGuid(),
            NroExpediente = nroExpediente,
            Causante = causante,
            TipoAccion = tipo,
            OficinaAnterior = oficinaAnterior,
            Status = UploadStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        await _uploadJobRepository.AddAsync(job);
        return job;
    }

    private async Task EjecutarCancelacionAsync(UploadJob job, string nroExpediente, string expId, int maxRetries)
    {
        int attempt = 0;

        while (attempt < maxRetries)
        {
            job.Status = UploadStatus.InProgress;
            job.StartedAt = DateTime.UtcNow;
            await _uploadJobRepository.UpdateAsync(job);

            try
            {
                // En un reintento: si ya no esta en transito, el intento anterior cancelo el pase
                if (attempt > 0 && (await _automationBusqueda.ConsultarEstadoAsync(nroExpediente))?.EnTransito == false)
                {
                    _logger.LogInformation("El Pase de {NroExpediente} ya no esta en transito: la cancelacion anterior se hizo, no se reenvia", nroExpediente);
                    return;
                }

                if (await _automationPase.ConfirmarCancelarPaseAsync(expId))
                {
                    return;
                }

                job.LastError = "Fallo de Cancelacion de Pase en Playwright";
                _logger.LogWarning("Intento {Intento}/{Maximo} de Cancelacion de Pase de {NroExpediente} fallido: el sistema no confirmo",
                    attempt + 1, maxRetries, nroExpediente);
            }
            catch (Exception ex)
            {
                // Incluye fallas de la verificacion previa: se sigue con el reintento normal
                job.LastError = ex.Message;
                _logger.LogWarning(ex, "Intento {Intento}/{Maximo} de Cancelacion de Pase de {NroExpediente} fallido",
                    attempt + 1, maxRetries, nroExpediente);
            }

            job.RetryCount++;
            attempt++;
            await _uploadJobRepository.UpdateAsync(job);

            if (attempt < maxRetries)
            {
                await Task.Delay(5000);
            }
        }

        var ultimoError = job.LastError;
        _logger.LogError("Cancelacion de Pase de {NroExpediente} fallida tras {Maximo} intentos. Ultimo error: {Error}",
            nroExpediente, maxRetries, ultimoError);
        job.Status = UploadStatus.Failed;
        job.LastError = $"Se Alcanzaron los Intentos Maximos de Cancelacion de Pase. Ultimo error: {ultimoError}";
        await _uploadJobRepository.UpdateAsync(job);

        throw new InvalidOperationException(job.LastError);
    }
}
