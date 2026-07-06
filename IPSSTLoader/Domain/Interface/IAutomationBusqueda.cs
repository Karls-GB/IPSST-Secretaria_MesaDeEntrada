using IPSSTLoader.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace IPSSTLoader.Domain.Interface;

public interface IAutomationBusqueda
{
    Task<ResultadoBusqueda?> SearchAsync(string nroExpediente);
}
