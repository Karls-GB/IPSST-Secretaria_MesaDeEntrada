using IPSST.Application.Configuration;
using IPSST.Application.Services;
using IPSST.Domain.Entities;
using IPSST.Views;
using IPSST.Application.Workflows;
using System.Collections.ObjectModel;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace IPSST;
public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly BusquedaService _busquedaService;
    private readonly RecepcionService _recepcionService;
    private readonly PaseWorkflow _paseWorkflow;
    private readonly ResolucionWorkflow _resolucionWorkflow;
    private readonly CancelarPaseWorkflow _cancelarPaseWorkflow;
    private readonly OficinaCacheService _oficinaCacheService;
    private readonly Dictionary<string, PaseDefaultConfig> _paseDefaults;
    private readonly Dictionary<string, ResolucionDefaultConfig> _resolucionDefaults;

    private readonly HistorialService _historialService;

    private ResultadoBusquedaWindow? _resultadoBusquedaWindow;

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
        CancelarPaseWorkflow cancelarPaseWorkflow,
        OficinaCacheService oficinaCacheService,
        HistorialService historialService,
        Dictionary<string, PaseDefaultConfig> paseDefaults,
        Dictionary<string, ResolucionDefaultConfig> resolucionDefaults)
    {
        InitializeComponent();
        this.AjustarAPantalla();

        _busquedaService = busquedaService;
        _recepcionService = recepcionService;
        _paseWorkflow = paseWorkflow;
        _resolucionWorkflow = resolucionWorkflow;
        _cancelarPaseWorkflow = cancelarPaseWorkflow;
        _oficinaCacheService = oficinaCacheService;
        _paseDefaults = paseDefaults;
        _resolucionDefaults = resolucionDefaults;
        _historialService = historialService;
    }

    //Busqueda

    private async void BuscarButton_Click(object sender, RoutedEventArgs e)
    {
        await EjecutarBusquedaAsync(NroExpedienteBox.Text);
    }

    public async Task EjecutarBusquedaAsync(string nroExpediente)
    {
        setBuscandoState(true);
        int observacionesFaltantes = 0;

        try
        {
            var result = await _busquedaService.SearchAsync(nroExpediente);

            if (result == null)
            {
                await Mensajes.AvisoAsync("Expediente no encontrado.");
                return;
            }

            _historialService.RegistrarBusqueda(result);

            if (_resultadoBusquedaWindow == null)
            {
                _resultadoBusquedaWindow = new ResultadoBusquedaWindow(result, _busquedaService);
                _resultadoBusquedaWindow.Closed += (s, args) => _resultadoBusquedaWindow = null;
                _resultadoBusquedaWindow.Show();
            }
            else
            {
                _resultadoBusquedaWindow.UpdateResultado(result);
            }

            observacionesFaltantes = result.ObservacionesFaltantes;
        }
        catch (ArgumentException ex)
        {
            await Mensajes.AvisoAsync(ex.Message);
        }
        catch (Exception ex)
        {
            // BusquedaService ya registra el error en el log
            await Mensajes.ErrorAsync($"Error al buscar: {ex.Message}");
        }
        finally
        {
            setBuscandoState(false);
        }

        if (observacionesFaltantes > 0)
        {
            await BusquedaMostrarToastAsync($"{observacionesFaltantes} observaciones no terminaron de cargar. Vuelva a buscar para verlas.", esError: true);
        }
    }

    private void setBuscandoState(bool buscando)
    {
        BuscandoProgressBar.Visibility = buscando ? Visibility.Visible : Visibility.Collapsed;
        BuscandoText.Visibility = buscando ? Visibility.Visible : Visibility.Collapsed;
        BuscarButton.IsEnabled = !buscando;
        NroExpedienteBox.IsEnabled = !buscando;
    }

    //Historial

    private const int HistorialTabIndex = 4;

    private async Task CargarHistorialAsync()
    {
        var accion = (FiltroAccion)Math.Max(0, HistorialAccionCombo.SelectedIndex);
        var fecha = (FiltroFecha)Math.Max(0, HistorialFechaCombo.SelectedIndex);

        HistBuscarColumn.Visibility = accion == FiltroAccion.Busqueda ? Visibility.Visible : Visibility.Collapsed;

        try
        {
            var entradas = await _historialService.ObtenerAsync(accion, fecha);
            HistorialGrid.ItemsSource = entradas;
            HistorialCantidadText.Text = $"{entradas.Count} registros";
        }
        catch (Exception ex)
        {
            await Mensajes.ErrorAsync($"Error al cargar el historial: {ex.Message}");
        }
    }

    private async void HistorialFiltro_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Evita cargar durante InitializeComponent (los controles todavia no existen)
        if (IsLoaded)
        {
            await CargarHistorialAsync();
        }
    }

    private async void HistorialActualizarButton_Click(object sender, RoutedEventArgs e)
    {
        await CargarHistorialAsync();
    }

    private async void HistorialBuscarDeNuevo_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is HistorialEntry entrada)
        {
            NroExpedienteBox.Text = entrada.NroExpediente;
            await EjecutarBusquedaAsync(entrada.NroExpediente);
            await CargarHistorialAsync();
        }
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

        // Frecuentes primero (ver OficinasFrecuentes en appsettings.json)
        var filtradas = _oficinaCacheService.Filtrar(texto);

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

    private void LimpiarFormularioPase()
    {
        PaseNroExpedienteBox.Clear();
    }

    private void CancelarPaseCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        // Para cancelar solo hace falta el numero de expediente
        var cancelar = CancelarPaseCheckBox.IsChecked == true;
        OficinaTextBox.IsEnabled = !cancelar;
        FoliosNuevosBox.IsEnabled = !cancelar;
        FoliosTotalBox.IsEnabled = !cancelar;
        ObservacionesPaseBox.IsEnabled = !cancelar;
    }

    // Muestra por que el expediente no esta disponible y ofrece abrir su busqueda completa con un click (Enter = Si)
    private async Task OfrecerBusquedaAsync(string nroExpediente, string motivo)
    {
        var buscar = await Mensajes.ConfirmarAsync(
            $"{motivo}\n\n¿Desea realizar la búsqueda del expediente?",
            "Expediente no disponible", MessageBoxImage.Information);

        if (buscar)
        {
            await EjecutarBusquedaAsync(nroExpediente.Trim());
        }
    }

    private static bool MismaOficina(string a, string b)
    {
        static string Normalizar(string s) => Regex.Replace(s.Trim(), @"\s+", " ");
        return string.Equals(Normalizar(a), Normalizar(b), StringComparison.OrdinalIgnoreCase);
    }

    private async Task CancelarPaseAsync()
    {
        var nro = PaseNroExpedienteBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(nro))
        {
            await Mensajes.AvisoAsync("Nro de Expediente requerido.");
            return;
        }

        try
        {
            _paseWorkflow.ValidateExp(nro);
        }
        catch (ArgumentException ex)
        {
            await Mensajes.AvisoAsync(ex.Message);
            return;
        }

        ConfirmarPaseButton.IsEnabled = false;

        PasePreparation? preparacion;
        try
        {
            preparacion = await _cancelarPaseWorkflow.PrepararAsync(nro);
        }
        catch (Exception ex)
        {
            await Mensajes.ErrorAsync($"Error al buscar el expediente: {ex.Message}");
            ConfirmarPaseButton.IsEnabled = true;
            return;
        }

        ConfirmarPaseButton.IsEnabled = true;

        if (preparacion == null)
        {
            await Mensajes.AvisoAsync("Expediente no encontrado.");
            return;
        }

        if (preparacion.PasePendiente is not PasePendiente pendiente)
        {
            await OfrecerBusquedaAsync(nro, preparacion.MotivoNoDisponible ?? "El expediente no tiene un pase pendiente desde esta oficina.");
            return;
        }

        var cancelar = await Mensajes.ConfirmarAsync(
            $"¿Cancelar el pase del expediente {pendiente.NroExpediente} hacia {pendiente.OficinaDestino} con {pendiente.FoliosPase} folios?",
            "Cancelar pase");

        if (!cancelar)
        {
            return;
        }

        LimpiarFormularioPase();
        CancelarPaseCheckBox.IsChecked = false;

        try
        {
            await _cancelarPaseWorkflow.CancelarAsync(nro, pendiente.ExpId, pendiente.OficinaDestino, pendiente.Causante);
            await PaseMostrarToastAsync($"Pase del Expediente {pendiente.NroExpediente} cancelado con exito.", esError: false);
        }
        catch (Exception ex)
        {
            await PaseMostrarToastAsync($"Error al cancelar el Pase del Expediente {pendiente.NroExpediente}: {ex.Message}", esError: true);
        }
    }

    private async void ConfirmarPaseButton_Click(object sender, RoutedEventArgs e)
    {
        if (CancelarPaseCheckBox.IsChecked == true)
        {
            await CancelarPaseAsync();
            return;
        }

        // Validar antes de hacer cualquier busqueda, para fallar rapido si falta algo
        if (string.IsNullOrWhiteSpace(PaseNroExpedienteBox.Text) ||
            (string.IsNullOrWhiteSpace(FoliosTotalBox.Text) && string.IsNullOrWhiteSpace(FoliosNuevosBox.Text)) ||
            string.IsNullOrWhiteSpace(ObservacionesPaseBox.Text))
        {
            await Mensajes.AvisoAsync("Todos los campos son requeridos.");
            return;
        }

        if(_oficinaSeleccionadaPase is not OficinaOption oficinaSeleccionada)
        {
            await Mensajes.AvisoAsync("Seleccione una Oficina Valida");
            return;
        }

        PaseNroExpedienteBox.Focus();

        try
        {
            _paseWorkflow.ValidateExp(PaseNroExpedienteBox.Text);
        }
        catch (ArgumentException ex)
        {
            await Mensajes.AvisoAsync(ex.Message);
            return;
        }

        ConfirmarPaseButton.IsEnabled = false;

        PasePreparation? result;
        try
        {
            result = await _paseWorkflow.PrepararAsync(PaseNroExpedienteBox.Text);
        }
        catch (Exception ex)
        {
            await Mensajes.ErrorAsync($"Error al buscar el expediente: {ex.Message}");
            ConfirmarPaseButton.IsEnabled = true;
            return;
        }

        if (result == null)
        {
            await Mensajes.AvisoAsync("Expediente no encontrado.");
            ConfirmarPaseButton.IsEnabled = true;
            return;
        }

        // Existe pero no se puede hacer el pase desde aca: el mensaje indica donde esta
        if (result.MotivoNoDisponible is string motivo)
        {
            ConfirmarPaseButton.IsEnabled = true;
            await OfrecerBusquedaAsync(PaseNroExpedienteBox.Text, motivo);
            return;
        }

        int totalFolios;

        if (!string.IsNullOrWhiteSpace(FoliosTotalBox.Text))
        {
            if (!int.TryParse(FoliosTotalBox.Text, out totalFolios))
            {
                await Mensajes.AvisoAsync("Folios Total debe ser un numero valido.");
                ConfirmarPaseButton.IsEnabled = true;
                return;
            }
            if (totalFolios < result.FolioActual)
            {
                await Mensajes.AvisoAsync($"Folios Total debe ser mayor o igual a los que muestra el sistema: {result.FolioActual} Folios");
                ConfirmarPaseButton.IsEnabled = true;
                return;
            }
        }
        else
        {
            if (!int.TryParse(FoliosNuevosBox.Text, out var nuevos))
            {
                await Mensajes.AvisoAsync("Folios Nuevos debe ser un numero valido.");
                ConfirmarPaseButton.IsEnabled = true;
                return;
            }
            totalFolios = result.FolioActual + nuevos;
        }

        var expediente = new Expediente
        {
            NroExpediente = PaseNroExpedienteBox.Text,
            Causante = result.Causante,
            ExpedienteIdWeb = result.ExpId,
            Pase = new PaseData
            {
                OficinaDestino = oficinaSeleccionada.Nombre,
                Folios = totalFolios,
                Observaciones = ObservacionesPaseBox.Text
            }
        };

        // Ya tiene un pase hecho desde esta oficina: se ofrece cancelarlo y hacer el pase corregido
        if (result.PasePendiente is PasePendiente pendiente)
        {
            ConfirmarPaseButton.IsEnabled = true;

            if (MismaOficina(pendiente.OficinaDestino, oficinaSeleccionada.Nombre))
            {
                await Mensajes.AvisoAsync($"El expediente ya tiene un pase hacia esa oficina con {pendiente.FoliosPase} folios.");
                return;
            }

            var corregir = await Mensajes.ConfirmarAsync(
                $"El expediente {pendiente.NroExpediente} ya tiene un pase hacia {pendiente.OficinaDestino} con {pendiente.FoliosPase} folios.\n\n" +
                $"¿Cancelar ese pase y enviarlo a {oficinaSeleccionada.Nombre} con {totalFolios} folios?\n\n" +
                $"Observaciones: {ObservacionesPaseBox.Text}",
                "Corregir pase");

            if (!corregir)
            {
                return;
            }

            LimpiarFormularioPase();

            try
            {
                await _cancelarPaseWorkflow.CancelarYRehacerAsync(expediente, pendiente.ExpId, pendiente.OficinaDestino);
                await PaseMostrarToastAsync($"Pase del Expediente {expediente.NroExpediente} corregido: enviado a {oficinaSeleccionada.Nombre}.", esError: false);
            }
            catch (Exception ex)
            {
                await PaseMostrarToastAsync($"Error al corregir el Pase del Expediente {expediente.NroExpediente}: {ex.Message}", esError: true);
            }

            return;
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

        LimpiarFormularioPase();

        try
        {
            await _paseWorkflow.ExecuteAsync(expediente, result.ExpId ?? string.Empty);
            await PaseMostrarToastAsync($"Pase del Expediente {expediente.NroExpediente} realizado con exito.", esError: false);
        }
        catch (Exception ex)
        {
            await PaseMostrarToastAsync($"Error al realizar el Pase del Expediente {expediente.NroExpediente}: {ex.Message}", esError: true);
        }
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

        // Frecuentes primero (ver OficinasFrecuentes en appsettings.json)
        var filtradas = _oficinaCacheService.Filtrar(texto);

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

    private void ResolucionFoliosNuevosBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_actualizandoFoliosDesdePrograma) return;
        _actualizandoFoliosDesdePrograma = true;
        if (!string.IsNullOrWhiteSpace(ResolucionFoliosNuevosBox.Text))
        {
            ResolucionFoliosTotalBox.Text = string.Empty;
        }

        _actualizandoFoliosDesdePrograma = false;
    }
    // El boton del calendario queda fuera del recorrido con Tab (sigue funcionando con el mouse).
    // Se hace en Loaded porque la pestaña Resolucion arma su contenido recien al abrirse.
    private void FechaResolucionBox_Loaded(object sender, RoutedEventArgs e)
    {
        if (FechaResolucionBox.Template.FindName("PART_Button", FechaResolucionBox) is Button calendario)
        {
            calendario.IsTabStop = false;
        }
    }

    private void FechaResolucionBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Up && e.Key != Key.Down) return;

        var fechaActual = FechaResolucionBox.SelectedDate ?? DateTime.Today;
        fechaActual = fechaActual.AddDays(e.Key == Key.Up ? 1 : -1);

        FechaResolucionBox.SelectedDate = fechaActual;
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
            await Mensajes.AvisoAsync("Todos los campos son requeridos.");
            return;
        }

        if (_oficinaSeleccionadaResolucion is not OficinaOption oficinaSeleccionada)
        {
            await Mensajes.AvisoAsync("Seleccione una Oficina Valida");
            return;
        }

        ResolucionNroExpedienteBox.Focus();

        try
        {
            _paseWorkflow.ValidateExp(ResolucionNroExpedienteBox.Text);
        }
        catch (ArgumentException ex)
        {
            await Mensajes.AvisoAsync(ex.Message);
            return;
        }

        ConfirmarResolucionButton.IsEnabled = false;

        ResPreparation? result;
        try
        {
            result = await _resolucionWorkflow.PrepararResolucionAsync(ResolucionNroExpedienteBox.Text);
        }
        catch (Exception ex)
        {
            await Mensajes.ErrorAsync($"Error al buscar el expediente: {ex.Message}");
            ConfirmarResolucionButton.IsEnabled = true;
            return;
        }

        if (result == null)
        {
            await Mensajes.AvisoAsync("Expediente no encontrado en la cola de Resoluciones.");
            ConfirmarResolucionButton.IsEnabled = true;
            return;
        }

        int totalFolios;

        if (!string.IsNullOrWhiteSpace(ResolucionFoliosTotalBox.Text))
        {
            if (!int.TryParse(ResolucionFoliosTotalBox.Text, out totalFolios))
            {
                await Mensajes.AvisoAsync("Folios Total debe ser un numero valido.");
                ConfirmarResolucionButton.IsEnabled = true;
                return;
            }
        }
        else
        {
            if (!int.TryParse(ResolucionFoliosNuevosBox.Text, out var nuevos))
            {
                await Mensajes.AvisoAsync("Folios Nuevos debe ser un numero valido.");
                ConfirmarResolucionButton.IsEnabled = true;
                return;
            }
            totalFolios = result.FolioActual + nuevos;
        }

        for (int i = 0; i < result.ResolucionesAnteriores.Count; i++)
        {
            if (result.ResolucionesAnteriores[i]!.Contains($"{NroResolucionBox.Text}/", StringComparison.OrdinalIgnoreCase))
            {
                await Mensajes.AvisoAsync("Numero de Resolucion ya fue cargado al expediente");
                ConfirmarResolucionButton.IsEnabled = true;
                return;
            }
        }

        if (FechaResolucionBox.SelectedDate is null)
        {
            await Mensajes.AvisoAsync("Debe seleccionar una fecha de resolucion.");
            ConfirmarResolucionButton.IsEnabled = true;
            return;
        }

        var confirmResWindow = new ConfirmarResWindow(
            ResolucionNroExpedienteBox.Text,
            result.Causante,
            NroResolucionBox.Text,
            FechaResolucionBox.SelectedDate.Value.ToString("dd/MM/yyyy"),
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
            Causante = result.Causante,
            Pase = new PaseData
            {
                OficinaDestino = oficinaSeleccionada.Nombre,
                Folios = totalFolios,
                Observaciones = ObservacionesPase2Box.Text
            },
            Resolucion = new ResolucionData
            {
                NroResolucion = NroResolucionBox.Text,
                FechaResolucion = FechaResolucionBox.SelectedDate.Value,
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

    //Avisos: uno por pestaña, visible solo en la pestaña donde se origino

    private const int BusquedaTabIndex = 0;
    private const int PaseTabIndex = 1;
    private const int ResolucionTabIndex = 2;
    private const int RecepcionTabIndex = 3;

    // Avisos con el Snackbar de WPF-UI: un SnackbarPresenter por pestaña (solo visible en su pestaña).
    // ImmediatelyDisplay reemplaza al aviso actual sin encolar; click oculta.
    private readonly Dictionary<int, Wpf.Ui.Controls.SnackbarPresenter> _avisos = new();

    // Pase y Resolucion: fijo hasta la siguiente accion, asi queda a la vista el ultimo resultado
    private Task PaseMostrarToastAsync(string mensaje, bool esError) => MostrarAvisoAsync(PaseTabIndex, mensaje, esError, fijo: true);
    private Task ResolucionMostrarToastAsync(string mensaje, bool esError) => MostrarAvisoAsync(ResolucionTabIndex, mensaje, esError, fijo: true);

    // Busqueda y Recepcion: temporal (5 s)
    private Task BusquedaMostrarToastAsync(string mensaje, bool esError) => MostrarAvisoAsync(BusquedaTabIndex, mensaje, esError, fijo: false);
    private Task RecepcionMostrarToastAsync(string mensaje, bool esError) => MostrarAvisoAsync(RecepcionTabIndex, mensaje, esError, fijo: false);

    private Task MostrarAvisoAsync(int tab, string mensaje, bool esError, bool fijo)
    {
        var presenter = ObtenerAviso(tab);
        var aviso = new Wpf.Ui.Controls.Snackbar(presenter)
        {
            Title = fijo ? DateTime.Now.ToString("HH:mm") : null,
            MinHeight = 0, // sin hora no queda un espacio vacio
            Content = mensaje,
            Appearance = esError ? Wpf.Ui.Controls.ControlAppearance.Danger : Wpf.Ui.Controls.ControlAppearance.Success,
            Timeout = fijo ? Timeout.InfiniteTimeSpan : TimeSpan.FromSeconds(5),
            IsCloseButtonEnabled = false,
            Cursor = Cursors.Hand,
            ToolTip = "Click para ocultar",
            // Colores de la paleta encima de los de Appearance
            Background = (Brush)FindResource(esError ? "ErrorFondoBrush" : "ExitoFondoBrush"),
            Foreground = (Brush)FindResource(esError ? "ErrorTextoBrush" : "ExitoTextoBrush"),
            ContentForeground = (Brush)FindResource(esError ? "ErrorTextoBrush" : "ExitoTextoBrush"),
            BorderBrush = (Brush)FindResource(esError ? "ErrorBrush" : "ExitoBrush")
        };
        aviso.MouseLeftButtonUp += async (s, e) => await presenter.HideCurrent();

        // No se espera: con Timeout infinito la tarea no termina hasta que lo reemplace otro aviso
        _ = presenter.ImmediatelyDisplay(aviso);
        ActualizarAvisos();
        return Task.CompletedTask;
    }

    private Wpf.Ui.Controls.SnackbarPresenter ObtenerAviso(int tab)
    {
        if (_avisos.TryGetValue(tab, out var existente))
        {
            return existente;
        }

        var presenter = new Wpf.Ui.Controls.SnackbarPresenter
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 28, 28),
            MaxWidth = 400
        };
        RootGrid.Children.Add(presenter);
        _avisos[tab] = presenter;
        return presenter;
    }

    private void ActualizarAvisos()
    {
        foreach (var (tab, presenter) in _avisos)
        {
            presenter.Visibility = MainTabControl.SelectedIndex == tab ? Visibility.Visible : Visibility.Collapsed;
        }
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

        // Frecuentes primero (ver OficinasFrecuentes en appsettings.json)
        var filtradas = _oficinaCacheService.Filtrar(texto);

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
            await Mensajes.AvisoAsync("Nro de Expediente requerido.");
            return;
        }

        RecepNroExpedienteBox.Focus();

        try
        {
            _recepcionService.ValidateExp(RecepNroExpedienteBox.Text);
        }
        catch (ArgumentException ex)
        {
            await Mensajes.AvisoAsync(ex.Message);
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
            await Mensajes.ErrorAsync($"Error al buscar: {ex.Message}");
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
            await Mensajes.AvisoAsync("Expediente no encontrado para Recepcion.");
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
                await RecepcionMostrarToastAsync($"Expediente {nro} recibido con exito.", esError: false);
            }
            else
            {
                await RecepcionMostrarToastAsync($"No se pudo recibir el expediente {nro}.", esError: true);
            }
        }
        catch (Exception ex)
        {
            await RecepcionMostrarToastAsync($"Error al recibir {nro}: {ex.Message}", esError: true);
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
            await Mensajes.AvisoAsync("Ingrese una oficina.");
            return;
        }

        if (_oficinaSeleccionadaRecepcion is not OficinaOption oficinaSeleccionada)
        {
            await Mensajes.AvisoAsync("Seleccione una Oficina Valida");
            return;
        }

        RecepOficinaTextBox.Focus();

        RecepOficinaBuscandoProgressBar.Visibility = Visibility.Visible;
        setRecepButtonEnabled(false);

        try
        {
            var listado = await _recepcionService.BuscarPorOficinaAsync(oficina);
            _recepcionEncontrados = listado.Items;
            RecepOficinaGrid.ItemsSource = null;
            RecepOficinaGrid.ItemsSource = _recepcionEncontrados;

            if (listado.FilasFaltantes > 0)
            {
                await Mensajes.AvisoAsync($"{listado.FilasFaltantes} expedientes no terminaron de cargar y no aparecen en la lista. Vuelva a buscar.");
            }
            else if (_recepcionEncontrados.Count == 0)
            {
                await Mensajes.AvisoAsync("No se encontraron expedientes para esa oficina.");
            }
        }
        catch (Exception ex)
        {
            await Mensajes.ErrorAsync($"Error al buscar: {ex.Message}");
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
            await Mensajes.AvisoAsync("No hay expedientes seleccionados.");
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

            await RecepcionMostrarToastAsync(
                $"Recibidos: {result.Admitted.Count} de {nrosSeleccionados.Count}." +
                (result.NotFound.Count > 0 ? $" No encontrados: {result.NotFound.Count}." : "") +
                (result.Failed.Count > 0 ? $" Fallidos: {result.Failed.Count}." : ""),
                esError: result.NotFound.Count > 0 || result.Failed.Count > 0);
        }
        catch (Exception ex)
        {
            await Mensajes.ErrorAsync($"Error al recibir seleccionados: {ex.Message}");
            setRecepButtonEnabled(true);
        }
    }

    //Globales

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        NroExpedienteBox.Focus();
        FechaResolucionBox.SelectedDate = DateTime.Today;
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
            case Key.F5:
                MainTabControl.SelectedIndex = HistorialTabIndex;
                HistorialAccionCombo.Focus();
                e.Handled = true;
                break;
            case Key.Enter:
                // Solo se consume el Enter en pestañas que lo usan: en Historial queda para el control con foco
                if(!OficinaPopup.IsOpen && !ResolucionOficinaPopup.IsOpen && !RecepOficinaPopup.IsOpen
                    && MainTabControl.SelectedIndex != HistorialTabIndex){
                    HandleEnterShortcut();
                    e.Handled = true;
                }
                break;
        }
    }

    // Cada pestaña usa el Enter a su manera, segun la pestaña seleccionada
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

    private async void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // SelectionChanged tambien llega desde ComboBox/DataGrid internos: solo interesa el cambio de pestaña
        if (e.Source != MainTabControl || !IsLoaded)
        {
            return;
        }

        ActualizarAvisos();

        if (MainTabControl.SelectedIndex == HistorialTabIndex)
        {
            await CargarHistorialAsync();
        }
    }

    
}