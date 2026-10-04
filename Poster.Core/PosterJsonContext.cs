using System.Text.Json.Serialization;

namespace Poster.Core;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(Workspace))]
internal partial class PosterJsonContext : JsonSerializerContext
{
}
