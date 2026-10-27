# GV MnM

MnM Projector (Manufacturing and Maintenance) for the GV servers: an advanced projector that auto-builds and repairs using components from connected cargo. Published as [Workshop 3038342658](https://steamcommunity.com/sharedfiles/filedetails/?id=3038342658).

## Philosophy
- **Printing and repair without scripts or LCDs.** The projector does the work from connected cargo.
- **One at a time.** A construct runs one active MnM, and it must be stationary.

## Requirements
Loaded with the rest of the server mods ([Steam collection](https://steamcommunity.com/sharedfiles/filedetails/?id=2650582206)). Where it may be used is set by the GVK zone rules, not here.

## Folder map
| Path | What is in it |
| --- | --- |
| `ModScripts/` | The projector's logic |
| `Content/` | Block definitions, categories and icons |

## Build
MDK2 mod project. Scripts are in `ModScripts/`, SBC and textures in `Content/`. Build `AutoRepair.csproj` to deploy to `%AppData%\SpaceEngineers\Mods\AutoRepair`. Keep `modinfo.sbmi` in that deploy folder so publishing updates the existing Workshop item.

## Quick links
- Hub board and issues: [GV-Server-Mods/GVK-Settings](https://github.com/GV-Server-Mods/GVK-Settings)
- Workshop: [3038342658](https://steamcommunity.com/sharedfiles/filedetails/?id=3038342658)
- Steam guide: [players' rules and gameplay](https://steamcommunity.com/sharedfiles/filedetails/?id=2781522559); mod collection: [2650582206](https://steamcommunity.com/sharedfiles/filedetails/?id=2650582206)
- Skills: [se-dev-kharak](https://github.com/GV-Server-Mods/se-dev-kharak)
