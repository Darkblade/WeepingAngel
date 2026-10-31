using System.Text.Json;
using Blink.Exceptions;

namespace DontBlink.Services;

internal sealed class LoginCooldown
{
    private record State(DateTimeOffset Until, string Reason, int Failures, DateTimeOffset LastFailure);
    private State _state=new(default,"",0,default);
    private readonly string _path;
    private readonly Func<DateTimeOffset> _now;
    public LoginCooldown(string? path=null,Func<DateTimeOffset>? now=null)
    {
        _path=path??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"DontBlink","login-cooldown.json");
        _now=now??(()=>DateTimeOffset.UtcNow);
        try { if(File.Exists(_path))_state=JsonSerializer.Deserialize<State>(File.ReadAllText(_path))??_state; } catch(JsonException) { } catch(IOException) { }
    }
    public TimeSpan Remaining => _state.Until>_now()?_state.Until-_now():TimeSpan.Zero;
    public string Reason=>_state.Reason=="Blink supplied this retry time."?"Previously calculated server retry time; original response was not retained.":_state.Reason;
    public string Diagnostic=>$"Login cooldown: deadline UTC={_state.Until:O}; remaining={Display}; {Reason}";
    public void EnsureAllowed(){if(Remaining>TimeSpan.Zero)throw new InvalidOperationException("Login paused: "+Display);}
    public string Display
    {
        get
        {
            var remaining=TimeSpan.FromSeconds(Math.Ceiling(Remaining.TotalSeconds));
            return $"{remaining.Days:000}:{remaining.Hours:00}:{remaining.Minutes:00}:{remaining.Seconds:00}";
        }
    }
    public void RateLimited(BlinkLoginRateLimitException ex){Set(ex.RetryAtUtc,ex.ServerSpecified?"Calculated from server retry values (see debug log).":"10-minute precaution; Blink supplied no retry time.");}
    public void Rejected(){int count=_now()-_state.LastFailure<TimeSpan.FromMinutes(10)?_state.Failures+1:1;_state=_state with{Failures=count,LastFailure=_now()};if(count>=4)Set(_now().AddMinutes(10),"Paused after four rejected credential attempts to avoid Blink's five-attempt lockout.");else Save();}
    public void Succeeded(){_state=new(default,"",0,default);Save();}
    private void Set(DateTimeOffset until,string reason){if(until>_state.Until)_state=_state with{Until=until,Reason=reason};Save();}
    private void Save(){Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);File.WriteAllText(_path,JsonSerializer.Serialize(_state));}
}
