using IPSST.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace IPSST.Domain.Interface;

public interface IAutomationBusqueda
{
    Task<ResultadoBusqueda?> SearchAsync(string nroExpediente);
    Task<EstadoExpediente?> ConsultarEstadoAsync(string nroExpediente);
    Task<(List<ObservacionItem> Observaciones, int Faltantes)> ObtenerTodasObservacionesAsync(string nroExpediente);
    Task<string?> ObtenerTrabajadoPorAsync(string nroExpediente);
}
