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

public partial class ConfirmarPaseWindow : Window
{
    public ConfirmarPaseWindow(string nroExpediente, string? causante, string oficina, int totalFolios, string observaciones)
    {
        InitializeComponent();

        NroExpedienteText.Text = nroExpediente;
        CausanteText.Text = causante;
        OficinaText.Text = oficina;
        FoliosText.Text = totalFolios.ToString();
        ObservacionesText.Text = observaciones;
    }

    private void Confirmar_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void Cancelar_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
