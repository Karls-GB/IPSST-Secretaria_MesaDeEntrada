using System.Windows;

namespace IPSST.Views;

/// <summary>
/// Punto unico para mostrar mensajes al usuario. Es async para poder pasar
/// al MessageBox de WPF-UI (fase 5) sin tocar cada llamada.
/// </summary>
public static class Mensajes
{
    /// <summary>Mensaje informativo o de validacion.</summary>
    public static Task AvisoAsync(string texto, Window? owner = null)
    {
        Mostrar(owner, texto, string.Empty, MessageBoxButton.OK, MessageBoxImage.None);
        return Task.CompletedTask;
    }

    /// <summary>Error inesperado o de una operacion fallida.</summary>
    public static Task ErrorAsync(string texto, Window? owner = null)
    {
        Mostrar(owner, texto, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        return Task.CompletedTask;
    }

    /// <summary>Pregunta Si/No. Devuelve true si el usuario eligio Si.</summary>
    public static Task<bool> ConfirmarAsync(string texto, string titulo,
        MessageBoxImage icono = MessageBoxImage.Question, Window? owner = null)
    {
        var respuesta = Mostrar(owner, texto, titulo, MessageBoxButton.YesNo, icono);
        return Task.FromResult(respuesta == MessageBoxResult.Yes);
    }

    private static MessageBoxResult Mostrar(Window? owner, string texto, string titulo,
        MessageBoxButton botones, MessageBoxImage icono) =>
        owner is null
            ? MessageBox.Show(texto, titulo, botones, icono)
            : MessageBox.Show(owner, texto, titulo, botones, icono);
}
