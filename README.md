# Terrain Editor for The Long Dark

A MelonLoader mod (Il2Cpp) with two in-game editors:

## ⚠️The changes you make are specific to the save file, and if you uninstall the mod, the changes will revert to their original state.⚠️

* **Terrain brush**: raise, lower, flatten, fill and smooth the ground of any region.
* **Object editor**: remove (hide), move, turn and copy objects of a region (stray rail cars, vehicles, rocks, trees ...).

Changes are saved per region and played back every time the region loads. The game's own files are never changed.

## Install
1. Install MelonLoader for The Long Dark.
2. Copy `TerrainEditor.dll` into the game's `Mods` folder.

## Use
| Default | Action |
| --- | --- |
| F10 | Next editor: terrain brush, object editor, off |
| Left mouse (hold) | Paint |
| M | Next brush: raise, lower, flatten, fill, smooth |
| Mouse wheel | Brush size (Shift held: bigger steps) |
| Ctrl + wheel | Strength |
| PageUp / PageDown | Level used by flatten and fill, relative to the ground where the stroke starts |
| Ctrl + Z | Undo the last stroke of this session |

## Settings
With the [ModSettings](https://github.com/ds5678/ModSettings) mod installed, every setting is on a page in the game:
**Options > Mod Settings > Terrain Editor** (pick a key, press Confirm). ModSettings is optional: without it the mod
runs the same, and the settings live in `UserData/MelonPreferences.cfg`, section `[TerrainEditor]`, written the first
time the game starts with the mod. In the file the values are Unity `KeyCode` names such as `F9`, `B`, `Home`,
`Keypad7`; `Shift`, `Ctrl` and `Alt` (also `LeftShift`, `RightControl` ...) count for either side's key. An unknown
name switches that action off and is logged.

| Setting | Default | Meaning |
| --- | --- | --- |
| `Enabled` | `true` | Turns the whole mod off |
| `ToggleKey` | `F10` | Brush on / off |
| `NextBrushKey` | `M` | Next brush type |
| `LevelUpKey`, `LevelDownKey` | `PageUp`, `PageDown` | Level for flatten and fill |
| `UndoKey` | `Z` | Undo the last stroke or object edit |
| `UndoNeedsModifier` | `true` | Undo needs the strength key held (Ctrl+Z); set `false` for a plain key |
| `FastKey` | `Shift` | Held for bigger steps |
| `FineKey` | `Ctrl` | Objects: held while turning for 1 degree steps |
| `RemoveKey` | `Delete` | Objects: remove |
| `MoveKey` | `G` | Objects: pick up / put down |
| `CancelKey` | `X` | Objects: cancel the carry |
| `CopyKey` | `C` | Objects: copy |
| `OriginalKey` | `Home` | Objects: back as the region made it |
| `LargerPartKey`, `SmallerPartKey` | `LeftBracket`, `RightBracket` | Objects: select the larger / smaller part |
| `StrengthKey` | `Ctrl` | Held with the wheel to change strength |
| `PaintMouseButton` | `0` | 0 left, 1 right, 2 middle |
| `Verbose` | `false` | Extra log lines |

The help line on the screen shows the keys you have set.

### Object editor
Aim at an object and press a key. `[` and `]` choose a larger or smaller part of what the crosshair touches
(a whole wagon or just one wheel).

| Default | Action |
| --- | --- |
| Delete | Remove (hide) the object |
| G | Pick up, then put down; the object follows the crosshair |
| X | Cancel while carrying: it goes back where it was |
| Mouse wheel | Turn while carrying (5 degrees; Shift 15, Ctrl 1) |
| PageUp / PageDown | Raise / lower while carrying (Shift: more) |
| C | Make a copy to place; Delete and G also work on copies |
| Home | Put the object back as the region made it |
| Ctrl + Z | Undo |
| [ / ] | Select the larger / smaller part |

Objects with items inside show a warning: once such an object is removed the items cannot be reached. Region
transitions cannot be edited. Objects the game draws through GPU instancing (some regions draw their rails, logs
and rocks that way) can be removed and moved too; they may have no collision.

### Terrain brush notes
Fill raises the ground up to the level where the stroke started and never digs; it is meant for building snow
beds. The texts are in Turkish when the game language is Turkish, otherwise English.

## Where the edits are
Every save slot has its own edits, so a new game starts clean and one save's changes never show in another:
`UserData/TerrainEditor/<save name>/<region>.json` holds the terrain strokes and
`UserData/TerrainEditor/<save name>/Objects/<region>.json` the object edits. Delete a file to take back every edit of
that region in that save. Strokes are lists of dabs (x, z, radius, amount, target height), so the files are small and
can be shared. A new game only gets its save name once the game has made one; edits made before that are kept in
memory and written under the name when the next edit is saved.

When a save slot is deleted, or a new game starts under a name an earlier world used, that name's edits are removed
so they never show in a different world.

The game does not store terrain heights or hidden objects in its saves, so the edits only show while the mod is
installed. Without the mod the world is as the game made it.

## How it works
The ground is a normal Unity `Terrain`. A dab reads the block of the heightmap around the brush
(`TerrainData.GetHeights`), changes the heights in memory (a smooth falloff from the centre) and writes the block
back with `SetHeightsDelayLOD`; the level of detail and the collider are brought up to date once at the end of each
stroke (`SyncHeightmap`). On region load all saved dabs are applied to each terrain in one read and one write.

Things standing on the ground (rails, rocks, props) do not follow the terrain.

