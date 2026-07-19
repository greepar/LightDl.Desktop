using System.Text.Json.Serialization;
using LightDl.UI.Models;

namespace LightDl.UI.Services;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
internal partial class AppSettingsJsonContext : JsonSerializerContext;
