# Patchwork
A custom asset mod for Hollow Knight: Silksong with particular attention to ease of creation.

## Features
* Sprite replacement with individual sprite image files, including proper names, rotations & sizing - no more bulky spritesheets
* Ability to replace (at least almost) every sprite in the game
* Support for Texture2D Sprites (which previously required Silksong Customizer + CustomizerT2D)
* Full compatibility with spritesheet-based skins
* Automatic reloading of sprites when files change, so you can see your sprites ingame immediately
* Built-in sprite dumping functionality

## Configuration

### General

* `DumpSprites`: Enables sprite dumping, which saves sprites for any loaded scene into the "Patchwork/Dumps" folder. These files can be used to make new texture packs. DO NOT enable this during normal gameplay, as it slows down loading the game by a lot. If this is enabled, the mod will also let you dump textures from all scenes in the game by pressing the button configured under "Keybinds/FullDumpKey" (Default: F6)
* `LoadSprites`: On by default, lets you disable custom textures if you would like that for whatever reason.

### Reloading

* `ReloadSceneOnChange`: Disabled by default, watches sprite files for changes and automatically reloads the area you're in within the game whenever any changes happen. This lets you see your new sprites ingame immediately. WARNING: Depending on where you are in the game, this may cause instability or crashes.
* `EnableForceReload`: Disabled by default, lets you reload the area you're in ingame with a button press configured under "Keybinds/ForceReloadKey" (Default: F5)

### Logging

Enables/disables various types of logging. Only relevant for troubleshooting or development, you'll most likely not need these.

## Texture Creation Guide
1. If you'd like to see your changes update live while the game is running, set the ReloadSceneOnChange config option to "true".
   * It's a little unstable right now, you get the best results by loading into the game and going through at least one loading zone before modifying sprites.
2. Dump the textures you want to modify, or download the full set of modifiable game sprites from this mods download page. 
   * For dumping textures yourself, enable the "DumpSprites" option in the mod config, boot up the game and enter the areas where the sprites you want to modify are loaded. You can also press the "Full Dump" key (Default: F6) to dump all sprites, but this takes quite a while!
3. Put your modified sprites into "Patchwork/Sprites" in your game folder.
   * You can put them into as many subfolders as you want, but the names of the last two folders MUST be the same as in the dump (for example: "Patchwork/Sprites/HUD Cln/atlas0" is fine, so is "Patchwork/Sprites/SomeTexturePack/HUD Cln/atlas0"
4. Enjoy!

### Publishing Packs on Thunderstore

Patchwork is structured in a way that lets creators publish their packs as plugins on Thunderstore! In order to be automatically installed correctly when players download them, make sure to follow the following structure with your plugins:

```
YourName-PackName.zip
 \- icon.png
 \- manifest.json
 \- README.md
 \- plugins
     \- Patchwork
         |- Sprites
         |   \- <your files here...>
         \- Spritesheets
             \- <your files here...>
```

## Known Issues

### Duplicate Sprites
Some sprite textures have multiple definitions within the game files, meaning that while the game treats them as two separate textures, they take up the same space on the spritesheet. When dropping the full set of dumped sprites into the Patchwork folder, this may cause sprites to overwrite each other. (This is for example the case on Hornet's "idle" sprites.)

**Temporary Solution:** Only keep the Sprites you've already modified in your Patchwork folder, and move them there as you create new ones. The auto-reload feature will work regardless, and there's no risk of conflicts.

I am investigating a way to detect and handle these duplicate sprites, and this issue should be resolved at some point in the future with an update.

## Special Thanks

* To [Douglas Gregory](https://bsky.app/profile/dmgregory.ca) without whose help I wouldn't have been able to add the GPU-related performance improvements that made Patchwork v2 as fast as it is now.
* To [RatherChaoticDev](https://next.nexusmods.com/profile/RatherChaoticDev) who created the original [Silksong Customizer](https://www.nexusmods.com/hollowknightsilksong/mods/142) mod that Patchwork was based on
* To [Su4enka](https://next.nexusmods.com/profile/Su4enka) whose CustomizerT2D plugin (as part of their [Cute Hornet skin](https://www.nexusmods.com/hollowknightsilksong/mods/203)) helped with adding T2D support to Patchwork

## Links
GitHub Repository: https://github.com/Ashiepaws/silksong-patchwork

Discord: https://discord.gg/DeCtHA84AM (originally meant for my little Twitch community, but I'd be happy to chat and hang out! :3)

Ko-Fi Donations: https://ko-fi.com/ashiepaws