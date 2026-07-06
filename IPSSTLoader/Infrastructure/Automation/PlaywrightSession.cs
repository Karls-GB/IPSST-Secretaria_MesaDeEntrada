using Microsoft.Playwright;
using System;
using System.Collections.Generic;
using System.Text;

namespace IPSSTLoader.Infrastructure.Automation;

public class PlaywrightSession : IAsyncDisposable
{
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private IPage? _page;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private DateTime _lastActivity = DateTime.UtcNow;
    private Timer? _keepAliveTimer;
    private string? _homeUrl;

    private readonly bool _headless;
    private readonly TimeSpan _keepAliveInterval = TimeSpan.FromMinutes(15);
    private readonly string _loginUrl = "http://webinterna.ipsst.local:8080/expedientes/hexplogin.aspx";

    public PlaywrightSession(bool headless)
    {
        _headless = headless;
    }

    public async Task InitializeAsync()
    {
        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = _headless,
            Channel = "chrome"
        });
        _page = await _browser.NewPageAsync();

        _keepAliveTimer = new Timer(async _ => await KeepAliveTickAsync(), null, _keepAliveInterval, _keepAliveInterval);
    }

    public async Task<bool> LoginAsync(string username, string password)
    {
        if (_page == null)
        {
            throw new InvalidOperationException("Sesion no Inicializada");
        }

        await _lock.WaitAsync();
        try
        {
            await _page.GotoAsync(_loginUrl);

            await _page.FillAsync("input[name='_USUUSUARIO']", username);
            await _page.FillAsync("input[name='_USUCLAVE']", password);
            await _page.ClickAsync("input[name='BUTTON2']");

            await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            bool loggedIn = !_page.Url.Contains("Login");

            if (loggedIn)
            {
                _homeUrl = _page.Url;
            }

            _lastActivity = DateTime.UtcNow;
            return loggedIn;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<T> RunAsync<T>(Func<IPage, Task<T>> action)
    {
        if (_page == null)
        {
            throw new InvalidOperationException("Sesion no Inicializada");
        }

        await _lock.WaitAsync();
        try
        {
            var result = await action(_page);
            _lastActivity = DateTime.UtcNow;
            return result;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task KeepAliveTickAsync()
    {
        if(!await _lock.WaitAsync(0))
        {
            return;
        }

        try
        {
            if(_page == null || _homeUrl == null)
            {
                return;
            }

            var idleTimer = DateTime.UtcNow - _lastActivity;
            if (idleTimer < _keepAliveInterval)
            {
                return;
            }

            await _page.GotoAsync(_homeUrl);
            _lastActivity = DateTime.UtcNow;
        }
        catch
        {

        }
        finally
        {
            _lock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _keepAliveTimer?.Dispose();

        if(_browser != null)
        {
            await _browser.CloseAsync();
        }

        _playwright?.Dispose();
    }
}
