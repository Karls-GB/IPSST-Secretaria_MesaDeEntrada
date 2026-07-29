using IPSST.Application.Services;
using IPSST.Domain.Entities;
using IPSSTLoader.Domain.Entities;
using IPSSTLoader.Domain.Interface;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Controls;
using System.Windows.Input;

namespace IPSSTLoader.Infrastructure.Automation;

public class PlaywrightPase : IAutomationPase
{
    private readonly PlaywrightSession _session;
    private readonly ILogger<PlaywrightPase> _logger;

    private string PaseURL => $"{_session.BaseUrl}/expedientes/hviewexppases.{_session.ExtentionUrl}?Pases";
    private string PaseFormURL => $"{_session.BaseUrl}/expedientes/hviewexpedientespasesindividuales.{_session.ExtentionUrl}";

    public PlaywrightPase(PlaywrightSession session, ILogger<PlaywrightPase> logger)
    {
        _session = session;
        _logger = logger;
    }

    public async Task<List<OficinaOption>> GetOficinasDestinoAsync()
    {
        return await _session.RunAsync(async page =>
        {
            await page.GotoAsync(PaseURL);
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

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
            await page.GotoAsync(PaseURL);
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
            if(!page.Url.Contains("hviewexppases", StringComparison.OrdinalIgnoreCase))
            {
                await page.GotoAsync($"{PaseFormURL}?{expId},ExpedientesPases,0");
                await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            }

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
}
