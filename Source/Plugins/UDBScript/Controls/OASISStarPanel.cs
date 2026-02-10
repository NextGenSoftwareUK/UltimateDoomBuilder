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
		private ListBox listAssets;
		private Label lblGame;
		private Label lblAssets;

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
				Text = "Category / Asset (select then Place):",
				AutoSize = true,
				Location = new Point(6, 54)
			};

			listAssets = new ListBox
			{
				Location = new Point(6, 72),
				Width = 240,
				Height = 90,
				IntegralHeight = false
			};
			FillAssetList();

			btnPlace = new Button
			{
				Text = "★ Place selected asset at cursor",
				Location = new Point(6, 168),
				Width = 240,
				Height = 28,
				FlatStyle = FlatStyle.Standard
			};
			btnPlace.Click += BtnPlace_Click;

			lblHelp = new Label
			{
				Text = "Click in 2D map, then click Place. Or use STAR menu / toolbar.",
				AutoSize = true,
				Location = new Point(6, 200),
				MaximumSize = new Size(240, 0),
				ForeColor = SystemColors.GrayText,
				Font = new Font(Font.FontFamily, Font.Size - 1f)
			};

			Controls.Add(lblGame);
			Controls.Add(comboGame);
			Controls.Add(lblAssets);
			Controls.Add(listAssets);
			Controls.Add(btnPlace);
			Controls.Add(lblHelp);

			ResumeLayout(false);
		}

		private void FillAssetList()
		{
			listAssets.Items.Clear();
			string game = comboGame.SelectedItem?.ToString() ?? "ODOOM";
			if (game == "ODOOM")
			{
				listAssets.Items.Add("--- Keycards ---");
				listAssets.Items.Add("  Blue Keycard");
				listAssets.Items.Add("  Red Keycard");
				listAssets.Items.Add("  Yellow Keycard");
				listAssets.Items.Add("--- Weapons ---");
				listAssets.Items.Add("  Shotgun, Chaingun, Rocket, Plasma, Chainsaw, BFG");
				listAssets.Items.Add("--- Ammo / Health ---");
				listAssets.Items.Add("  Clip, Shells, Medikit, Soul Sphere, ...");
				listAssets.Items.Add("--- Monsters ---");
				listAssets.Items.Add("  Imp, Demon, Cacodemon, Baron, Revenant, ...");
			}
			else
			{
				listAssets.Items.Add("--- Keys ---");
				listAssets.Items.Add("  Silver Key, Gold Key");
				listAssets.Items.Add("--- Weapons / Ammo / Health ---");
				listAssets.Items.Add("  Shotgun, Nailgun, Rocket Launcher, ...");
				listAssets.Items.Add("--- Monsters ---");
				listAssets.Items.Add("  Grunt, Ogre, Shambler, ...");
			}
			listAssets.Items.Add("");
			listAssets.Items.Add("(Use 'Place at cursor' then choose in script dialog)");
		}

		private void ComboGame_SelectedIndexChanged(object sender, EventArgs e)
		{
			FillAssetList();
		}

		private void BtnPlace_Click(object sender, EventArgs e)
		{
			if (General.Map == null)
			{
				MessageBox.Show("Open a map first.", "OASIS STAR", MessageBoxButtons.OK, MessageBoxIcon.Information);
				return;
			}
			plug.RunScriptByPath(Path.Combine("OASIS", "OASIS_STAR_Place_Selected.js"));
		}
	}
}



