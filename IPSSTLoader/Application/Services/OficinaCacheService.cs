using IPSST.Domain.Entities;
using IPSSTLoader.Domain.Interface;
using System;
using System.Collections.Generic;
using System.Text;

namespace IPSST.Application.Services;

public class OficinaCacheService
{
    private readonly IAutomationPase _automationPase;

    public List<OficinaOption> Oficinas { get; private set; } = new();

    public OficinaCacheService(IAutomationPase automationPase)
    {
        _automationPase = automationPase;
    }

    public async Task InitializeAsync()
    {
        Oficinas = await _automationPase.GetOficinasDestinoAsync();
    }
}
