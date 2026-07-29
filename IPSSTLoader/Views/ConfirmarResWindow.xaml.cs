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

namespace IPSST.Views;

public partial class ConfirmarResWindow : Window
{
    public ConfirmarResWindow(string nroExpediente, string? causante, string nroResolucion, string fechaResolucion,
                              string observResolucion, string oficina, int totalFolios, string observPase)
    {
        InitializeComponent();

        NroExpedienteText.Text = nroExpediente;
        CausanteText.Text = causante;
        NroResText.Text = nroResolucion;
        FechaResText.Text = fechaResolucion;
        ObservacionesResText.Text = observResolucion;
        OficinaText.Text = oficina;
        FoliosText.Text = totalFolios.ToString();
        ObservacionesPaseText.Text = observPase;
    }

    private void Confirmar_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void Cancelar_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Confirmar_Click(this, new RoutedEventArgs());
        }
        else if (e.Key == Key.Escape)
        {
            Cancelar_Click(this, new RoutedEventArgs());
        }
    }
}
