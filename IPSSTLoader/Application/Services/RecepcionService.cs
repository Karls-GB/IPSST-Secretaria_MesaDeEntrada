using IPSST.Domain.Entities;
using IPSSTLoader.Domain.Entities;
using IPSSTLoader.Domain.Enums;
using IPSSTLoader.Domain.Interface;
using IPSSTLoader.Domain.Validation;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace IPSSTLoader.Application.Services;

public class RecepcionService
{
    private readonly IAutomationRecepcion _automationRecepcion;
    private readonly IUploadJobRepository _uploadJobRepository;
    private readonly ExpValidation _expValidation;
    private readonly ILogger<RecepcionService> _logger;


    public RecepcionService(
        IAutomationRecepcion automationRecepcion,
        IUploadJobRepository uploadJobRepository,
        ExpValidation expValidation,
        ILogger<RecepcionService> logger)
    {
        _automationRecepcion = automationRecepcion;
        _uploadJobRepository = uploadJobRepository;
        _expValidation = expValidation;
        _logger = logger;
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

    public async Task<RecepcionItem?> PrepararIndividualAsync(string nroExpediente)
    {
        return await _automationRecepcion.PrepararRecepcionAsync(nroExpediente);
    }

    public async Task<bool> ConfirmarIndividualAsync(string nroExpediente, bool isRetry = false, int maxRetries = 3)
    {
        var job = new UploadJob
        {
            Id = Guid.NewGuid(),
            NroExpediente = nroExpediente,
            Status = UploadStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        if (isRetry)
        {
            job.RetryCount++;
        }

        await _uploadJobRepository.AddAsync(job);

        int attempt = 0;

        while (attempt < maxRetries)
        {
            job.Status = UploadStatus.InProgress;
            job.StartedAt = DateTime.UtcNow;
            await _uploadJobRepository.UpdateAsync(job);

            try
            {
                var success = await _automationRecepcion.ConfirmarRecepcionAsync(nroExpediente);

                if (success)
                {
                    job.Status = UploadStatus.Completed;
                    job.CompletedAt = DateTime.UtcNow;
                    await _uploadJobRepository.UpdateAsync(job);
                    return true;
                }
                else
                {
                    job.RetryCount++;
                    attempt++;
                    job.LastError = "Fallo de Recepcion en Playwright";

                    if (attempt < maxRetries)
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

                if (attempt >= maxRetries)
                {
                    job.Status = UploadStatus.Failed;
                    await _uploadJobRepository.UpdateAsync(job);
                    throw;
                }

                await Task.Delay(5000);
            }
        }

        job.Status = UploadStatus.Failed;
        job.LastError = "Se Alcanzaron los Intentos Maximos de Recepcion";
        await _uploadJobRepository.UpdateAsync(job);
        return false;
    }

    public async Task<List<RecepcionItem>> BuscarPorOficinaAsync(string oficina)
    {
        if (string.IsNullOrWhiteSpace(oficina))
        {
            throw new ArgumentException("Oficina Requerida");
        }

        return await _automationRecepcion.BuscarPorOficinaAsync(oficina);
    }

    public async Task<BulkAdmitResult> AdmitBulkAsync(string oficina, List<string> nroExpedientes, bool isRetry = false, int maxRetries = 3)
    {
        if (string.IsNullOrWhiteSpace(oficina))
        {
            throw new ArgumentException("Oficina Requerida");
        }

        if (nroExpedientes == null || nroExpedientes.Count == 0)
        {
            throw new ArgumentException("Seleccione Expedientes para Recepcion");
        }

        var job = new UploadJob
        {
            Id = Guid.NewGuid(),
            NroExpediente = string.Join(",", nroExpedientes),
            Status = UploadStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        if (isRetry)
        {
            job.RetryCount++;
        }

        await _uploadJobRepository.AddAsync(job);

        int attempt = 0;
        List<string> pending = new List<string>(nroExpedientes);
        BulkAdmitResult? ultimoResultado = null;

        while (attempt < maxRetries && pending.Count > 0)
        {
            job.Status = UploadStatus.InProgress;
            job.StartedAt = DateTime.UtcNow;
            await _uploadJobRepository.UpdateAsync(job);

            try
            {
                var result = await _automationRecepcion.AdmitBulkAsync(oficina, pending);
                ultimoResultado = result;

                pending = new List<string>();
                pending.AddRange(result.NotFound);
                pending.AddRange(result.Failed);

                if (pending.Count > 0)
                {
                    job.RetryCount++;
                    attempt++;
                    job.LastError = $"Pendientes: {string.Join(",", pending)}";

                    if (attempt < maxRetries)
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

                if (attempt >= maxRetries)
                {
                    job.Status = UploadStatus.Failed;
                    await _uploadJobRepository.UpdateAsync(job);
                    throw;
                }

                await Task.Delay(5000);
            }
        }

        if (pending.Count == 0)
        {
            job.Status = UploadStatus.Completed;
            job.CompletedAt = DateTime.UtcNow;
        }
        else
        {
            job.Status = UploadStatus.Failed;
            job.LastError = $"No se pudieron admitir: {string.Join(",", pending)}";
        }

        await _uploadJobRepository.UpdateAsync(job);

        // Se devuelve el resultado acumulado a traves de todos los intentos, no solo el ultimo
        return new BulkAdmitResult
        {
            Admitted = nroExpedientes.Except(pending).ToList(),
            NotFound = ultimoResultado?.NotFound ?? new List<string>(),
            Failed = pending
        };
    }
}
