#region ================== Copyright (c) 2020 Boris Iwanski

/*
 * This program is free software: you can redistribute it and/or modify
 *
 * it under the terms of the GNU General Public License as published by
 * 
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 * 
 * This program is distributed in the hope that it will be useful,
 *    but WITHOUT ANY WARRANTY; without even the implied warranty of
 * 
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.See the
 * 
 * GNU General Public License for more details.
 * 
 * You should have received a copy of the GNU General Public License
 * along with this program.If not, see<http://www.gnu.org/licenses/>.
 */

#endregion

#region ================== Namespaces

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Dynamic;
using System.IO;
using System.Reflection;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using CodeImp.DoomBuilder.Actions;
using CodeImp.DoomBuilder.BuilderModes;
using CodeImp.DoomBuilder.Controls;
using CodeImp.DoomBuilder.Data;
using CodeImp.DoomBuilder.Editing;
using CodeImp.DoomBuilder.Geometry;
using CodeImp.DoomBuilder.IO;
using CodeImp.DoomBuilder.Map;
using CodeImp.DoomBuilder.Plugins;
using CodeImp.DoomBuilder.Types;
using CodeImp.DoomBuilder.UDBScript.Wrapper;
using CodeImp.DoomBuilder.VisualModes;
using CodeImp.DoomBuilder.Windows;

#endregion

namespace CodeImp.DoomBuilder.UDBScript
{
	internal class ScriptDirectoryStructure
	{
		public string Path;
		public string Name;
		public bool Expanded;
		public string Hash;
		public List<ScriptDirectoryStructure> Directories;
		public List<ScriptInfo> Scripts;

		public ScriptDirectoryStructure(string path, string name, bool expanded, string hash)
		{
			Path = path;
			Name = name;
			Expanded = expanded;
			Hash = hash;
			Directories = new List<ScriptDirectoryStructure>();
			Scripts = new List<ScriptInfo>();
		}
	}

	public class BuilderPlug : Plug
	{
		#region ================== Constants

		private static readonly string SCRIPT_FOLDER = "UDBScript";
		private static readonly string OASIS_SPRITES_FOLDER = "Sprites"; // UDBScript/Scripts/OASIS/Sprites (thing type PNGs)
		/// <summary>OQUAKE/OASIS unique thing types start at 5000; only these get display-pack overrides (avoid looking for 1.png etc.).</summary>
		private const int OASIS_THING_TYPE_MIN = 5000;
		// OQUAKE compatibility: these monster ids are intentionally below 5000.
		private static readonly HashSet<int> OASIS_LEGACY_THING_TYPES = new HashSet<int> { 3010, 3011 };
		public static readonly uint UDB_SCRIPT_VERSION = 5;

		#endregion

		private delegate void CallVoidMethodDeletage();

		private static bool IsOasisThingTypeForOverrides(int thingType)
		{
			return thingType >= OASIS_THING_TYPE_MIN || OASIS_LEGACY_THING_TYPES.Contains(thingType);
		}

		#region ================== Constants

		public const int NUM_SCRIPT_SLOTS = 30;

		#endregion

		#region ================== Variables

		private static BuilderPlug me;
		private ScriptDockerControl panel;
		private Docker docker;
		private string currentscriptfile;
		private ScriptInfo currentscript;
		private ScriptRunner scriptrunner;
		private List<ScriptInfo> scriptinfo;
		private ScriptDirectoryStructure scriptdirectorystructure;
		private FileSystemWatcher watcher;
		private object lockobj;
		private Dictionary<int, ScriptInfo> scriptslots;
		private string editorexepath;
		private PreferencesForm preferencesform;
		private ScriptRunnerForm scriptrunnerform;
		// OASIS STAR API
		private ToolStripMenuItem menuStar;
		private ToolStripMenuItem menuStarUnderTools;
		private ToolStripMenuItem menuStarUnderView;
		private ToolStripButton buttonStar;
		private OASISStarPanel starPanel;
		private Docker starDocker;
		// Click-to-place: after user selects asset in STAR dialog, next map click places it
		private int? pendingStarThingType;
		private string pendingStarAssetName;
		// OASIS display pack: optional PNGs per thing type for editor-only sprites (OQUAKE/ODOOM)
		private readonly Dictionary<int, ImageData> thingSpriteOverrideCache = new Dictionary<int, ImageData>();
		private readonly object thingSpriteOverrideLock = new object();
		private static bool loggedOasisSpritePathWarning;
		// OASIS STAR metadata: thing type -> (title, class/id), loaded from OASIS_STAR_Place_Selected.js
		private readonly Dictionary<int, KeyValuePair<string, string>> thingInfoOverrideCache = new Dictionary<int, KeyValuePair<string, string>>();
		private readonly object thingInfoOverrideLock = new object();
		private bool thingInfoOverrideLoaded;
		private static bool loggedOasisThingInfoPathWarning;

		#endregion

		#region ================== Properties

		public static BuilderPlug Me { get { return me; } }
		public string CurrentScriptFile { get { return currentscriptfile; } set { currentscriptfile = value; } }
		internal ScriptInfo CurrentScript { get { return currentscript; } set { currentscript = value; } }
		internal ScriptRunner ScriptRunner { get { return scriptrunner; } }
		internal ScriptDirectoryStructure ScriptDirectoryStructure { get { return scriptdirectorystructure; } }
		internal string EditorExePath { get { return editorexepath; } }
		public ScriptRunnerForm ScriptRunnerForm { get { return scriptrunnerform; } }
		/// <summary>True when OASIS STAR script set click-to-place; script runner should close so user can click the map.</summary>
		internal bool HasPendingStarPlacement { get { return pendingStarThingType.HasValue; } }

		#endregion

		public override void OnInitialize()
		{
			base.OnInitialize();

			me = this;

			lockobj = new object();

			scriptinfo = new List<ScriptInfo>();
			scriptslots = new Dictionary<int, ScriptInfo>();

			panel = new ScriptDockerControl(SCRIPT_FOLDER);
			docker = new Docker("udbscript", "Scripts", panel);
			General.Interface.AddDocker(docker);

			General.Actions.BindMethods(this);

			string scriptspath = Path.Combine(General.AppPath, SCRIPT_FOLDER, "Scripts");

			if (Directory.Exists(scriptspath))
			{
				watcher = new FileSystemWatcher(Path.Combine(General.AppPath, SCRIPT_FOLDER, "Scripts"));
				watcher.NotifyFilter = NotifyFilters.CreationTime | NotifyFilters.DirectoryName | NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size;
				watcher.IncludeSubdirectories = true;
				watcher.Changed += OnWatcherEvent;
				watcher.Created += OnWatcherEvent;
				watcher.Deleted += OnWatcherEvent;
				watcher.Renamed += OnWatcherEvent;
			}

			editorexepath = General.Settings.ReadPluginSetting("externaleditor", string.Empty);

			scriptrunnerform = new ScriptRunnerForm();

			FindEditor();

			// OASIS STAR: add menu and button once (same as other toolbar items); visibility follows map loaded
			try
			{
				var placeItem = new ToolStripMenuItem("Place ODOOM / OQUAKE asset at cursor...");
				placeItem.Tag = "oasisstar_place_selected";
				placeItem.Click += (s, e) => General.Interface.InvokeTaggedAction(s, e);
				var showPanelItem = new ToolStripMenuItem("Open OASIS STAR panel");
				showPanelItem.Tag = "oasisstar_show_panel";
				showPanelItem.Click += (s, e) => General.Interface.InvokeTaggedAction(s, e);
				var convQ2D = new ToolStripMenuItem("Convert OQUAKE .map → ODOOM...");
				convQ2D.Tag = "oasisstar_convert_quake2doom";
				convQ2D.Click += (s, e) => General.Interface.InvokeTaggedAction(s, e);
				var convD2Q = new ToolStripMenuItem("Convert ODOOM map → OQUAKE .map...");
				convD2Q.Tag = "oasisstar_convert_doom2quake";
				convD2Q.Click += (s, e) => General.Interface.InvokeTaggedAction(s, e);

				menuStar = new ToolStripMenuItem("★ STAR");
				menuStar.Name = "menuStar";
				menuStar.DropDownItems.Add(placeItem);
				menuStar.DropDownItems.Add(showPanelItem);
				menuStar.DropDownItems.Add(new ToolStripSeparator());
				menuStar.DropDownItems.Add(convQ2D);
				menuStar.DropDownItems.Add(convD2Q);

				menuStarUnderTools = new ToolStripMenuItem("OASIS STAR");
				menuStarUnderTools.DropDownItems.Add(new ToolStripMenuItem("Place ODOOM / OQUAKE asset at cursor...") { Tag = "oasisstar_place_selected" });
				menuStarUnderTools.DropDownItems.Add(new ToolStripMenuItem("Open OASIS STAR panel") { Tag = "oasisstar_show_panel" });
				menuStarUnderTools.DropDownItems.Add(new ToolStripSeparator());
				menuStarUnderTools.DropDownItems.Add(new ToolStripMenuItem("Convert OQUAKE .map → ODOOM...") { Tag = "oasisstar_convert_quake2doom" });
				menuStarUnderTools.DropDownItems.Add(new ToolStripMenuItem("Convert ODOOM map → OQUAKE .map...") { Tag = "oasisstar_convert_doom2quake" });
				foreach (ToolStripItem i in menuStarUnderTools.DropDownItems)
					if (i is ToolStripMenuItem mi && mi.Tag != null) mi.Click += (s, e) => General.Interface.InvokeTaggedAction(s, e);

				menuStarUnderView = new ToolStripMenuItem("OASIS STAR");
				menuStarUnderView.DropDownItems.Add(new ToolStripMenuItem("Place ODOOM / OQUAKE asset at cursor...") { Tag = "oasisstar_place_selected" });
				menuStarUnderView.DropDownItems.Add(new ToolStripMenuItem("Open OASIS STAR panel") { Tag = "oasisstar_show_panel" });
				menuStarUnderView.DropDownItems.Add(new ToolStripSeparator());
				menuStarUnderView.DropDownItems.Add(new ToolStripMenuItem("Convert OQUAKE .map → ODOOM...") { Tag = "oasisstar_convert_quake2doom" });
				menuStarUnderView.DropDownItems.Add(new ToolStripMenuItem("Convert ODOOM map → OQUAKE .map...") { Tag = "oasisstar_convert_doom2quake" });
				foreach (ToolStripItem i in menuStarUnderView.DropDownItems)
					if (i is ToolStripMenuItem mi && mi.Tag != null) mi.Click += (s, e) => General.Interface.InvokeTaggedAction(s, e);

				buttonStar = new ToolStripButton();
				buttonStar.DisplayStyle = ToolStripItemDisplayStyle.Text;
				buttonStar.Text = "★";
				buttonStar.ToolTipText = "OASIS STAR – Select asset, then click on the map to place";
				buttonStar.Tag = "oasisstar_place_selected";
				buttonStar.Click += (s, e) => General.Interface.InvokeTaggedAction(s, e);

				General.Interface.AddMenu(menuStar, CodeImp.DoomBuilder.Windows.MenuSection.Top);
				General.Interface.AddMenu(menuStarUnderTools, CodeImp.DoomBuilder.Windows.MenuSection.ToolsTesting);
				General.Interface.AddMenu(menuStarUnderView, CodeImp.DoomBuilder.Windows.MenuSection.ViewScriptEdit);
				General.Interface.AddButton(buttonStar, CodeImp.DoomBuilder.Windows.ToolbarSection.Testing);

				// Hide menus until a map is open (button visibility is set by MainForm UpdateToolbar with maploaded)
				menuStar.Visible = false;
				menuStarUnderTools.Visible = false;
				menuStarUnderView.Visible = false;
			}
			catch (Exception ex)
			{
				General.ErrorLogger.Add(CodeImp.DoomBuilder.ErrorType.Error, "OASIS STAR init failed: " + ex.Message);
				menuStar = null;
				menuStarUnderTools = null;
				menuStarUnderView = null;
				buttonStar = null;
			}
		}

		public override void OnMapNewEnd()
		{
			base.OnMapNewEnd();

			AddStarUi();
			// Methods called by LoadScripts might sleep for some time, so call LoadScripts asynchronously
			new Task(LoadScripts).Start();

			if (watcher != null)
				watcher.EnableRaisingEvents = true;
		}

		public override void OnMapOpenEnd()
		{
			base.OnMapOpenEnd();

			AddStarUi();
			// Methods called by LoadScripts might sleep for some time, so call LoadScripts asynchronously
			new Task(LoadScripts).Start();

			if (watcher != null)
				watcher.EnableRaisingEvents = true;
		}

		public override void OnMapCloseBegin()
		{
			RemoveStarUi();
			if (watcher != null)
				watcher.EnableRaisingEvents = false;

			SaveScriptSlotsAndOptions();
			SaveScriptDirectoryExpansionStatus(scriptdirectorystructure);
		}

		public override void OnShowPreferences(PreferencesController controller)
		{
			base.OnShowPreferences(controller);

			preferencesform = new PreferencesForm();
			preferencesform.Setup(controller);
		}

		public override void OnClosePreferences(PreferencesController controller)
		{
			base.OnClosePreferences(controller);

			preferencesform.Dispose();
			preferencesform = null;
		}

		private void OnWatcherEvent(object sender, FileSystemEventArgs e)
		{
			// We can't use the filter on the watcher, since for whatever reason that filter also applies to
			// directory names. So we have to do some filtering ourselves.
			bool load = false;
			if (e.ChangeType == WatcherChangeTypes.Deleted || (Directory.Exists(e.FullPath) && e.ChangeType != WatcherChangeTypes.Changed) || Path.GetExtension(e.FullPath).ToLowerInvariant() == ".js")
				load = true;

			if(load)
				LoadScripts();
		}

		private void AddStarUi()
		{
			if (menuStar != null)
			{
				menuStar.Visible = true;
				menuStarUnderTools.Visible = true;
				menuStarUnderView.Visible = true;
			}
		}

		private void RemoveStarUi()
		{
			if (menuStar != null)
			{
				menuStar.Visible = false;
				menuStarUnderTools.Visible = false;
				menuStarUnderView.Visible = false;
			}
		}

		// This is called when the plugin is terminated
		public override void Dispose()
		{
			try
			{
				if (menuStar != null) General.Interface.RemoveMenu(menuStar);
				if (menuStarUnderTools != null) General.Interface.RemoveMenu(menuStarUnderTools);
				if (menuStarUnderView != null) General.Interface.RemoveMenu(menuStarUnderView);
				if (buttonStar != null) General.Interface.RemoveButton(buttonStar);
				if (starDocker != null) General.Interface.RemoveDocker(starDocker);
			}
			catch (Exception ex)
			{
				General.WriteLogLine("OASIS STAR dispose: " + ex.Message);
			}
			menuStar = null;
			menuStarUnderTools = null;
			menuStarUnderView = null;
			buttonStar = null;
			starPanel = null;
			starDocker = null;
			base.Dispose();

			// This must be called to remove bound methods for actions.
			General.Actions.UnbindMethods(this);
		}

		internal void SaveScriptSlotsAndOptions()
		{
			// Save the script option values
			foreach (ScriptInfo si in scriptinfo)
				si.SaveOptionValues();

			// Save the script slots
			foreach (KeyValuePair<int, ScriptInfo> kvp in scriptslots)
			{
				if (kvp.Value == null || string.IsNullOrWhiteSpace(kvp.Value.ScriptFile))
					continue;

				General.Settings.WritePluginSetting("scriptslots.slot" + kvp.Key, kvp.Value.ScriptFile);
			}
		}

		internal void SaveScriptDirectoryExpansionStatus(ScriptDirectoryStructure root)
		{
			if (root == null)
				return;

			if(root.Expanded)
			{
				General.Settings.DeletePluginSetting("directoryexpand." + root.Hash);
			}
			else
			{
				General.Settings.WritePluginSetting("directoryexpand." + root.Hash, false);
			}

			foreach (ScriptDirectoryStructure sds in root.Directories)
				SaveScriptDirectoryExpansionStatus(sds);
		}

		private void FindEditor()
		{
			if (!string.IsNullOrWhiteSpace(editorexepath))
				return;

			string editor = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "notepad.exe");

			if (!File.Exists(editor))
				return;

			editorexepath = editor;
		}

		/// <summary>
		/// Sets the new external editor exe path.
		/// </summary>
		/// <param name="exepath">Path and file name of the external editor</param>
		internal void SetEditor(string exepath)
		{
			if (!string.IsNullOrWhiteSpace(exepath))
			{
				editorexepath = exepath;
				General.Settings.WritePluginSetting("externaleditor", editorexepath);
			}
		}

		/// <summary>
		/// Opens a script in the external editor.
		/// </summary>
		/// <param name="file"></param>
		internal void EditScript(string file)
		{
			if(string.IsNullOrWhiteSpace(editorexepath))
			{
				MessageBox.Show("No external editor set. Please set the external editor in the UDBScript tab in the preferences.");
				return;
			}

			Process p = new Process();
			p.StartInfo.FileName = editorexepath;
			p.StartInfo.Arguments = "\"" + file + "\""; // File name might contain spaces, so put it in quotes
			p.Start();
		}

		/// <summary>
		/// Sets a ScriptInfo to a specific slot.
		/// </summary>
		/// <param name="slot">The slot</param>
		/// <param name="si">The ScriptInfo to assign to the slot. Pass null to clear the slot</param>
		public void SetScriptSlot(int slot, ScriptInfo si)
		{
			if (si == null)
			{
				scriptslots.Remove(slot);
			}
			else
			{
				// Check if the ScriptInfo is already assigned to a slot, and remove it if so
				// Have to use ToList because otherwise the collection would be changed while iterating over it
				foreach (int s in scriptslots.Keys.ToList())
					if (scriptslots[s] == si)
						scriptslots[s] = null;

				scriptslots[slot] = si;
			}

			SaveScriptSlotsAndOptions();
		}

		/// <summary>
		/// Gets a ScriptInfo for a specific script slot.
		/// </summary>
		/// <param name="slot">The slot to get the ScriptInfo for</param>
		/// <returns>The ScriptInfo for the slot, or null if the ScriptInfo is at no slot</returns>
		public ScriptInfo GetScriptSlot(int slot)
		{
			if (scriptslots.ContainsKey(slot))
				return scriptslots[slot];
			else
				return null;
		}

		/// <summary>
		/// Gets the script slot by a ScriptInfo.
		/// </summary>
		/// <param name="si">The ScriptInfo to get the slot of</param>
		/// <returns>The slot the ScriptInfo is in, or 0 if the ScriptInfo is not assigned to a slot</returns>
		public int GetScriptSlotByScriptInfo(ScriptInfo si)
		{
			if (!scriptslots.Values.Contains(si))
				return 0;

			return scriptslots.FirstOrDefault(i => i.Value == si).Key;
		}

		/// <summary>
		/// Loads all scripts and fills the docker panel.
		/// </summary>
		public void LoadScripts()
		{
			lock (lockobj)
			{
				scriptinfo = new List<ScriptInfo>();
				scriptdirectorystructure = LoadScriptDirectoryStructure(Path.Combine(General.AppPath, SCRIPT_FOLDER, "Scripts"));

				scriptslots = new Dictionary<int, ScriptInfo>();
				for(int i=0; i < NUM_SCRIPT_SLOTS; i++)
				{
					int num = i + 1;
					string file = General.Settings.ReadPluginSetting("scriptslots.slot" + num, string.Empty);

					if (string.IsNullOrWhiteSpace(file))
						continue;

					foreach(ScriptInfo si in scriptinfo)
					{
						if (si.ScriptFile == file)
							scriptslots[num] = si;
					}
				}

				// This might not be called from the main thread when called by the file system watcher, so use a delegate
				// to run it cleanly
				if (panel.InvokeRequired)
				{
					CallVoidMethodDeletage d = panel.FillTree;
					panel.Invoke(d);
				}
				else
				{
					panel.FillTree();
				}
			}
		}

		/// <summary>
		/// Recursively load information about the script files in a directory and its subdirectories.
		/// </summary>
		/// <param name="path">Path to process</param>
		/// <returns>ScriptDirectoryStructure for the given path</returns>
		private ScriptDirectoryStructure LoadScriptDirectoryStructure(string path)
		{
			string hash = SHA256Hash.Get(path);
			bool expanded = General.Settings.ReadPluginSetting("directoryexpand." + hash, true);
			string name = path.TrimEnd(Path.DirectorySeparatorChar).Split(Path.DirectorySeparatorChar).Last();
			ScriptDirectoryStructure sds = new ScriptDirectoryStructure(path, name, expanded, hash);

			// Go through all subdirectories, skipping hidden ones (that start with a dot)
			foreach (string directory in Directory.EnumerateDirectories(path).Where(d => !Path.GetFileName(d).StartsWith(".")))
			{
				sds.Directories.Add(LoadScriptDirectoryStructure(directory));
			}

			foreach (string filename in Directory.EnumerateFiles(path, "*.js"))
			{
				bool retry = true;
				int retrycounter = 5;

				while (retry)
				{
					try
					{
						ScriptInfo si = new ScriptInfo(filename);
						sds.Scripts.Add(si);
						scriptinfo.Add(si);
						retry = false;
					}
					catch (IOException)
					{
						// The FileSystemWatcher can fire the event while the file is still being written, in that case we'll get
						// an IOException (file is locked by another process). So just try to load the file a couple times
						Thread.Sleep(100);
						retrycounter--;
						if (retrycounter == 0)
							retry = false;
					}
					catch (Exception e)
					{
						General.ErrorLogger.Add(ErrorType.Warning, "Failed to process " + filename + ": " + e.Message);
						General.WriteLogLine("Failed to process " + filename + ": " + e.Message);
						retry = false;
					}
				}
			}

			return sds;
		}

		/// <summary>
		/// Runs a script by its path relative to the Scripts folder (e.g. "OASIS\\OASIS_STAR_Place_Selected.js").
		/// Used by OASIS STAR toolbar/menu to place assets at cursor.
		/// </summary>
		/// <param name="relativePath">Path relative to UDBScript/Scripts (e.g. "OASIS\\OASIS_STAR_Place_Selected.js")</param>
		/// <returns>True if the script was found and run, false otherwise</returns>
		public bool RunScriptByPath(string relativePath)
		{
			if (string.IsNullOrWhiteSpace(relativePath))
				return false;
			string scriptsPath = Path.Combine(General.AppPath, SCRIPT_FOLDER, "Scripts");
			string fullPath = Path.Combine(scriptsPath, relativePath.TrimStart('\\', '/'));
			fullPath = Path.GetFullPath(fullPath);
			if (!fullPath.StartsWith(Path.GetFullPath(scriptsPath), StringComparison.OrdinalIgnoreCase))
				return false;
			foreach (ScriptInfo si in scriptinfo)
			{
				if (string.Equals(si.ScriptFile, fullPath, StringComparison.OrdinalIgnoreCase))
				{
					scriptrunner = new ScriptRunner(si);
					scriptrunnerform.ShowDialog();
					return true;
				}
			}
			// Script not in loaded list (e.g. OASIS folder not present); try loading from file
			if (File.Exists(fullPath))
			{
				try
				{
					ScriptInfo si = new ScriptInfo(fullPath);
					scriptrunner = new ScriptRunner(si);
					scriptrunnerform.ShowDialog();
					return true;
				}
				catch (Exception ex)
				{
					General.ErrorLogger.Add(CodeImp.DoomBuilder.ErrorType.Error, "OASIS STAR: Could not run script: " + ex.Message);
					return false;
				}
			}
			General.Interface.DisplayStatus(CodeImp.DoomBuilder.Windows.StatusType.Warning, "OASIS STAR: Script not found. Add OASIS scripts to UDBScript/Scripts/OASIS.");
			return false;
		}

		/// <summary>
		/// Gets the name of the script file. This is either read from the .cfg file of the script or taken from the file name
		/// </summary>
		/// <param name="filename">Full path with file name of the script</param>
		/// <returns></returns>
		public static string GetScriptName(string filename)
		{
			string configfile = Path.Combine(Path.GetDirectoryName(filename), Path.GetFileNameWithoutExtension(filename)) + ".cfg";

			if (File.Exists(configfile))
			{
				Configuration cfg = new Configuration(configfile, true);
				string name = cfg.ReadSetting("name", string.Empty);

				if (!string.IsNullOrEmpty(name))
					return name;
			}

			return Path.GetFileNameWithoutExtension(filename);
		}

		public void EndOptionEdit()
		{
			panel.EndEdit();
		}

		internal Vector3D GetVector3DFromObject(object data)
		{
			if (data is Vector2D)
				return (Vector2D)data;
			else if (data is Vector2DWrapper)
				return new Vector2D(((Vector2DWrapper)data)._x, ((Vector2DWrapper)data)._y);
			else if (data is Vector3D)
				return (Vector3D)data;
			else if (data is Vector3DWrapper)
				return new Vector3D(((Vector3DWrapper)data)._x, ((Vector3DWrapper)data)._y, ((Vector3DWrapper)data)._z);
			else if (data.GetType().IsArray)
			{
				object[] rawvals = (object[])data;
				List<double> vals = new List<double>(rawvals.Length);

				// Make sure all values in the array are doubles or BigIntegers
				foreach (object rv in rawvals)
				{
					if (!(rv is double || rv is BigInteger))
						throw new CantConvertToVectorException("Values in array must be numbers.");

					if (rv is double d)
						vals.Add(d);
					else if(rv is BigInteger bi)
						vals.Add((double)bi);
				}

				if (vals.Count == 2)
					return new Vector2D(vals[0], vals[1]);
				if (vals.Count == 3)
					return new Vector3D(vals[0], vals[1], vals[2]);
			}
			else if (data is ExpandoObject)
			{
				IDictionary<string, object> eo = data as IDictionary<string, object>;
				double x = double.NaN;
				double y = double.NaN;
				double z = 0.0;

				if (eo.ContainsKey("x"))
				{
					try
					{
						x = Convert.ToDouble(eo["x"]);
					}
					catch (Exception e)
					{
						throw new CantConvertToVectorException("Can not convert 'x' property of data: " + e.Message);
					}
				}

				if (eo.ContainsKey("y"))
				{
					try
					{
						y = Convert.ToDouble(eo["y"]);
					}
					catch (Exception e)
					{
						throw new CantConvertToVectorException("Can not convert 'y' property of data: " + e.Message);
					}
				}

				if (eo.ContainsKey("z"))
				{
					try
					{
						z = Convert.ToDouble(eo["z"]);
					}
					catch (Exception e)
					{
						throw new CantConvertToVectorException("Can not convert 'z' property of data: " + e.Message);
					}
				}

				if (!double.IsNaN(x) && !double.IsNaN(y) && !double.IsNaN(z))
					return new Vector3D(x, y, z);
			}

			throw new CantConvertToVectorException("Data must be a Vector2D, Vector3D, an array of numbers, or an object with (x, y, z) members.");
		}

		internal object GetConvertedUniValue(UniValue uv)
		{
			switch ((UniversalType)uv.Type)
			{
				case UniversalType.AngleRadians:
				case UniversalType.AngleDegreesFloat:
				case UniversalType.Float:
					return Convert.ToDouble(uv.Value);
				case UniversalType.AngleDegrees:
				case UniversalType.AngleByte: //mxd
				case UniversalType.Color:
				case UniversalType.EnumBits:
				case UniversalType.EnumOption:
				case UniversalType.Integer:
				case UniversalType.LinedefTag:
				case UniversalType.LinedefType:
				case UniversalType.SectorEffect:
				case UniversalType.SectorTag:
				case UniversalType.ThingTag:
				case UniversalType.ThingType:
					return Convert.ToInt32(uv.Value);
				case UniversalType.Boolean:
					return Convert.ToBoolean(uv.Value);
				case UniversalType.Flat:
				case UniversalType.String:
				case UniversalType.Texture:
				case UniversalType.EnumStrings:
				case UniversalType.ThingClass:
					return Convert.ToString(uv.Value);
			}

			return null;
		}

		internal Type GetTypeFromUniversalType(int type)
		{
			switch ((UniversalType)type)
			{
				case UniversalType.AngleRadians:
				case UniversalType.AngleDegreesFloat:
				case UniversalType.Float:
					return typeof(double);
				case UniversalType.AngleDegrees:
				case UniversalType.AngleByte: //mxd
				case UniversalType.Color:
				case UniversalType.EnumBits:
				case UniversalType.EnumOption:
				case UniversalType.Integer:
				case UniversalType.LinedefTag:
				case UniversalType.LinedefType:
				case UniversalType.SectorEffect:
				case UniversalType.SectorTag:
				case UniversalType.ThingTag:
				case UniversalType.ThingType:
					return typeof(int);
				case UniversalType.Boolean:
					return typeof(bool);
				case UniversalType.Flat:
				case UniversalType.String:
				case UniversalType.Texture:
				case UniversalType.EnumStrings:
				case UniversalType.ThingClass:
					return typeof(string);
			}

			return null;
		}

		#region ================== Actions

		#region OASIS STAR Actions

		/// <summary>
		/// Called by OASIS STAR script after user selects an asset: next map click will place it.
		/// </summary>
		internal void SetPendingStarPlacement(int thingType, string assetName)
		{
			pendingStarThingType = thingType;
			pendingStarAssetName = assetName ?? "asset";
			General.Interface.DisplayStatus(CodeImp.DoomBuilder.Windows.StatusType.Info, "OASIS STAR: Click on the map to place " + pendingStarAssetName + ".");
		}

		public override void OnEditMouseDown(MouseEventArgs e)
		{
			if (pendingStarThingType.HasValue && General.Map != null && General.Editing.Mode != null && e.Button == MouseButtons.Left)
			{
				Vector2D pos;
				if (General.Editing.Mode is ClassicMode cm)
					pos = cm.MouseMapPos;
				else if (General.Editing.Mode is VisualMode vm)
					pos = new Vector2D(vm.GetHitPosition().x, vm.GetHitPosition().y);
				else
					pos = new Vector2D(0, 0);

				// Reject clicks outside map boundaries (same as ThingsMode.InsertThing)
				if (pos.x < General.Map.Config.LeftBoundary || pos.x > General.Map.Config.RightBoundary ||
					pos.y > General.Map.Config.TopBoundary || pos.y < General.Map.Config.BottomBoundary)
				{
					General.Interface.DisplayStatus(CodeImp.DoomBuilder.Windows.StatusType.Warning, "OASIS STAR: Click inside the map.");
					return;
				}

				int type = pendingStarThingType.Value;
				pendingStarThingType = null;
				string name = pendingStarAssetName;
				pendingStarAssetName = null;

				Vector2D snapped = General.Map.Grid.SnappedToGrid(pos);
				General.Map.UndoRedo.CreateUndo("OASIS STAR place " + name);
				Thing t = General.Map.Map.CreateThing();
				if (t != null)
				{
					General.Settings.ApplyCleanThingSettings(t, type);
					t.Move(snapped);
					t.DetermineSector();
					// Set height to sector floor so the thing is at the correct Z
					if (t.Sector != null)
						t.Move(new Vector3D(snapped.x, snapped.y, t.Sector.FloorHeight));
					t.UpdateConfiguration();
					if (General.Map.UDMF)
					{
						t.SetFlag("skill1", true);
						t.SetFlag("skill2", true);
						t.SetFlag("skill3", true);
						t.SetFlag("skill4", true);
						t.SetFlag("skill5", true);
					}
					else
					{
						t.SetFlag("1", true);
						t.SetFlag("2", true);
						t.SetFlag("4", true);
					}
					General.Map.Map.Update();
					// Rebuild things filter so the new thing appears in VisibleThings (2D view draws from filter, not Map.Things)
					General.Map.ThingsFilter.Update();
					General.Interface.RedrawDisplay();
					General.Interface.DisplayStatus(CodeImp.DoomBuilder.Windows.StatusType.Info, "OASIS STAR: Placed " + name + ".");
				}
				return;
			}
			base.OnEditMouseDown(e);
		}

		[BeginAction("oasisstar_place_selected")]
		public void OASISStarPlaceSelected()
		{
			if (General.Map == null) return;
			RunScriptByPath(Path.Combine("OASIS", "OASIS_STAR_Place_Selected.js"));
		}

		[BeginAction("oasisstar_show_panel")]
		public void OASISStarShowPanel()
		{
			if (starDocker == null)
			{
				try
				{
					starPanel = new OASISStarPanel(this);
					starDocker = new Docker("oasisstar", "OASIS STAR", starPanel);
					General.Interface.AddDocker(starDocker);
				}
				catch (Exception ex)
				{
					General.ErrorLogger.Add(CodeImp.DoomBuilder.ErrorType.Error, "OASIS STAR panel: " + ex.Message);
					return;
				}
			}
			General.Interface.SelectDocker(starDocker);
		}

		[BeginAction("oasisstar_convert_quake2doom")]
		public void OASISStarConvertQuake2Doom()
		{
			OASISMapConverter.ConvertQuakeToDoom(General.Interface);
		}

		[BeginAction("oasisstar_convert_doom2quake")]
		public void OASISStarConvertDoom2Quake()
		{
			OASISMapConverter.ConvertDoomToQuake(General.Interface);
		}

		/// <summary>
		/// Optional sprite override for thing types (e.g. OQUAKE/ODOOM display pack). Returns ImageData from pack if present.
		/// Tries AppPath\UDBScript\Scripts\OASIS\Sprites then Assets\Common\UDBScript\Scripts\OASIS\Sprites so extractor output in either place is used.
		/// </summary>
		public override ImageData GetThingSpriteOverride(int thingType)
		{
			// Only OASIS/OQUAKE types use display-pack PNGs; skip normal Doom things.
			if (!IsOasisThingTypeForOverrides(thingType))
				return null;
			lock (thingSpriteOverrideLock)
			{
				if (thingSpriteOverrideCache.TryGetValue(thingType, out ImageData cached))
					return cached;
			}
			string fileName = thingType + ".png";
			var candidateDirs = new List<string>
			{
				Path.Combine(General.AppPath, SCRIPT_FOLDER, "Scripts", "OASIS", OASIS_SPRITES_FOLDER),
				Path.Combine(General.AppPath, "Assets", "Common", SCRIPT_FOLDER, "Scripts", "OASIS", OASIS_SPRITES_FOLDER),
				Path.Combine(General.AppPath, "..", "Assets", "Common", SCRIPT_FOLDER, "Scripts", "OASIS", OASIS_SPRITES_FOLDER),
			};
			// Also try relative to plugin DLL and to entry exe (VS can run from different output dirs)
			try
			{
				string pluginDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
				if (!string.IsNullOrEmpty(pluginDir))
				{
					candidateDirs.Add(Path.Combine(pluginDir, "..", SCRIPT_FOLDER, "Scripts", "OASIS", OASIS_SPRITES_FOLDER));
					candidateDirs.Add(Path.Combine(pluginDir, "..", "..", "Assets", "Common", SCRIPT_FOLDER, "Scripts", "OASIS", OASIS_SPRITES_FOLDER));
					candidateDirs.Add(Path.Combine(pluginDir, "..", "..", "..", "..", "Assets", "Common", SCRIPT_FOLDER, "Scripts", "OASIS", OASIS_SPRITES_FOLDER));
				}
				Assembly entry = Assembly.GetEntryAssembly();
				if (entry != null)
				{
					string entryDir = Path.GetDirectoryName(entry.Location);
					if (!string.IsNullOrEmpty(entryDir))
					{
						candidateDirs.Add(Path.Combine(entryDir, SCRIPT_FOLDER, "Scripts", "OASIS", OASIS_SPRITES_FOLDER));
						candidateDirs.Add(Path.Combine(entryDir, "Assets", "Common", SCRIPT_FOLDER, "Scripts", "OASIS", OASIS_SPRITES_FOLDER));
						// Walk up from exe dir (e.g. bin\Debug\ -> bin\ -> solution root) to find Sprites
						for (string walk = Path.GetDirectoryName(entryDir); !string.IsNullOrEmpty(walk) && walk != Path.GetPathRoot(walk); walk = Path.GetDirectoryName(walk))
						{
							string spritesUnderUdb = Path.Combine(walk, SCRIPT_FOLDER, "Scripts", "OASIS", OASIS_SPRITES_FOLDER);
							if (!candidateDirs.Contains(spritesUnderUdb)) candidateDirs.Add(spritesUnderUdb);
							string spritesUnderAssets = Path.Combine(walk, "Assets", "Common", SCRIPT_FOLDER, "Scripts", "OASIS", OASIS_SPRITES_FOLDER);
							if (!candidateDirs.Contains(spritesUnderAssets)) candidateDirs.Add(spritesUnderAssets);
						}
					}
				}
			}
			catch { /* ignore */ }
			string pathByType = null;
			foreach (string dir in candidateDirs)
			{
				string fullDir = Path.GetFullPath(dir);
				if (Directory.Exists(fullDir))
				{
					string p = Path.Combine(fullDir, fileName);
					if (File.Exists(p))
					{
						pathByType = p;
						break;
					}
				}
			}
			if (pathByType == null)
			{
				// Log once so user sees why 3D shows "sprite unknown" (Tools → Errors & Warnings)
				if (!loggedOasisSpritePathWarning)
				{
					loggedOasisSpritePathWarning = true;
					string firstResolved = candidateDirs.Count > 0 ? Path.GetFullPath(candidateDirs[0]) : "(none)";
					General.ErrorLogger.Add(CodeImp.DoomBuilder.ErrorType.Warning,
						"OASIS display pack: " + fileName + " not found. AppPath=" + (General.AppPath ?? "") + "; first path tried: " + firstResolved + ". Copy Sprites folder to exe directory under UDBScript/Scripts/OASIS/Sprites or Assets/Common/UDBScript/Scripts/OASIS/Sprites.");
				}
				return null;
			}
			try
			{
				using (Bitmap fromFile = (Bitmap)Image.FromFile(pathByType))
				{
					Bitmap prepared = new Bitmap(fromFile);
					// Key-only alpha cleanup (5005/5013). Non-key sprites must stay unfiltered.
					bool applyKeyAlphaProcessing = (thingType == 5005 || thingType == 5013);
					if (applyKeyAlphaProcessing)
					{
						// Normalize alpha for external OASIS key sprites:
						// some generated PNGs have alpha in range [N..255] (never reaching 0),
						// which renders as a full rectangular slab in 3D. Remap min alpha to 0.
						byte minA = 255;
						byte maxA = 0;
						for (int y = 0; y < prepared.Height; y++)
						{
							for (int x = 0; x < prepared.Width; x++)
							{
								byte a = prepared.GetPixel(x, y).A;
								if (a < minA) minA = a;
								if (a > maxA) maxA = a;
							}
						}
						if (minA > 0 && maxA > minA)
						{
							int denom = maxA - minA;
							for (int y = 0; y < prepared.Height; y++)
							{
								for (int x = 0; x < prepared.Width; x++)
								{
									Color c = prepared.GetPixel(x, y);
									int na = ((c.A - minA) * 255) / denom;
									if (na < 8) na = 0; // snap tiny fringe to transparent
									prepared.SetPixel(x, y, Color.FromArgb(na, c.R, c.G, c.B));
								}
							}
						}
						// Remove flat square background by flood-filling from image borders using border-color similarity.
						int bw = prepared.Width;
						int bh = prepared.Height;
						if (bw > 2 && bh > 2)
						{
							long sumR = 0, sumG = 0, sumB = 0, bcount = 0;
							for (int x = 0; x < bw; x++)
							{
								Color ct = prepared.GetPixel(x, 0);
								Color cb = prepared.GetPixel(x, bh - 1);
								sumR += ct.R + cb.R; sumG += ct.G + cb.G; sumB += ct.B + cb.B; bcount += 2;
							}
							for (int y = 1; y < bh - 1; y++)
							{
								Color cl = prepared.GetPixel(0, y);
								Color cr = prepared.GetPixel(bw - 1, y);
								sumR += cl.R + cr.R; sumG += cl.G + cr.G; sumB += cl.B + cr.B; bcount += 2;
							}
							Color bg = Color.FromArgb((int)(sumR / bcount), (int)(sumG / bcount), (int)(sumB / bcount));
							const int BG_TOL = 42; // Manhattan RGB distance threshold

							bool[,] visited = new bool[bw, bh];
							Queue<Point> q = new Queue<Point>();
							Action<int, int> enqueue = (xx, yy) =>
							{
								if (xx < 0 || yy < 0 || xx >= bw || yy >= bh) return;
								if (visited[xx, yy]) return;
								visited[xx, yy] = true;
								q.Enqueue(new Point(xx, yy));
							};

							for (int x = 0; x < bw; x++) { enqueue(x, 0); enqueue(x, bh - 1); }
							for (int y = 1; y < bh - 1; y++) { enqueue(0, y); enqueue(bw - 1, y); }

							while (q.Count > 0)
							{
								Point pnt = q.Dequeue();
								Color c = prepared.GetPixel(pnt.X, pnt.Y);
								if (c.A == 0) continue;
								int dist = Math.Abs(c.R - bg.R) + Math.Abs(c.G - bg.G) + Math.Abs(c.B - bg.B);
								if (dist > BG_TOL) continue;
								prepared.SetPixel(pnt.X, pnt.Y, Color.FromArgb(0, c.R, c.G, c.B));
								enqueue(pnt.X - 1, pnt.Y);
								enqueue(pnt.X + 1, pnt.Y);
								enqueue(pnt.X, pnt.Y - 1);
								enqueue(pnt.X, pnt.Y + 1);
							}
						}
					}

					var bim = new BitmapImage(prepared, "OASIS_" + thingType);
					bim.LoadImageNow();
					lock (thingSpriteOverrideLock)
					{
						thingSpriteOverrideCache[thingType] = bim;
					}
					return bim;
				}
			}
			catch (Exception ex)
			{
				General.ErrorLogger.Add(CodeImp.DoomBuilder.ErrorType.Error, "OASIS display pack: failed to load " + pathByType + ": " + ex.Message);
				return null;
			}
		}

		public override bool TryGetThingInfoOverride(int thingType, out KeyValuePair<string, string> thingInfo)
		{
			thingInfo = new KeyValuePair<string, string>();
			if(!IsOasisThingTypeForOverrides(thingType))
				return false;

			EnsureOasisThingInfoOverridesLoaded();
			lock(thingInfoOverrideLock)
			{
				return thingInfoOverrideCache.TryGetValue(thingType, out thingInfo);
			}
		}

		private void EnsureOasisThingInfoOverridesLoaded()
		{
			lock(thingInfoOverrideLock)
			{
				if(thingInfoOverrideLoaded) return;
			}

			List<string> candidates = GetOasisStarScriptCandidates("OASIS_STAR_Place_Selected.js");
			foreach(string scriptPath in candidates)
			{
				try
				{
					string fullPath = Path.GetFullPath(scriptPath);
					if(!File.Exists(fullPath)) continue;

					LoadOasisThingInfoOverridesFromScript(fullPath);
					lock(thingInfoOverrideLock) thingInfoOverrideLoaded = true;
					return;
				}
				catch
				{
					// Try next candidate.
				}
			}

			if(!loggedOasisThingInfoPathWarning)
			{
				loggedOasisThingInfoPathWarning = true;
				string firstResolved = candidates.Count > 0 ? Path.GetFullPath(candidates[0]) : "(none)";
				General.ErrorLogger.Add(CodeImp.DoomBuilder.ErrorType.Warning,
					"OASIS metadata: OASIS_STAR_Place_Selected.js not found. First path tried: " + firstResolved + ".");
			}

			lock(thingInfoOverrideLock) thingInfoOverrideLoaded = true;
		}

		private void LoadOasisThingInfoOverridesFromScript(string scriptPath)
		{
			string script = File.ReadAllText(scriptPath);
			Regex rowRegex = new Regex(@"\[\s*""([^""]+)""\s*,\s*""[^""]+""\s*,\s*""([^""]+)""\s*,\s*""([^""]+)""\s*,\s*(\d+)\s*\]", RegexOptions.Compiled);
			MatchCollection matches = rowRegex.Matches(script);

			lock(thingInfoOverrideLock)
			{
				thingInfoOverrideCache.Clear();
				foreach(Match m in matches)
				{
					if(!m.Success) continue;

					int type;
					if(!int.TryParse(m.Groups[4].Value, out type)) continue;

					string game = UnescapeJsString(m.Groups[1].Value);
					string classId = UnescapeJsString(m.Groups[2].Value);
					string title = UnescapeJsString(m.Groups[3].Value);
					if(string.IsNullOrEmpty(title))
						title = classId;

					if(string.Equals(game, "OQUAKE", StringComparison.OrdinalIgnoreCase))
						title += " (OQUAKE)";
					else if(string.Equals(game, "ODOOM", StringComparison.OrdinalIgnoreCase))
						title += " {ODOOM}";

					thingInfoOverrideCache[type] = new KeyValuePair<string, string>(title, classId);
				}
			}
		}

		private static string UnescapeJsString(string value)
		{
			if(string.IsNullOrEmpty(value)) return value;
			return value.Replace("\\\"", "\"").Replace("\\\\", "\\");
		}

		private static List<string> GetOasisStarScriptCandidates(string fileName)
		{
			var candidates = new List<string>
			{
				Path.Combine(General.AppPath, SCRIPT_FOLDER, "Scripts", "OASIS", fileName),
				Path.Combine(General.AppPath, "Assets", "Common", SCRIPT_FOLDER, "Scripts", "OASIS", fileName),
				Path.Combine(General.AppPath, "..", "Assets", "Common", SCRIPT_FOLDER, "Scripts", "OASIS", fileName),
			};

			try
			{
				string pluginDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
				if(!string.IsNullOrEmpty(pluginDir))
				{
					candidates.Add(Path.Combine(pluginDir, "..", SCRIPT_FOLDER, "Scripts", "OASIS", fileName));
					candidates.Add(Path.Combine(pluginDir, "..", "..", "Assets", "Common", SCRIPT_FOLDER, "Scripts", "OASIS", fileName));
					candidates.Add(Path.Combine(pluginDir, "..", "..", "..", "..", "Assets", "Common", SCRIPT_FOLDER, "Scripts", "OASIS", fileName));
				}

				Assembly entry = Assembly.GetEntryAssembly();
				if(entry != null)
				{
					string entryDir = Path.GetDirectoryName(entry.Location);
					if(!string.IsNullOrEmpty(entryDir))
					{
						candidates.Add(Path.Combine(entryDir, SCRIPT_FOLDER, "Scripts", "OASIS", fileName));
						candidates.Add(Path.Combine(entryDir, "Assets", "Common", SCRIPT_FOLDER, "Scripts", "OASIS", fileName));
						for(string walk = Path.GetDirectoryName(entryDir); !string.IsNullOrEmpty(walk) && walk != Path.GetPathRoot(walk); walk = Path.GetDirectoryName(walk))
						{
							string scriptUnderUdb = Path.Combine(walk, SCRIPT_FOLDER, "Scripts", "OASIS", fileName);
							if(!candidates.Contains(scriptUnderUdb)) candidates.Add(scriptUnderUdb);
							string scriptUnderAssets = Path.Combine(walk, "Assets", "Common", SCRIPT_FOLDER, "Scripts", "OASIS", fileName);
							if(!candidates.Contains(scriptUnderAssets)) candidates.Add(scriptUnderAssets);
						}
					}
				}
			}
			catch { }

			return candidates;
		}

		#endregion

		[BeginAction("udbscriptexecute")]
		public void ScriptExecute()
		{
			if (currentscript == null)
				return;

			scriptrunner = new ScriptRunner(currentscript);
			scriptrunnerform.ShowDialog();
		}

		[BeginAction("udbscriptexecuteslot1")]
		[BeginAction("udbscriptexecuteslot2")]
		[BeginAction("udbscriptexecuteslot3")]
		[BeginAction("udbscriptexecuteslot4")]
		[BeginAction("udbscriptexecuteslot5")]
		[BeginAction("udbscriptexecuteslot6")]
		[BeginAction("udbscriptexecuteslot7")]
		[BeginAction("udbscriptexecuteslot8")]
		[BeginAction("udbscriptexecuteslot9")]
		[BeginAction("udbscriptexecuteslot10")]
		[BeginAction("udbscriptexecuteslot11")]
		[BeginAction("udbscriptexecuteslot12")]
		[BeginAction("udbscriptexecuteslot13")]
		[BeginAction("udbscriptexecuteslot14")]
		[BeginAction("udbscriptexecuteslot15")]
		[BeginAction("udbscriptexecuteslot16")]
		[BeginAction("udbscriptexecuteslot17")]
		[BeginAction("udbscriptexecuteslot18")]
		[BeginAction("udbscriptexecuteslot19")]
		[BeginAction("udbscriptexecuteslot20")]
		[BeginAction("udbscriptexecuteslot21")]
		[BeginAction("udbscriptexecuteslot22")]
		[BeginAction("udbscriptexecuteslot23")]
		[BeginAction("udbscriptexecuteslot24")]
		[BeginAction("udbscriptexecuteslot25")]
		[BeginAction("udbscriptexecuteslot26")]
		[BeginAction("udbscriptexecuteslot27")]
		[BeginAction("udbscriptexecuteslot28")]
		[BeginAction("udbscriptexecuteslot29")]
		[BeginAction("udbscriptexecuteslot30")]
		public void ScriptExecuteSlot()
		{
			// Extract the slot number from the action name. The action name is something like udbscript__udbscriptexecuteslot1.
			// Not super nice, but better than having 30 identical methods for each slot.
			Regex re = new Regex(@"(\d+)$");
			Match m = re.Match(General.Actions.Current.Name);

			if(m.Success)
			{
				int slot = int.Parse(m.Value);

				// Check if there's a ScriptInfo in the slot and run it if so
				if (scriptslots.ContainsKey(slot) && scriptslots[slot] != null)
				{
					scriptrunner = new ScriptRunner(scriptslots[slot]);
					scriptrunnerform.ShowDialog();
				}
			}
		}

		#endregion
	}
}
