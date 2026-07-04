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
    public MainWindow(BusquedaService busquedaService, PaseWorkflow paseWorkflow, RecepcionService recepcionService, ResolucionWorkflow resolucionWorkflow)
    {
        InitializeComponent();
    }
}