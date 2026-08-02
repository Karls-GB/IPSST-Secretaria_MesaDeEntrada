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

    public PlaywrightRecepcion(PlaywrightSession session, ILogger<PlaywrightRecepcion> logger)
    {
        _session = session;
        _logger = logger;
    }

    public async Task<RecepcionItem?> PrepararRecepcionAsync(string nroExpediente)
    {
        return await _session.RunAsync(async page =>
        {
            await page.GotoAsync(RecepcionURL);
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            await page.FillAsync("input[name='_EXPNOMBRE']", nroExpediente);
            await page.ClickAsync("input[name='BTN_BUSCAR']");
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            var rowCountRaw = await page.InputValueAsync("input[name='nRC_Gridrecep']");
            int.TryParse(rowCountRaw, out var rowCount);

            if (rowCount == 0)
            {
                _logger.LogWarning("Expediente {NroExpediente} no encontrado en Recepcion", nroExpediente);
                return null;
            }

            var asunto = await page.InputValueAsync("input[name='EXPASUNTO_0001']");
            var causante = await page.InputValueAsync("input[name='EXPCAUSANTE_0001']");
            var foliosRaw = await page.InputValueAsync("input[name='EXPPASESFOLIOS_0001']");
            var oficinaOrigen = await page.InputValueAsync("input[name='OFICINAORIGENNOMBRE_0001']");

            int.TryParse(foliosRaw?.Trim(), out var folios);

            return new RecepcionItem
            {
                NroExpediente = nroExpediente,
                Asunto = asunto,
                Causante = causante,
                Folios = folios,
                OficinaOrigen = oficinaOrigen
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
                await page.GotoAsync(RecepcionURL);
                await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
                await page.FillAsync("input[name='_EXPNOMBRE']", nroExpediente);
                await page.ClickAsync("input[name='BTN_BUSCAR']");
                await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            }

            await page.CheckAsync("input[name='_SEL_0001']");
            await page.ClickAsync("input[name='BTN_RECEPCION']");
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            if (!page.Url.Contains("hexprecepcionmultiple", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("No se pudo confirmar la recepcion de {NroExpediente}", nroExpediente);
            }

            _logger.LogInformation("Expediente {NroExpediente} recibido con exito", nroExpediente);
            return true;
        });
    }

    public async Task<List<RecepcionItem>> BuscarPorOficinaAsync(string oficina)
    {
        return await _session.RunAsync(async page =>
        {
            var items = new List<RecepcionItem>();

            await page.GotoAsync(RecepcionURL);
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            await page.FillAsync("input[name='_OFINOMBRECOMPLETO']", oficina);
            await page.ClickAsync("input[name='BTN_BUSCAR']");
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            while (true)
            {
                var rowCountRaw = await page.InputValueAsync("input[name='nRC_Gridrecep']");
                int.TryParse(rowCountRaw, out var rowCount);

                for (int i = 1; i <= rowCount; i++)
                {
                    var suffix = i.ToString("D4");

                    var nro = await page.InputValueAsync($"input[name='EXPNOMBRENUEVO_{suffix}']");
                    var asunto = await page.InputValueAsync($"input[name='EXPASUNTO_{suffix}']");
                    var causante = await page.InputValueAsync($"input[name='EXPCAUSANTE_{suffix}']");
                    var foliosRaw = await page.InputValueAsync($"input[name='EXPPASESFOLIOS_{suffix}']");
                    var oficinaOrigen = await page.InputValueAsync($"input[name='OFICINAORIGENNOMBRE_{suffix}']");

                    int.TryParse(foliosRaw?.Trim(), out var folios);

                    items.Add(new RecepcionItem
                    {
                        NroExpediente = nro,
                        Asunto = asunto,
                        Causante = causante,
                        Folios = folios,
                        OficinaOrigen = oficinaOrigen
                    });
                }

                var eofRaw = await page.InputValueAsync("input[name='Gridrecep_nEOF']");
                if (eofRaw == "1")
                {
                    break;
                }

                await page.ClickAsync("#SIGUIENTE");
                await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            }

            _logger.LogInformation("Se encontraron {Cantidad} expedientes para la oficina {Oficina}", items.Count, oficina);
            return items;
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
                await page.GotoAsync(RecepcionURL);
                await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
                await page.FillAsync("input[name='_OFINOMBRECOMPLETO']", oficina);
                await page.ClickAsync("input[name='BTN_BUSCAR']");
                await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
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
                var rowCountRaw = await page.InputValueAsync("input[name='nRC_Gridrecep']");
                int.TryParse(rowCountRaw, out var rowCount);

                var marcoAlgunaFila = false;

                for (int i = 1; i <= rowCount; i++)
                {
                    var suffix = i.ToString("D4");
                    var nro = await page.InputValueAsync($"input[name='EXPNOMBRENUEVO_{suffix}']");

                    if (pendientes.Contains(nro))
                    {
                        await page.CheckAsync($"input[name='_SEL_{suffix}']");
                        _logger.LogInformation("Expediente {NroExpediente} seleccionado para Recepcion", nro);
                        result.Admitted.Add(nro);
                        pendientes.Remove(nro);
                        marcoAlgunaFila = true;
                    }
                }

                currentPage--;

                if (marcoAlgunaFila)
                {
                    await page.ClickAsync("input[name='BTN_RECEPCION']");
                    await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

                    if (!page.Url.Contains("hexprecepcionmultiple", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogWarning("No se pudieron confirmar las Recepciones");
                        return result = new BulkAdmitResult();
                    }

                }

                if (pendientes.Count == 0)
                {
                    break;
                }

                await page.GotoAsync(RecepcionURL);
                await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
                await page.FillAsync("input[name='_OFINOMBRECOMPLETO']", oficina);
                await page.ClickAsync("input[name='BTN_BUSCAR']");
                await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

                try
                {
                    for (int i = 0; i < currentPage - 1; i++)
                    {
                        await page.ClickAsync("#SIGUIENTE", new PageClickOptions { Timeout = 2000 });
                        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
                    }
                }
                catch (TimeoutException)
                {
                    break; // Ya estamos en la primera pagina; no hay mas para recorrer
                }
            }

            result.NotFound.AddRange(pendientes);

            if (result.NotFound.Count > 0)
            {
                _logger.LogWarning("Expedientes no encontrados en Recepcion: {Expedientes}", string.Join(", ", result.NotFound));
            }

            return result;
        });
    }
}
