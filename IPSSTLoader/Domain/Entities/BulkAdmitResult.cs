using System;
using System.Collections.Generic;
using System.Text;

namespace IPSSTLoader.Domain.Entities;

public class BulkAdmitResult
{
    public List<string> Admitted { get; set; } = new();
    public List<string> NotFound { get; set; } = new();
    public List<string> Failed { get; set; } = new();
}
