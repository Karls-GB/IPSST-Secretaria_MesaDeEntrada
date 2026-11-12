using IPSST.Application.Configuration;
using IPSST.Domain.Entities;
using IPSST.Domain.Enums;
using IPSST.Domain.Interface;
using IPSST.Domain.Validation;
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

    public CancelarPaseWorkflow(
        IAutomationPase automationPase,
        IAutomationBusqueda automationBusqueda,
        IUploadJobRepository uploadJobRepository,
        ExpValidation expValidation,
        PaseWorkflow paseWorkflow,
        OficinaConfig oficinaConfig)
    {
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
    public async Task CancelarAsync(string nroExpediente, string expId, int maxRetries = 3)
    {
        var validationResult = _expValidation.Validate(new Expediente { NroExpediente = nroExpediente }, ExpValidationContext.Busqueda);
        if (!validationResult.IsValid)
        {
            throw new ArgumentException(string.Join(", ", validationResult.Errors));
        }

        var job = await CrearJobAsync(nroExpediente);
        await EjecutarCancelacionAsync(job, nroExpediente, expId, maxRetries);

        job.Status = UploadStatus.Completed;
        job.CompletedAt = DateTime.UtcNow;
        await _uploadJobRepository.UpdateAsync(job);
    }

    // Cancela el pase pendiente y hace el pase corregido. Ambas fases comparten un UploadJob:
    // si la cancelacion salio bien y el pase falla, solo se reintenta el pase
    public async Task CancelarYRehacerAsync(Expediente expediente, string expId, int maxRetries = 3)
    {
        // Se valida el pase antes de cancelar, para no cancelar si el pase nuevo no se va a poder hacer
        var validationResult = _expValidation.Validate(expediente, ExpValidationContext.Pase);
        if (!validationResult.IsValid)
        {
            throw new ArgumentException(string.Join(", ", validationResult.Errors));
        }

        var job = await CrearJobAsync(expediente.NroExpediente);
        await EjecutarCancelacionAsync(job, expediente.NroExpediente, expId, maxRetries);

        job.Status = UploadStatus.CancelacionCompleted;
        job.RetryCount = 0;
        await _uploadJobRepository.UpdateAsync(job);

        await _paseWorkflow.ExecuteAsync(expediente, expId, maxRetries: maxRetries, existingJob: job);
    }

    private async Task<UploadJob> CrearJobAsync(string nroExpediente)
    {
        var job = new UploadJob
        {
            Id = Guid.NewGuid(),
            NroExpediente = nroExpediente,
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
                    return;
                }

                if (await _automationPase.ConfirmarCancelarPaseAsync(expId))
                {
                    return;
                }

                job.LastError = "Fallo de Cancelacion de Pase en Playwright";
            }
            catch (Exception ex)
            {
                job.LastError = ex.Message;
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
        job.Status = UploadStatus.Failed;
        job.LastError = $"Se Alcanzaron los Intentos Maximos de Cancelacion de Pase. Ultimo error: {ultimoError}";
        await _uploadJobRepository.UpdateAsync(job);

        throw new InvalidOperationException(job.LastError);
    }
}
