using System.Text.Json.Serialization;

namespace SkyCareer.Localization;

[JsonSourceGenerationOptions(ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip, AllowTrailingCommas = true)]
[JsonSerializable(typeof(Dictionary<string, string>))]
internal sealed partial class LocalizationJson : JsonSerializerContext;
