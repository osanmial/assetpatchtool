using System.Text.Json;
using AssetPatchTool;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using Microsoft.Extensions.Logging;
using Tomlyn;
using Tomlyn.Model;

using ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
ILogger logger = loggerFactory.CreateLogger("AssetPatchTool");

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
        if (patch.Value is TomlTable table)
        {
            if (Int32.TryParse(patch.Key, out int index))
            {
                ApplyPatch(field[index], table, [.. parents, patch.Key]);
            }
            else
            {
                ApplyPatch(field[patch.Key], table, [.. parents, patch.Key]);
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
                            ApplyPatch(newItem, innerTable, [.. parents, "Array"]);
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


        logger.LogInformation("{Parents}.{Key}: {Type} = {Value}", String.Join(".", parents), key, fieldType, field[key].AsString);
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
