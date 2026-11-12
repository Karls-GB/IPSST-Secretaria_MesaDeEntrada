using IPSST.Domain.Entities;
using IPSST.Domain.Interface;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace IPSST.Infrastructure.Automation;

public class PlaywrightBusqueda : IAutomationBusqueda
{
    private readonly PlaywrightSession _session;
    private readonly ILogger<PlaywrightBusqueda> _logger;

    private string SearchUrl => $"{_session.BaseUrl}/expedientes/hviewbuscarexpte.{_session.ExtentionUrl}?Expedientes";
    private string PaseUrl => $"{_session.BaseUrl}/expedientes/hviewexppases.{_session.ExtentionUrl}?Pases";

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
            if (!int.TryParse(rowCountRaw?.Trim(), out var rowCount) || rowCount == 0)
            {
                _logger.LogInformation("No se encontraron resultados para el expediente: {NroExpediente}", nroExpediente);
                return null;
            }

            //TODO: Agregar funcionalidad para mas de un resultado.
            //Se hara cuando la opcion de ingresar dni o nombre este disponible

            await GeneXusGrid.EsperarFilasAsync(page, "W0007nRC_Gridexp", "W0007_EXPID", _logger, "Gridexp (Busqueda)");

            var expId = await GeneXusGrid.LeerCampoAsync(page, "W0007_EXPID", 1)
                ?? throw new InvalidOperationException("La tabla de resultados no termino de cargar. Intente nuevamente");
            var cuitCuil = await GeneXusGrid.LeerCampoAsync(page, "W0007_EXPCUITCUIL", 1);

            await page.ClickAsync("#W0007_DISPLAY_0001");
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            var nroExpedienteRaw = await page.InputValueAsync("input[name='EXPNOMBRENUEVO']");
            var fechaAltaRaw = await page.InputValueAsync("input[name='EXPFECHAALTA']");
            var folioRaw = await page.InputValueAsync("input[name='EXPFOLIO']");
            var motivoRaw = await page.InputValueAsync("input[name='EXPAREAAREA']");
            var asuntoRaw = await page.InputValueAsync("input[name='EXPASUNTO']");
            var causanteRaw = await page.InputValueAsync("input[name='EXPCAUSANTE']");
            var estadoRaw = $"{await page.InputValueAsync("input[name='EXPESTADODESCRIPCION']")} {await page.InputValueAsync("input[name='W0050_OFICINADESTINONOMBRECOMPLETO']")}";
            var oficinaRaw = await page.InputValueAsync("input[name='W0050_EXPOFINOMBRE']");
            var sucursalRaw = await page.InputValueAsync("input[name='W0050_EXPSUCURSALNOMBRE']");
            var fechaPaseRaw = await page.InputValueAsync("input[name='W0050_EXPPASESFECHAHORA']");
            var usuarioPaseRaw = await page.InputValueAsync("input[name='W0050_EXPPASESUSUID']");

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

            (result.Observaciones, result.ObservacionesFaltantes) = await ObtenerObservacionesAsync(page, expId);
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
        // Navega a la cola de Pases solo si no se esta ya en ella
        if (!page.Url.Contains($"hviewexppases.{_session.ExtentionUrl}", StringComparison.OrdinalIgnoreCase))
        {
            await page.GotoAsync(PaseUrl);
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        }

        await page.FillAsync("input[name='W0009_TEXTOBUSQUEDA']", nroExpediente);
        await page.ClickAsync("input[name='W0009BUTTON2']");

        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var rawCount = await GeneXusGrid.LeerCantidadAsync(page, "W0009nRC_Gridexppase");

        if (rawCount == 0)
        {
            return null;
        }

        await GeneXusGrid.EsperarFilasAsync(page, "W0009nRC_Gridexppase", "W0009EXPPASEUSUARIO", _logger, "Gridexppase (Trabajado por)");
        return await GeneXusGrid.LeerCampoAsync(page, "W0009EXPPASEUSUARIO", 1);
    }

    private async Task<(List<ObservacionItem> Observaciones, int Faltantes)> ObtenerObservacionesAsync(IPage page, string expId)
    {
        const string campoNrc = "W0050nRC_Gridobserv";
        const string campoFila = "W0050EXPOBSERVFECHA";

        var observaciones = new List<ObservacionItem>();
        int faltantes = 0;

        await page.ClickAsync("#W0047TAB_0002");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        int pagina = 1;

        while (true)
        {
            // Primera pagina: se recarga volviendo a abrir la solapa. Ultima pagina: volviendo a pedir #W0050ULTIMO
            var selectorRecarga = pagina == 1 ? "#W0047TAB_0002" : "#W0050ULTIMO";
            await GeneXusGrid.EsperarFilasAsync(page, campoNrc, campoFila, _logger, $"Gridobserv pagina {pagina}",
                recargar: async () =>
                {
                    await page.ClickAsync(selectorRecarga);
                    await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
                });

            var rowCount = await GeneXusGrid.LeerCantidadAsync(page, campoNrc);
            _logger.LogDebug("Leyendo página {Pagina} de observaciones. Cantidad de filas: {RowCount}", pagina, rowCount);

            for (int i = 1; i <= rowCount; i++)
            {
                var fecha = await GeneXusGrid.LeerCampoAsync(page, campoFila, i);

                if (fecha is null)
                {
                    // La fila no cargo: se cuenta y se sigue con las demas
                    faltantes++;
                    continue;
                }

                observaciones.Add(new ObservacionItem
                {
                    FechaHora = fecha,
                    Descripcion = await GeneXusGrid.LeerCampoAsync(page, "W0050EXPOBSERVACIONES", i) ?? string.Empty,
                    Usuario = await GeneXusGrid.LeerCampoAsync(page, "W0050EXPOBSERVUSUNOMBRE", i) ?? string.Empty,
                    Oficina = await GeneXusGrid.LeerCampoAsync(page, "W0050EXPOBSERVOFINOMBRECOMPLETO", i) ?? string.Empty
                });
            }

            var eofRaw = await page.InputValueAsync("input[name='W0050Gridobserv_nEOF']");
            if( eofRaw == "1")
            {
                break;
            }

            observaciones.Add(new ObservacionItem
            {
                FechaHora = "",
                Descripcion = "...",
                Usuario = "",
                Oficina = ""
            });

            await page.ClickAsync("#W0050ULTIMO");
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            pagina++;
        }

        return (observaciones, faltantes);
    }
}
