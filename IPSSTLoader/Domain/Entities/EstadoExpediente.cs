using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace IPSST.Domain.Entities;

// Resultado de la busqueda rapida: donde esta el expediente
public class EstadoExpediente
{
    public string Estado { get; set; } = string.Empty;
    public string OficinaDestino { get; set; } = string.Empty;
    public string Oficina { get; set; } = string.Empty;

    // Tiene un pase hecho que todavia no fue recibido
    public bool EnTransito => Estado.Contains("TRANSITO", StringComparison.OrdinalIgnoreCase);

    // Ej: "EN TRANSITO A SECRETARIA GENERAL - CASA CENTRAL"
    public string Descripcion => Normalizar($"{Estado} {OficinaDestino}");

    // Un pase solo se puede cancelar si el expediente esta en la oficina que lo hizo
    public bool EstaEnOficina(string oficina) =>
        string.Equals(Normalizar(Oficina), Normalizar(oficina), StringComparison.OrdinalIgnoreCase);

    // Mensaje para el usuario cuando el expediente no esta disponible en su oficina
    public string DescribirUbicacion()
    {
        if (string.IsNullOrWhiteSpace(Oficina))
        {
            return $"El expediente no se encuentra en una oficina: {Descripcion}.";
        }

        var transito = EnTransito ? $", {Descripcion}" : string.Empty;
        return $"El expediente se encuentra en {Normalizar(Oficina)}{transito}.";
    }

    private static string Normalizar(string texto) => Regex.Replace(texto.Trim(), @"\s+", " ");
}
