// Extract OQUAKE sprites from Quake 1 id1/pak0.pak (MDL skins) to UDB OASIS Sprites folder.
// Run: ExtractOquakeSprites.exe [id1_path] [output_sprites_path]
// Example: ExtractOquakeSprites.exe C:\Source\vkQuake\id1 C:\Source\UltimateDoomBuilder\Assets\Common\UDBScript\Scripts\OASIS\Sprites

using System;
using System.Collections.Generic;
using System.Drawing;
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

		// Quake 1 progs model (in pak) -> OASIS thing type (same asset works in Doom & OQuake)
		static readonly Dictionary<string, int> ModelToThingType = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
		{
			{ "progs/w_s_key.mdl", 13 },   // Silver key (interop: collect in Doom, use in OQuake)
			{ "progs/w_g_key.mdl", 5 },    // Gold key
			{ "progs/g_shot.mdl", 2001 },
			{ "progs/g_nail.mdl", 2002 },
			{ "progs/g_rock.mdl", 2003 },
			{ "progs/g_light.mdl", 2004 },
			{ "progs/armor.mdl", 2015 },
			{ "progs/backpack.mdl", 2049 },
			{ "progs/ogre.mdl", 9 },
			{ "progs/demon.mdl", 3002 },
			{ "progs/shambler.mdl", 3003 },
			{ "progs/soldier.mdl", 3004 },
			{ "progs/zombie.mdl", 3011 },
			{ "progs/knight.mdl", 69 },
			{ "progs/wizard.mdl", 66 },
			{ "progs/dog.mdl", 3010 },
		};

		// Standard Quake 1 palette (256 * 3 bytes). Index 255 = transparent in Quake.
		static readonly byte[] QuakePalette = LoadQuakePalette();

		static byte[] LoadQuakePalette()
		{
			// Default Quake 1 palette (first 256 colors). Entry 255 is often green/magenta for transparency.
			var p = new byte[768];
			for (int i = 0; i < 256; i++)
			{
				// Simple fallback: grayscale + index 255 = magenta (transparent)
				if (i == 255) { p[i * 3] = 255; p[i * 3 + 1] = 0; p[i * 3 + 2] = 255; }
				else { byte b = (byte)i; p[i * 3] = b; p[i * 3 + 1] = b; p[i * 3 + 2] = b; }
			}
			return p;
		}

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

			int count = 0;
			foreach (var kv in ModelToThingType)
			{
				string pakName = kv.Key.Replace('\\', '/');
				int doomType = kv.Value;
				byte[] mdlData = pak.ReadFile(pakName);
				if (mdlData == null)
				{
					Console.WriteLine("  skip " + pakName + ": not in pak");
					continue;
				}
				string failReason;
				byte[] skinRgba = ExtractFirstSkin(mdlData, out failReason);
				if (skinRgba == null)
				{
					Console.WriteLine("  skip " + pakName + ": " + failReason);
					continue;
				}
				int w = BitConverter.ToInt32(mdlData, 52);
				int h = BitConverter.ToInt32(mdlData, 56);
				string pngPath = Path.Combine(outPath, doomType + ".png");
				WritePng(pngPath, w, h, skinRgba);
				Console.WriteLine("  " + pakName + " -> " + doomType + ".png");
				count++;
			}
			Console.WriteLine("Done. Wrote " + count + " sprites to " + outPath);
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
			// Convert indexed to RGBA using palette; index 255 = transparent
			byte[] rgba = new byte[skinSize * 4];
			for (int i = 0; i < skinSize; i++)
			{
				int idx = skinPixels[i] & 0xFF;
				rgba[i * 4]     = QuakePalette[idx * 3];
				rgba[i * 4 + 1] = QuakePalette[idx * 3 + 1];
				rgba[i * 4 + 2] = QuakePalette[idx * 3 + 2];
				rgba[i * 4 + 3] = (byte)(idx == 255 ? 0 : 255);
			}
			return rgba;
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


