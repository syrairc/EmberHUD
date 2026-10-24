using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using EmberHUD;

namespace Tartarus;

sealed class DecisionLog(Action<string>? sink = null)
{
    static readonly JsonSerializerOptions Options = new() { Converters = { new JsonStringEnumConverter() }, IncludeFields = true };

    readonly Action<string> _sink = sink ?? Log.Info;

    public void Write(string kind, Func<object?> data)
    {
        try
        {
            _sink($"{kind} {JsonSerializer.Serialize(data(), Options)}");
        }
        catch (Exception error)
        {
            Log.Error(error, "tartarus decision log");
        }
    }
}
