using System;
using System.Collections.Generic;
using System.Text;

namespace IPSST.Domain.Entities;

public class ListadoRecepcion
{
    public List<RecepcionItem> Items { get; set; } = new();

    // Filas que el sistema informo pero no terminaron de cargar
    public int FilasFaltantes { get; set; }
}
