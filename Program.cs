using System.Text.Json;
using AssetPatchTool;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using Microsoft.Extensions.Logging;
using Tomlyn;
using Tomlyn.Model;

using ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
ILogger logger = loggerFactory.CreateLogger<Program>();

const string managed = "Managed";
const string globalGameManagersPath = "globalgamemanagers.assets";
const string configPath = "config.json";
string[] tpkPaths = ["lz4.tpk", "lzma_file.tpk", "uncompressed.tpk"];

string gameAssetPath;
string classPackage;
string backupPath;


void ApplyPatchesToFile(string assetFileName, List<AssetPatch> patchGroup)
{
    logger.LogInformation("Patching file \"{FileName}\"", assetFileName);

    // This should never happen but lets make extra sure
    if (!patchGroup.All(patch => patch.AssetFile == assetFileName))
    {
        throw new Exception("Developer Error: patch files were not grouped correctly");
    }

    ensureGlobalGameManagersExists();

    // Initialize asset manager
    var manager = new AssetsManager
    {
        MonoTempGenerator = new MonoCecilTempGenerator(Path.Combine(gameAssetPath, managed))
    };
    manager.LoadClassPackage(classPackage);

    // Load asset file
    string assetFilePath = Path.Combine(gameAssetPath, assetFileName);
    string backupFilePath = Path.Combine(backupPath, assetFileName);

    if (!File.Exists(backupFilePath))
    {
        if (!File.Exists(assetFilePath))
        {
            throw new Exception(String.Format("Asset file \"{0}\" not found", assetFileName));
        }

        logger.LogInformation("Backing up \"{OriginalFile}\" to \"{BackupFile}\"", assetFilePath, backupFilePath);

        File.Copy(assetFilePath, backupFilePath);
    }

    AssetsFileInstance assetFileInstance = manager.LoadAssetsFile(backupFilePath, false);
    AssetsFile assetFile = assetFileInstance.file;

    manager.LoadClassDatabaseFromPackage(assetFile.Metadata.UnityVersion);

    // Apply patches
    foreach (AssetPatch assetPatch in patchGroup)
    {
        var assetFileInfo = assetFile.GetAssetInfo(assetPatch.AssetPathId);
        var baseInfo = manager.GetBaseField(assetFileInstance, assetFileInfo);

        if (baseInfo == null)
        {
            continue;
        }

        ApplyPatch(baseInfo, assetPatch.Patches, [baseInfo["m_Name"].AsString]);

        assetFileInfo.SetNewData(baseInfo);
    }

    logger.LogInformation("Writing file \"{OutputFile}\"", assetFileName);

    using AssetsFileWriter writer = new(assetFilePath);
    assetFile.Write(writer);
}

void ensureGlobalGameManagersExists()
{
    string gameManagerPath = Path.Combine(gameAssetPath, globalGameManagersPath);
    string backupGameManagerPath = Path.Combine(backupPath, globalGameManagersPath);

    if (!File.Exists(backupGameManagerPath))
    {
        if (!File.Exists(gameManagerPath))
        {
            throw new Exception(String.Format("Game manager file \"{0}\" not found", gameManagerPath));
        }

        logger.LogInformation("Backing up \"{OriginalFile}\" to \"{BackupFile}\"", gameManagerPath, backupGameManagerPath);

        File.Copy(gameManagerPath, backupGameManagerPath);
    }
}

void ApplyPatch(AssetTypeValueField field, TomlTable patches, string[] parents)
{
    foreach (var patch in patches)
    {
        string key = patch.Key;
        object value = patch.Value;

        string fieldName = field[key].TemplateField.Name;
        string fieldType = field[key].TemplateField.Type;

        // TODO: Put this thing into it's own function
        AssetValueType assetValueType = field[key].TemplateField.ValueType;
        switch (assetValueType)
        {
            case AssetValueType.String:
                field[key].AsString = Convert.ToString(value);
                break;
            case AssetValueType.Int32:
                field[key].AsInt = Convert.ToInt32(value);
                break;
            case AssetValueType.Int64:
                field[key].AsLong = Convert.ToInt64(value);
                break;
            case AssetValueType.UInt8:
                field[key].AsByte = Convert.ToByte(value);
                break;
            case AssetValueType.Float:
                field[key].AsFloat = Convert.ToSingle(value);
                break;
            case AssetValueType.Double:
                field[key].AsDouble = Convert.ToDouble(value);
                break;
            case AssetValueType.Array:
                ApplyArrayValue(field[key], value, [.. parents, key]);
                continue;
            case AssetValueType.None:
                ApplyObjectValue(field[key], value, [.. parents, key]);
                continue;
            default:
                throw new Exception(String.Format("Unimplemented AssetValueType: {0}", assetValueType));
        }

        logger.LogDebug("{Parent}.{Key}: {Type} = {Value}",
                        string.Join(".", parents),
                        key,
                        fieldType,
                        field[key].AsString
                        );

    }
}

void ApplyArrayValue(AssetTypeValueField field, object value, string[] parents)
{
    if (!field.TemplateField.IsArray)
    {
        throw new Exception("Not an array");
    }

    // In the first case we are indexing a value inside the table
    if (value is TomlTable table)
    {
        foreach (var item in table)
        {
            string key = item.Key;
            if (Int32.TryParse(key, out int index))
            {
                logger.LogDebug("Key is index: {}", index);
                if (item.Value is TomlTable subtable)
                {
                    ApplyPatch(field[index], subtable, [.. parents, key]);
                }
            }

            logger.LogDebug(key);
        }
    }
    // In the rest of the cases we are replacing the whole array
    else if (value is TomlTableArray tableArray)
                {
        field.Children.Clear();
        int index = 0;
        foreach (TomlTable itemParams in tableArray)
                    {
            AssetTypeValueField newItem = ValueBuilder.DefaultValueFieldFromArrayTemplate(field);

            ApplyPatch(newItem, itemParams, [.. parents, index.ToString()]);
            field.Children.Add(newItem);
            index++;
        }
    }
    else if (value is TomlArray array)
    {
        field.Children.Clear();
        int index = 0;
        foreach (object? itemParams in array)
        {
            AssetTypeValueField newItem = ValueBuilder.DefaultValueFieldFromArrayTemplate(field);

            if (itemParams is TomlTable subTable)
                        {
                ApplyPatch(newItem, subTable, [.. parents, index.ToString()]);
                        }
            else
            {
                AssetValueType assetValueType = newItem.TemplateField.ValueType;
                switch (assetValueType)
                {
                    case AssetValueType.String:
                        newItem.AsString = Convert.ToString(itemParams);
                        break;
                    case AssetValueType.Int32:
                        newItem.AsInt = Convert.ToInt32(itemParams);
                        break;
                    case AssetValueType.Int64:
                        newItem.AsLong = Convert.ToInt64(itemParams);
                        break;
                    case AssetValueType.UInt8:
                        newItem.AsByte = Convert.ToByte(itemParams);
                        break;
                    case AssetValueType.Float:
                        newItem.AsFloat = Convert.ToSingle(itemParams);
                        break;
                    case AssetValueType.Double:
                        newItem.AsDouble = Convert.ToDouble(itemParams);
                        break;
                    default:
                        logger.LogDebug("{Name}: {Type}({Type2})", newItem.TemplateField.Name, newItem.TemplateField.Type, newItem.TemplateField.ValueType);

                        throw new Exception("Unsupported type");
                }
            }

            field.Children.Add(newItem);
            index++;
        }
    }
}

void ApplyObjectValue(AssetTypeValueField field, object value, string[] parents)
{
    if (value is TomlTable table)
    {
        ApplyPatch(field, table, parents);
    }
    else
    {
        throw new Exception("Unimplemented Object Value type");

    }
}

List<AssetPatch> getPatches(Config config)
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

List<AssetPatch> readPatchFiles(List<string> patchFiles)
{
    List<AssetPatch> result = [];

    foreach (string path in patchFiles)
    {
        if (!File.Exists(path))
        {
            throw new Exception(String.Format("File \"{0}\" not found", path));
        }

        string patchConfigText = File.ReadAllText(path);

        AssetPatch? patchObject = TomlSerializer.Deserialize<AssetPatch>(patchConfigText);

        if (patchObject == null)
        {
            continue;
        }

        logger.LogInformation("Patch file \"{File}\" added", path);

        result.Add(patchObject);
    }

    return result;
}

void setGameAssetPath(Config config)
{
    if (Path.Exists(config.GameAssetPath))
    {
        gameAssetPath = config.GameAssetPath;
        logger.LogInformation("Game asset path set to: \"{GameAssetPath}\"", gameAssetPath);
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
            logger.LogInformation("Class Package set to: \"{ClassPackage}\"", classPackage);
            return;
        }
    }

    foreach (string item in tpkPaths)
    {
        if (File.Exists(item))
        {
            classPackage = item;
            logger.LogInformation("Class Package set to: \"{ClassPackage}\"", classPackage);
            return;
        }
    }

    throw new Exception("Class Package file not found");
}

void setupBackup(Config config)
{
    backupPath = config.BackupPath;

    if (!Directory.Exists(backupPath))
    {
        Directory.CreateDirectory(backupPath);
    }
}

string configText = File.ReadAllText(configPath);
Config config = JsonSerializer.Deserialize<Config>(configText) ?? throw new Exception("Config is not valid");

logger.LogInformation("Read \"{ConfigPath}\"", configPath);

setGameAssetPath(config);
setClassPackagePath(config);
setupBackup(config);

List<AssetPatch> patches = getPatches(config);

var patchGroups = patches.GroupBy(patch => patch.AssetFile);

foreach (var patchGroup in patchGroups)
{
    ApplyPatchesToFile(patchGroup.Key, patchGroup.ToList());
}
