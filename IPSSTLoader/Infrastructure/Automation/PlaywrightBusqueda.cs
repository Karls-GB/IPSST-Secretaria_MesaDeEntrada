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
            var encontrado = await BuscarYAbrirAsync(page, nroExpediente);
            if (encontrado is null)
            {
                return null;
            }

            var (expId, cuitCuil) = encontrado.Value;

            var nroExpedienteRaw = await page.InputValueAsync("input[name='EXPNOMBRENUEVO']");
            var fechaAltaRaw = await page.InputValueAsync("input[name='EXPFECHAALTA']");
            var folioRaw = await page.InputValueAsync("input[name='EXPFOLIO']");
            var motivoRaw = await page.InputValueAsync("input[name='EXPAREAAREA']");
            var asuntoRaw = await page.InputValueAsync("input[name='EXPASUNTO']");
            var causanteRaw = await page.InputValueAsync("input[name='EXPCAUSANTE']");
            var estadoRaw = (await page.InputValueAsync("input[name='EXPESTADODESCRIPCION']"))?.Trim();
            var oficinaDestinoRaw = (await page.InputValueAsync("input[name='W0050_OFICINADESTINONOMBRECOMPLETO']"))?.Trim();
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
                OficinaDestino = string.IsNullOrWhiteSpace(oficinaDestinoRaw) ? null : oficinaDestinoRaw,
                Sucursal = sucursalRaw,
                FechaPase = fechaPaseRaw,
                UsuarioPase = usuarioPaseRaw
            };

            // "Trabajado por" ya no se busca aca: el usuario lo pide desde la ventana (ObtenerTrabajadoPorAsync)
            await CargarObservacionesInicialesAsync(page, result);
            _logger.LogInformation("Busqueda las observaciones de {NroExpediente} completada: {CantidadObservaciones} observaciones en la primera pagina", nroExpediente, result.Observaciones.Count);

            return result;
        });
    }

    // Todas las paginas de observaciones, a pedido del usuario. Se vuelve a abrir el expediente
    // porque la pagina compartida pudo cambiar desde la busqueda (otra operacion o keep-alive)
    public async Task<(List<ObservacionItem> Observaciones, int Faltantes)> ObtenerTodasObservacionesAsync(string nroExpediente)
    {
        return await _session.RunAsync(async page =>
        {
            var todas = new List<ObservacionItem>();
            int faltantes = 0;

            if (await BuscarYAbrirAsync(page, nroExpediente) is null)
            {
                return (todas, faltantes);
            }

            await page.ClickAsync("#W0047TAB_0002");
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            // Tope de seguridad por si el indicador de fin de tabla nunca llega
            for (int pagina = 1; pagina <= 200; pagina++)
            {
                var (filas, faltantesPagina) = await LeerPaginaObservacionesAsync(page, pagina, pagina == 1 ? "#W0047TAB_0002" : null);
                todas.AddRange(filas);
                faltantes += faltantesPagina;

                if (filas.Count + faltantesPagina == 0 || await EsUltimaPaginaObservacionesAsync(page))
                {
                    break;
                }

                await page.ClickAsync("#W0050SIGUIENTE");
                await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            }

            _logger.LogInformation("Observaciones completas de {NroExpediente}: {Cantidad} ({Faltantes} sin cargar)", nroExpediente, todas.Count, faltantes);
            return (todas, faltantes);
        });
    }

    // "Trabajado por", a pedido del usuario
    public async Task<string?> ObtenerTrabajadoPorAsync(string nroExpediente)
    {
        return await _session.RunAsync(async page =>
        {
            _logger.LogDebug("Buscando usuario asignado al expediente {NroExpediente}", nroExpediente);
            var usuario = await ConsultarTrabajadoPorAsync(page, nroExpediente);
            _logger.LogInformation("Expediente {NroExpediente} trabajado por: {TrabajadoPor}", nroExpediente, usuario ?? "(sin asignar)");
            return usuario;
        });
    }

    // Busqueda rapida: solo el estado y la ubicacion, sin observaciones ni "trabajado por"
    public async Task<EstadoExpediente?> ConsultarEstadoAsync(string nroExpediente)
    {
        return await _session.RunAsync<EstadoExpediente?>(async page =>
        {
            if (await BuscarYAbrirAsync(page, nroExpediente) is null)
            {
                return null;
            }

            var estado = new EstadoExpediente
            {
                Estado = await LeerCampoAsync(page, "EXPESTADODESCRIPCION") ?? string.Empty,
                OficinaDestino = await LeerCampoAsync(page, "W0050_OFICINADESTINONOMBRECOMPLETO") ?? string.Empty,
                Oficina = await LeerCampoAsync(page, "W0050_EXPOFINOMBRE") ?? string.Empty
            };

            _logger.LogInformation("Estado de {NroExpediente}: {Descripcion}", nroExpediente, estado.Descripcion);
            return estado;
        });
    }

    // Busca el expediente y abre su pagina. Devuelve null si no hay resultados
    private async Task<(string ExpId, string? CuitCuil)?> BuscarYAbrirAsync(IPage page, string nroExpediente)
    {
        _logger.LogDebug("Navegando para buscar expediente: {NroExpediente}", nroExpediente);

        await page.GotoAsync(SearchUrl);

        await page.FillAsync("input[name='W0007_TEXTOBUSQUEDA']", nroExpediente);
        await page.ClickAsync("input[name='W0007BUTTON1']");

        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        //Ver Cantidad de Resultados
        var rowCount = await GeneXusGrid.LeerCantidadAsync(page, "W0007nRC_Gridexp");
        if (rowCount == 0)
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

        return (expId, cuitCuil);
    }

    // Lee un campo que no es de tabla sin esperar el timeout. Devuelve null si no existe
    private static async Task<string?> LeerCampoAsync(IPage page, string nombre)
    {
        var locator = page.Locator($"input[name='{nombre}']");
        return await locator.CountAsync() > 0 ? await locator.InputValueAsync() : null;
    }

    private async Task<string?> ConsultarTrabajadoPorAsync(IPage page, string nroExpediente)
    {
        // Navega a la cola de Pases solo si no se esta ya en ella
        // Se incluye "?Pases": la lista de Cancelacion usa la misma pagina con "?CancelPases"
        if (!page.Url.Contains($"hviewexppases.{_session.ExtentionUrl}?Pases", StringComparison.OrdinalIgnoreCase))
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

    private const string ObservNrc = "W0050nRC_Gridobserv";
    private const string ObservFila = "W0050EXPOBSERVFECHA";

    // La tabla va de la mas reciente a la mas antigua, 30 por pagina.
    // Se carga solo la primera pagina y, aparte, la primera observacion que se agrego
    // (ultima fila de la ultima pagina). El resto se pide desde la ventana
    private async Task CargarObservacionesInicialesAsync(IPage page, ResultadoBusqueda result)
    {
        await page.ClickAsync("#W0047TAB_0002");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        (result.Observaciones, result.ObservacionesFaltantes) = await LeerPaginaObservacionesAsync(page, 1, "#W0047TAB_0002");
        // Sin filas no hay paginas que recorrer, aunque falte el indicador de fin de tabla
        var filasPagina = result.Observaciones.Count + result.ObservacionesFaltantes;
        result.HayMasObservaciones = filasPagina > 0 && !await EsUltimaPaginaObservacionesAsync(page);

        if (!result.HayMasObservaciones)
        {
            result.PrimeraObservacion = result.Observaciones.LastOrDefault();
            return;
        }

        await page.ClickAsync("#W0050ULTIMO");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await GeneXusGrid.EsperarFilasAsync(page, ObservNrc, ObservFila, _logger, "Gridobserv ultima pagina",
            recargar: async () =>
            {
                await page.ClickAsync("#W0050ULTIMO");
                await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            });

        // Ultima fila de la ultima pagina; si justo esa no cargo, la anterior mas antigua
        for (int i = await GeneXusGrid.LeerCantidadAsync(page, ObservNrc); i >= 1 && result.PrimeraObservacion is null; i--)
        {
            result.PrimeraObservacion = await LeerObservacionAsync(page, i);
        }
    }

    // Lee una pagina de la tabla. Las filas que no cargaron se cuentan, no se cortan las demas
    private async Task<(List<ObservacionItem> Filas, int Faltantes)> LeerPaginaObservacionesAsync(IPage page, int pagina, string? selectorRecarga)
    {
        await GeneXusGrid.EsperarFilasAsync(page, ObservNrc, ObservFila, _logger, $"Gridobserv pagina {pagina}",
            recargar: selectorRecarga is null ? null : async () =>
            {
                await page.ClickAsync(selectorRecarga);
                await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            });

        var rowCount = await GeneXusGrid.LeerCantidadAsync(page, ObservNrc);
        _logger.LogDebug("Leyendo página {Pagina} de observaciones. Cantidad de filas: {RowCount}", pagina, rowCount);

        var filas = new List<ObservacionItem>();
        int faltantes = 0;

        for (int i = 1; i <= rowCount; i++)
        {
            var observacion = await LeerObservacionAsync(page, i);
            if (observacion is null)
            {
                faltantes++;
                continue;
            }

            filas.Add(observacion);
        }

        return (filas, faltantes);
    }

    private static async Task<ObservacionItem?> LeerObservacionAsync(IPage page, int fila)
    {
        var fecha = await GeneXusGrid.LeerCampoAsync(page, ObservFila, fila);
        if (fecha is null)
        {
            return null;
        }

        return new ObservacionItem
        {
            FechaHora = fecha,
            Descripcion = await GeneXusGrid.LeerCampoAsync(page, "W0050EXPOBSERVACIONES", fila) ?? string.Empty,
            Usuario = await GeneXusGrid.LeerCampoAsync(page, "W0050EXPOBSERVUSUNOMBRE", fila) ?? string.Empty,
            Oficina = await GeneXusGrid.LeerCampoAsync(page, "W0050EXPOBSERVOFINOMBRECOMPLETO", fila) ?? string.Empty
        };
    }

    private static async Task<bool> EsUltimaPaginaObservacionesAsync(IPage page) =>
        (await LeerCampoAsync(page, "W0050Gridobserv_nEOF"))?.Trim() == "1";
}
