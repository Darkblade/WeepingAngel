using System;
using System.Threading.Tasks;
using Blink;

namespace DontBlink.Services;

public class BlinkAuthService
{
    private Blink.BlinkClient? _blinkClient;
    private bool _forceNewLogin;
    private readonly BlinkSessionStore _sessionStore;
    private BlinkSessionStore.Session? _session;
    private readonly Func<Blink.BlinkClient> _clientFactory;
    public BlinkAuthService() : this(new BlinkSessionStore(),()=>new Blink.BlinkClient()) { }
    internal BlinkAuthService(BlinkSessionStore store,Func<Blink.BlinkClient> factory) {_sessionStore=store;_clientFactory=factory;}
    private Blink.BlinkClient CreateClient()
    {
        _session??=_sessionStore.LoadOrCreate();
        var client=_clientFactory();client.HardwareId=_session.HardwareId;
        client.AuthenticationDiagnostic+=message=>Diagnostic?.Invoke(message);
        client.OnTokenRefreshed+=token=>{
            _session=_session! with{RefreshToken=token};
            try {_sessionStore.Save(_session);Diagnostic?.Invoke("Blink refresh token saved securely for the next launch.");}
            catch(Exception){Diagnostic?.Invoke("Could not save the Blink refresh token. This session works, but the next launch may require login.");}
        };
        return client;
    }
    internal async Task<bool> RestoreSessionAsync(string? email=null)
    {
        _session??=_sessionStore.LoadOrCreate();
        if(string.IsNullOrWhiteSpace(_session.RefreshToken))return false;
        if(!string.IsNullOrWhiteSpace(email)&&!string.Equals(email,_session.Email,StringComparison.OrdinalIgnoreCase))return false;
        _blinkClient=CreateClient();RequiresPin=false;IsAuthenticated=false;
        Diagnostic?.Invoke("Restoring saved Blink session using the refresh token; no SMS requested.");
        bool restored=await _blinkClient.TryLoginWithRefreshTokenAsync(_session.RefreshToken);
        if(!restored){_session=_session with{RefreshToken=null};_sessionStore.Save(_session);Diagnostic?.Invoke("Blink rejected the saved authorization. A fresh manual login is required.");return false;}
        IsAuthenticated=true;Diagnostic?.Invoke("Saved Blink session restored.");return true;
    }
    internal LoginCooldown Cooldown { get; } = new();
    public event Action<string>? Diagnostic;

    public bool IsAuthenticated { get; private set; }
    public bool RequiresPin { get; private set; }

    public void MarkSessionUnavailable() => IsAuthenticated = false;

    public void Reset()
    {
        _forceNewLogin=true;
        _blinkClient = null;
        IsAuthenticated = false;
        RequiresPin = false;
    }

    public async Task<bool> LoginAsync(string email, string password)
    {
        if(!_forceNewLogin && !RequiresPin && await RestoreSessionAsync(email))return true;
        _forceNewLogin=false;
        Cooldown.EnsureAllowed();
        try
        {
            _blinkClient = CreateClient();
            _session=_session! with{Email=email,RefreshToken=null};
            _sessionStore.Save(_session);
            IsAuthenticated = false;
            RequiresPin = false;

            var success = await _blinkClient.TryLoginAsync(email, password);
            if (!success) return false;

            // OAuth can complete without a PIN for an already trusted device.
            IsAuthenticated = !string.IsNullOrWhiteSpace(_blinkClient.RefreshToken);
            RequiresPin = !IsAuthenticated;
            if(IsAuthenticated) Cooldown.Succeeded();
            return true;
        }
        catch (Blink.Exceptions.BlinkLoginRateLimitException ex) { Cooldown.RateLimited(ex); Diagnostic?.Invoke($"Retry calculation: source={ex.TimingSource}; UTC deadline={ex.RetryAtUtc:O}; server timing supplied={ex.ServerSpecified}. A retry deadline does not identify the cause of the block."); Diagnostic?.Invoke(Cooldown.Diagnostic); throw; }
        catch (Blink.Exceptions.BlinkLoginCredentialsException) { Cooldown.Rejected(); throw; }
        catch (Exception ex)
        {
            throw new Exception($"Blink Login Failed: {ex.Message}", ex);
        }
    }

    public async Task<bool> VerifyPinAsync(string pin)
    {
        Cooldown.EnsureAllowed();
        if (_blinkClient == null)
            throw new InvalidOperationException("Session not started. Call LoginAsync first.");

        if (string.IsNullOrWhiteSpace(pin))
            throw new ArgumentException("PIN cannot be empty.");

        bool success;
        try { success = await _blinkClient.TryVerifyPinAsync(pin.Trim()); }
        catch(Blink.Exceptions.BlinkLoginRateLimitException ex) { Cooldown.RateLimited(ex); throw; }
        IsAuthenticated = success;

        if (IsAuthenticated)
            RequiresPin = false;

        if(IsAuthenticated) Cooldown.Succeeded();
        return IsAuthenticated;
    }

    public Blink.BlinkClient GetClient() =>
        (IsAuthenticated && _blinkClient != null)
            ? _blinkClient
            : throw new BlinkSessionUnavailableException();
}
