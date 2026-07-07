using IPSSTLoader.Domain.Entities;
using IPSSTLoader.Domain.Interface;
using System.Threading.Tasks;

namespace IPSSTLoader.Infrastructure.Automation;

public class PlaywrightPase : IAutomationPase
{
    // Minimal implementation to satisfy DI. Replace with real Playwright logic later.
    public PlaywrightPase()
    {
    }

    public Task<bool> SubmitAsync(Expediente expediente)
    {
        // TODO: implement actual automation using PlaywrightSession
        return Task.FromResult(false);
    }
}
