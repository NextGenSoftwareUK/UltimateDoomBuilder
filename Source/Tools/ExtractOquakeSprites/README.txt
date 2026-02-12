ExtractOquakeSprites - Import Quake 1 sprites for OASIS STAR display pack
============================================================================

This tool reads Quake 1 id1/pak0.pak, extracts the first skin from relevant
progs/*.mdl models, and writes PNGs named by Doom thing type into the UDB
OASIS Sprites folder. The editor then uses these for OQUAKE assets (keys,
weapons, health, armor, ammo, monsters).

Current output behavior:
  - Scales sprites to a 64px max side (Doom-like size, avoids oversized keys)
  - Removes border-connected flat backgrounds
  - Normalizes alpha so masked rendering behaves correctly in-game/editor

REQUIREMENTS
  - Quake 1 game data: a folder containing pak0.pak (e.g. from Steam, GOG,
    or a full Quake 1 install). The folder is usually named "id1".

USAGE
  ExtractOquakeSprites.exe [id1_path] [output_sprites_path]
  ExtractOquakeSprites.exe --list [id1_path]   list progs/*.mdl in pak

  Verbose output (pak entry count, skip reasons) is always on.

  id1_path           Folder that contains pak0.pak (default: C:\Source\vkQuake\id1)
  output_sprites_path Where to write 5.png, 13.png, etc. (default: UDB Assets\...\OASIS\Sprites)

EXAMPLES
  "C:\Program Files (x86)\Steam\steamapps\common\Quake\id1"
  "C:\GOG Games\Quake\id1"
  C:\Source\vkQuake\id1   (if you copy pak0.pak there)

After running, open your map in Ultimate Doom Builder; OQUAKE thing types
that have a PNG in the Sprites folder will show the Quake sprite in 2D/3D.




