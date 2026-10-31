using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Blink
{
    internal static class OAuthDiagnostics
    {
        internal static async Task<string> DescribeAsync(HttpResponseMessage response)
        {
            string Header(string name)=>response.Headers.TryGetValues(name,out var values)?string.Join(", ",values):"(absent)";
            string body=await response.Content.ReadAsStringAsync();
            string safeBody;
            try { using var json=JsonDocument.Parse(body);safeBody=Clean(json.RootElement); }
            catch(JsonException){safeBody=$"[non-JSON response omitted, {body.Length} characters]";}
            return $"HTTP {(int)response.StatusCode}; Date={Header("Date")}; Retry-After={Header("Retry-After")}; Content-Type={response.Content.Headers.ContentType}; body={safeBody}";
        }
        private static string Clean(JsonElement value)
        {
            if(value.ValueKind!=JsonValueKind.Object)return "[redacted non-object value]";
            return "{"+string.Join(", ",value.EnumerateObject().Take(40).Select(p=>JsonSerializer.Serialize(p.Name)+": "+Value(p.Name,p.Value)))+"}";
        }
        private static string Value(string key,JsonElement value)
        {
            if(value.ValueKind==JsonValueKind.Object)return Clean(value);
            bool allowed=new[]{"next_time_in_secs","retry_after","status","error","error_cause","error_description","message","tsv_state","expires_in"}.Contains(key);
            if(!allowed)return "\"[redacted]\"";
            string text=value.ToString();
            text=Regex.Replace(text,@"[\w.+-]+@[\w.-]+", "[redacted email]");
            text=Regex.Replace(text,@"(?i)(bearer\s+|(?:token|password|code|cookie|secret)\s*[:=]\s*)[^\s,;]+","$1[redacted]");
            text=Regex.Replace(text,@"[A-Za-z0-9_+/=-]{32,}","[redacted long value]");
            return JsonSerializer.Serialize(text.Length>1200?text.Substring(0,1200)+"…":text);
        }
    }
}
