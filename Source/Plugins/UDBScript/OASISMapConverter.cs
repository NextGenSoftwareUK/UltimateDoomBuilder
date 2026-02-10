#region ================== OASIS STAR – Map conversion

/*
 * Converts between OQUAKE .map (entity/brush text) and ODOOM (WAD/things).
 * Quake .map → Doom: extract entities, map classnames to Doom thing types, export thing list or minimal map.
 * Doom → Quake .map: read map things from WAD, write Quake .map with point entities.
 */

#endregion

#region ================== Namespaces

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using CodeImp.DoomBuilder.Windows;
using CodeImp.DoomBuilder.Map;

#endregion

namespace CodeImp.DoomBuilder.UDBScript
{
	public static class OASISMapConverter
	{
		private static readonly Dictionary<string, int> QuakeKeyToDoom = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
		{
			{ "key_silver", 13 }, { "key_gold", 5 }
		};

		private static readonly Dictionary<string, int> QuakeItemToDoom = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
		{
			{ "weapon_shotgun", 2001 }, { "weapon_supershotgun", 2001 }, { "weapon_nailgun", 2002 },
			{ "weapon_supernailgun", 2002 }, { "weapon_grenadelauncher", 2003 }, { "weapon_rocketlauncher", 2003 },
			{ "weapon_lightning", 2004 }, { "item_shells", 2008 }, { "item_spikes", 2007 },
			{ "item_rockets", 2010 }, { "item_cells", 2047 }, { "item_health", 2011 },
			{ "item_health_small", 2012 }, { "item_armor1", 2015 }, { "item_armor2", 2016 },
			{ "item_armorInv", 2013 }, { "monster_grunt", 3004 }, { "monster_ogre", 9 },
			{ "monster_demon", 3002 }, { "monster_dog", 3002 }, { "monster_shambler", 3003 },
			{ "monster_zombie", 3004 }, { "monster_hell_knight", 69 }, { "monster_enforcer", 66 },
			{ "monster_fish", 3005 }, { "monster_spawn", 68 }
		};

		/// <summary>
		/// Convert a Quake .map file to a Doom thing list (JSON-style) or simple text for import.
		/// </summary>
		public static void ConvertQuakeToDoom(IWin32Window owner)
		{
			using (var ofd = new OpenFileDialog())
			{
				ofd.Title = "Select OQUAKE .map file";
				ofd.Filter = "Quake map (*.map)|*.map|All files (*.*)|*.*";
				if (ofd.ShowDialog(owner) != DialogResult.OK) return;
				string inPath = ofd.FileName;
				string outPath = Path.Combine(Path.GetDirectoryName(inPath), Path.GetFileNameWithoutExtension(inPath) + "_doom_things.txt");
				try
				{
					var entities = ParseQuakeMapEntities(inPath);
					var sb = new StringBuilder();
					sb.AppendLine("# OASIS STAR – Doom thing list from " + Path.GetFileName(inPath));
					sb.AppendLine("# Format: x y type (Doom thing type)");
					foreach (var e in entities)
					{
						int doomType;
						if (e.Classname.Equals("worldspawn", StringComparison.OrdinalIgnoreCase)) continue;
						if (QuakeKeyToDoom.TryGetValue(e.Classname, out doomType) || QuakeItemToDoom.TryGetValue(e.Classname, out doomType))
							sb.AppendLine(e.OriginX + " " + e.OriginY + " " + doomType);
					}
					File.WriteAllText(outPath, sb.ToString());
					General.Interface.DisplayStatus(CodeImp.DoomBuilder.Windows.StatusType.Info, "OASIS STAR: Exported thing list to " + outPath);
				}
				catch (Exception ex)
				{
					MessageBox.Show("Conversion failed: " + ex.Message, "OASIS STAR", MessageBoxButtons.OK, MessageBoxIcon.Warning);
				}
			}
		}

		/// <summary>
		/// Convert current Doom map (or selected WAD) to Quake .map point entities.
		/// </summary>
		public static void ConvertDoomToQuake(IWin32Window owner)
		{
			if (General.Map != null)
			{
				string mapPath = General.Map.FilePathName ?? "";
				string outPath = Path.Combine(Path.GetDirectoryName(mapPath) ?? "", Path.GetFileNameWithoutExtension(mapPath) + "_quake.map");
				using (var sfd = new SaveFileDialog())
				{
					sfd.Title = "Save as OQUAKE .map";
					sfd.Filter = "Quake map (*.map)|*.map|All files (*.*)|*.*";
					sfd.FileName = Path.GetFileName(outPath);
					sfd.InitialDirectory = Path.GetDirectoryName(outPath) ?? Environment.CurrentDirectory;
					if (sfd.ShowDialog(owner) != DialogResult.OK) return;
					outPath = sfd.FileName;
				}
				try
				{
					var sb = new StringBuilder();
					sb.AppendLine("// OASIS STAR – Quake .map from current Doom map");
					foreach (Thing t in General.Map.Map.Things)
					{
						string classname = DoomThingTypeToQuakeClassname(t.Type);
						sb.AppendLine("{");
						sb.AppendLine("  \"classname\" \"" + classname + "\"");
						sb.AppendLine("  \"origin\" \"" + (int)t.Position.x + " " + (int)t.Position.y + " 0\"");
						sb.AppendLine("}");
					}
					File.WriteAllText(outPath, sb.ToString());
					General.Interface.DisplayStatus(CodeImp.DoomBuilder.Windows.StatusType.Info, "OASIS STAR: Exported to " + outPath);
				}
				catch (Exception ex)
				{
					MessageBox.Show("Conversion failed: " + ex.Message, "OASIS STAR", MessageBoxButtons.OK, MessageBoxIcon.Warning);
				}
			}
			else
			{
				MessageBox.Show("Open a map first, or use File → Export for a WAD.", "OASIS STAR", MessageBoxButtons.OK, MessageBoxIcon.Information);
			}
		}

		private static string DoomThingTypeToQuakeClassname(int type)
		{
			switch (type)
			{
				case 5: return "key_gold";
				case 13: return "key_silver";
				case 2001: return "weapon_shotgun";
				case 2002: return "weapon_nailgun";
				case 2003: return "weapon_rocketlauncher";
				case 2011: return "item_health";
				case 2012: return "item_health_small";
				case 3004: return "monster_grunt";
				case 9: return "monster_ogre";
				case 3002: return "monster_demon";
				case 3003: return "monster_shambler";
				default: return "info_null";
			}
		}

		private struct QuakeEntity
		{
			public string Classname;
			public double OriginX, OriginY, OriginZ;
		}

		private static List<QuakeEntity> ParseQuakeMapEntities(string path)
		{
			var list = new List<QuakeEntity>();
			string text = File.ReadAllText(path);
			// Simple brace matching: find { ... } blocks and parse "classname" "value" and "origin" "x y z"
			var blockRegex = new Regex(@"\{([^{}]*(?:\{[^{}]*\}[^{}]*)*)\}", RegexOptions.Singleline);
			var keyValRegex = new Regex(@"""([^""]+)""\s+""([^""]*)""", RegexOptions.Singleline);
			foreach (Match block in blockRegex.Matches(text))
			{
				string inner = block.Groups[1].Value;
				string classname = null;
				double ox = 0, oy = 0, oz = 0;
				foreach (Match kv in keyValRegex.Matches(inner))
				{
					string k = kv.Groups[1].Value.Trim();
					string v = kv.Groups[2].Value.Trim();
					if (k.Equals("classname", StringComparison.OrdinalIgnoreCase)) classname = v;
					if (k.Equals("origin", StringComparison.OrdinalIgnoreCase))
					{
						var parts = v.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
						if (parts.Length >= 3)
						{
							double.TryParse(parts[0], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out ox);
							double.TryParse(parts[1], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out oy);
							double.TryParse(parts[2], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out oz);
						}
					}
				}
				if (!string.IsNullOrEmpty(classname))
					list.Add(new QuakeEntity { Classname = classname, OriginX = ox, OriginY = oy, OriginZ = oz });
			}
			return list;
		}
	}
}




