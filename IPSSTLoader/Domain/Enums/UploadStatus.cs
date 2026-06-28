using System;
using System.Collections.Generic;
using System.Text;

namespace IPSSTLoader.Domain.Enums;

public enum UploadStatus
{
    Pending,
    InProgress,
    ResolucionCompleted,
    PaseCompleted,
    Completed,
    Failed,
    Retrying,
    Cancelled
}
