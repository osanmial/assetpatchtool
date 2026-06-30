
using Tomlyn.Model;

namespace AssetPatchTool;

public class AssetPatch
{
    public required long AssetPathId { get; set; }

    public string AssetName { get; set; } = "";

    public uint AssetType = 144;

    public TomlTable Patches { get; set; } = default!;
}