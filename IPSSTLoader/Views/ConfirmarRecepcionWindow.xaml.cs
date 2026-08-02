using IPSST.Domain.Entities;
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
public partial class ConfirmarRecepcionWindow : Window
{
    public ConfirmarRecepcionWindow(RecepcionItem item)
    {
        InitializeComponent();

        NroExpedienteText.Text = item.NroExpediente;
        CausanteText.Text = item.Causante;
        OficinaOrigenText.Text = item.OficinaOrigen;
        FoliosText.Text = item.Folios?.ToString() ?? string.Empty;
    }

    private void Confirmar_Click(object sender, RoutedEventArgs e) => DialogResult = true;
    private void Cancelar_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { DialogResult = true; e.Handled = true; }
        if (e.Key == Key.Escape) { DialogResult = false; e.Handled = true; }
    }
}
