using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Wpf.Ui.Controls;
using MessageBox = Wpf.Ui.Controls.MessageBox;
using MessageBoxButton = Wpf.Ui.Controls.MessageBoxButton;
using MessageBoxResult = Wpf.Ui.Controls.MessageBoxResult;
using TextBlock = System.Windows.Controls.TextBlock;

namespace IPSST.Views;

/// <summary>
/// Punto unico para mostrar mensajes al usuario, con el MessageBox de WPF-UI
/// (botones en español, sin depender del idioma de Windows).
/// Enter = boton principal (o Aceptar), Escape = No / cerrar.
/// </summary>
public static class Mensajes
{
    /// <summary>Mensaje informativo o de validacion.</summary>
    public static Task AvisoAsync(string texto, Window? owner = null) =>
        MostrarAsync(owner, "IPSST", texto, SymbolRegular.Info24, "AcentoBrush", textoPrincipal: null, textoCerrar: "Aceptar");

    /// <summary>Error inesperado o de una operacion fallida.</summary>
    public static Task ErrorAsync(string texto, Window? owner = null) =>
        MostrarAsync(owner, "Error", texto, SymbolRegular.ErrorCircle24, "ErrorBrush", textoPrincipal: null, textoCerrar: "Aceptar");

    /// <summary>Pregunta Si/No. Devuelve true si el usuario eligio Si.</summary>
    public static async Task<bool> ConfirmarAsync(string texto, string titulo,
        MessageBoxImage icono = MessageBoxImage.Question, Window? owner = null)
    {
        var simbolo = icono == MessageBoxImage.Information ? SymbolRegular.Info24 : SymbolRegular.QuestionCircle24;
        var resultado = await MostrarAsync(owner, titulo, texto, simbolo, "AcentoBrush", textoPrincipal: "Sí", textoCerrar: "No");
        return resultado == MessageBoxResult.Primary;
    }

    private static async Task<MessageBoxResult> MostrarAsync(Window? owner, string titulo, string texto,
        SymbolRegular simbolo, string colorIcono, string? textoPrincipal, string textoCerrar)
    {
        owner ??= System.Windows.Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);

        var contenido = new DockPanel { MaxWidth = 460 };
        var icono = new SymbolIcon
        {
            Symbol = simbolo,
            FontSize = 28,
            Margin = new Thickness(0, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Top,
            Foreground = (Brush)System.Windows.Application.Current.FindResource(colorIcono)
        };
        DockPanel.SetDock(icono, Dock.Left);
        contenido.Children.Add(icono);
        contenido.Children.Add(new TextBlock { Text = texto, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });

        var mensaje = new MessageBox
        {
            Title = titulo,
            Content = contenido,
            IsPrimaryButtonEnabled = textoPrincipal is not null,
            PrimaryButtonText = textoPrincipal ?? string.Empty,
            PrimaryButtonAppearance = ControlAppearance.Primary,
            IsSecondaryButtonEnabled = false,
            CloseButtonText = textoCerrar,
            SizeToContent = SizeToContent.WidthAndHeight,
            Owner = owner,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner
        };

        // Teclado: Enter confirma (o cierra si solo hay "Aceptar"), Escape cierra / responde No
        mensaje.PreviewKeyDown += (s, e) =>
        {
            if (e.Key == Key.Enter)
            {
                mensaje.TemplateButtonCommand.Execute(textoPrincipal is not null ? MessageBoxButton.Primary : MessageBoxButton.Close);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                mensaje.TemplateButtonCommand.Execute(MessageBoxButton.Close);
                e.Handled = true;
            }
        };

        return await mensaje.ShowDialogAsync();
    }
}
