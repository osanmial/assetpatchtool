
# AssetPatchTool

A [AssetsTools.NET](https://github.com/nesrak1/AssetsTools.NET) based command line tool for overwriting data in Unity assets. Patches are defined as TOML files.

## Using AssetPatchTool

AssetPatchTool is used by defining fields in "config.json" located in the same directiry as AssetPatchTool and then running AssetPatchTool without arguments. Error handling is currently very limited and crashes might not provide useful error messages.

Necessary fields to define are:
1) "game_asset_path" - This should point to the data directory of the Unity game, where you can find files "levelX" and "sharedassetsX.assets".

2) "class_package" - This needs to point to a tpk file, which is needed to deserialize Unity asset files properly. You can find a tpk file here https://github.com/AssetRipper/Tpk/blob/master/README.md. Because AssetPatchTool uses AssetsTools.NET Brotli compression is not supported.

3) "patches" - This is a list of JSON objects defining paths to directories where patches are located. Search for patch files is not recursive currently.

```json
{
    "path": "path/to/patches/",
    "enabled": true
}
```

Running the AssetPatchTool creates copies of affected Unity asset into the current directory and patches them according to patch files.

After this the patched files can be copied into the games data directory to overwrite the original ones. It is recommended to back up the original game files before doing so to have easy access to the unpatched files.

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