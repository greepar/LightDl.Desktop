using System.Text.Json.Serialization;
using LightDl.UI.Models;

namespace LightDl.UI.Services;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true)]
[JsonSerializable(typeof(NativeMessagingManifest))]
internal partial class NativeMessagingManifestJsonContext : JsonSerializerContext;
