using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using System;
using System.Collections.Generic;
using System.Text;

namespace IPSST.Infrastructure.Automation;

// GeneXus informa la cantidad de filas (nRC_<Grid>) antes de terminar de dibujarlas.
// Antes de leer una tabla hay que esperar a que las filas existan en la pagina.
internal static class GeneXusGrid
{
    private const int TimeoutFilasMs = 10000;

    // Lee el contador nRC de la tabla. Devuelve 0 si el campo no existe
    public static async Task<int> LeerCantidadAsync(IPage page, string campoNrc)
    {
        var locator = page.Locator($"input[name='{campoNrc}']");
        if (await locator.CountAsync() == 0)
        {
            return 0;
        }

        int.TryParse((await locator.InputValueAsync())?.Trim(), out var cantidad);
        return cantidad;
    }

    // Lee un campo de una fila sin esperar el timeout. Devuelve null si la fila no existe
    public static async Task<string?> LeerCampoAsync(IPage page, string campo, int fila)
    {
        var locator = page.Locator($"input[name='{campo}_{fila:D4}']");
        return await locator.CountAsync() > 0 ? await locator.InputValueAsync() : null;
    }

    // Espera a que la tabla tenga todas las filas que informa nRC.
    // Si no terminan de cargar, recarga una vez (si se indica como) y vuelve a esperar.
    // Devuelve true si estan todas las filas.
    public static async Task<bool> EsperarFilasAsync(
        IPage page, string campoNrc, string campoFila, ILogger logger, string tabla, Func<Task>? recargar = null)
    {
        if (await EsperarAsync(page, campoNrc, campoFila))
        {
            return true;
        }

        if (recargar != null)
        {
            logger.LogWarning("Tabla {Tabla}: las filas no terminaron de cargar, recargando", tabla);
            await recargar();

            if (await EsperarAsync(page, campoNrc, campoFila))
            {
                logger.LogInformation("Tabla {Tabla}: filas completas despues de recargar", tabla);
                return true;
            }
        }

        var esperadas = await LeerCantidadAsync(page, campoNrc);
        var presentes = await page.Locator($"input[name^='{campoFila}_']").CountAsync();
        logger.LogWarning("Tabla {Tabla}: nRC informa {Esperadas} filas pero se encontraron {Presentes}", tabla, esperadas, presentes);
        return false;
    }

    private static async Task<bool> EsperarAsync(IPage page, string campoNrc, string campoFila)
    {
        try
        {
            await page.WaitForFunctionAsync(@"([campoNrc, campoFila]) => {
                const nrc = document.getElementsByName(campoNrc)[0];
                if (!nrc) return false;
                const esperadas = parseInt(nrc.value.trim(), 10) || 0;
                return document.querySelectorAll(`input[name^='${campoFila}_']`).length >= esperadas;
            }", new object[] { campoNrc, campoFila }, new PageWaitForFunctionOptions { Timeout = TimeoutFilasMs, PollingInterval = 200 });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }
}
