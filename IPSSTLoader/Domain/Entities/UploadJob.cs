using System;
using System.Collections.Generic;
using System.Text;
using IPSSTLoader.Domain.Enums;

namespace IPSSTLoader.Domain.Entities;

public class UploadJob
{
    public Guid Id { get; set; }
    public Guid ExpedienteId { get; set; }
    public UploadStatus Status { get; set; } = UploadStatus.Pending;
    public int RetryCount { get; set; }
    public string? LastError { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    //Tabla de Transiciones
    public bool CanTransitionTo(UploadStatus newStatus)
    {
        return (Status, newStatus) switch
        {
            (UploadStatus.Pending, UploadStatus.InProgress) => true,
            (UploadStatus.Pending, UploadStatus.Cancelled) => true,
            (UploadStatus.InProgress, UploadStatus.ResolucionCompleted) => true,
            (UploadStatus.InProgress, UploadStatus.PaseCompleted) => true,
            (UploadStatus.InProgress, UploadStatus.Completed) => true,
            (UploadStatus.InProgress, UploadStatus.Failed) => true,
            (UploadStatus.ResolucionCompleted, UploadStatus.InProgress) => true,
            (UploadStatus.PaseCompleted, UploadStatus.Completed) => true,
            (UploadStatus.Failed, UploadStatus.Retrying) => true,
            (UploadStatus.Retrying, UploadStatus.InProgress) => true,
            _ => false
        };
    }
}
