using IPSST.Application.Configuration;
using IPSST.Application.Services;
using IPSST.Domain.Entities;
using IPSSTLoader.Application.Services;
using IPSSTLoader.Application.Workflows;
using IPSSTLoader.Domain.Entities;
using IPSSTLoader.Views;
using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace IPSSTLoader;
public partial class MainWindow : Window
{
    private readonly BusquedaService _busquedaService;
    private readonly RecepcionService _recepcionService;
    private readonly PaseWorkflow _paseWorkflow;
    private readonly ResolucionWorkflow _resolucionWorkflow;
    private readonly OficinaCacheService _oficinaCacheService;
    private readonly Dictionary<string, PaseDefaultConfig> _paseDefaults;

    private ResultadoBusquedaWindow? _resultadoBusquedaWindow;
    private readonly ObservableCollection<HistorialItem> _historial = new();

    private int _currentFoliosPase;
    private bool _actualizandoFoliosDesdePrograma;
    private OficinaOption? _oficinaSeleccionada;

    public MainWindow(
        BusquedaService busquedaService, 
        PaseWorkflow paseWorkflow, 
        RecepcionService recepcionService, 
        ResolucionWorkflow resolucionWorkflow,
        OficinaCacheService oficinaCacheService,
        Dictionary<string, PaseDefaultConfig> paseDefaults)
    {
        InitializeComponent();

        _busquedaService = busquedaService;
        _recepcionService = recepcionService;
        _paseWorkflow = paseWorkflow;
        _resolucionWorkflow = resolucionWorkflow;
        _oficinaCacheService = oficinaCacheService;
        _paseDefaults = paseDefaults;

        HistorialListBox.ItemsSource = _historial;
    }

    //Busqueda

    private async void BuscarButton_Click(object sender, RoutedEventArgs e)
    {
        await EjecutarBusquedaAsync(NroExpedienteBox.Text);
    }

    private async void HistorialListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (HistorialListBox.SelectedItem is HistorialItem item)
        {
            NroExpedienteBox.Text = item.NroExpediente;
            await EjecutarBusquedaAsync(item.NroExpediente);
        }
    }

    public async Task EjecutarBusquedaAsync(string nroExpediente)
    {
        setBuscandoState(true);

        try
        {
            var result = await _busquedaService.SearchAsync(nroExpediente);

            if (result == null)
            {
                MessageBox.Show("Expediente no encontrado.");
                return;
            }

            AgregarAlHistorial(result.NroExpediente, result.Causante);

            if (_resultadoBusquedaWindow == null)
            {
                _resultadoBusquedaWindow = new ResultadoBusquedaWindow(result);
                _resultadoBusquedaWindow.Closed += (s, args) => _resultadoBusquedaWindow = null;
                _resultadoBusquedaWindow.Show();
            }
            else
            {
                _resultadoBusquedaWindow.UpdateResultado(result);
            }
        }
        catch (ArgumentException ex)
        {
            MessageBox.Show(ex.Message);
        }
        finally
        {
            setBuscandoState(false);
        }
    }

    private void setBuscandoState(bool buscando)
    {
        BuscandoProgressBar.Visibility = buscando ? Visibility.Visible : Visibility.Collapsed;
        BuscandoText.Visibility = buscando ? Visibility.Visible : Visibility.Collapsed;
        BuscarButton.IsEnabled = !buscando;
        NroExpedienteBox.IsEnabled = !buscando;
    }

    private void AgregarAlHistorial(string nroExpediente, string? causante)
    {
        var existente = _historial.FirstOrDefault(h => h.NroExpediente == nroExpediente);
        if(existente != null)
        {
            _historial.Remove(existente);
        }

        _historial.Insert(0, new HistorialItem { NroExpediente = nroExpediente, Causante = causante });
    }

    //Pase

    private void OficinaTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        // Si el usuario sigue escribiendo, la seleccion anterior ya no es valida hasta que elija de nuevo
        _oficinaSeleccionada = null;

        var texto = OficinaTextBox.Text;

        if (string.IsNullOrWhiteSpace(texto))
        {
            OficinaPopup.IsOpen = false;
            return;
        }

        var filtradas = _oficinaCacheService.Oficinas
            .Where(o => o.Nombre.Contains(texto, StringComparison.OrdinalIgnoreCase))
            .ToList();

        OficinaSuggestionsListBox.ItemsSource = filtradas;
        OficinaPopup.IsOpen = filtradas.Count > 0;

        if (filtradas.Count > 0)
        {
            OficinaSuggestionsListBox.SelectedIndex = 0;
        }
    }

    private void OficinaTextBox_PreviewGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {    
         OficinaTextBox.SelectAll();
    }

    private void OficinaTextBox_GotMouseCapture(object sender, MouseEventArgs e)
    {
        OficinaTextBox.SelectAll();
    }

    private void OficinaTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!OficinaPopup.IsOpen) return;

        switch (e.Key)
        {
            case Key.Down:
                if (OficinaSuggestionsListBox.SelectedIndex < OficinaSuggestionsListBox.Items.Count - 1)
                    OficinaSuggestionsListBox.SelectedIndex++;
                e.Handled = true;
                break;

            case Key.Up:
                if (OficinaSuggestionsListBox.SelectedIndex > 0)
                    OficinaSuggestionsListBox.SelectedIndex--;
                e.Handled = true;
                break;

            case Key.Enter:
                ConfirmarSeleccionOficina();
                e.Handled = true;
                break;

            case Key.Escape or Key.Tab:
                OficinaPopup.IsOpen = false;
                if(e.Key == Key.Tab)
                {
                    // Mueve el foco al siguiente control
                    var request = new TraversalRequest(FocusNavigationDirection.Next);
                    OficinaTextBox.MoveFocus(request);
                }
                e.Handled = true;
                break;
        }
    }

    private void OficinaSuggestionsListBox_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        ConfirmarSeleccionOficina();
    }

    private void ConfirmarSeleccionOficina()
    {
        if (OficinaSuggestionsListBox.SelectedItem is OficinaOption oficina)
        {
            _oficinaSeleccionada = oficina;
            OficinaTextBox.TextChanged -= OficinaTextBox_TextChanged; // evita reabrir el popup al setear el texto
            OficinaTextBox.Text = oficina.Nombre;
            OficinaTextBox.CaretIndex = OficinaTextBox.Text.Length;
            OficinaTextBox.TextChanged += OficinaTextBox_TextChanged;

            OficinaPopup.IsOpen = false;

            AplicarDefaultsDeOficina(oficina);
        }
    }

    private void AplicarDefaultsDeOficina(OficinaOption oficina)
    {
        if (_paseDefaults.TryGetValue(oficina.Nombre, out var config))
        {
            if (string.IsNullOrWhiteSpace(FoliosNuevosBox.Text) && string.IsNullOrWhiteSpace(FoliosTotalBox.Text))
            {
                FoliosNuevosBox.Text = config.FoliosNuevos.ToString();
            }

            if (string.IsNullOrWhiteSpace(ObservacionesPaseBox.Text))
            {
                ObservacionesPaseBox.Text = config.Observaciones;
            }
        }
    }

    private void FoliosTotalBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_actualizandoFoliosDesdePrograma) return;
        _actualizandoFoliosDesdePrograma = true;
        if (!string.IsNullOrWhiteSpace(FoliosTotalBox.Text))
        {
            FoliosNuevosBox.Text = string.Empty;
        }

        _actualizandoFoliosDesdePrograma = false;
    }

    private void FoliosNuevosBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_actualizandoFoliosDesdePrograma) return;
        _actualizandoFoliosDesdePrograma = true;
        if (!string.IsNullOrWhiteSpace(FoliosNuevosBox.Text))
        {
            FoliosTotalBox.Text = string.Empty;
        }

        _actualizandoFoliosDesdePrograma = false;
    }

    private void FoliosNuevosBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Up && e.Key != Key.Down) return;

        int valorActual = int.TryParse(FoliosNuevosBox.Text, out var v) ? v : 0;
        valorActual += e.Key == Key.Up ? 1 : -1;
        if (valorActual < 0) valorActual = 0;

        FoliosNuevosBox.Text = valorActual.ToString();
        FoliosNuevosBox.CaretIndex = FoliosNuevosBox.Text.Length;
        e.Handled = true;
    }

    private void LimpiarFormularioPase()
    {
        PaseNroExpedienteBox.Clear();
        FoliosTotalBox.IsEnabled = true;
        FoliosNuevosBox.IsEnabled = true;
    }

    private async void ConfirmarPaseButton_Click(object sender, RoutedEventArgs e)
    {
        // Validar antes de hacer cualquier busqueda, para fallar rapido si falta algo
        if (string.IsNullOrWhiteSpace(PaseNroExpedienteBox.Text) ||
            (string.IsNullOrWhiteSpace(FoliosTotalBox.Text) && string.IsNullOrWhiteSpace(FoliosNuevosBox.Text)) ||
            string.IsNullOrWhiteSpace(ObservacionesPaseBox.Text))
        {
            MessageBox.Show("Todos los campos son requeridos.");
            return;
        }

        if(_oficinaSeleccionada is not OficinaOption oficinaSeleccionada)
        {
            MessageBox.Show("Seleccione una Oficina Valida");
            return;
        }

        PaseNroExpedienteBox.Focus();

        try
        {
            _paseWorkflow.ValidateExp(PaseNroExpedienteBox.Text);
        }
        catch (ArgumentException ex)
        {
            MessageBox.Show(ex.Message);
            return;
        }

        ConfirmarPaseButton.IsEnabled = false;

        PasePreparation? result;
        try
        {
            result = await _paseWorkflow.PrepararAsync(PaseNroExpedienteBox.Text);
        }
        finally
        {

        }

        if (result == null)
        {
            MessageBox.Show("Expediente no encontrado en la cola de Pases.");
            return;
        }

        int totalFolios;

        if (!string.IsNullOrWhiteSpace(FoliosTotalBox.Text))
        {
            if (!int.TryParse(FoliosTotalBox.Text, out totalFolios))
            {
                MessageBox.Show("Folios Total debe ser un numero valido.");
                return;
            }
        }
        else
        {
            if (!int.TryParse(FoliosNuevosBox.Text, out var nuevos))
            {
                MessageBox.Show("Folios Nuevos debe ser un numero valido.");
                return;
            }
            totalFolios = result.FolioActual + nuevos;
        }

        var confirmWindow = new ConfirmarPaseWindow(
            PaseNroExpedienteBox.Text,
            result.Causante,
            oficinaSeleccionada.Nombre,
            totalFolios,
            ObservacionesPaseBox.Text);

        if (confirmWindow.ShowDialog() != true)
        {
            ConfirmarPaseButton.IsEnabled = true;
            return;
        }

        ConfirmarPaseButton.IsEnabled = true;

        var expediente = new Expediente
        {
            NroExpediente = PaseNroExpedienteBox.Text,
            Pase = new PaseData
            {
                OficinaDestino = oficinaSeleccionada.Nombre,
                Folios = totalFolios,
                Observaciones = ObservacionesPaseBox.Text
            }
        };

        LimpiarFormularioPase();

        try
        {
            await _paseWorkflow.ExecuteAsync(expediente);
            await MostrarToastAsync($"Pase del Expediente {expediente.NroExpediente} realizado con exito.", esError: false);
        }
        catch (Exception ex)
        {
            await MostrarToastAsync($"Error al realizar el Pase del Expediente {expediente.NroExpediente}: {ex.Message}", esError: true);
        }
    }

    private async Task MostrarToastAsync(string mensaje, bool esError)
    {
        ToastBorder.Background = esError ? new SolidColorBrush(Color.FromRgb(0xC0, 0x39, 0x2B))
                                          : new SolidColorBrush(Color.FromRgb(0x2E, 0x8B, 0x57));
        ToastText.Text = mensaje;
        ToastBorder.Visibility = Visibility.Visible;

        await Task.Delay(5000);

        ToastBorder.Visibility = Visibility.Collapsed;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        NroExpedienteBox.Focus();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.F1:
                MainTabControl.SelectedIndex = 0;
                NroExpedienteBox.Focus();
                e.Handled = true;
                break;
            case Key.F2:
                MainTabControl.SelectedIndex = 1;
                PaseNroExpedienteBox.Focus();
                e.Handled = true;
                break;
            case Key.F3:
                MainTabControl.SelectedIndex = 2;
                e.Handled = true;
                break;
            case Key.F4:
                MainTabControl.SelectedIndex = 3;
                e.Handled = true;
                break;
            case Key.Enter:
                if(!OficinaPopup.IsOpen){
                    HandleEnterShortcut();
                    e.Handled = true;
                }
                break;
        }
    }

    private void HandleEnterShortcut()
    {
        switch (MainTabControl.SelectedIndex)
        {
            case 0: // Busqueda
                if (BuscarButton.IsEnabled)
                {
                    BuscarButton_Click(this, new RoutedEventArgs());
                }
                break;
            case 1: // Pase
                if (ConfirmarPaseButton.IsEnabled)
                {
                    ConfirmarPaseButton_Click(this, new RoutedEventArgs());
                }
                break;
            case 2: // Recepcion
                // Implementar si es necesario
                break;
            case 3: // Resolucion
                // Implementar si es necesario
                break;
        }
    }

    private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {

    }

    private class HistorialItem
    {
        public string NroExpediente { get; set; } = string.Empty;
        public string? Causante { get; set; }

        public override string ToString() =>
            string.IsNullOrWhiteSpace(Causante) ? NroExpediente : $"{NroExpediente} - {Causante}";
    }

}