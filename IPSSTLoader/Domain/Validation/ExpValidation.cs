using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using IPSST.Domain.Entities;
using IPSSTLoader.Domain.Entities;

namespace IPSSTLoader.Domain.Validation;

public class ExpValidation
{
    private static readonly Regex ExpRegex = new(@"^(43(0[1-9]|[1-4]\d|50)|1)-\d{1,6}-\d{4}(-[a-zA-Z])?$");

    public ValidationResult Validate(Expediente exp, ExpValidationContext context)
    {
        var result = new ValidationResult();

        //Campos Requeridos Siempre
        if (string.IsNullOrWhiteSpace(exp.NroExpediente))
        {
            result.Errors.Add("Numero de Expediente es Requerido");
        }

        //Reglas de Formato
        if (!string.IsNullOrWhiteSpace(exp.NroExpediente) && !ExpRegex.IsMatch(exp.NroExpediente))
        {
            result.Errors.Add("Numero de Expediente Invalido");
        }

        switch (context)
        {
            case ExpValidationContext.Busqueda:
                break;

            case ExpValidationContext.Pase:
                if (exp.Pase is null)
                {
                    result.Errors.Add("Datos del Pase Requeridos");
                    break;
                }

                if (string.IsNullOrWhiteSpace(exp.Pase.OficinaDestino))
                {
                    result.Errors.Add("Oficina de Destino es Requerido");
                }

                if (exp.Pase.Folios <= 0)
                {
                    result.Errors.Add("Numero de Folios Invalido");
                }

                if (string.IsNullOrEmpty(exp.Pase.Observaciones))
                {
                    result.Errors.Add("Observacion De Pase Requerida");
                }

                break;

            case ExpValidationContext.Resolucion:
                if (exp.Resolucion is null)
                {
                    result.Errors.Add("Datos de la Resolucion Requeridos");
                    break;
                }

                if (string.IsNullOrWhiteSpace(exp.Resolucion.NroResolucion))
                {
                    result.Errors.Add("Numero de Resolucion es Requerido");
                }

                if (exp.Resolucion.FechaResolucion == default)
                {
                    result.Errors.Add("Fecha Requerida o es Invalida");
                }

                if (exp.Resolucion.Observaciones == null)
                {
                    result.Errors.Add("Onservacion de Resolucion Requerida");
                }
                break;

        }

        return result;
    }
}
