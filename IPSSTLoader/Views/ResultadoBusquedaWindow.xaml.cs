using IPSST.Application.Services;
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

public partial class ResultadoBusquedaWindow : Window
{
    private readonly BusquedaService _busquedaService;

    // Expediente que muestra la ventana; si cambia mientras se consulta algo, la respuesta vieja se descarta
    private string _nroExpediente = string.Empty;

    public ResultadoBusquedaWindow(ResultadoBusqueda resultado, BusquedaService busquedaService)
    {
        InitializeComponent();
        this.AjustarAPantalla();
        _busquedaService = busquedaService;
        UpdateResultado(resultado);
    }

    public void UpdateResultado(ResultadoBusqueda resultado)
    {
        _nroExpediente = resultado.NroExpediente;
        NroExpedienteText.Text = resultado.NroExpediente;

        // Datos principales
        CausanteText.Text = Valor(resultado.Causante);
        AsuntoText.Text = Valor(resultado.Asunto);
        OficinaText.Text = Valor(resultado.Oficina);
        FoliosText.Text = resultado.Folios?.ToString() ?? "-";

        var conPase = resultado.EnTransito && !string.IsNullOrWhiteSpace(resultado.OficinaDestino);
        OficinaDestinoText.Text = conPase ? $"Con pase {resultado.OficinaDestino}" : string.Empty;
        OficinaDestinoText.Visibility = conPase ? Visibility.Visible : Visibility.Collapsed;

        // Primera observacion
        PrimeraObservacionText.Text = resultado.PrimeraObservacion?.Descripcion?.Trim() ?? string.Empty;
        PrimeraObservacionPanel.Visibility = resultado.PrimeraObservacion is null ? Visibility.Collapsed : Visibility.Visible;

        // Trabajado por: boton para consultarlo, o "no aplica"
        TrabajadoPorText.Visibility = Visibility.Collapsed;
        ConsultarTrabajadoPorButton.Content = "Consultar";
        ConsultarTrabajadoPorButton.IsEnabled = true;
        ConsultarTrabajadoPorButton.Visibility = resultado.PuedeConsultarTrabajadoPor ? Visibility.Visible : Visibility.Collapsed;
        TrabajadoPorNoAplicaText.Visibility = resultado.PuedeConsultarTrabajadoPor ? Visibility.Collapsed : Visibility.Visible;

        // Datos secundarios
        FechaAltaText.Text = resultado.FechaAlta?.ToString("dd/MM/yyyy") ?? "-";
        MotivoText.Text = Valor(resultado.Motivo);
        EstadoText.Text = Valor(resultado.Estado);
        SucursalText.Text = Valor(resultado.Sucursal);
        CuitCuilText.Text = Valor(resultado.CuitCuil);

        // Observaciones: primera pagina
        ObservacionesGrid.ItemsSource = resultado.Observaciones;
        ObservacionesTituloText.Text = resultado.HayMasObservaciones
            ? $"Observaciones (las {resultado.Observaciones.Count} más recientes)"
            : $"Observaciones ({resultado.Observaciones.Count})";
        CargarTodasButton.Content = "Cargar todas";
        CargarTodasButton.IsEnabled = true;
        CargarTodasButton.Visibility = resultado.HayMasObservaciones ? Visibility.Visible : Visibility.Collapsed;

        if(WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }
        Activate();
    }

    private static string Valor(string? texto) => string.IsNullOrWhiteSpace(texto) ? "-" : texto.Trim();

    private async void ConsultarTrabajadoPorButton_Click(object sender, RoutedEventArgs e)
    {
        var nro = _nroExpediente;
        ConsultarTrabajadoPorButton.IsEnabled = false;
        ConsultarTrabajadoPorButton.Content = "Consultando...";

        try
        {
            var usuario = await _busquedaService.ObtenerTrabajadoPorAsync(nro);
            if (nro != _nroExpediente)
            {
                return;
            }

            // null: no figura en la cola de Pases. Vacio: esta en la cola pero sin usuario asignado
            TrabajadoPorText.Text = usuario is null
                ? "No figura en la cola de Pases"
                : string.IsNullOrWhiteSpace(usuario) ? "Sin asignar" : usuario.Trim();

            ConsultarTrabajadoPorButton.Visibility = Visibility.Collapsed;
            TrabajadoPorText.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            if (nro != _nroExpediente)
            {
                return;
            }

            await Mensajes.ErrorAsync($"Error al consultar quién trabaja el expediente: {ex.Message}", this);
            ConsultarTrabajadoPorButton.Content = "Consultar";
            ConsultarTrabajadoPorButton.IsEnabled = true;
        }
    }

    private async void CargarTodasButton_Click(object sender, RoutedEventArgs e)
    {
        var nro = _nroExpediente;
        CargarTodasButton.IsEnabled = false;
        CargarTodasButton.Content = "Cargando...";

        try
        {
            var (observaciones, faltantes) = await _busquedaService.ObtenerTodasObservacionesAsync(nro);
            if (nro != _nroExpediente)
            {
                return;
            }

            ObservacionesGrid.ItemsSource = observaciones;

            if (faltantes > 0)
            {
                // No se ocultan filas en silencio: se avisa y se deja reintentar
                ObservacionesTituloText.Text = $"Observaciones ({observaciones.Count}, {faltantes} no terminaron de cargar)";
                CargarTodasButton.Content = "Reintentar";
                CargarTodasButton.IsEnabled = true;
            }
            else
            {
                ObservacionesTituloText.Text = $"Observaciones (todas: {observaciones.Count})";
                CargarTodasButton.Visibility = Visibility.Collapsed;
            }
        }
        catch (Exception ex)
        {
            if (nro != _nroExpediente)
            {
                return;
            }

            await Mensajes.ErrorAsync($"Error al cargar las observaciones: {ex.Message}", this);
            CargarTodasButton.Content = "Cargar todas";
            CargarTodasButton.IsEnabled = true;
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
        }
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
