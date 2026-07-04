
using System.Text.Json.Serialization;
using Tomlyn.Model;

namespace AssetPatchTool;

public class AssetPatch
{
    [JsonPropertyName("assetFile")]
    public required string AssetFile { get; set; }

    [JsonPropertyName("assetPathId")]
    public required long AssetPathId { get; set; }

    public uint? AssetType = 144;

    [JsonPropertyName("patch")]
    public TomlTable Patches { get; set; } = default!;
}