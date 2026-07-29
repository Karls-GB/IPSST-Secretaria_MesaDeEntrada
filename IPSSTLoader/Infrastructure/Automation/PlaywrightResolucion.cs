using IPSST.Domain.Entities;
using IPSSTLoader.Domain.Entities;
using IPSSTLoader.Domain.Interface;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using System;
using System.Collections.Generic;
using System.Text;

namespace IPSSTLoader.Infrastructure.Automation;

public class PlaywrightResolucion : IAutomationResolucion
{    
    private readonly PlaywrightSession _session;
    private readonly ILogger<PlaywrightResolucion> _logger;

    private string ResURL => $"{_session.BaseUrl}/expedientes/hexpresolucion.{_session.ExtentionUrl}";

    public PlaywrightResolucion(PlaywrightSession session, ILogger<PlaywrightResolucion> logger)
    {
        _session = session;
        _logger = logger;
    }

    public async Task<ResPreparation?> PrepararResolucionAsync(string nroExpediente)
    {
        return await _session.RunAsync<ResPreparation?>(async page =>
        {
            await page.GotoAsync(ResURL);
            await page.FillAsync("input[name='_TEXTOBUSQUEDA']", nroExpediente);
            await page.ClickAsync("input[name='BUTTON1']");
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            var rowCountRaw = await page.InputValueAsync("input[name='nRC_Gridexp']");
            if (!int.TryParse(rowCountRaw, out var rowCount) || rowCount == 0)
            {
                _logger.LogWarning("Expediente {NroExpediente} no encontrado en la cola de Resoluciones", nroExpediente);
                return null;
            }

            await page.ClickAsync("#_DISPLAY_0001");
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            var expId = await page.InputValueAsync("input[name='W0035AV79ExpID_PARM']");
            var causante = await page.InputValueAsync("input[name='EXPCAUSANTE']");

            var folioActualRaw = await page.InputValueAsync("input[name='EXPFOLIO']");
            int.TryParse(folioActualRaw?.Trim(), out var folioActual);

            var resoluciones = new List<string?>();
            int i = 1;

            while (true)
            {
                var suffix = i.ToString("D4");
                var res = await page.InputValueAsync($"input[name='W0035EXPRESNRORESCOMPLETO_{suffix}']");

                if (!res.Contains($"/{DateTime.Now.Year.ToString("D4")}", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                resoluciones.Add(res);
                i++;
            }
            

            return new ResPreparation
            {
                Causante = causante,
                FolioActual = folioActual,
                ExpId = expId,
                ResolucionesAnteriores = resoluciones
            };
        });
    }

    public async Task<bool> ConfirmarResAsync(string nroResolucion, DateTime fechaResolucion, string observacionesRes)
    {
        return await _session.RunAsync(async page =>
        {
            await page.ClickAsync("input[name='BTN_RESOLUCION']");
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            await page.FillAsync("input[name='W0023_EXPRESNRORES']", nroResolucion);
            await page.FillAsync("input[name='W0023_EXPRESFECHA']", fechaResolucion.ToString("dd/MM/yyyy"));
            await page.FillAsync("textarea[name='W0023_EXPRESMOTIVO']", observacionesRes);

            await page.ClickAsync("input[name='W0023BTN_ACEPTAR']");
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            var resExitoso = page.Url.Contains($"hviewexpedientesresolucion.{_session.ExtentionUrl}", StringComparison.OrdinalIgnoreCase);
            if (resExitoso)
            {
                _logger.LogInformation("Resolucion cargada para {nroResolucion}", nroResolucion);
                return true;
            }
            else
            {
                _logger.LogError("No se pudo cargar la resolucion para {nroResolucion}", nroResolucion);
                return false;
            }
            
        });
    }
}
