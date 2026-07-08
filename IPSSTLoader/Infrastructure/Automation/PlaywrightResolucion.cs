using IPSSTLoader.Domain.Entities;
using IPSSTLoader.Domain.Interface;
using System;
using System.Collections.Generic;
using System.Text;

namespace IPSSTLoader.Infrastructure.Automation;

public class PlaywrightResolucion : IAutomationResolucion
{
    public Task<bool> SubmitAsync(Expediente expediente)
    {
        throw new NotImplementedException("Resolucion Aun no Implementado");
    }
}
