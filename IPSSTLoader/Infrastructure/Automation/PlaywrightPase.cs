using IPSST.Application.Services;
using IPSST.Domain.Entities;
using IPSST.Domain.Interface;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Controls;
using System.Windows.Input;

namespace IPSST.Infrastructure.Automation;

public class PlaywrightPase : IAutomationPase
{
    private readonly PlaywrightSession _session;
    private readonly ILogger<PlaywrightPase> _logger;

    private string PaseURL => $"{_session.BaseUrl}/expedientes/hviewexppases.{_session.ExtentionUrl}?Pases";
    private string PaseFormURL => $"{_session.BaseUrl}/expedientes/hviewexpedientespasesindividuales.{_session.ExtentionUrl}";
    // Misma pagina que la cola de Pases, con otro parametro
    private string CancelPaseURL => $"{_session.BaseUrl}/expedientes/hviewexppases.{_session.ExtentionUrl}?CancelPases";
    private string CancelPaseFormURL => $"{_session.BaseUrl}/expedientes/hviewexpedientescancelapasesindividuales.{_session.ExtentionUrl}";

    private const string CancelNrc = "W0009nRC_Gridexpcancelpase";
    private const string CancelFila = "W0009EXPNOMBRENUEVO";

    public PlaywrightPase(PlaywrightSession session, ILogger<PlaywrightPase> logger)
    {
        _session = session;
        _logger = logger;
    }

    // Navega a la cola de Pases solo si no se esta ya en ella
    private async Task IrAColaDePasesAsync(IPage page)
    {
        // Se incluye "?Pases": la lista de Cancelacion usa la misma pagina con "?CancelPases"
        if (page.Url.Contains($"hviewexppases.{_session.ExtentionUrl}?Pases", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug("Ya en la cola de Pases, se omite la navegacion");
            return;
        }

        _logger.LogDebug("Navegando a la cola de Pases");
        await page.GotoAsync(PaseURL);
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    public async Task<List<OficinaOption>> GetOficinasDestinoAsync()
    {
        return await _session.RunAsync(async page =>
        {
            await IrAColaDePasesAsync(page);

            var rowCountRaw = await page.InputValueAsync("input[name='W0009nRC_Gridexppase']");
            int.TryParse(rowCountRaw, out var rowCount);

            if(rowCount == 0)
            {
                _logger.LogWarning("No hay expedientes en la cola de Pases; no se pudo leer la lista de oficinas");
                return new List<OficinaOption>();
            }

            await page.ClickAsync("#W0009_PASE_0001");
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            var options = await page.QuerySelectorAllAsync("select[name='W0026_OFIIDDESTINO'] option");
            var oficinas = new List<OficinaOption>();

            foreach (var option in options)
            {
                var value = await option.GetAttributeAsync("value");
                var text = await option.InnerTextAsync();

                if (!string.IsNullOrEmpty(value) && value != "0")
                {
                    oficinas.Add(new OficinaOption { Id = value, Nombre = text.Trim() });
                }
            }

            await page.ClickAsync("input[name='W0026BUTTON2']");
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            _logger.LogInformation("Se obtuvieron {Cantidad} oficinas de destino", oficinas.Count);
            return oficinas;
        });
    }

    public async Task<PasePreparation?> PrepararPaseAsync(string nroExpediente)
    {
        return await _session.RunAsync<PasePreparation?>(async page =>
        {
            await IrAColaDePasesAsync(page);
            await page.FillAsync("input[name='W0009_TEXTOBUSQUEDA']", nroExpediente);
            await page.ClickAsync("input[name='W0009BUTTON2']");
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            var rowCountRaw = await page.InputValueAsync("input[name='W0009nRC_Gridexppase']");
            if (!int.TryParse(rowCountRaw, out var rowCount) || rowCount == 0)
            {
                _logger.LogWarning("Expediente {NroExpediente} no encontrado en la cola de Pases", nroExpediente);
                return null;
            }

            await page.ClickAsync("#W0009_PASE_0001");
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            var expId = await page.InputValueAsync("input[name='W0026A916ExpID_PARM']");
            var causante = await page.InputValueAsync("input[name='EXPCAUSANTE']");

            var folioActualRaw = await page.InputValueAsync("input[name='W0026_EXPPASESFOLIOS']");
            int.TryParse(folioActualRaw?.Trim(), out var folioActual);

            return new PasePreparation
            {
                Causante = causante,
                FolioActual = folioActual,
                ExpId = expId
            };
        });
    }

    public async Task<bool> ConfirmarPaseAsync(string oficinaDestino, int foliosTotal, string observaciones, string expId)
    {
        return await _session.RunAsync(async page =>
        {
            // Siempre se abre el formulario del expediente: antes de esto la pagina puede estar
            // en el formulario, en la lista de Cancelacion o en otra (reintento o keep-alive)
            await page.GotoAsync($"{PaseFormURL}?{expId},ExpedientesPases,0");
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            await page.SelectOptionAsync("select[name='W0026_OFIIDDESTINO']", new SelectOptionValue { Label = oficinaDestino });
            await page.FillAsync("input[name='W0026_EXPPASESFOLIOS']", foliosTotal.ToString());
            await page.FillAsync("textarea[name='W0026_EXPPASESOBSERVACIONES']", observaciones ?? string.Empty);
            
            await page.ClickAsync("input[name='W0026BTN_PASE']");
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            
            var paseExitoso = page.Url.Contains($"hviewexppases.{_session.ExtentionUrl}", StringComparison.OrdinalIgnoreCase);
            if (paseExitoso)
            {
                _logger.LogInformation("Pase confirmado para {OficinaDestino}", oficinaDestino);
                return true;
            }
           
            var errorMessage = await page.InputValueAsync("input[name='_RAZONNOPUEDE']");
            _logger.LogError("Pase rechazado para {OficinaDestino}: {Error}", oficinaDestino, errorMessage);
            return false;
        });
    }

    public async Task<PasePendiente?> PrepararCancelarPaseAsync(string nroExpediente)
    {
        return await _session.RunAsync<PasePendiente?>(async page =>
        {
            var fila = await BuscarEnCancelacionAsync(page, nroExpediente);
            if (fila == 0)
            {
                _logger.LogInformation("Expediente {NroExpediente} sin pase pendiente para cancelar", nroExpediente);
                return null;
            }

            int.TryParse((await GeneXusGrid.LeerCampoAsync(page, "W0009EXPPASESFOLIOS", fila))?.Trim(), out var foliosPase);

            var pendiente = new PasePendiente
            {
                NroExpediente = await GeneXusGrid.LeerCampoAsync(page, CancelFila, fila) ?? nroExpediente,
                ExpId = await GeneXusGrid.LeerCampoAsync(page, "W0009EXPID", fila) ?? string.Empty,
                OficinaDestino = (await GeneXusGrid.LeerCampoAsync(page, "W0009OFICINADESTINONOMBRECOMPLETO", fila))?.Trim() ?? string.Empty,
                Causante = await GeneXusGrid.LeerCampoAsync(page, "W0009EXPCAUSANTE", fila),
                FoliosPase = foliosPase
            };

            // Se abre la pagina de cancelacion: ahi estan los folios del expediente sin el pase pendiente
            await page.ClickAsync($"#W0009_CANCELAPASE_{fila:D4}");
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            int.TryParse((await LeerCampoAsync(page, "EXPFOLIO"))?.Trim(), out var foliosExpediente);
            pendiente.FoliosExpediente = foliosExpediente;

            _logger.LogInformation("Expediente {NroExpediente} con pase pendiente hacia {OficinaDestino} ({FoliosPase} folios)",
                pendiente.NroExpediente, pendiente.OficinaDestino, pendiente.FoliosPase);
            return pendiente;
        });
    }

    public async Task<bool> ConfirmarCancelarPaseAsync(string expId)
    {
        return await _session.RunAsync(async page =>
        {
            // Verificacion de seguridad por si la sesion cambio de pagina (reintento o keep-alive)
            if (await page.Locator("input[name='W0027BTN_CANCELARPASE']").CountAsync() == 0)
            {
                _logger.LogDebug("Abriendo la pagina de cancelacion del expediente {ExpId}", expId);
                await page.GotoAsync($"{CancelPaseFormURL}?{expId},ExpedientesCancelarPases,0");
                await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            }

            // El boton pide confirm(); lo acepta el manejador global de dialogos
            await page.ClickAsync("input[name='W0027BTN_CANCELARPASE']");
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            // El resultado se muestra en rojo en _RAZONNOPUEDE, en la misma pagina
            var mensaje = (await LeerCampoAsync(page, "_RAZONNOPUEDE"))?.Trim() ?? string.Empty;

            if (mensaje.Contains("cancel", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("Pase cancelado (ExpId {ExpId}): {Mensaje}", expId, mensaje);
                return true;
            }

            if (!string.IsNullOrWhiteSpace(mensaje))
            {
                _logger.LogWarning("Cancelacion de pase rechazada (ExpId {ExpId}): {Mensaje}", expId, mensaje);
            }
            else
            {
                _logger.LogWarning("No se pudo confirmar la cancelacion del pase (ExpId {ExpId}): sin mensaje", expId);
            }

            return false;
        });
    }

    // Busca el expediente en la lista de Cancelacion de Pases. Devuelve el numero de fila, o 0 si no esta
    private async Task<int> BuscarEnCancelacionAsync(IPage page, string nroExpediente)
    {
        if (!page.Url.Contains($"hviewexppases.{_session.ExtentionUrl}?CancelPases", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug("Navegando a la lista de Cancelacion de Pases");
            await page.GotoAsync(CancelPaseURL);
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        }

        await page.FillAsync("input[name='W0009_TEXTOBUSQUEDA']", nroExpediente);
        await page.ClickAsync("input[name='W0009BUTTON1']");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var rowCount = await GeneXusGrid.LeerCantidadAsync(page, CancelNrc);
        if (rowCount == 0)
        {
            return 0;
        }

        await GeneXusGrid.EsperarFilasAsync(page, CancelNrc, CancelFila, _logger, "Gridexpcancelpase");

        // La busqueda puede traer otros expedientes parecidos: se busca el numero exacto (con o sin letra)
        for (int i = 1; i <= rowCount; i++)
        {
            var nroNuevo = (await GeneXusGrid.LeerCampoAsync(page, CancelFila, i))?.Trim();
            var nro = (await GeneXusGrid.LeerCampoAsync(page, "W0009EXPNOMBRE", i))?.Trim();

            if (string.Equals(nroNuevo, nroExpediente, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(nro, nroExpediente, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return 0;
    }

    // Lee un campo que no es de tabla sin esperar el timeout. Devuelve null si no existe
    private static async Task<string?> LeerCampoAsync(IPage page, string nombre)
    {
        var locator = page.Locator($"input[name='{nombre}']");
        return await locator.CountAsync() > 0 ? await locator.InputValueAsync() : null;
    }
}
