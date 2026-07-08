using IPSSTLoader.Domain.Entities;
using IPSSTLoader.Domain.Interface;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace IPSSTLoader.Infrastructure.Automation;

public class PlaywrightBusqueda : IAutomationBusqueda
{
    private readonly PlaywrightSession _session;
    private readonly ILogger<PlaywrightBusqueda> _logger;

    private string SearchUrl => $"{_session.BaseUrl}/hviewbuscarexpte.aspx?Expedientes";
    private string PaseUrl => $"{_session.BaseUrl}/hviewexppases.aspx?Pases";

    public PlaywrightBusqueda(PlaywrightSession session, ILogger<PlaywrightBusqueda> logger)
    {
        _session = session;
        _logger = logger;
    }

    public async Task<ResultadoBusqueda?> SearchAsync(string nroExpediente)
    {
        return await _session.RunAsync(async page =>
        {
            _logger.LogDebug("Navegando para buscar expediente: {NroExpediente}", nroExpediente);

            await page.GotoAsync(SearchUrl);

            await page.FillAsync("input[name='W0007_TEXTOBUSQUEDA']", nroExpediente);
            await page.ClickAsync("input[name='W0007BUTTON1']");

            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            //Ver Cantidad de Resultados
            var rowCountRaw = await page.InputValueAsync("input[name='W0007nRC_Gridexp']");
            int rowCount = int.Parse(rowCountRaw);

            if (rowCount == 0)
            {
                _logger.LogInformation("No se encontraron resultados para el expediente: {NroExpediente}", nroExpediente);
                return null;
            }

            //TODO: Agregar funcionalidad para mas de un resultado.
            //Se hara cuando la opcion de ingresar dni o nombre este disponible

            var expId = await page.InputValueAsync("input[name='W0007_EXPID_0001']");
            var cuitCuil = await page.InputValueAsync("input[name='W0007_EXPCUITCUIL_0001']");

            await page.ClickAsync("#W0007_DISPLAY_0001");
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            var nroExpedienteRaw = await page.InputValueAsync("input[name='EXPNOMBRENUEVO']");
            var fechaAltaRaw = await page.InputValueAsync("input[name='EXPFECHAALTA']");
            var folioRaw = await page.InputValueAsync("input[name='EXPFOLIO']");
            var motivoRaw = await page.InputValueAsync("input[name='EXPAREAAREA']");
            var asuntoRaw = await page.InputValueAsync("input[name='EXPASUNTO']");
            var causanteRaw = await page.InputValueAsync("input[name='EXPCAUSANTE']");
            var estadoRaw = await page.InputValueAsync("input[name='EXPESTADODESCRIPCION']");
            var oficinaRaw = await page.InputValueAsync("input[name='W0050_EXPOFINOMBRE']");
            var sucursalRaw = await page.InputValueAsync("input[name='W0050_EXPSUCURSALNOMBRE']");
            var fechaPaseRaw = await page.InputValueAsync("input[name='W0050_EXPPASESFECHAHORA']");
            var usuarioPaseRaw = await page.InputValueAsync("#span_W0050_EXPPASESUSUID");

            DateTime? fechaAlta = null;
            if (DateTime.TryParseExact(fechaAltaRaw, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
            {
                fechaAlta = parsedDate;
            }

            int? folios = null;
            if(int.TryParse(folioRaw?.Trim(), out var parsedFolios))
            {
                folios = parsedFolios;
            }

            DateTime? fechaPase = null;
            if(DateTime.TryParseExact(fechaPaseRaw, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDatePase))
            {
                fechaPase = parsedDatePase;
            }

            var result = new ResultadoBusqueda
            {
                NroExpediente = nroExpedienteRaw,
                ExpedienteIdWeb = expId,
                FechaAlta = fechaAlta,
                Folios = folios,
                Motivo = motivoRaw,
                Asunto = asuntoRaw,
                Causante = causanteRaw,
                CuitCuil = cuitCuil,
                Estado = estadoRaw,
                Oficina = oficinaRaw,
                Sucursal = sucursalRaw,
                FechaPase = fechaPaseRaw,
                UsuarioPase = usuarioPaseRaw
            };

            //Logica para encontrar quien lo trabajo
            bool enSecretaria = oficinaRaw?.Contains("SECRETARIA", StringComparison.OrdinalIgnoreCase) == true;
            bool enTransito = estadoRaw?.Contains("EN TRANSITO", StringComparison.OrdinalIgnoreCase) == true;

            result.Observaciones = await ObtenerObservacionesAsync(page, expId);
            _logger.LogInformation("Busqueda las observaciones de {NroExpediente} completada: {CantidadObservaciones} observaciones encontradas", nroExpediente, result.Observaciones.Count);

            if (enSecretaria && !enTransito)
            {
                _logger.LogDebug("Buscando usuario asignado al expediente {NroExpediente} (Oficina: {Oficina}, Estado: {Estado})", nroExpediente, oficinaRaw, estadoRaw);
                result.TrabajadoPor = await ObtenerTrabajadoPorAsync(page, nroExpediente);
                _logger.LogDebug("Busqueda del usuario asignado al expediente {NroExpediente} completada: {TrabajadoPor}", nroExpediente, result.TrabajadoPor);
            }

            return result;
        });
    }

    private async Task<string?> ObtenerTrabajadoPorAsync(IPage page, string nroExpediente)
    {
        await page.GotoAsync(PaseUrl);

        await page.FillAsync("input[name='W0009_TEXTOBUSQUEDA']", nroExpediente);
        await page.ClickAsync("input[name='W0009BUTTON2']");

        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        return await page.InputValueAsync("input[name='W0009EXPPASEUSUARIO_0001']");
    }

    private async Task<List<ObservacionItem>> ObtenerObservacionesAsync(IPage page, string expId)
    {
        var observaciones = new List<ObservacionItem>();

        await page.ClickAsync("#W0047TAB_0002");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        int pagina = 1;

        while (true)
        {
            var rowCountRaw = await page.InputValueAsync("input[name='W0050nRC_Gridobserv']");
            int.TryParse(rowCountRaw, out var rowCount);

            _logger.LogDebug("Leyendo página {Pagina} de observaciones. Cantidad de filas: {RowCount}", pagina, rowCount);

            for (int i = 1; i <= rowCount; i++)
            {
                var suffix = i.ToString("D4");

                observaciones.Add(new ObservacionItem
                {
                    FechaHora = await page.InputValueAsync($"input[name='W0050EXPOBSERVFECHA_{suffix}']"),
                    Descripcion = await page.InputValueAsync($"input[name='W0050EXPOBSERVACIONES_{suffix}']"),
                    Usuario = await page.InputValueAsync($"input[name='W0050EXPOBSERVUSUNOMBRE_{suffix}']"),
                    Oficina = await page.InputValueAsync($"input[name='W0050EXPOBSERVOFINOMBRECOMPLETO_{suffix}']")
                });
            }

            var eofRaw = await page.InputValueAsync("input[name='W0050Gridobserv_nEOF']");
            if( eofRaw == "1")
            {
                break;
            }

            await page.ClickAsync("#W0050SIGUIENTE");
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            pagina++;
        }

        return observaciones;
    }
}
