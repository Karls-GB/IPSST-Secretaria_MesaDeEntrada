using IPSSTLoader.Domain.Entities;
using IPSSTLoader.Domain.Enums;
using IPSSTLoader.Domain.Interface;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace IPSSTLoader.Application.Services;

public class RecepcionService
{
    private readonly IAutomationRecepcion _automationRecepcion;
    private readonly IUploadJobRepository _uploadJobRepository;

    public RecepcionService(
        IAutomationRecepcion automationRecepcion,
        IUploadJobRepository uploadJobRepository)
    {
        _automationRecepcion = automationRecepcion;
        _uploadJobRepository = uploadJobRepository;
    }

    public async Task AdmitSingleAsync(string nroExpediente, bool isRetry = false, int maxRetries = 3)
    {
        if (string.IsNullOrWhiteSpace(nroExpediente))
        {
            throw new ArgumentException("Numero de Expediente Requerido");
        }

        var job = new UploadJob
        {
            Id = Guid.NewGuid(),
            ExpedienteId = Guid.Empty,
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
                var success = await _automationRecepcion.AdmitSingleAsync(nroExpediente);

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

                if(attempt >= maxRetries)
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
    }

    public async Task AdmitBulkAsync(string oficina, List<string> nroExpedientes, bool isRetry = false, int maxRetries = 3)
    {
        if (string.IsNullOrWhiteSpace(oficina))
        {
            throw new ArgumentException("Oficina Requerida");
        }

        if (nroExpedientes == null || nroExpedientes.Count == 0)
        {
            throw new ArgumentException("Al Menos un Expediente es Requerido");
        }

        var job = new UploadJob
        {
            Id = Guid.NewGuid(),
            ExpedienteId = Guid.Empty,
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

        while (attempt < maxRetries && pending.Count > 0)
        {
            job.Status = UploadStatus.InProgress;
            job.StartedAt = DateTime.UtcNow;
            await _uploadJobRepository.UpdateAsync(job);

            try
            {
                var result = await _automationRecepcion.AdmitBulkAsync(oficina, pending);

                pending = new List<string>();
                pending.AddRange(result.NotFound);
                pending.AddRange(result.Failed);

                if (pending.Count > 0)
                {
                    job.RetryCount++;
                    attempt++;
                    job.LastError = $"Pendientes: {string.Join(",", pending)}";

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

                if (attempt >= maxRetries)
                {
                    job.Status = UploadStatus.Failed;
                    await _uploadJobRepository.UpdateAsync(job);
                    throw;
                }

                await Task.Delay(5000);
            }
        }

        if(pending.Count == 0)
        {
            job.Status = UploadStatus.Completed;
            job.CompletedAt = DateTime.UtcNow;
        }
        else
        {
            job.Status = UploadStatus.Failed;
            job.LastError = $"No se pudieron admintir: {string.Join(", ", pending)}";
        }
        
        await _uploadJobRepository.UpdateAsync(job);
    }
}
