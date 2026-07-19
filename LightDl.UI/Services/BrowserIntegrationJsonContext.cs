using System.Text.Json.Serialization;
using LightDl.UI.Models;

namespace LightDl.UI.Services;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(BrowserCaptureRequest))]
[JsonSerializable(typeof(BrowserCaptureResponse))]
public partial class BrowserIntegrationJsonContext : JsonSerializerContext;
