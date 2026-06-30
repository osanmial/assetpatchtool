using System.Text.Json;
using System.Text.Json.Nodes;
using AssetPatchTool;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using Tomlyn;
using Tomlyn.Model;

const string managed = "Managed";
const string config_path = "config.json";

string gameAssetPath;
string classPackage;


void ApplyPatchesToFile(string assetFile, List<AssetPatch> patchGroup)
{
    // Initialize asset manager
    string bundlePath = Path.Combine(gameAssetPath, assetFile);
    string managedPath = Path.Combine(gameAssetPath, managed);

    var manager = new AssetsManager
    {
        MonoTempGenerator = new MonoCecilTempGenerator(managedPath)
    };
    manager.LoadClassPackage(classPackage);


    // Load bundle file
    AssetsFileInstance bundleInst = manager.LoadAssetsFile(bundlePath, true);
    AssetsFile bundleFile = bundleInst.file;

    manager.LoadClassDatabaseFromPackage(bundleFile.Metadata.UnityVersion);

    // Apply patches
    foreach (AssetPatch assetPatch in patchGroup)
    {
        var assetFileInfo = bundleFile.GetAssetInfo(assetPatch.AssetPathId);
        var baseInfo = manager.GetBaseField(bundleInst, assetFileInfo);

        if (baseInfo == null)
        {
            continue;
        }

        ApplyPatch(baseInfo, assetPatch.Patches);

        assetFileInfo.SetNewData(baseInfo);
    }

    using AssetsFileWriter writer = new(assetFile);
    bundleFile.Write(writer);
}

void ApplyPatch(AssetTypeValueField field, TomlTable patches)
{
    foreach (var patch in patches)
    {
        if (patch.Value is TomlTable table)
        {
            if (Int32.TryParse(patch.Key, out int index))
            {
                ApplyPatch(field[index], table);
            }
            else
            {
                ApplyPatch(field[patch.Key], table);
            }

            continue;
        }

        string key = patch.Key;

        string fieldName = field[key].TemplateField.Name;
        string fieldType = field[key].TemplateField.Type;

        switch (fieldType)
        {
            case "int":
                field[key].AsInt = Convert.ToInt32(patch.Value);
                break;
            case "UInt8":
                field[key].AsByte = Convert.ToByte(patch.Value);
                break;
            case "long":
            case "SInt64":
                field[key].AsLong = Convert.ToInt64(patch.Value);
                break;
            case "float":
                field[key].AsFloat = Convert.ToSingle(patch.Value);
                break;
            case "double":
                field[key].AsDouble = Convert.ToDouble(patch.Value);
                break;
            case "string":
                field[key].AsString = (string)patch.Value;
                break;
            case "Array":
                field[key].Children.Clear();
                if (patch.Value is TomlArray array)
                {
                    foreach (var item in array)
                    {
                        AssetTypeValueField newItem = ValueBuilder.DefaultValueFieldFromArrayTemplate(field[key]);

                        if (item is TomlTable innerTable)
                        {
                            ApplyPatch(newItem, innerTable);
                        }

                        field[key].Children.Add(newItem);
                    }
                }

                continue;
            default:

                Console.Write(fieldType);
                Console.WriteLine(" unsupported");
                continue;
        }

        Console.Write(key);
        Console.Write(": ");
        Console.Write(fieldType);
        Console.Write(" = ");
        Console.WriteLine(field[key].AsString);
    }
}

Dictionary<string, JsonNode> readConfig(string config_path)
{
    string config_text = File.ReadAllText(config_path);

    Dictionary<string, JsonNode>? result = JsonSerializer.Deserialize<Dictionary<string, JsonNode>>(config_text);

    return result ?? [];
}

Dictionary<string, List<AssetPatch>> getPatches(JsonArray patch_configs)
{
    List<string> files = [];

    foreach (JsonNode? patch_config in patch_configs)
    {
        bool enabled = patch_config["enabled"].GetValue<bool>();
        string path = patch_config["path"].ToString();

        if (!enabled)
        {
            continue;
        }

        if (Path.EndsInDirectorySeparator(path))
        {
            files.AddRange(Directory.GetFiles(path));
        }
        else
        {
            files.Add(path);
        }
    }

    files = files.FindAll(f => File.Exists(f));

    return readPatchFiles(files);

}

Dictionary<string, List<AssetPatch>> readPatchFiles(List<string> filepaths)
{
    Dictionary<string, List<AssetPatch>> result = [];

    foreach (string path in filepaths)
    {
        TomlTable? data = TomlSerializer.Deserialize<TomlTable>(File.ReadAllText(path));

        if (data == null)
        {
            continue;
        }

        string assetFile = data["assetFile"].ToString() ?? throw new Exception("Undefined assetFile");

        AssetPatch patch = new()
        {
            AssetPathId = (long)data["assetPathId"],
            Patches = (TomlTable)data["patch"],
        };

        if (!result.ContainsKey(assetFile))
        {
            result.Add(assetFile, []);
        }

        result[assetFile].Add(patch);
    }

    return result;
}


Dictionary<string, JsonNode> config = readConfig(config_path);

gameAssetPath = config["game_asset_path"].ToString();
classPackage = config["class_package"].ToString() ?? throw new Exception("Class package path undefined");

if (string.IsNullOrEmpty(gameAssetPath))
{
    throw new Exception(
        "\"game_asset_path\" in config.json is undefined or empty. This path should point to the game's data directory"
        );

}

if (string.IsNullOrEmpty(classPackage))
{
    throw new Exception(
            "\"class_package\" in config.json is undefined or empty. It is a necessary file for reading asset files properly. Instructions on how to get one can be found int the README"
            );
}

JsonArray patch_config = config["patches"] as JsonArray ?? [];

Dictionary<string, List<AssetPatch>> patches = getPatches(patch_config);

foreach (var patchGroup in patches)
{
    ApplyPatchesToFile(patchGroup.Key, patchGroup.Value);
}
