using System.Text.Json.Serialization;
using Zoomies.Core.Frecency;
using Zoomies.Core.History;
using Zoomies.Core.Settings;
using Zoomies.Core.Workspaces;

namespace Zoomies.Core.Storage;

/// <summary>Compile-time JSON serializers: no reflection at startup, which keeps launch fast and memory low.</summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(List<JournalEntry>))]
[JsonSerializable(typeof(Dictionary<string, FrecencyEntry>))]
[JsonSerializable(typeof(List<Workspace>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
public sealed partial class CoreJsonContext : JsonSerializerContext;
