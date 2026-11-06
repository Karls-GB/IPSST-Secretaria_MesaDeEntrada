using IPSST.Domain.Entities;
using IPSSTLoader.Domain.Entities;
using IPSSTLoader.Domain.Interface;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using System;
using System.Collections.Generic;
using System.Text;

namespace IPSSTLoader.Infrastructure.Automation;

public class PlaywrightRecepcion : IAutomationRecepcion
{
    private readonly PlaywrightSession _session;
    private readonly ILogger<PlaywrightRecepcion> _logger;

    private string RecepcionURL => $"{_session.BaseUrl}/expedientes/hrecepcion.{_session.ExtentionUrl}";

    private const string CampoNrc = "nRC_Gridrecep";
    private const string CampoFila = "EXPNOMBRENUEVO";

    public PlaywrightRecepcion(PlaywrightSession session, ILogger<PlaywrightRecepcion> logger)
    {
        _session = session;
        _logger = logger;
    }

    public async Task<RecepcionItem?> PrepararRecepcionAsync(string nroExpediente)
    {
        return await _session.RunAsync(async page =>
        {
            await BuscarExpedienteAsync(page, nroExpediente);

            var rowCount = await GeneXusGrid.LeerCantidadAsync(page, CampoNrc);

            if (rowCount == 0)
            {
                _logger.LogWarning("Expediente {NroExpediente} no encontrado en Recepcion", nroExpediente);
                return null;
            }

            await GeneXusGrid.EsperarFilasAsync(page, CampoNrc, CampoFila, _logger, "Gridrecep",
                recargar: () => BuscarExpedienteAsync(page, nroExpediente));

            if (await GeneXusGrid.LeerCampoAsync(page, CampoFila, 1) is null)
            {
                throw new InvalidOperationException("La tabla de Recepcion no termino de cargar. Intente nuevamente");
            }

            var asunto = await GeneXusGrid.LeerCampoAsync(page, "EXPASUNTO", 1);
            var causante = await GeneXusGrid.LeerCampoAsync(page, "EXPCAUSANTE", 1);
            var foliosRaw = await GeneXusGrid.LeerCampoAsync(page, "EXPPASESFOLIOS", 1);
            var oficinaOrigen = await GeneXusGrid.LeerCampoAsync(page, "OFICINAORIGENNOMBRE", 1);

            int.TryParse(foliosRaw?.Trim(), out var folios);

            return new RecepcionItem
            {
                NroExpediente = nroExpediente,
                Asunto = asunto ?? string.Empty,
                Causante = causante ?? string.Empty,
                Folios = folios,
                OficinaOrigen = oficinaOrigen ?? string.Empty
            };
        });
    }

    public async Task<bool> ConfirmarRecepcionAsync(string nroExpediente)
    {
        return await _session.RunAsync(async page =>
        {
            // Verificacion de seguridad por si la sesion cambio de pagina
            if (!page.Url.Contains("hrecepcion", StringComparison.OrdinalIgnoreCase))
            {
                await BuscarExpedienteAsync(page, nroExpediente);
                await GeneXusGrid.EsperarFilasAsync(page, CampoNrc, CampoFila, _logger, "Gridrecep");
            }

            await page.CheckAsync("input[name='_SEL_0001']");
            await page.ClickAsync("input[name='BTN_RECEPCION']");
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            if (!page.Url.Contains("hexprecepcionmultiple", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("No se pudo confirmar la recepcion de {NroExpediente}", nroExpediente);
                return false;
            }

            _logger.LogInformation("Expediente {NroExpediente} recibido con exito", nroExpediente);
            return true;
        });
    }

    public async Task<ListadoRecepcion> BuscarPorOficinaAsync(string oficina)
    {
        return await _session.RunAsync(async page =>
        {
            var listado = new ListadoRecepcion();

            await AbrirListadoOficinaAsync(page, oficina);
            int pagina = 1;

            while (true)
            {
                var paginaActual = pagina;
                await GeneXusGrid.EsperarFilasAsync(page, CampoNrc, CampoFila, _logger, $"Gridrecep pagina {paginaActual}",
                    recargar: () => AbrirListadoOficinaAsync(page, oficina, paginaActual));

                var rowCount = await GeneXusGrid.LeerCantidadAsync(page, CampoNrc);

                for (int i = 1; i <= rowCount; i++)
                {
                    var nro = await GeneXusGrid.LeerCampoAsync(page, CampoFila, i);

                    if (nro is null)
                    {
                        // La fila no cargo: se informa al usuario en lugar de cortar la lectura
                        listado.FilasFaltantes++;
                        continue;
                    }

                    var asunto = await GeneXusGrid.LeerCampoAsync(page, "EXPASUNTO", i);
                    var causante = await GeneXusGrid.LeerCampoAsync(page, "EXPCAUSANTE", i);
                    var foliosRaw = await GeneXusGrid.LeerCampoAsync(page, "EXPPASESFOLIOS", i);
                    var oficinaOrigen = await GeneXusGrid.LeerCampoAsync(page, "OFICINAORIGENNOMBRE", i);

                    int.TryParse(foliosRaw?.Trim(), out var folios);

                    listado.Items.Add(new RecepcionItem
                    {
                        NroExpediente = nro,
                        Asunto = asunto ?? string.Empty,
                        Causante = causante ?? string.Empty,
                        Folios = folios,
                        OficinaOrigen = oficinaOrigen ?? string.Empty
                    });
                }

                var eofRaw = await page.InputValueAsync("input[name='Gridrecep_nEOF']");
                if (eofRaw == "1")
                {
                    break;
                }

                await page.ClickAsync("#SIGUIENTE");
                await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
                pagina++;
            }

            if (listado.FilasFaltantes > 0)
            {
                _logger.LogWarning("Listado de Recepcion de {Oficina} incompleto: {Faltantes} filas no cargaron", oficina, listado.FilasFaltantes);
            }

            _logger.LogInformation("Se encontraron {Cantidad} expedientes para la oficina {Oficina}", listado.Items.Count, oficina);
            return listado;
        });
    }

    public async Task<BulkAdmitResult> AdmitBulkAsync(string oficina, List<string> nroExpedientes)
    {
        return await AdmitBulkInternalAsync(nroExpedientes, buscarPorExpediente: false, oficina: oficina);
    }

    private async Task<BulkAdmitResult> AdmitBulkInternalAsync(List<string> nroExpedientes, bool buscarPorExpediente, string oficina = "")
    {
        return await _session.RunAsync(async page =>
        {
            // Verificacion de seguridad por si la sesion cambio de pagina
            if (!page.Url.Contains("hrecepcion", StringComparison.OrdinalIgnoreCase))
            {
                await AbrirListadoOficinaAsync(page, oficina);
            }

            var result = new BulkAdmitResult();
            var pendientes = new List<string>(nroExpedientes);

            // Se recorre de atras hacia adelante: al admitir filas, las siguientes
            // se corren hacia arriba, y esto evita saltearse expedientes.
            await page.ClickAsync("#ULTIMO");
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            var currentPageRaw = await page.InputValueAsync("input[name='_CURRENTPAGE']");
            int.TryParse(currentPageRaw, out var currentPage);

            while (pendientes.Count > 0)
            {
                var paginaActual = currentPage;
                await GeneXusGrid.EsperarFilasAsync(page, CampoNrc, CampoFila, _logger, $"Gridrecep pagina {paginaActual}",
                    recargar: () => AbrirListadoOficinaAsync(page, oficina, Math.Max(paginaActual, 1)));

                var rowCount = await GeneXusGrid.LeerCantidadAsync(page, CampoNrc);

                var marcadosEnPagina = new List<string>();

                for (int i = 1; i <= rowCount; i++)
                {
                    // Si la fila no cargo, el expediente sigue pendiente y RecepcionService lo reintenta
                    var nro = await GeneXusGrid.LeerCampoAsync(page, CampoFila, i);

                    if (nro is not null && pendientes.Contains(nro))
                    {
                        var suffix = i.ToString("D4");
                        await page.CheckAsync($"input[name='_SEL_{suffix}']");
                        _logger.LogInformation("Expediente {NroExpediente} seleccionado para Recepcion", nro);
                        marcadosEnPagina.Add(nro);
                        pendientes.Remove(nro);
                    }
                }

                currentPage--;

                if (marcadosEnPagina.Count > 0)
                {
                    await page.ClickAsync("input[name='BTN_RECEPCION']");
                    await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

                    if (!page.Url.Contains("hexprecepcionmultiple", StringComparison.OrdinalIgnoreCase))
                    {
                        // Los de esta pagina y los que faltaban recorrer quedan como fallidos para reintentar
                        _logger.LogWarning("No se pudieron confirmar las Recepciones: {Expedientes}", string.Join(", ", marcadosEnPagina));
                        result.Failed.AddRange(marcadosEnPagina);
                        result.Failed.AddRange(pendientes);
                        return result;
                    }

                    result.Admitted.AddRange(marcadosEnPagina);
                }

                // Sin pendientes, o ya se recorrio la primera pagina
                if (pendientes.Count == 0 || currentPage < 1)
                {
                    break;
                }

                await AbrirListadoOficinaAsync(page, oficina, currentPage);
            }

            result.NotFound.AddRange(pendientes);

            if (result.NotFound.Count > 0)
            {
                _logger.LogWarning("Expedientes no encontrados en Recepcion: {Expedientes}", string.Join(", ", result.NotFound));
            }

            return result;
        });
    }

    private async Task BuscarExpedienteAsync(IPage page, string nroExpediente)
    {
        await page.GotoAsync(RecepcionURL);
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await page.FillAsync("input[name='_EXPNOMBRE']", nroExpediente);
        await page.ClickAsync("input[name='BTN_BUSCAR']");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    // Abre el listado de la oficina y avanza hasta la pagina indicada (1 = primera)
    private async Task AbrirListadoOficinaAsync(IPage page, string oficina, int pagina = 1)
    {
        await page.GotoAsync(RecepcionURL);
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await page.FillAsync("input[name='_OFINOMBRECOMPLETO']", oficina);
        await page.ClickAsync("input[name='BTN_BUSCAR']");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        for (int i = 1; i < pagina; i++)
        {
            await page.ClickAsync("#SIGUIENTE");
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        }
    }
}
