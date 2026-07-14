
# AssetPatchTool

A [AssetsTools.NET](https://github.com/nesrak1/AssetsTools.NET) based command line tool for overwriting data in Unity assets. Patches are defined as TOML files.

## Using AssetPatchTool

AssetPatchTool requires a TPK file and a "config.json" file to function.

You can find a TPK file here https://github.com/AssetRipper/Tpk/blob/master/README.md. Because AssetPatchTool uses AssetsTools.NET Brotli compression is not supported. The TPK file should be placed in the same directorty as AssetPatchTool executable. 

Config file is used to define parameters for AssetPatchTool and is mandatory for AssetPatchTool to run. config.json file should be placed in the same directory as AssetPatchTool executable.

Config fields are:
1) "game_asset_path" - This should point to the data directory of the Unity game, where you can find files "levelX" and "sharedassetsX.assets".

2) "class_package" (optional) - By default AssetPatchTool searches for a appropriate TPK file from the same directory it's in. If your TPK file is elsewhere you can define path to it here.

3) "patches" - This is a list of JSON objects defining paths to directories where patches are located. If path points to a directory, it is searched recursively.

```json
{
    "path": "path/to/patches/",
    "enabled": true
}
```

Running AssetPatchTool modifies the asset files defined in "game_asset_path" while creating backups. These backups are used as base for future patches so reverting the game files between different versions of a mod is not necessary.

## Patch files

Patch files are TOML files with the following structure. 

```toml
assetFile = "[file where the asset to be patched is]"
assetPathId = "[asset's path id as a number]"

[patch]
propertA = "[new value]"
propertyB.nestedProperty = "[another new value]"

# Accesses an object in a specific index of a list
listPropertyC.4.propertyInObjectInList = "[third new value]"

# Overrides the whole list
listPropertyD.Array = [
    {"propertyX" = 0, "propertyY" = 0 },
    {"propertyX" = 0, "propertyY" = 0 },
    {"propertyX" = 0, "propertyY" = 0 },
]

```

To find out the assetFile, AssetPathId and the structure of the \[patch\] section you can use tools like [UABEA](https://github.com/nesrak1/UABEA/tree/master)