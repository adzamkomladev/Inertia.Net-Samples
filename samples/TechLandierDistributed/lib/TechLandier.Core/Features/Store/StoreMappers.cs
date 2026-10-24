using System.Text.Json;

namespace TechLandier.Features;

public static class StoreMappers
{
    public static List<SpecRow> ParseSpecs(string json)
    {
        try { return JsonSerializer.Deserialize<List<SpecRow>>(json, JsonSerializerOptions.Web) ?? []; }
        catch (JsonException) { return []; }
    }

}
