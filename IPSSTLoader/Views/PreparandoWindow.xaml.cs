using System.Windows;

namespace IPSST.Views;

public partial class PreparandoWindow : Window
{
    public PreparandoWindow()
    {
        InitializeComponent();
    }

    /// <summary>Muestra el paso actual (ej. "Iniciando sesión...").</summary>
    public void Estado(string texto) => EstadoText.Text = texto;
}
