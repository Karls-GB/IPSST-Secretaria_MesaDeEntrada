using IPSST.Domain.Entities;
using IPSST.Domain.Enums;
using IPSST.Domain.Interface;
using IPSST.Domain.Validation;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace IPSST.Application.Services;

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
        _logger.LogInformation("Preparando Recepcion de {NroExpediente}", nroExpediente);

        try
        {
            return await _automationRecepcion.PrepararRecepcionAsync(nroExpediente);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al preparar la Recepcion de {NroExpediente}", nroExpediente);
            throw;
        }
    }

    public async Task<bool> ConfirmarIndividualAsync(string nroExpediente, bool isRetry = false, int maxRetries = 3)
    {
        _logger.LogInformation("Iniciando Recepcion de {NroExpediente}", nroExpediente);

        var job = new UploadJob
        {
            Id = Guid.NewGuid(),
            NroExpediente = nroExpediente,
            TipoAccion = TipoAccion.Recepcion,
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
                    _logger.LogWarning("Intento {Intento}/{Maximo} de Recepcion de {NroExpediente} fallido: el sistema no confirmo",
                        attempt + 1, maxRetries, nroExpediente);
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
                _logger.LogWarning(ex, "Intento {Intento}/{Maximo} de Recepcion de {NroExpediente} fallido",
                    attempt + 1, maxRetries, nroExpediente);
                job.RetryCount++;
                attempt++;
                job.LastError = ex.Message;

                if (attempt >= maxRetries)
                {
                    _logger.LogError("Recepcion de {NroExpediente} fallida tras {Maximo} intentos. Ultimo error: {Error}",
                        nroExpediente, maxRetries, ex.Message);
                    job.Status = UploadStatus.Failed;
                    await _uploadJobRepository.UpdateAsync(job);
                    throw;
                }

                await Task.Delay(5000);
            }
        }

        _logger.LogError("Recepcion de {NroExpediente} fallida tras {Maximo} intentos: el sistema no confirmo", nroExpediente, maxRetries);
        job.Status = UploadStatus.Failed;
        job.LastError = "Se Alcanzaron los Intentos Maximos de Recepcion";
        await _uploadJobRepository.UpdateAsync(job);
        return false;
    }

    public async Task<ListadoRecepcion> BuscarPorOficinaAsync(string oficina)
    {
        if (string.IsNullOrWhiteSpace(oficina))
        {
            throw new ArgumentException("Oficina Requerida");
        }

        try
        {
            return await _automationRecepcion.BuscarPorOficinaAsync(oficina);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al buscar el listado de Recepcion de {Oficina}", oficina);
            throw;
        }
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

        _logger.LogInformation("Iniciando Recepcion multiple de {Cantidad} expedientes de {Oficina}: {Expedientes}",
            nroExpedientes.Count, oficina, string.Join(", ", nroExpedientes));

        var job = new UploadJob
        {
            Id = Guid.NewGuid(),
            NroExpediente = string.Join(",", nroExpedientes),
            TipoAccion = TipoAccion.RecepcionMultiple,
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
                    _logger.LogWarning("Intento {Intento}/{Maximo} de Recepcion multiple: quedan pendientes {Pendientes}",
                        attempt + 1, maxRetries, string.Join(", ", pending));
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
                _logger.LogWarning(ex, "Intento {Intento}/{Maximo} de Recepcion multiple fallido. Pendientes: {Pendientes}",
                    attempt + 1, maxRetries, string.Join(", ", pending));
                job.RetryCount++;
                attempt++;
                job.LastError = ex.Message;

                if (attempt >= maxRetries)
                {
                    _logger.LogError("Recepcion multiple fallida tras {Maximo} intentos. Sin recibir: {Pendientes}. Ultimo error: {Error}",
                        maxRetries, string.Join(", ", pending), ex.Message);
                    job.Status = UploadStatus.Failed;
                    await _uploadJobRepository.UpdateAsync(job);
                    throw;
                }

                await Task.Delay(5000);
            }
        }

        if (pending.Count == 0)
        {
            _logger.LogInformation("Recepcion multiple completa: {Cantidad} expedientes recibidos", nroExpedientes.Count);
            job.Status = UploadStatus.Completed;
            job.CompletedAt = DateTime.UtcNow;
        }
        else
        {
            _logger.LogError("Recepcion multiple incompleta tras {Maximo} intentos. Sin recibir: {Pendientes}",
                maxRetries, string.Join(", ", pending));
            job.Status = UploadStatus.Failed;
            job.LastError = $"No se pudieron admitir: {string.Join(",", pending)}";
        }

        await _uploadJobRepository.UpdateAsync(job);

        // Se devuelve el resultado acumulado a traves de todos los intentos, no solo el ultimo
        var notFound = ultimoResultado?.NotFound ?? new List<string>();

        return new BulkAdmitResult
        {
            Admitted = nroExpedientes.Except(pending).ToList(),
            NotFound = notFound,
            Failed = pending.Except(notFound).ToList()
        };
    }
}
