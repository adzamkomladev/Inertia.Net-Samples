using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using TickerQ.Utilities.Entities;

namespace VuePgReplicasAdvBlog.Infrastructure;

// Reflection metadata retains TickerQ's JsonInclude properties with non-public setters.
internal sealed class CronJsonContext : JsonSerializerContext, IJsonTypeInfoResolver
{
    internal static CronJsonContext Default { get; } = new();
    readonly DefaultJsonTypeInfoResolver resolver = new();
    private CronJsonContext() : base(new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
    {
        resolver.Modifiers.Add(info =>
        {
            foreach (var property in info.Properties)
            {
                // Lua compares UpdatedAt to .NET's round-trip format byte for byte.
                if (property.PropertyType == typeof(DateTime)) property.CustomConverter = new RoundTripDateTimeConverter();
                // Redis Lua cjson turns an empty Children array into an empty object.
                if (property.PropertyType == typeof(ICollection<TimeTickerEntity>)) property.CustomConverter = new ChildrenConverter();
            }
        });
    }
    protected override JsonSerializerOptions? GeneratedSerializerOptions => null;
    public override JsonTypeInfo? GetTypeInfo(Type type) => resolver.GetTypeInfo(type, Options);
    JsonTypeInfo? IJsonTypeInfoResolver.GetTypeInfo(Type type, JsonSerializerOptions options) => resolver.GetTypeInfo(type, options);

    sealed class RoundTripDateTimeConverter : JsonConverter<DateTime>
    {
        public override DateTime Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) => reader.GetDateTime();
        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) => writer.WriteStringValue(value.ToString("O", CultureInfo.InvariantCulture));
    }

    sealed class ChildrenConverter : JsonConverter<ICollection<TimeTickerEntity>>
    {
        public override ICollection<TimeTickerEntity>? Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartObject) return JsonSerializer.Deserialize<List<TimeTickerEntity>>(ref reader, options);
            if (!reader.Read() || reader.TokenType != JsonTokenType.EndObject) throw new JsonException("TickerQ Children must be an array or an empty Lua object.");
            return [];
        }
        public override void Write(Utf8JsonWriter writer, ICollection<TimeTickerEntity> value, JsonSerializerOptions options) => JsonSerializer.Serialize(writer, value.ToArray(), options);
    }
}
