using System.Text.Json.Serialization;

namespace AssetPatchTool;

public class Config
{
    public class PatchConfig
    {
        [JsonPropertyName("path")]
        public required string FilePath { get; set; }

        [JsonPropertyName("enabled")]
        public required bool Enabled { get; set; }
    }

    [JsonPropertyName("game_asset_path")]
    public required string GameAssetPath { get; set; }

    [JsonPropertyName("backup_path")]
    public string BackupPath { get; set; } = "backup/";

    [JsonPropertyName("class_package")]
    public string? ClassPackage { get; set; }

    [JsonPropertyName("patches")]
    public PatchConfig[] Patches { get; set; } = [];

    public List<string> EnabledPatches()
    {
        List<string> patchFilePaths = [];

        foreach (PatchConfig patch in Patches)
        {
            if (patch.Enabled)
            {
                patchFilePaths.Add(patch.FilePath);
            }
        }

        return patchFilePaths;
    }
}
