using IPSST.Application.Configuration;
using IPSST.Application.Services;
using IPSST.Domain.Entities;
using IPSST.Views;
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
    private readonly Dictionary<string, ResolucionDefaultConfig> _resolucionDefaults;

    private ResultadoBusquedaWindow? _resultadoBusquedaWindow;
    private readonly ObservableCollection<HistorialItem> _historial = new();

    private List<RecepcionItem> _recepcionEncontrados = new();

    private bool _actualizandoFoliosDesdePrograma;
    private OficinaOption? _oficinaSeleccionadaPase;
    private OficinaOption? _oficinaSeleccionadaResolucion;
    private OficinaOption? _oficinaSeleccionadaRecepcion;
    private const string DefaultConfigKey = "__default__";

    public MainWindow(
        BusquedaService busquedaService, 
        PaseWorkflow paseWorkflow, 
        RecepcionService recepcionService, 
        ResolucionWorkflow resolucionWorkflow,
        OficinaCacheService oficinaCacheService,
        Dictionary<string, PaseDefaultConfig> paseDefaults,
        Dictionary<string, ResolucionDefaultConfig> resolucionDefaults)
    {
        InitializeComponent();

        _busquedaService = busquedaService;
        _recepcionService = recepcionService;
        _paseWorkflow = paseWorkflow;
        _resolucionWorkflow = resolucionWorkflow;
        _oficinaCacheService = oficinaCacheService;
        _paseDefaults = paseDefaults;
        _resolucionDefaults = resolucionDefaults;

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

    private class HistorialItem
    {
        public string NroExpediente { get; set; } = string.Empty;
        public string? Causante { get; set; }

        public override string ToString() =>
            string.IsNullOrWhiteSpace(Causante) ? NroExpediente : $"{NroExpediente} - {Causante}";
    }

    //Pase

    private void OficinaTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        // Si el usuario sigue escribiendo, la seleccion anterior ya no es valida hasta que elija de nuevo
        _oficinaSeleccionadaPase = null;

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
            _oficinaSeleccionadaPase = oficina;
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
        if (!_paseDefaults.TryGetValue(oficina.Nombre, out var config))
        {
            _paseDefaults.TryGetValue(DefaultConfigKey, out config);
        }

        if (config == null)
        {
            return; // Ni la oficina especifica ni el default general estan configurados
        }

        FoliosNuevosBox.Text = config.FoliosNuevos.ToString();
        ObservacionesPaseBox.Text = config.Observaciones;
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

    private void FoliosNuevosBox_ValueChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (_actualizandoFoliosDesdePrograma) return;
        _actualizandoFoliosDesdePrograma = true;
        if (!string.IsNullOrWhiteSpace(FoliosNuevosBox.Text))
        {
            FoliosTotalBox.Text = string.Empty;
        }

        _actualizandoFoliosDesdePrograma = false;
    }

    private void LimpiarFormularioPase()
    {
        PaseNroExpedienteBox.Clear();
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

        if(_oficinaSeleccionadaPase is not OficinaOption oficinaSeleccionada)
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
            ConfirmarPaseButton.IsEnabled = true;
            return;
        }

        int totalFolios;

        if (!string.IsNullOrWhiteSpace(FoliosTotalBox.Text))
        {
            if (!int.TryParse(FoliosTotalBox.Text, out totalFolios))
            {
                MessageBox.Show("Folios Total debe ser un numero valido.");
                ConfirmarPaseButton.IsEnabled = true;
                return;
            }
            if (totalFolios < result.FolioActual)
            {
                MessageBox.Show($"Folios Total debe ser mayor o igual a los que muestra el sistema: {result.FolioActual} Folios");
                ConfirmarPaseButton.IsEnabled = true;
                return;
            }
        }
        else
        {
            if (!int.TryParse(FoliosNuevosBox.Text, out var nuevos))
            {
                MessageBox.Show("Folios Nuevos debe ser un numero valido.");
                ConfirmarPaseButton.IsEnabled = true;
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
            ExpedienteIdWeb = result.ExpId,
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
            await _paseWorkflow.ExecuteAsync(expediente, result.ExpId ?? string.Empty);
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

    //Resolucion

    private void ResolucionOficinaTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        // Si el usuario sigue escribiendo, la seleccion anterior ya no es valida hasta que elija de nuevo
        _oficinaSeleccionadaResolucion = null;

        var texto = ResolucionOficinaTextBox.Text;

        if (string.IsNullOrWhiteSpace(texto))
        {
            ResolucionOficinaPopup.IsOpen = false;
            return;
        }

        var filtradas = _oficinaCacheService.Oficinas
            .Where(o => o.Nombre.Contains(texto, StringComparison.OrdinalIgnoreCase))
            .ToList();

        ResolucionOficinaSuggestionsListBox.ItemsSource = filtradas;
        ResolucionOficinaPopup.IsOpen = filtradas.Count > 0;

        if (filtradas.Count > 0)
        {
            ResolucionOficinaSuggestionsListBox.SelectedIndex = 0;
        }
    }

    private void ResolucionOficinaTextBox_PreviewGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        ResolucionOficinaTextBox.SelectAll();
    }

    private void ResolucionOficinaTextBox_GotMouseCapture(object sender, MouseEventArgs e)
    {
        ResolucionOficinaTextBox.SelectAll();
    }

    private void ResolucionOficinaTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!ResolucionOficinaPopup.IsOpen) return;

        switch (e.Key)
        {
            case Key.Down:
                if (ResolucionOficinaSuggestionsListBox.SelectedIndex < ResolucionOficinaSuggestionsListBox.Items.Count - 1)
                    ResolucionOficinaSuggestionsListBox.SelectedIndex++;
                e.Handled = true;
                break;

            case Key.Up:
                if (ResolucionOficinaSuggestionsListBox.SelectedIndex > 0)
                    ResolucionOficinaSuggestionsListBox.SelectedIndex--;
                e.Handled = true;
                break;

            case Key.Enter:
                ResolucionConfirmarSeleccionOficina();
                e.Handled = true;
                break;

            case Key.Escape or Key.Tab:
                ResolucionOficinaPopup.IsOpen = false;
                if (e.Key == Key.Tab)
                {
                    // Mueve el foco al siguiente control
                    var request = new TraversalRequest(FocusNavigationDirection.Next);
                    ResolucionOficinaTextBox.MoveFocus(request);
                }
                e.Handled = true;
                break;
        }
    }

    private void ResolucionOficinaSuggestionsListBox_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        ResolucionConfirmarSeleccionOficina();
    }

    private void ResolucionConfirmarSeleccionOficina()
    {
        if (ResolucionOficinaSuggestionsListBox.SelectedItem is OficinaOption oficina)
        {
            _oficinaSeleccionadaResolucion = oficina;
            ResolucionOficinaTextBox.TextChanged -= ResolucionOficinaTextBox_TextChanged; // evita reabrir el popup al setear el texto
            ResolucionOficinaTextBox.Text = oficina.Nombre;
            ResolucionOficinaTextBox.CaretIndex = ResolucionOficinaTextBox.Text.Length;
            ResolucionOficinaTextBox.TextChanged += ResolucionOficinaTextBox_TextChanged;

            ResolucionOficinaPopup.IsOpen = false;

            ResolucionAplicarDefaultsDeOficina(oficina);
        }
    }

    private void ResolucionAplicarDefaultsDeOficina(OficinaOption oficina)
    {
        if (!_resolucionDefaults.TryGetValue(oficina.Nombre, out var config))
        {
            _resolucionDefaults.TryGetValue(DefaultConfigKey, out config);
        }

        if (config == null)
        {
            return; // Ni la oficina especifica ni el default general estan configurados
        }

        ResolucionFoliosNuevosBox.Text = config.FoliosNuevos.ToString();
    }

    private void ResolucionFoliosTotalBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_actualizandoFoliosDesdePrograma) return;
        _actualizandoFoliosDesdePrograma = true;
        if (!string.IsNullOrWhiteSpace(ResolucionFoliosTotalBox.Text))
        {
            ResolucionFoliosNuevosBox.Text = string.Empty;
        }

        _actualizandoFoliosDesdePrograma = false;
    }

    private void ResolucionFoliosNuevosBox_ValueChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (_actualizandoFoliosDesdePrograma) return;
        _actualizandoFoliosDesdePrograma = true;
        if (!string.IsNullOrWhiteSpace(ResolucionFoliosNuevosBox.Text))
        {
            ResolucionFoliosTotalBox.Text = string.Empty;
        }

        _actualizandoFoliosDesdePrograma = false;
    }
    private void FechaResolucionBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Up && e.Key != Key.Down) return;

        var fechaActual = FechaResolucionBox.Value ?? DateTime.Today;
        fechaActual = fechaActual.AddDays(e.Key == Key.Up ? 1 : -1);

        FechaResolucionBox.Value = fechaActual;
        e.Handled = true;
    }

    private void ResolucionLimpiarFormularioPase()
    {
        ResolucionNroExpedienteBox.Clear();
        NroResolucionBox.Clear();
    }

    private async void ConfirmarResolucionButton_Click(object sender, RoutedEventArgs e)
    {
        // Validar antes de hacer cualquier busqueda, para fallar rapido si falta algo
        if (string.IsNullOrWhiteSpace(ResolucionNroExpedienteBox.Text) ||
            (string.IsNullOrWhiteSpace(ResolucionFoliosTotalBox.Text) && string.IsNullOrWhiteSpace(ResolucionFoliosNuevosBox.Text)) ||
            string.IsNullOrWhiteSpace(ObservacionesResolucionBox.Text))
        {
            MessageBox.Show("Todos los campos son requeridos.");
            return;
        }

        if (_oficinaSeleccionadaResolucion is not OficinaOption oficinaSeleccionada)
        {
            MessageBox.Show("Seleccione una Oficina Valida");
            return;
        }

        ResolucionNroExpedienteBox.Focus();

        try
        {
            _paseWorkflow.ValidateExp(ResolucionNroExpedienteBox.Text);
        }
        catch (ArgumentException ex)
        {
            MessageBox.Show(ex.Message);
            return;
        }

        ConfirmarResolucionButton.IsEnabled = false;

        ResPreparation? result;
        try
        {
            result = await _resolucionWorkflow.PrepararResolucionAsync(ResolucionNroExpedienteBox.Text);
        }
        finally
        {

        }

        if (result == null)
        {
            MessageBox.Show("Expediente no encontrado en la cola de Pases.");
            ConfirmarResolucionButton.IsEnabled = true;
            return;
        }

        int totalFolios;

        if (!string.IsNullOrWhiteSpace(ResolucionFoliosTotalBox.Text))
        {
            if (!int.TryParse(ResolucionFoliosTotalBox.Text, out totalFolios))
            {
                MessageBox.Show("Folios Total debe ser un numero valido.");
                ConfirmarResolucionButton.IsEnabled = true;
                return;
            }
        }
        else
        {
            if (!int.TryParse(ResolucionFoliosNuevosBox.Text, out var nuevos))
            {
                MessageBox.Show("Folios Nuevos debe ser un numero valido.");
                ConfirmarResolucionButton.IsEnabled = true;
                return;
            }
            totalFolios = result.FolioActual + nuevos;
        }

        for (int i = 0; i < result.ResolucionesAnteriores.Count; i++)
        {
            if (result.ResolucionesAnteriores[i]!.Contains($"{NroResolucionBox.Text}/", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("Numero de Resolucion ya fue cargado al expediente");
                ConfirmarResolucionButton.IsEnabled = true;
                return;
            }
        }

        if (FechaResolucionBox.Value is null)
        {
            MessageBox.Show("Debe seleccionar una fecha de resolucion.");
            ConfirmarResolucionButton.IsEnabled = true;
            return;
        }

        var confirmResWindow = new ConfirmarResWindow(
            ResolucionNroExpedienteBox.Text,
            result.Causante,
            NroResolucionBox.Text,
            FechaResolucionBox.Text,
            ObservacionesResolucionBox.Text,
            oficinaSeleccionada.Nombre,
            totalFolios,
            ObservacionesPase2Box.Text);

        if (confirmResWindow.ShowDialog() != true)
        {
            ConfirmarResolucionButton.IsEnabled = true;
            return;
        }

        ConfirmarResolucionButton.IsEnabled = true;

        var expediente = new Expediente
        {
            NroExpediente = ResolucionNroExpedienteBox.Text,
            Pase = new PaseData
            {
                OficinaDestino = oficinaSeleccionada.Nombre,
                Folios = totalFolios,
                Observaciones = ObservacionesPase2Box.Text
            },
            Resolucion = new ResolucionData
            {
                NroResolucion = NroResolucionBox.Text,
                FechaResolucion = FechaResolucionBox.Value.Value,
                Observaciones = ObservacionesResolucionBox.Text
            }
        };

        ResolucionLimpiarFormularioPase();

        try
        {
            await _resolucionWorkflow.ExecuteAsync(expediente, result.ExpId);
            await ResolucionMostrarToastAsync($"Resolucion del Expediente {expediente.NroExpediente} cargada con exito.", esError: false);
        }
        catch (Exception ex)
        {
            await ResolucionMostrarToastAsync($"Error al cargar la resolucion del Expediente {expediente.NroExpediente}: {ex.Message}", esError: true);
        }
    }

    private async Task ResolucionMostrarToastAsync(string mensaje, bool esError)
    {
        ToastBorder.Background = esError ? new SolidColorBrush(Color.FromRgb(0xC0, 0x39, 0x2B))
                                          : new SolidColorBrush(Color.FromRgb(0x2E, 0x8B, 0x57));
        ToastText.Text = mensaje;
        ToastBorder.Visibility = Visibility.Visible;

        await Task.Delay(5000);

        ToastBorder.Visibility = Visibility.Collapsed;
    }

    //Recepcion
    private void RecepNroExpedienteBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if(!string.IsNullOrWhiteSpace(RecepNroExpedienteBox.Text))
        {
            RecepOficinaTextBox.Text = string.Empty;
        }
    }

    private void RecepOficinaTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        // Si el usuario sigue escribiendo, la seleccion anterior ya no es valida hasta que elija de nuevo
        _oficinaSeleccionadaRecepcion = null;

        if (!string.IsNullOrWhiteSpace(RecepOficinaTextBox.Text))
        {
            RecepNroExpedienteBox.Text = string.Empty;
        }

        var texto = RecepOficinaTextBox.Text;

        if (string.IsNullOrWhiteSpace(texto))
        {
            RecepOficinaPopup.IsOpen = false;
            return;
        }

        var filtradas = _oficinaCacheService.Oficinas
            .Where(o => o.Nombre.Contains(texto, StringComparison.OrdinalIgnoreCase))
            .ToList();

        RecepOficinaSuggestionsListBox.ItemsSource = filtradas;
        RecepOficinaPopup.IsOpen = filtradas.Count > 0;

        if (filtradas.Count > 0)
        {
            RecepOficinaSuggestionsListBox.SelectedIndex = 0;
        }
    }

    private void RecepOficinaTextBox_PreviewGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        RecepOficinaTextBox.SelectAll();
    }

    private void RecepOficinaTextBox_GotMouseCapture(object sender, MouseEventArgs e)
    {
        RecepOficinaTextBox.SelectAll();
    }

    private void RecepOficinaTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!RecepOficinaPopup.IsOpen) return;

        switch (e.Key)
        {
            case Key.Down:
                if (RecepOficinaSuggestionsListBox.SelectedIndex < RecepOficinaSuggestionsListBox.Items.Count - 1)
                    RecepOficinaSuggestionsListBox.SelectedIndex++;
                e.Handled = true;
                break;

            case Key.Up:
                if (RecepOficinaSuggestionsListBox.SelectedIndex > 0)
                    RecepOficinaSuggestionsListBox.SelectedIndex--;
                e.Handled = true;
                break;

            case Key.Enter:
                RecepConfirmarSeleccionOficina();
                e.Handled = true;
                break;

            case Key.Escape or Key.Tab:
                RecepOficinaPopup.IsOpen = false;
                if (e.Key == Key.Tab)
                {
                    // Mueve el foco al siguiente control
                    var request = new TraversalRequest(FocusNavigationDirection.Next);
                    RecepOficinaTextBox.MoveFocus(request);
                }
                e.Handled = true;
                break;
        }
    }

    private void RecepOficinaSuggestionsListBox_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        RecepConfirmarSeleccionOficina();
    }

    private void RecepConfirmarSeleccionOficina()
    {
        if (RecepOficinaSuggestionsListBox.SelectedItem is OficinaOption oficina)
        {
            _oficinaSeleccionadaRecepcion = oficina;
            RecepOficinaTextBox.TextChanged -= RecepOficinaTextBox_TextChanged; // evita reabrir el popup al setear el texto
            RecepOficinaTextBox.Text = oficina.Nombre;
            RecepOficinaTextBox.CaretIndex = RecepOficinaTextBox.Text.Length;
            RecepOficinaTextBox.TextChanged += RecepOficinaTextBox_TextChanged;

            RecepOficinaPopup.IsOpen = false;
        }
    }

    private async void RecepBuscarButton_Click(object sender, RoutedEventArgs e)
    {
        var nro = RecepNroExpedienteBox.Text;

        if (string.IsNullOrWhiteSpace(RecepNroExpedienteBox.Text))
        {
            MessageBox.Show("Nro de Expediente requerido.");
            return;
        }

        RecepNroExpedienteBox.Focus();

        try
        {
            _recepcionService.ValidateExp(RecepNroExpedienteBox.Text);
        }
        catch (ArgumentException ex)
        {
            MessageBox.Show(ex.Message);
            return;
        }

        setRecibiendoState(true);

        RecepcionItem? item;
        try
        {
            item = await _recepcionService.PrepararIndividualAsync(nro);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error al buscar: {ex.Message}");
            setRecepButtonEnabled(true);
            return;
        }
        finally
        {
            RecepBuscandoProgressBar.Visibility = Visibility.Collapsed;
            RecepBuscandoText.Visibility = Visibility.Collapsed;
        }

        if (item == null)
        {
            MessageBox.Show("Expediente no encontrado para Recepcion.");
            setRecepButtonEnabled(true);
            return;
        }

        var confirmWindow = new ConfirmarRecepcionWindow(item);
        if (confirmWindow.ShowDialog() != true)
        {
            setRecepButtonEnabled(true);
            return;
        }

        setRecepButtonEnabled(true);
        RecepNroExpedienteBox.Clear();

        try
        {
            var admitido = await _recepcionService.ConfirmarIndividualAsync(nro);

            if (admitido)
            {
                await MostrarToastAsync($"Expediente {nro} recibido con exito.", esError: false);
            }
            else
            {
                await MostrarToastAsync($"No se pudo recibir el expediente {nro}.", esError: true);
            }
        }
        catch (Exception ex)
        {
            await MostrarToastAsync($"Error al recibir {nro}: {ex.Message}", esError: true);
        }
    }
    private void setRecibiendoState(bool buscando)
    {
        RecepBuscandoProgressBar.Visibility = buscando ? Visibility.Visible : Visibility.Collapsed;
        RecepBuscandoText.Visibility = buscando ? Visibility.Visible : Visibility.Collapsed;
        setRecepButtonEnabled(!buscando);
    }

    private void setRecepButtonEnabled(bool enabled)
    {
        RecepBuscarButton.IsEnabled = enabled;
        RecepOficinaBuscarButton.IsEnabled = enabled;
        RecepConfirmarSeleccionadosButton.IsEnabled = enabled;
    }

    private async void RecepOficinaBuscarButton_Click(object sender, RoutedEventArgs e)
    {
        var oficina = RecepOficinaTextBox.Text;

        if (string.IsNullOrWhiteSpace(oficina))
        {
            MessageBox.Show("Ingrese una oficina.");
            return;
        }

        if (_oficinaSeleccionadaRecepcion is not OficinaOption oficinaSeleccionada)
        {
            MessageBox.Show("Seleccione una Oficina Valida");
            return;
        }

        RecepOficinaTextBox.Focus();

        RecepOficinaBuscandoProgressBar.Visibility = Visibility.Visible;
        setRecepButtonEnabled(false);

        try
        {
            _recepcionEncontrados = await _recepcionService.BuscarPorOficinaAsync(oficina);
            RecepOficinaGrid.ItemsSource = null;
            RecepOficinaGrid.ItemsSource = _recepcionEncontrados;

            if (_recepcionEncontrados.Count == 0)
            {
                MessageBox.Show("No se encontraron expedientes para esa oficina.");
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error al buscar: {ex.Message}");
        }
        finally
        {
            RecepOficinaBuscandoProgressBar.Visibility = Visibility.Collapsed;
            setRecepButtonEnabled(true);
            RecepOficinaGrid.Focus();
        }
    }

    private async void RecepConfirmarSeleccionadosButton_Click(object sender, RoutedEventArgs e)
    {
        RecepOficinaGrid.CommitEdit(DataGridEditingUnit.Row, true);

        var seleccionados = _recepcionEncontrados.Where(r => r.Seleccionado).ToList();

        if (seleccionados.Count == 0)
        {
            MessageBox.Show("No hay expedientes seleccionados.");
            return;
        }

        var confirmWindow = new ConfirmarRecepcionMultipleWindow(seleccionados);
        if (confirmWindow.ShowDialog() != true)
        {
            return;
        }

        setRecepButtonEnabled(false);

        try
        {
            var nrosSeleccionados = seleccionados.Select(r => r.NroExpediente).ToList();
            var result = await _recepcionService.AdmitBulkAsync(RecepOficinaTextBox.Text, nrosSeleccionados);
            RecepOficinaGrid.ItemsSource = null;
            setRecepButtonEnabled(true);


            await MostrarToastAsync(
                $"Recibidos: {result.Admitted.Count} de {nrosSeleccionados.Count}." +
                (result.NotFound.Count > 0 ? $" No encontrados: {result.NotFound.Count}." : ""),
                esError: result.NotFound.Count > 0);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error al recibir seleccionados: {ex.Message}");
        }
    }

    //Globales

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        NroExpedienteBox.Focus();
        FechaResolucionBox.Value = DateTime.Today;
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
                if(!OficinaPopup.IsOpen && !ResolucionOficinaPopup.IsOpen && !RecepOficinaPopup.IsOpen){
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
            case 2: // Resolucion
                if (ConfirmarResolucionButton.IsEnabled)
                {
                    ConfirmarResolucionButton_Click(this, new RoutedEventArgs());
                }
                break;
            case 3: // Recepcion
                if (RecepBuscarButton.IsEnabled && RecepNroExpedienteBox.Text != string.Empty)
                {
                    RecepBuscarButton_Click(this, new RoutedEventArgs());
                }
                else if (RecepOficinaBuscarButton.IsEnabled && RecepOficinaTextBox.Text != string.Empty && RecepOficinaTextBox.IsFocused)
                {
                    RecepOficinaBuscarButton_Click(this, new RoutedEventArgs());
                } 
                else if (RecepConfirmarSeleccionadosButton.IsEnabled && RecepOficinaGrid != null)
                {
                    RecepConfirmarSeleccionadosButton_Click(this, new RoutedEventArgs());
                }

                break;
        }
    }

    private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {

    }

    
}