using IPSSTLoader.Application.Services;
using IPSSTLoader.Application.Workflows;
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

    public MainWindow(BusquedaService busquedaService, PaseWorkflow paseWorkflow, RecepcionService recepcionService, ResolucionWorkflow resolucionWorkflow)
    {
        InitializeComponent();

        _busquedaService = busquedaService;
        _recepcionService = recepcionService;
        _paseWorkflow = paseWorkflow;
        _resolucionWorkflow = resolucionWorkflow;
    }

    public async void BuscarButton_Click (object sender, RoutedEventArgs e)
    {
        string nroExpediente = NroExpedienteBox.Text;

        try
        {
            var result = await _busquedaService.SearchAsync(nroExpediente);

            if(result == null)
            {
                MessageBox.Show("Expediente no encontrado.");
                return;
            }

            //var resultWindow = new ResultadoBusquedaWindow(result);
            //resultWindow.Show();
        }
        catch(ArgumentException ex)
        {
            MessageBox.Show(ex.Message);
        }
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
        }
    }

    private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {

    }
}