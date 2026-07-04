using System.Text.Json;
using AssetPatchTool;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using Tomlyn;
using Tomlyn.Model;

const string managed = "Managed";
const string configPath = "config.json";
string[] tpkPaths = ["lz4.tpk", "lzma_file.tpk", "uncompressed.tpk"];

string gameAssetPath;
string classPackage;
string backupPath;
string managedPath;


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

Dictionary<string, List<AssetPatch>> getPatches(Config config)
{
    List<string> patchFiles = [];

    foreach (string patchPath in config.EnabledPatches())
    {
        if (Path.EndsInDirectorySeparator(patchPath))
        {
            patchFiles.AddRange(Directory.GetFiles(patchPath));
        }
        else
        {
            patchFiles.Add(patchPath);
        }
    }

    patchFiles = patchFiles.FindAll(f => File.Exists(f));

    return readPatchFiles(patchFiles);
}

Dictionary<string, List<AssetPatch>> readPatchFiles(List<string> patchFiles)
{
    Dictionary<string, List<AssetPatch>> result = [];

    foreach (string path in patchFiles)
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

void setGameAssetPath(Config config)
{
    if (Path.Exists(config.GameAssetPath))
    {
        gameAssetPath = config.GameAssetPath;
        return;
    }

    throw new Exception("\"game_asset_path\" not found");
}

void setClassPackagePath(Config config)
{
    if (config.ClassPackage != null)
    {
        if (File.Exists(config.ClassPackage))
        {
            classPackage = config.ClassPackage;
            return;
        }
    }

    foreach (string item in tpkPaths)
    {
        if (File.Exists(item))
        {
            classPackage = item;
            return;
        }
    }

    throw new Exception("TPK file not found");
}

void setBackupPath(Config config)
{
    backupPath = config.BackupPath;
}

void setManagedPath(Config config)
{
    managedPath = Path.Combine(config.GameAssetPath, "Managed");

    if (!Path.Exists(managedPath))
    {
        throw new Exception("Failed to find \"Managed\" path in game data");
    }
}

string configText = File.ReadAllText(configPath);
Config config1 = JsonSerializer.Deserialize<Config>(configText) ?? throw new Exception("Config is not valid");

setGameAssetPath(config1);
setClassPackagePath(config1);
setBackupPath(config1);
setManagedPath(config1);

Dictionary<string, List<AssetPatch>> patches = getPatches(config1);

foreach (var patchGroup in patches)
{
    ApplyPatchesToFile(patchGroup.Key, patchGroup.Value);
}
