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
        OficinaComboBox.ItemsSource = _oficinaCacheService.Oficinas;
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

    private void OficinaComboBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var texto = OficinaComboBox.Text;

        var filtradas = string.IsNullOrWhiteSpace(texto)
            ? _oficinaCacheService.Oficinas
            : _oficinaCacheService.Oficinas.Where(o => o.Nombre.Contains(texto, StringComparison.OrdinalIgnoreCase)).ToList();

        OficinaComboBox.ItemsSource = filtradas;
        OficinaComboBox.IsDropDownOpen = filtradas.Count > 0 && !string.IsNullOrWhiteSpace(texto);
    }

    private void OficinaComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (OficinaComboBox.SelectedItem is OficinaOption oficina &&
        _paseDefaults.TryGetValue(oficina.Nombre, out var config))
        {
            // Solo completa si el usuario todavia no escribio nada ahi, para no pisar su entrada
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
            FoliosNuevosBox.IsEnabled = false;
        }
        else
        {
            FoliosNuevosBox.IsEnabled = true;
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
            FoliosTotalBox.IsEnabled = false;
        }
        else
        {
            FoliosTotalBox.IsEnabled = true;
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
            OficinaComboBox.SelectedItem is not OficinaOption oficinaSeleccionada ||
            (string.IsNullOrWhiteSpace(FoliosTotalBox.Text) && string.IsNullOrWhiteSpace(FoliosNuevosBox.Text)) ||
            string.IsNullOrWhiteSpace(ObservacionesPaseBox.Text))
        {
            MessageBox.Show("Todos los campos son requeridos.");
            return;
        }

        if (!string.IsNullOrWhiteSpace(FoliosTotalBox.Text))
        {
            if (!int.TryParse(FoliosTotalBox.Text, out var totalFolios))
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
        }

        var confirmWindow = new ConfirmarPaseWindow(
        PaseNroExpedienteBox.Text,
        result.Causante,
        oficinaSeleccionada.Nombre,
        totalFolios,
        ObservacionesPaseBox.Text);

        if (confirmWindow.ShowDialog() != true)
        {
            return;
        }

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

        ConfirmarPaseButton.IsEnabled = false;
        try
        {
            await _paseWorkflow.ExecuteAsync(expediente);
            MessageBox.Show("Pase realizado con exito.");
            LimpiarFormularioPase();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error al realizar el Pase: {ex.Message}");
        }
        finally
        {
            ConfirmarPaseButton.IsEnabled = true;
        }
    }

    private void SetPaseCargandoState(bool cargando)
    {
        PaseProgressBar.Visibility = cargando ? Visibility.Visible : Visibility.Collapsed;
        PaseCargandoText.Visibility = cargando ? Visibility.Visible : Visibility.Collapsed;
        ConfirmarPaseButton.IsEnabled = !cargando;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.F1:
                MainTabControl.SelectedIndex = 0;
                e.Handled = true;
                break;
            case Key.F2:
                MainTabControl.SelectedIndex = 1;
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
                BuscarButton_Click(sender, e);
                e.Handled = true;
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