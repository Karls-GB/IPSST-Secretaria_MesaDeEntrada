using System.Windows.Controls;

namespace IPSST.Views;

/// <summary>Barra de titulo con el degradé IPSST, reutilizada por las ventanas secundarias.</summary>
public partial class BarraTitulo : UserControl
{
    public BarraTitulo()
    {
        InitializeComponent();
    }

    public string Titulo
    {
        get => TituloText.Text;
        set => TituloText.Text = value;
    }

    /// <summary>Muestra minimizar y maximizar (ventanas redimensionables). Por defecto solo cerrar.</summary>
    public bool PuedeMaximizar
    {
        get => Barra.ShowMaximize;
        set
        {
            Barra.ShowMinimize = value;
            Barra.ShowMaximize = value;
            Barra.CanMaximize = value;
        }
    }
}
