// Extract OQUAKE sprites from Quake 1 id1/pak0.pak (MDL skins) to UDB OASIS Sprites folder.
// Run: ExtractOquakeSprites.exe [id1_path] [output_sprites_path]
// Example: ExtractOquakeSprites.exe C:\Source\vkQuake\id1 C:\Source\UltimateDoomBuilder\Assets\Common\UDBScript\Scripts\OASIS\Sprites

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace ExtractOquakeSprites
{
	static class Program
	{
		const int ALIAS_VERSION = 6;
		const int ALIAS_SKIN_SINGLE = 0;
		// "IDPO" in file (little-endian): bytes I,D,P,O -> read as 0x4F504449
		const int IDPOLYHEADER = 0x4F504449;

		// Quake 1 progs model (in pak) -> OASIS unique thing type(s). One model can output multiple PNGs (same image).
		// Keys/weapons/ammo/health: 5xxx. Monsters: 53xx, 3010, 3011.
		static readonly List<(string model, int uniqueType)> ModelToUniqueType = new List<(string, int)>
		{
			("progs/w_s_key.mdl", 5013),   // Silver key
			("progs/w_g_key.mdl", 5005),   // Gold key
			("progs/g_shot.mdl", 5201),    // Shotgun
			("progs/g_shot.mdl", 5202),    // Super Shotgun (same sprite)
			("progs/g_nail.mdl", 5203),
			("progs/g_nail.mdl", 5204),    // Super Nailgun
			("progs/g_rock.mdl", 5205),
			("progs/g_rock.mdl", 5206),    // Rocket Launcher
			("progs/g_light.mdl", 5207),
			("progs/g_shot.mdl", 5209),     // Shells
			("progs/g_nail.mdl", 5208),    // Nails/spikes
			("progs/g_rock.mdl", 5210),    // Rockets
			("progs/g_light.mdl", 5211),   // Cells
			("progs/armor.mdl", 5214),     // Green Armor
			("progs/armor.mdl", 5215),     // Yellow Armor
			("progs/armor.mdl", 5216),     // Mega Armor (reuse armor sprite)
			("progs/ogre.mdl", 5309),
			("progs/demon.mdl", 5302),
			("progs/shambler.mdl", 5303),
			("progs/soldier.mdl", 5304),
			("progs/zombie.mdl", 3011),
			("progs/knight.mdl", 5369),
			("progs/wizard.mdl", 5366),
			("progs/dog.mdl", 3010),
			("progs/fish.mdl", 5305),
			("progs/spawn.mdl", 5368),
		};
		// Health: reuse armor sprite if no health model in pak
		static readonly List<(string model, int uniqueType)> ModelToUniqueTypeHealth = new List<(string, int)>
		{
			("progs/armor.mdl", 5212),     // Health (reuse armor if no health model)
			("progs/armor.mdl", 5213),     // Small Health
		};

		// Quake 1 palette (256 * 3 bytes). Set from gfx/palette.lmp in pak, or fallback. Index 255 = transparent.
		static byte[] QuakePalette;

		/// <summary>Load palette from pak gfx/palette.lmp (768 bytes) for correct colors; else grayscale fallback.</summary>
		static void LoadQuakePalette(PakFile pak)
		{
			byte[] fromPak = pak.ReadFile("gfx/palette.lmp");
			if (fromPak != null && fromPak.Length >= 768)
			{
				QuakePalette = new byte[768];
				Buffer.BlockCopy(fromPak, 0, QuakePalette, 0, 768);
				Console.WriteLine("  palette: gfx/palette.lmp (color)");
				return;
			}
			QuakePalette = new byte[768];
			for (int i = 0; i < 256; i++)
			{
				if (i == 255) { QuakePalette[i * 3] = 255; QuakePalette[i * 3 + 1] = 0; QuakePalette[i * 3 + 2] = 255; }
				else { byte b = (byte)i; QuakePalette[i * 3] = b; QuakePalette[i * 3 + 1] = b; QuakePalette[i * 3 + 2] = b; }
			}
			Console.WriteLine("  palette: fallback (grayscale)");
		}

		// Target longest side in pixels so Quake sprites match Doom sprite scale in the editor (Doom keycard is large; 1024 so golden key ~matches blue keycard).
		const int TARGET_MAX_SIZE = 1024;

		static int Main(string[] args)
		{
			bool listOnly = args.Length > 0 && string.Equals(args[0], "--list", StringComparison.OrdinalIgnoreCase);
			int argStart = listOnly ? 1 : 0;
			string id1Path = args.Length > argStart ? args[argStart] : @"C:\Source\vkQuake\id1";
			string outPath = args.Length > argStart + 1 ? args[argStart + 1] : Path.Combine(
				Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location),
				"..", "..", "..", "..", "Assets", "Common", "UDBScript", "Scripts", "OASIS", "Sprites");

			outPath = Path.GetFullPath(outPath);
			string pakPath = Path.Combine(id1Path, "pak0.pak");

			Console.WriteLine("OQUAKE sprite extractor");
			Console.WriteLine("  id1 path: " + id1Path);
			Console.WriteLine("  output:   " + outPath);
			if (!File.Exists(pakPath))
			{
				Console.WriteLine("ERROR: pak0.pak not found at " + pakPath);
				Console.WriteLine("Place your Quake 1 id1 folder (containing pak0.pak) at " + id1Path + " or pass path as first argument.");
				return 1;
			}
			Directory.CreateDirectory(outPath);

			var pak = new PakFile(pakPath);
			int dirCount = pak.GetEntryCount();
			if (dirCount == 0)
			{
				Console.WriteLine("ERROR: pak directory is empty or wrong format. Is " + pakPath + " a valid Quake 1 pak0.pak?");
				return 1;
			}
			Console.WriteLine("  pak entries: " + dirCount);
			if (listOnly)
			{
				foreach (var p in pak.ListProgsMdl())
					Console.WriteLine(p);
				return 0;
			}

			LoadQuakePalette(pak);
			Console.WriteLine("  scale: longest side " + TARGET_MAX_SIZE + " px (match Doom sprite size)");
			string spritesFullPath = Path.Combine(Path.GetDirectoryName(outPath), "SpritesFull");
			Directory.CreateDirectory(spritesFullPath);
			Console.WriteLine("  Sprites (editor): front half only -> " + outPath);
			Console.WriteLine("  SpritesFull: full image -> " + spritesFullPath);

			var allMappings = new List<(string model, int uniqueType)>();
			allMappings.AddRange(ModelToUniqueType);
			allMappings.AddRange(ModelToUniqueTypeHealth);
			var skinCache = new Dictionary<string, (byte[] rgba, int w, int h)>(StringComparer.OrdinalIgnoreCase);
			int count = 0;
			foreach (var entry in allMappings)
			{
				string pakName = entry.model.Replace('\\', '/');
				int uniqueType = entry.uniqueType;
				byte[] mdlData = pak.ReadFile(pakName);
				if (mdlData == null)
				{
					Console.WriteLine("  skip " + pakName + " (type " + uniqueType + "): not in pak");
					continue;
				}
				byte[] skinRgba;
				int w, h;
				if (skinCache.TryGetValue(pakName, out var cached))
				{
					skinRgba = cached.rgba;
					w = cached.w;
					h = cached.h;
				}
				else
				{
					string failReason;
					skinRgba = ExtractFirstSkin(mdlData, out failReason);
					if (skinRgba == null)
					{
						Console.WriteLine("  skip " + pakName + ": " + failReason);
						continue;
					}
					w = BitConverter.ToInt32(mdlData, 52);
					h = BitConverter.ToInt32(mdlData, 56);
					skinCache[pakName] = (skinRgba, w, h);
				}
				int fullW = w, fullH = h;
				byte[] fullRgba = skinRgba;
				ScaleToTargetSize(ref fullRgba, ref fullW, ref fullH);
				// Save full image to SpritesFull
				WritePng(Path.Combine(spritesFullPath, uniqueType + ".png"), fullW, fullH, fullRgba);
				// Crop to first half (front side) for editor Sprites folder
				byte[] frontRgba; int frontW, frontH;
				CropToFirstHalf(fullRgba, fullW, fullH, out frontRgba, out frontW, out frontH);
				WritePng(Path.Combine(outPath, uniqueType + ".png"), frontW, frontH, frontRgba);
				Console.WriteLine("  " + pakName + " " + w + "x" + h + " -> " + uniqueType + ".png (front " + frontW + "x" + frontH + ", full " + fullW + "x" + fullH + ")");
				count++;
			}
			Console.WriteLine("Done. Wrote " + count + " front-half sprites to " + outPath + ", " + count + " full to " + spritesFullPath);
			return 0;
		}

		static byte[] ExtractFirstSkin(byte[] mdl, out string failReason)
		{
			failReason = "";
			if (mdl.Length < 84) { failReason = "mdl too short"; return null; }
			int ident = BitConverter.ToInt32(mdl, 0);
			if (ident != IDPOLYHEADER) { failReason = "ident=0x" + ident.ToString("X8") + " (expected 0x4F505449 IDPO)"; return null; }
			int version = BitConverter.ToInt32(mdl, 4);
			if (version != ALIAS_VERSION) { failReason = "version=" + version + " (expected " + ALIAS_VERSION + ")"; return null; }
			int numskins = BitConverter.ToInt32(mdl, 48);
			int skinwidth = BitConverter.ToInt32(mdl, 52);
			int skinheight = BitConverter.ToInt32(mdl, 56);
			if (skinwidth <= 0 || skinheight <= 0 || numskins < 1) { failReason = "skin dims=" + skinwidth + "x" + skinheight + " numskins=" + numskins; return null; }
			int skinSize = skinwidth * skinheight;
			int pos = 84; // sizeof(mdl_t)
			if (pos + 4 > mdl.Length) { failReason = "no skin header"; return null; }
			int skinType = BitConverter.ToInt32(mdl, pos);
			pos += 4;
			byte[] skinPixels;
			if (skinType == ALIAS_SKIN_SINGLE)
			{
				if (pos + skinSize > mdl.Length) { failReason = "single skin truncated"; return null; }
				skinPixels = new byte[skinSize];
				Buffer.BlockCopy(mdl, pos, skinPixels, 0, skinSize);
			}
			else
			{
				if (pos + 4 > mdl.Length) { failReason = "group skin truncated"; return null; }
				int numInGroup = BitConverter.ToInt32(mdl, pos);
				pos += 4 + numInGroup * 4;
				if (pos + skinSize > mdl.Length) { failReason = "group skin data truncated"; return null; }
				skinPixels = new byte[skinSize];
				Buffer.BlockCopy(mdl, pos, skinPixels, 0, skinSize);
			}
			// Convert indexed to BGRA for GDI+ Bitmap (Format32bppArgb uses BGRA in memory on Windows; PNG save outputs correct RGB).
			byte[] rgba = new byte[skinSize * 4];
			for (int i = 0; i < skinSize; i++)
			{
				int idx = skinPixels[i] & 0xFF;
				int p = idx * 3;
				rgba[i * 4]     = QuakePalette[p + 2]; // B first (BGRA)
				rgba[i * 4 + 1] = QuakePalette[p + 1]; // G
				rgba[i * 4 + 2] = QuakePalette[p];     // R
				rgba[i * 4 + 3] = (byte)(idx == 255 ? 0 : 255);
			}
			return rgba;
		}

		/// <summary>Extract the first half (left side = front) of the image for editor display. Quake MDL skins pack front+back side by side.</summary>
		static void CropToFirstHalf(byte[] rgba, int width, int height, out byte[] outRgba, out int outWidth, out int outHeight)
		{
			int halfW = Math.Max(1, width / 2);
			outWidth = halfW;
			outHeight = height;
			outRgba = new byte[halfW * height * 4];
			int srcRowBytes = width * 4;
			int dstRowBytes = halfW * 4;
			for (int y = 0; y < height; y++)
				Buffer.BlockCopy(rgba, y * srcRowBytes, outRgba, y * dstRowBytes, dstRowBytes);
		}

		/// <summary>Scale RGBA so longest side is TARGET_MAX_SIZE. Preserves aspect ratio.</summary>
		static void ScaleToTargetSize(ref byte[] rgba, ref int width, ref int height)
		{
			int maxSide = Math.Max(width, height);
			if (maxSide <= 0 || maxSide == TARGET_MAX_SIZE) return;
			double scale = (double)TARGET_MAX_SIZE / maxSide;
			int newW = Math.Max(1, (int)Math.Round(width * scale));
			int newH = Math.Max(1, (int)Math.Round(height * scale));
			using (var src = new Bitmap(width, height, PixelFormat.Format32bppArgb))
			{
				var bd = src.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
				int rowBytes = width * 4;
				for (int y = 0; y < height; y++)
					Marshal.Copy(rgba, y * rowBytes, IntPtr.Add(bd.Scan0, y * bd.Stride), rowBytes);
				src.UnlockBits(bd);
				using (var dst = new Bitmap(newW, newH, PixelFormat.Format32bppArgb))
				using (var g = Graphics.FromImage(dst))
				{
					g.InterpolationMode = InterpolationMode.HighQualityBicubic;
					g.SmoothingMode = SmoothingMode.HighQuality;
					g.PixelOffsetMode = PixelOffsetMode.HighQuality;
					g.DrawImage(src, 0, 0, newW, newH);
					var dstBd = dst.LockBits(new Rectangle(0, 0, newW, newH), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
					rgba = new byte[newW * newH * 4];
					for (int y = 0; y < newH; y++)
						Marshal.Copy(IntPtr.Add(dstBd.Scan0, y * dstBd.Stride), rgba, y * newW * 4, newW * 4);
					dst.UnlockBits(dstBd);
				}
			}
			width = newW;
			height = newH;
		}

		static void WritePng(string path, int width, int height, byte[] rgba)
		{
			using (var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb))
			{
				var bd = bmp.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
				int rowBytes = width * 4;
				for (int y = 0; y < height; y++)
					Marshal.Copy(rgba, y * rowBytes, IntPtr.Add(bd.Scan0, y * bd.Stride), rowBytes);
				bmp.UnlockBits(bd);
				bmp.Save(path, ImageFormat.Png);
			}
		}

		class PakFile
		{
			readonly string _path;
			readonly Dictionary<string, (int offset, int size)> _dir = new Dictionary<string, (int, int)>(StringComparer.OrdinalIgnoreCase);

			public PakFile(string pakPath)
			{
				_path = pakPath;
				using (var fs = File.OpenRead(pakPath))
				using (var br = new BinaryReader(fs))
				{
					if (br.ReadInt32() != 0x4B434150) return; // "PACK"
					int diroffset = br.ReadInt32();
					int dirsize = br.ReadInt32();
					fs.Seek(diroffset, SeekOrigin.Begin);
					int count = dirsize / 64;
					for (int i = 0; i < count; i++)
					{
						char[] nameChars = br.ReadChars(56);
						int offset = br.ReadInt32();
						int size = br.ReadInt32();
						string name = new string(nameChars).TrimEnd('\0');
						if (string.IsNullOrEmpty(name)) continue;
						_dir[name.Replace('\\', '/')] = (offset, size);
					}
				}
			}

			public int GetEntryCount() { return _dir.Count; }

			public byte[] ReadFile(string name)
			{
				name = name.Replace('\\', '/');
				if (!_dir.TryGetValue(name, out var e)) return null;
				using (var fs = File.OpenRead(_path))
				{
					fs.Seek(e.offset, SeekOrigin.Begin);
					byte[] data = new byte[e.size];
					fs.Read(data, 0, data.Length);
					return data;
				}
			}

			public System.Collections.Generic.IEnumerable<string> ListProgsMdl()
			{
				foreach (var k in _dir.Keys)
					if (k.StartsWith("progs/", StringComparison.OrdinalIgnoreCase) && k.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase))
						yield return k;
			}
		}
	}
}



