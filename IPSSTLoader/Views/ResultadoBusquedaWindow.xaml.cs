using IPSSTLoader.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace IPSSTLoader.Views;

public partial class ResultadoBusquedaWindow : Window
{
    public ResultadoBusquedaWindow(ResultadoBusqueda resultado)
    {
        InitializeComponent();
        UpdateResultado(resultado);
    }

    public void UpdateResultado(ResultadoBusqueda resultado)
    {
        NroExpedienteText.Text = resultado.NroExpediente;
        FechaAltaText.Text = resultado.FechaAlta?.ToString("dd/MM/yyyy") ?? string.Empty    ;
        FoliosText.Text = resultado.Folios?.ToString() ?? string.Empty;
        MotivoText.Text = resultado.Motivo;
        EstadoText.Text = resultado.Estado;
        AsuntoText.Text = resultado.Asunto;
        OficinaText.Text = resultado.Oficina;
        CausanteText.Text = resultado.Causante;
        SucursalText.Text = resultado.Sucursal;
        CuitCuilText.Text = resultado.CuitCuil;
        TrabajadoPorText.Text = resultado.TrabajadoPor;

        ObservacionesGrid.ItemsSource = resultado.Observaciones;

        if(WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }
        Activate();
    }

    private async void NroExpedienteText_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if(!string.IsNullOrEmpty(NroExpedienteText.Text))
        {
            Clipboard.SetText(NroExpedienteText.Text);
            PopupText.Text = "Copiado!";
            NotificationPopup.IsOpen = true;

            await Task.Delay(2000);

            NotificationPopup.IsOpen = false;
        }
    }
}
