using System.Text.Json;
using System.Text.Json.Serialization;

namespace OrderProcessing.Shared.Storage;

public static class JsonDefaults
{
    // Shared settings keep the JSON format consistent across all files.
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };
}
