using IPSST.Application.Services;
using IPSST.Domain.Entities;
using IPSSTLoader.Domain.Entities;
using IPSSTLoader.Domain.Interface;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Input;

namespace IPSSTLoader.Infrastructure.Automation;

public class PlaywrightPase : IAutomationPase
{
    private readonly PlaywrightSession _session;
    private readonly ILogger<PlaywrightPase> _logger;

    private string PaseURL => $"{_session.BaseUrl}/expedientes/hviewexppases.aspx?Pases";

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

    public async Task<bool> SubmitAsync(Expediente expediente)
    {
        return await _session.RunAsync(async page =>
        {
            await page.GotoAsync(PaseURL);
            await page.FillAsync("input[name='W0009_TEXTOBUSQUEDA']", expediente.NroExpediente);
            await page.ClickAsync("input[name='W0009BUTTON2']");
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            var rowCountRaw = await page.InputValueAsync("input[name='W0009nRC_Gridexppase']");
            if(!int.TryParse(rowCountRaw, out var rowCount) || rowCount == 0)
            {
                _logger.LogWarning("Expediente {NroExpediente} no encontrado en la cola de Pases", expediente.NroExpediente);
                return false;
            }

            await page.ClickAsync("#W0009_PASE_0001");
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            await page.SelectOptionAsync("select[name='W0026_OFIIDDESTINO']", new SelectOptionValue { Label = expediente.Pase!.OficinaDestino });

            await page.FillAsync("input[name='W0026_EXPPASESFOLIOS']", expediente.Pase.Folios.ToString());

            await page.FillAsync("textarea[name='W0026_EXPPASESOBSERVACIONES']", expediente.Pase.Observaciones ?? string.Empty);

            await page.ClickAsync("input[name='W0026BTN_PASE']");
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            var paseExitoso = page.Url.Contains("hviewexppases.aspx", StringComparison.OrdinalIgnoreCase);

            if (paseExitoso)
            {
                _logger.LogInformation("Pase completado para {NroExpediente}", expediente.NroExpediente);
                return true;
            }

            var errorMessage = await page.InputValueAsync("input[name='_RAZONNOPUEDE']");
            _logger.LogError("Pase rechazado para {NroExpediente}: {Error}", expediente.NroExpediente, errorMessage);
            return false;
        });
    }
}
