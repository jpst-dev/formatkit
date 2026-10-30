using System.Text.Json.Serialization;

namespace FormatKit.App.Worker;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    WriteIndented = false)]
[JsonSerializable(typeof(WorkerMessage))]
internal sealed partial class MessageJsonContext : JsonSerializerContext;
