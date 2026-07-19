using System.Text.Json.Serialization;
using LightDl.UI.Models;

namespace LightDl.UI.Services;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(List<DownloadTaskRecord>))]
internal partial class DownloadTaskJsonContext : JsonSerializerContext;
