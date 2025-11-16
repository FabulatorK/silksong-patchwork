![Patchwork](https://raw.githubusercontent.com/Ashiepaws/silksong-patchwork/refs/heads/master/img/Patchwork.png)
[![Discord](https://img.shields.io/discord/1297141570805694554?style=for-the-badge&label=Discord&link=https%3A%2F%2Fdiscord.gg%2FDeCtHA84AM)](https://discord.gg/DeCtHA84AM)
 [![Thunderstore Downloads](https://img.shields.io/thunderstore/dt/Ashiepaws/Patchwork?style=for-the-badge)](https://thunderstore.io/c/hollow-knight-silksong/p/Ashiepaws/Patchwork/) [![Twitch Status](https://img.shields.io/twitch/status/ashiepaws?style=for-the-badge&link=https%3A%2F%2Ftwitch.tv%2Fashiepaws)](https://twitch.tv/ashiepaws)
 [![Static Badge](https://img.shields.io/badge/ko--fi-donate-blue?style=for-the-badge&link=https%3A%2F%2Fko-fi.com%2Fashiepaws)](https://ko-fi.com/ashiepaws)



A custom asset mod for Hollow Knight: Silksong with particular attention to ease of creation.

## Features
* Sprite replacement with individual sprite image files, including proper names, rotations & sizing - no more bulky spritesheets
* Support for Texture2D Sprites (which previously required Silksong Customizer + CustomizerT2D)
* Full compatibility with spritesheet-based skins
* Automatic reloading of assets when files change, so you can see your skin ingame immediately without closing the game
* Built-in sprite dumping functionality
* Audio replacement supporting both sound effects and music

### Planned features
* Conditional sprites (e.g. different textures for Hornet depending on crest, health, etc.)
* Ingame asset pack manager
* Support for cutscene replacements
* Support for text replacements

## Configuration

### Sprites

* `DumpSprites`: Enables sprite dumping, which saves sprites for any loaded scene into the "Patchwork/Dumps" folder. These files can be used to make new texture packs. DO NOT enable this during normal gameplay, as it slows down loading the game by a lot. If this is enabled, the mod will also let you dump textures from all scenes in the game by pressing the button configured under "Keybinds/FullDumpKey" (Default: F6)

### Audio
* `LogAudioDuration`: How long the name of a sound is shown in the log after being played, in seconds. (Default: 5)
* `HideModdedAudioInLog`: On by default. If enabled, sounds which already have a modded file are omitted from the log. This makes it easier to find the names of specific sounds you want to mod.

### Menu Hotkeys
* `ShowAudioLog`: Keybind for showing the audio log window that logs the names of all sounds that are being played that Patchwork has access to. If you're adding custom files to the "Sounds" folder, you must name them exactly the same as shown in order for Patchwork to replace them. (Default: 1)
* `ShowAudioList`: Keybind for showing the audio list, which lists all statically loaded sound effects. Can be used to find names of sounds which don't show up in the Audio Log. (Default: 2)
* `ShowAnimationController`: Keybind for showing the animation controller, which lets you freeze individual objects, pause and play specific animations, and pick individual sprite images to be dumped and edited immediately. (Default: 3)

### Animation Controller Hotkeys
* `AnimationControllerPauseKey`: Toggles the "Paused" state on the currently selected object, freezing the animation and allowing for manual frame selection.
* `AnimationControllerNextFrameKey`: Advances the animation by one frame, if the selected object is paused
* `AnimationControllerPrevFrameKey`: Shows the previous frame of the animation, if the selected object is paused
* `AnimationControllerFreezeKey`: Toggles the "Frozen" state on the currently selected object, locking its position within the game world, to make animations that include movement easier to see and test.

## Texture Creation Guide
1. Load a save file and open the Patchwork Animation Controller (this is bound to the "3" key by default)
2. Open the pause menu and click the name of the object you want to modify the sprites of. It should then turn green in the Animation Controller
3. Select the animation you want to edit using the dropdown, or pause the sprite with the animation controller pause key (by default "Home") while it's doing the animation you want to edit
4. Use the animation controller frame forwards/backwards keys (by default "Page Up"/"Insert" respectively) to select the specific frame you want to edit
5. Click the "Edit Current Sprite" button (you may have to open the pause menu again for this if you've closed it)
6. The sprite will now open up in your default image editing program. Edit it as you like and save the file.
7. Your edited sprite will now automatically appear in the game while it's still running. You don't need to restart the game to apply your changes.

**NOTE:** You can find your custom sprites at `BepInEx/plugins/Ashiepaws-Patchwork/Sprites` after clicking the "Edit Current Sprite" button.

## Audio Creation Guide
1. Open the audio log and audio list windows after booting the game. (By default these are mapped to the "1" and "2" keys)
2. Cause the sound you want to modify to be played. You should see its name pop up in the Audio Log in the top right corner of the screen.
   * If you don't see your sound show up in the Audio Log, it may be a pre-loaded one. Make sure to check the Loaded Audio list in that case.
3. Put an audio file with the same name you saw in the Audio Log (or Loaded Audio list) into the "Patchwork/Sounds" folder. For example, to replace the title screen music, you'd name the file "Title.wav" or "Title.mp3".
4. Cause the sound to be played again. It should now play your custom sound!

**NOTE:** Patchwork supports all [Unity-supported audio formats.](https://docs.unity3d.com/Manual/AudioFiles-compatibility.html)

## Publishing Packs on Thunderstore

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
         |- Spritesheets
         |   \- <your files here...>
         \- Sounds
             \- <your files here...>
```

## Special Thanks

* To [Douglas Gregory](https://bsky.app/profile/dmgregory.ca) without whose help I wouldn't have been able to add the GPU-related performance improvements that made Patchwork v2 as fast as it is now.
* To [RatherChaoticDev](https://next.nexusmods.com/profile/RatherChaoticDev) who created the original [Silksong Customizer](https://www.nexusmods.com/hollowknightsilksong/mods/142) mod that Patchwork was based on
* To [Su4enka](https://next.nexusmods.com/profile/Su4enka) whose CustomizerT2D plugin (as part of their [Cute Hornet skin](https://www.nexusmods.com/hollowknightsilksong/mods/203)) helped with adding T2D support to Patchwork