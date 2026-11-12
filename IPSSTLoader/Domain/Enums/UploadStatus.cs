using System;
using System.Collections.Generic;
using System.Text;

namespace IPSST.Domain.Enums;

public enum UploadStatus
{
    Pending,
    InProgress,
    ResolucionCompleted,
    PaseCompleted,
    Completed,
    Failed,
    Retrying,
    Cancelled,
    // Va al final: el estado se guarda como numero en la base
    CancelacionCompleted
}
