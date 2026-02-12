#region ================== OASIS STAR API

/*
 * OASIS STAR panel: select and place ODOOM/OQUAKE assets (keycards, monsters, weapons, health, ammo)
 * into maps. Cross-game: same thing types in Doom and OQuake (e.g. Silver Key works in both).
 */

#endregion

#region ================== Namespaces

using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using CodeImp.DoomBuilder.Windows;

#endregion

namespace CodeImp.DoomBuilder.UDBScript
{
	public class OASISStarPanel : UserControl
	{
		private readonly BuilderPlug plug;
		private Button btnPlace;
		private Label lblHelp;
		private ComboBox comboGame;
		private ComboBox comboAsset;
		private Label lblGame;
		private Label lblAssets;
		private readonly System.Collections.Generic.List<StarAsset> assets = new System.Collections.Generic.List<StarAsset>
		{
			new StarAsset("ODOOM", "Blue Keycard", 5),
			new StarAsset("ODOOM", "Red Keycard", 13),
			new StarAsset("ODOOM", "Yellow Keycard", 6),
			new StarAsset("ODOOM", "Red Skull Key", 38),
			new StarAsset("ODOOM", "Blue Skull Key", 39),
			new StarAsset("ODOOM", "Yellow Skull Key", 40),
			new StarAsset("ODOOM", "Shotgun", 2001),
			new StarAsset("ODOOM", "Chaingun", 2002),
			new StarAsset("ODOOM", "Rocket Launcher", 2003),
			new StarAsset("ODOOM", "Plasma Rifle", 2004),
			new StarAsset("ODOOM", "Chainsaw", 2005),
			new StarAsset("ODOOM", "BFG 9000", 2006),
			new StarAsset("ODOOM", "Clip", 2007),
			new StarAsset("ODOOM", "Shells", 2008),
			new StarAsset("ODOOM", "Rocket", 2010),
			new StarAsset("ODOOM", "Cell", 2047),
			new StarAsset("ODOOM", "Cell Pack", 2048),
			new StarAsset("ODOOM", "Ammo Box", 2049),
			new StarAsset("ODOOM", "Medikit", 2011),
			new StarAsset("ODOOM", "Stimpack", 2012),
			new StarAsset("ODOOM", "Soul Sphere", 2013),
			new StarAsset("ODOOM", "Health Potion", 2014),
			new StarAsset("ODOOM", "Armor Bonus", 2015),
			new StarAsset("ODOOM", "Armor Helmet", 2016),
			new StarAsset("ODOOM", "Zombieman", 3004),
			new StarAsset("ODOOM", "Sergeant", 9),
			new StarAsset("ODOOM", "Imp", 3001),
			new StarAsset("ODOOM", "Demon", 3002),
			new StarAsset("ODOOM", "Spectre", 58),
			new StarAsset("ODOOM", "Cacodemon", 3005),
			new StarAsset("ODOOM", "Baron of Hell", 3003),
			new StarAsset("ODOOM", "Hell Knight", 69),
			new StarAsset("ODOOM", "Lost Soul", 3006),
			new StarAsset("ODOOM", "Revenant", 65),
			new StarAsset("ODOOM", "Mancubus", 66),
			new StarAsset("ODOOM", "Arch-Vile", 64),
			new StarAsset("ODOOM", "Pain Elemental", 68),
			new StarAsset("ODOOM", "Arachnotron", 67),
			new StarAsset("ODOOM", "Spider Mastermind", 7),
			new StarAsset("ODOOM", "Cyberdemon", 16),
			new StarAsset("OQUAKE", "Silver Key", 5013),
			new StarAsset("OQUAKE", "Gold Key", 5005),
			new StarAsset("OQUAKE", "Shotgun", 5201),
			new StarAsset("OQUAKE", "Super Shotgun", 5202),
			new StarAsset("OQUAKE", "Nailgun", 5203),
			new StarAsset("OQUAKE", "Super Nailgun", 5204),
			new StarAsset("OQUAKE", "Grenade Launcher", 5205),
			new StarAsset("OQUAKE", "Rocket Launcher", 5206),
			new StarAsset("OQUAKE", "Thunderbolt", 5207),
			new StarAsset("OQUAKE", "Nails", 5208),
			new StarAsset("OQUAKE", "Shells", 5209),
			new StarAsset("OQUAKE", "Rockets", 5210),
			new StarAsset("OQUAKE", "Cells", 5211),
			new StarAsset("OQUAKE", "Health", 5212),
			new StarAsset("OQUAKE", "Small Health", 5213),
			new StarAsset("OQUAKE", "Green Armor", 5214),
			new StarAsset("OQUAKE", "Yellow Armor", 5215),
			new StarAsset("OQUAKE", "Mega Armor", 5216),
			new StarAsset("OQUAKE", "Grunt", 5304),
			new StarAsset("OQUAKE", "Ogre", 5309),
			new StarAsset("OQUAKE", "Demon", 5302),
			new StarAsset("OQUAKE", "Rottweiler", 3010),
			new StarAsset("OQUAKE", "Shambler", 5303),
			new StarAsset("OQUAKE", "Zombie", 3011),
			new StarAsset("OQUAKE", "Hell Knight", 5369),
			new StarAsset("OQUAKE", "Enforcer", 5366),
			new StarAsset("OQUAKE", "Fish", 5305),
			new StarAsset("OQUAKE", "Spawn", 5368),
		};

		public OASISStarPanel(BuilderPlug builderPlug)
		{
			plug = builderPlug ?? throw new ArgumentNullException(nameof(builderPlug));
			InitializeComponent();
		}

		private void InitializeComponent()
		{
			SuspendLayout();
			BackColor = SystemColors.Control;
			Padding = new Padding(6);
			MinimumSize = new Size(200, 180);
			Size = new Size(260, 220);

			lblGame = new Label
			{
				Text = "Game:",
				AutoSize = true,
				Location = new Point(6, 8)
			};

			comboGame = new ComboBox
			{
				DropDownStyle = ComboBoxStyle.DropDownList,
				Location = new Point(6, 26),
				Width = 240,
				Items = { "ODOOM", "OQUAKE" }
			};
			comboGame.SelectedIndex = 0;
			comboGame.SelectedIndexChanged += ComboGame_SelectedIndexChanged;

			lblAssets = new Label
			{
				Text = "Asset:",
				AutoSize = true,
				Location = new Point(6, 54)
			};

			comboAsset = new ComboBox
			{
				DropDownStyle = ComboBoxStyle.DropDownList,
				Location = new Point(6, 72),
				Width = 240,
				DropDownWidth = 360
			};
			FillAssetListByGame();

			btnPlace = new Button
			{
				Text = "★ Place selected asset at cursor",
				Location = new Point(6, 108),
				Width = 240,
				Height = 28,
				FlatStyle = FlatStyle.Standard
			};
			btnPlace.Click += BtnPlace_Click;

			lblHelp = new Label
			{
				Text = "Click in 2D map, then click Place. Or use STAR menu / toolbar.",
				AutoSize = true,
				Location = new Point(6, 142),
				MaximumSize = new Size(240, 0),
				ForeColor = SystemColors.GrayText,
				Font = new Font(Font.FontFamily, Font.Size - 1f)
			};

			Controls.Add(lblGame);
			Controls.Add(comboGame);
			Controls.Add(lblAssets);
			Controls.Add(comboAsset);
			Controls.Add(btnPlace);
			Controls.Add(lblHelp);

			ResumeLayout(false);
		}

		private void FillAssetListByGame()
		{
			string game = comboGame.SelectedItem?.ToString() ?? "ODOOM";
			comboAsset.Items.Clear();
			foreach(var a in assets)
			{
				if(string.Equals(a.Game, game, StringComparison.OrdinalIgnoreCase))
					comboAsset.Items.Add(a);
			}
			if(comboAsset.Items.Count > 0)
			{
				comboAsset.SelectedIndex = 0;
			}
		}

		private void ComboGame_SelectedIndexChanged(object sender, EventArgs e)
		{
			FillAssetListByGame();
		}

		private void BtnPlace_Click(object sender, EventArgs e)
		{
			if (General.Map == null)
			{
				MessageBox.Show("Open a map first.", "OASIS STAR", MessageBoxButtons.OK, MessageBoxIcon.Information);
				return;
			}
			StarAsset selected = comboAsset.SelectedItem as StarAsset;
			if(selected == null)
			{
				MessageBox.Show("Select an asset first.", "OASIS STAR", MessageBoxButtons.OK, MessageBoxIcon.Information);
				return;
			}

			plug.SetPendingStarPlacement(selected.ThingType, selected.Name);
		}

		private sealed class StarAsset
		{
			public string Game { get; private set; }
			public string Name { get; private set; }
			public int ThingType { get; private set; }

			public StarAsset(string game, string name, int thingType)
			{
				Game = game;
				Name = name;
				ThingType = thingType;
			}

			public override string ToString()
			{
				return Name + " (" + ThingType + ")";
			}
		}
	}
}








