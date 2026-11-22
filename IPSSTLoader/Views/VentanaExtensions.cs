using System;
using System.Windows;

namespace IPSST.Views;

public static class VentanaExtensions
{
    // En pantallas chicas (ej. 1024x576, o 1280x720 con la barra de tareas) la ventana se achica
    // para entrar en el area visible. WorkArea ya descuenta la barra de tareas y la escala de Windows
    public static void AjustarAPantalla(this Window ventana)
    {
        var area = SystemParameters.WorkArea;

        ventana.MinWidth = Math.Min(ventana.MinWidth, area.Width);
        ventana.MinHeight = Math.Min(ventana.MinHeight, area.Height);
        ventana.Width = Math.Min(ventana.Width, area.Width);
        ventana.Height = Math.Min(ventana.Height, area.Height);
    }
}
