using System;
using System.Collections.Generic;
using System.Text;

namespace IPSST.Domain.Exceptions;

// El sitio rechazo la operacion y dio un motivo: con los mismos datos reintentar no sirve
public class OperacionRechazadaException : Exception
{
    public OperacionRechazadaException(string motivo) : base(motivo) { }
}
