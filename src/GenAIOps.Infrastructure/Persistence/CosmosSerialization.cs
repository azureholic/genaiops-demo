using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Azure.Cosmos;

namespace GenAIOps.Infrastructure.Persistence;

public sealed class SystemTextJsonCosmosSerializer : CosmosSerializer
{
    public static JsonSerializerOptions DefaultOptions { get; } = CreateOptions();

    private readonly JsonSerializerOptions options;

    public SystemTextJsonCosmosSerializer(JsonSerializerOptions? options = null)
    {
        this.options = options ?? DefaultOptions;
    }

    public override T FromStream<T>(Stream stream)
    {
        using (stream)
        {
            return JsonSerializer.Deserialize<T>(stream, options)
                ?? throw new JsonException($"Cosmos returned an empty {typeof(T).Name} document.");
        }
    }

    public override Stream ToStream<T>(T input)
    {
        MemoryStream stream = new();
        JsonSerializer.Serialize(stream, input, options);
        stream.Position = 0;
        return stream;
    }

    private static JsonSerializerOptions CreateOptions()
    {
        JsonSerializerOptions serializerOptions = new(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        serializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return serializerOptions;
    }
}
