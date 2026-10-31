using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Media;
using System.Windows.Forms;
using SCClassLoader;

namespace ChimeraModManager
{
    /// <summary>
    /// The manager's window: every mod in load order with an on/off box, moved with buttons, Alt+arrows or drag and drop
    /// (never ahead of what it needs or loads after); the selected mod's details; installing from a .zip or folder,
    /// removing to the Recycle Bin, and Play. Every change is saved at once to the loader's choices file.
    /// </summary>
    internal class MainForm : Form
    {
        private static readonly Color Good = Color.FromArgb(0, 120, 40), Bad = Color.FromArgb(190, 20, 20), Warn = Color.FromArgb(170, 95, 0),
            Faint = Color.FromArgb(110, 110, 110);

        private readonly ModFiles files;
        private readonly float S;
        private readonly ListView list;
        private readonly RichTextBox info;
        private readonly ToolStripButton play;
        private readonly ToolStripStatusLabel said, gameState;
        private readonly Button up, down, top, bottom, openFolder, needs, allowCode, remove;
        private bool filling;
        private ModPack selected;
        private ModPlan next = new ModPlan();
        private List<ModPack> order = new List<ModPack>();

        internal MainForm(ModFiles files)
        {
            this.files = files;
            using (Graphics g = CreateGraphics()) S = g.DpiX / 96f;
            Text = "Breachwork Mod Manager";
            Font = new Font("Segoe UI", 9f);
            Size = new Size(Px(1280), Px(780));
            MinimumSize = new Size(Px(900), Px(520));
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;

            // ---- toolbar
            var bar = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Padding = new Padding(Px(6), Px(3), Px(6), Px(3)), ImageScalingSize = new Size(Px(16), Px(16)) };
            play = new ToolStripButton("▶  Play") { Font = new Font(Font, FontStyle.Bold), ToolTipText = "Start the game with these mods" };
            play.Click += (s, e) => { try { files.Play(); Say("Starting the game."); } catch (Exception ex) { Fail("Could not start the game", ex); } };
            var zip = new ToolStripButton("Install from .zip…") { ToolTipText = "Install the mods in a .zip download" };
            zip.Click += (s, e) => PickZip();
            var folder = new ToolStripButton("Install from folder…") { ToolTipText = "Install a mod folder (or an unpacked download)" };
            folder.Click += (s, e) => PickFolder();
            var modsFolder = new ToolStripButton("Open Mods Folder");
            modsFolder.Click += (s, e) => ModFiles.OpenFolder(files.ModsDir);
            var refresh = new ToolStripButton("Refresh") { ToolTipText = "Read the Mods folder again (F5)" };
            refresh.Click += (s, e) => Reload("Read the Mods folder again.");
            var reset = new ToolStripButton("Automatic Order") { ToolTipText = "Forget the chosen load order and go back to the automatic one (by requirements, then folder name)" };
            reset.Click += (s, e) => { ModChoices.SetOrder(new string[0]); Say("Saved: back to the automatic load order."); Fill(); };
            bar.Items.AddRange(new ToolStripItem[] { play, new ToolStripSeparator(), zip, folder, modsFolder, refresh, new ToolStripSeparator(), reset });

            // ---- mod list and its move buttons
            list = new ListView
            {
                Dock = DockStyle.Fill, View = View.Details, CheckBoxes = true, FullRowSelect = true, MultiSelect = false,
                HideSelection = false, AllowDrop = true, HeaderStyle = ColumnHeaderStyle.Nonclickable, ShowItemToolTips = true,
            };
            list.Columns.Add("#", Px(62));
            list.Columns.Add("Mod", Px(210));
            list.Columns.Add("Version", Px(70));
            list.Columns.Add("Author", Px(90));
            list.Columns.Add("Status", Px(300));
            list.ClientSizeChanged += (s, e) => FitStatus();
            list.ItemChecked += (s, e) =>
            {
                if (filling || !(e.Item.Tag is ModPack p)) return;
                ModChoices.Set(p, e.Item.Checked);
                selected = p;
                Say($"Saved: {p.Title} turned {(e.Item.Checked ? "on" : "off")}.");
                BeginInvoke((Action)Fill); // not while the list is still raising its own event
            };
            list.SelectedIndexChanged += (s, e) =>
            {
                if (filling || list.SelectedItems.Count == 0) return;
                selected = (ModPack)list.SelectedItems[0].Tag;
                ShowInfo();
                UpdateButtons();
            };
            list.ItemDrag += (s, e) => list.DoDragDrop(e.Item, DragDropEffects.Move);
            list.DragOver += (s, e) => e.Effect = e.Data.GetDataPresent(typeof(ListViewItem)) ? DragDropEffects.Move
                : e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            list.DragDrop += (s, e) =>
            {
                if (e.Data.GetData(DataFormats.FileDrop) is string[] paths) { foreach (string path in paths) Install(path); return; }
                if (!(e.Data.GetData(typeof(ListViewItem)) is ListViewItem dragged)) return;
                ListViewItem at = list.GetItemAt(4, list.PointToClient(new Point(e.X, e.Y)).Y);
                selected = (ModPack)dragged.Tag;
                MoveTo(at != null ? at.Index : order.Count - 1);
            };

            up = SideButton("Move Up", () => MoveTo(order.IndexOf(selected) - 1));
            down = SideButton("Move Down", () => MoveTo(order.IndexOf(selected) + 1));
            top = SideButton("To Top", () => MoveTo(0));
            bottom = SideButton("To Bottom", () => MoveTo(order.Count - 1));
            var side = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(Px(4), 0, 0, 0) };
            side.Controls.AddRange(new Control[] { up, down, top, bottom });
            var hint = new Label { AutoSize = false, Width = Px(104), Height = Px(150), ForeColor = Faint, Margin = new Padding(0, Px(10), 0, 0),
                Text = "Drag rows to reorder (Alt+Up/Down). Tick a box to turn a mod on or off. Drop a .zip or folder here to install it." };
            side.Controls.Add(hint);
            var left = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(Px(8), Px(8), 0, Px(8)) };
            left.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            left.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Px(116)));
            left.Controls.Add(list, 0, 0);
            left.Controls.Add(side, 1, 0);

            // ---- details
            info = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = SystemColors.Window, DetectUrls = false };
            openFolder = ActionButton("Open Folder", () => { if (selected != null) ModFiles.OpenFolder(selected.folder); });
            needs = ActionButton("Turn On What It Needs", TurnOnNeeds);
            allowCode = ActionButton("Allow Its Code", ToggleCode);
            remove = ActionButton("Remove…", Remove);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(0, Px(4), 0, 0) };
            actions.Controls.AddRange(new Control[] { openFolder, needs, allowCode, remove });
            var infoBox = new Panel { Dock = DockStyle.Fill, BackColor = SystemColors.Window, Padding = new Padding(Px(12)), BorderStyle = BorderStyle.FixedSingle };
            infoBox.Controls.Add(info);
            var right = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(0, Px(8), Px(8), Px(8)) };
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, Px(44)));
            right.Controls.Add(infoBox, 0, 0);
            right.Controls.Add(actions, 0, 1);

            var split = new SplitContainer { Dock = DockStyle.Fill, SplitterWidth = Px(6) };
            split.Panel1.Controls.Add(left);
            split.Panel2.Controls.Add(right);

            // ---- status bar
            var strip = new StatusStrip { SizingGrip = true };
            said = new ToolStripStatusLabel { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
            gameState = new ToolStripStatusLabel { TextAlign = ContentAlignment.MiddleRight };
            strip.Items.AddRange(new ToolStripItem[] { said, gameState });

            Controls.Add(split);
            Controls.Add(bar);
            Controls.Add(strip);
            Load += (s, e) => { split.SplitterDistance = (int)(ClientSize.Width * 0.56f); Reload(null); CheckGame(); };
            KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.F5) { Reload("Read the Mods folder again."); e.Handled = true; }
                else if (e.Alt && e.KeyCode == Keys.Up && selected != null) { MoveTo(order.IndexOf(selected) - 1); e.Handled = true; }
                else if (e.Alt && e.KeyCode == Keys.Down && selected != null) { MoveTo(order.IndexOf(selected) + 1); e.Handled = true; }
            };
            var timer = new Timer { Interval = 2000 };
            timer.Tick += (s, e) => CheckGame();
            timer.Start();
        }

        private int Px(float v) => (int)Math.Round(v * S);

        /// <summary>The Status column takes whatever width the others leave.</summary>
        private void FitStatus()
        {
            int rest = list.ClientSize.Width - list.Columns.Cast<ColumnHeader>().Take(4).Sum(c => c.Width);
            list.Columns[4].Width = Math.Max(Px(120), rest);
        }

        private Button SideButton(string text, Action click)
        {
            var b = new Button { Text = text, Width = Px(104), Height = Px(30), Margin = new Padding(0, 0, 0, Px(6)) };
            b.Click += (s, e) => click();
            return b;
        }

        private Button ActionButton(string text, Action click)
        {
            var b = new Button { Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(Px(8), Px(2), Px(8), Px(2)), Margin = new Padding(0, 0, Px(8), 0) };
            b.Click += (s, e) => click();
            return b;
        }

        // ---- state

        internal void Reload(string message)
        {
            try { files.Rescan(); }
            catch (Exception ex) { Fail("Could not read the Mods folder", ex); }
            if (message != null) Say(message);
            else if (ModChoices.LoadError != null) Say($"Could not read {ModChoices.FilePath} ({ModChoices.LoadError}); every mod uses its own setting until it is fixed.", Bad);
            else if (!files.HasLoader) Say("The mod loader is not installed in this game folder, so no mod loads until it is.", Bad);
            else Say($"{files.ModsDir}  ·  choices are saved at once and apply the next time the game starts.");
            Fill();
        }

        /// <summary>
        /// Every mod in load order: the order the loader would use with every mod on (the chosen order where it respects
        /// requires/loadAfter), then the mods that can't load at all. Mods that are off keep their place.
        /// </summary>
        private static List<ModPack> DisplayOrder()
        {
            var all = ModPacks.Resolve(_ => true).Ordered;
            return all.Concat(ModPacks.All.Where(p => !all.Contains(p)).OrderBy(p => p.Title, StringComparer.OrdinalIgnoreCase)).ToList();
        }

        private (string text, Color color) Status(ModPack p)
        {
            if (next.Ordered.Contains(p)) return ("Loads", Good);
            string why = next.Skipped.TryGetValue(p, out string w) ? w : "not loaded";
            if (why == ModPacks.Off) return ("Off", Faint);
            if (why == ModPacks.NeedsOk) return ("Waiting for your OK to run its code", Warn);
            return (char.ToUpper(why[0]) + why.Substring(1), Bad);
        }

        internal void Fill()
        {
            filling = true;
            next = ModPacks.Resolve(ModChoices.IsOn);
            order = DisplayOrder();
            if (selected != null) selected = order.FirstOrDefault(p => p.folder == selected.folder);
            if (selected == null) selected = order.FirstOrDefault();
            list.BeginUpdate();
            list.Items.Clear();
            foreach (ModPack p in order)
            {
                bool on = ModChoices.IsOn(p);
                int at = next.Ordered.IndexOf(p);
                var (status, color) = Status(p);
                var item = new ListViewItem(new[] { at >= 0 ? (at + 1).ToString() : "–", p.Title, p.version ?? "", p.author ?? "", status })
                    { Checked = on, Tag = p, UseItemStyleForSubItems = false };
                foreach (ListViewItem.ListViewSubItem sub in item.SubItems) sub.ForeColor = on ? SystemColors.WindowText : Faint;
                item.SubItems[4].ForeColor = color;
                if (p == selected) item.Selected = true;
                list.Items.Add(item);
            }
            list.EndUpdate();
            FitStatus();
            list.SelectedItems.Cast<ListViewItem>().FirstOrDefault()?.EnsureVisible();
            filling = false;
            int loads = next.Ordered.Count;
            Text = $"Breachwork Mod Manager  —  {loads} of {order.Count} mods load{(ModChoices.Order.Count > 0 ? "  ·  chosen load order" : "")}";
            foreach (string w in next.Warnings) Say(w, Warn);
            ShowInfo();
            UpdateButtons();
        }

        private void UpdateButtons()
        {
            int i = order.IndexOf(selected);
            up.Enabled = top.Enabled = i > 0;
            down.Enabled = bottom.Enabled = i >= 0 && i < order.Count - 1;
            openFolder.Enabled = selected != null;
            remove.Enabled = selected != null && !files.IsRoot(selected);
            needs.Visible = selected != null && ModChoices.IsOn(selected) && OffNeeds(selected).Any();
            allowCode.Visible = selected != null && selected.pluginFiles.Count > 0;
            allowCode.Text = selected != null && ModChoices.CodeOk(selected) ? "Stop Allowing Its Code" : "Allow Its Code";
        }

        private void CheckGame()
        {
            bool running = files.GameRunning;
            play.Enabled = !running;
            gameState.Text = running ? "The game is running: changes apply the next time it starts." : "";
            gameState.ForeColor = Warn;
        }

        internal void Say(string text, Color? color = null)
        {
            said.Text = text;
            said.ForeColor = color ?? SystemColors.ControlText;
        }

        private void Fail(string what, Exception ex)
        {
            Say($"{what}: {ex.Message}", Bad);
            MessageBox.Show(this, $"{what}.\n\n{ex.Message}", "Breachwork Mod Manager", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        // ---- load order

        /// <summary>
        /// Moves the selected mod to <paramref name="to"/>, or as close to it as its requires/loadAfter allow, and saves the
        /// order. Returns whether it moved.
        /// </summary>
        internal bool MoveTo(int to)
        {
            int from = order.IndexOf(selected);
            if (from < 0) return false;
            to = Math.Max(0, Math.Min(order.Count - 1, to));
            string why = null;
            for (int at = to; at != from; at += Math.Sign(from - to))
            {
                var moved = order.ToList();
                moved.RemoveAt(from);
                moved.Insert(at, selected);
                string blocked = Blocked(moved, selected);
                if (blocked != null) { why = why ?? blocked; continue; }
                ModChoices.SetOrder(moved.Select(p => p.id).Distinct(StringComparer.OrdinalIgnoreCase));
                Say(why == null ? $"Saved: {selected.Title} moved to place {at + 1}." : $"Saved: {selected.Title} moved to place {at + 1}, as far as it can go. {why}");
                Fill();
                return true;
            }
            if (why != null) { Say(why, Bad); SystemSounds.Beep.Play(); }
            return false;
        }

        /// <summary>Why <paramref name="p"/> can't sit where it is in <paramref name="l"/>, or null.</summary>
        private static string Blocked(List<ModPack> l, ModPack p)
        {
            int i = l.IndexOf(p);
            string How(ModPack a, string id) => (a.requires ?? new List<string>()).Contains(id, StringComparer.OrdinalIgnoreCase) ? "needs" : "loads after";
            foreach (string dep in ModPacks.After(p))
            {
                int j = l.FindIndex(x => string.Equals(x.id, dep, StringComparison.OrdinalIgnoreCase));
                if (j > i) return $"{p.Title} {How(p, dep)} {l[j].Title}, so it has to come after it.";
            }
            for (int j = 0; j < i; j++)
                if (ModPacks.After(l[j]).Contains(p.id, StringComparer.OrdinalIgnoreCase))
                    return $"{l[j].Title} {How(l[j], p.id)} {p.Title}, so it has to come after it.";
            return null;
        }

        // ---- on/off

        private IEnumerable<ModPack> OffNeeds(ModPack p) =>
            (p.requires ?? new List<string>()).Select(ModPacks.Find).Where(r => r != null && !next.Ordered.Contains(r));

        internal void SetOn(ModPack p, bool on)
        {
            ModChoices.Set(p, on);
            selected = p;
            Say($"Saved: {p.Title} turned {(on ? "on" : "off")}.");
            Fill();
        }

        /// <summary>Gives or takes back the player's OK for the selected mod's plugin code.</summary>
        internal void ToggleCode()
        {
            if (selected == null) return;
            bool allow = !ModChoices.CodeOk(selected);
            if (allow) ModChoices.AllowCode(selected);
            else ModChoices.ForbidCode(selected);
            Say($"Saved: {selected.Title}'s code is {(allow ? "allowed" : "no longer allowed")}.");
            Fill();
        }

        /// <summary>Turns on everything the selected mod needs (and what that needs) that is installed but off.</summary>
        internal void TurnOnNeeds()
        {
            if (selected == null) return;
            var seen = new HashSet<ModPack>();
            var turned = new List<string>();
            void Up(ModPack m)
            {
                if (!seen.Add(m)) return;
                foreach (ModPack r in (m.requires ?? new List<string>()).Select(ModPacks.Find).Where(r => r != null))
                {
                    if (!ModChoices.IsOn(r)) { ModChoices.Set(r, true); turned.Add(r.Title); }
                    Up(r);
                }
            }
            Up(selected);
            Say(turned.Count > 0 ? "Saved: turned on " + string.Join(", ", turned) + "." : "Nothing to turn on: what it needs is missing or can't load; see its details.");
            Fill();
        }

        // ---- install and remove

        private void PickZip()
        {
            using (var dlg = new OpenFileDialog { Title = "Install mods from a .zip", Filter = "Zip archives (*.zip)|*.zip", Multiselect = true })
                if (dlg.ShowDialog(this) == DialogResult.OK) foreach (string f in dlg.FileNames) Install(f);
        }

        private void PickFolder()
        {
            using (var dlg = new FolderBrowserDialog { Description = "A mod folder (with its mod.json), or an unpacked download" })
                if (dlg.ShowDialog(this) == DialogResult.OK) Install(dlg.SelectedPath);
        }

        internal void Install(string path)
        {
            try
            {
                var done = files.Install(path, target => MessageBox.Show(this,
                    $"\"{Path.GetFileName(target)}\" is already installed.\n\nReplace it with the one from {Path.GetFileName(path)}? The installed copy goes to the Recycle Bin.",
                    "Breachwork Mod Manager", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes);
                files.Rescan();
                if (done.Count > 0) selected = ModPacks.All.FirstOrDefault(p => string.Equals(Path.GetFileName(p.folder), done[0], StringComparison.OrdinalIgnoreCase)) ?? selected;
                Say(done.Count > 0 ? "Installed: " + string.Join(", ", done) + "." : "Nothing installed.");
                Fill();
            }
            catch (Exception ex) { Fail("Could not install " + Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar)), ex); }
        }

        private void Remove()
        {
            ModPack p = selected;
            if (p == null || files.IsRoot(p)) return;
            var users = ModPacks.All.Where(o => o != p && (o.requires ?? new List<string>()).Contains(p.id, StringComparer.OrdinalIgnoreCase)).Select(o => o.Title).ToList();
            string msg = $"Move \"{p.Title}\" ({p.folder}) to the Recycle Bin?" +
                (users.Count > 0 ? $"\n\n{string.Join(", ", users)} need{(users.Count == 1 ? "s" : "")} it and won't load without it." : "") +
                (SavesUsing(p).Count > 0 ? "\n\nSaves using its classes: " + string.Join(", ", SavesUsing(p).Select(s => s.save).Distinct()) + "." : "") +
                "\n\nTo keep it but not load it, untick it instead.";
            if (MessageBox.Show(this, msg, "Remove mod", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            try
            {
                files.Remove(p);
                files.Rescan();
                selected = null;
                Say($"Removed: {p.Title} (in the Recycle Bin).");
                Fill();
            }
            catch (Exception ex) { Fail("Could not remove " + p.Title, ex); }
        }

        // ---- details

        private List<(string save, string cls)> SavesUsing(ModPack p) =>
            p.classes.Where(c => ModPacks.PatchTarget(p, c.id) == null)
                .SelectMany(c => files.SaveUse.TryGetValue(c.id, out var l) ? l.Select(s => (s, c.name)) : Enumerable.Empty<(string, string)>()).ToList();

        private void ShowInfo()
        {
            info.Clear();
            ModPack p = selected;
            if (p == null) { Add($"No mods in {files.ModsDir}.\n", Faint); return; }

            Add(p.Title + "\n", null, 15f, FontStyle.Bold);
            Add(string.Join("  ·  ", new[] { p.id, string.IsNullOrEmpty(p.version) ? null : "v" + p.version,
                string.IsNullOrEmpty(p.author) ? null : "by " + p.author, p.hasManifest ? null : "no mod.json" }.Where(s => s != null)) + "\n", Faint);
            if (!string.IsNullOrEmpty(p.license)) Add("License: " + p.license + "\n", Faint);
            int at = next.Ordered.IndexOf(p);
            var (status, color) = Status(p);
            Add("\n" + (at >= 0 ? $"Loads {Ordinal(at + 1)} of {next.Ordered.Count}" : status) + "\n", color, null, FontStyle.Bold);
            if (!string.IsNullOrEmpty(p.description)) Add("\n" + p.description + "\n");
            Add("\n");

            Head("Folder");
            Add(p.folder.StartsWith(files.GameDir, StringComparison.OrdinalIgnoreCase) ? p.folder.Substring(files.GameDir.Length + 1) : p.folder);
            Add("\n");
            void Deps(string label, IEnumerable<string> ids, bool optional)
            {
                var l = ids?.ToList();
                if (l == null || l.Count == 0) return;
                Head(label);
                for (int i = 0; i < l.Count; i++)
                {
                    ModPack d = ModPacks.Find(l[i]);
                    if (i > 0) Add(", ");
                    if (d == null) Add(l[i] + " (not installed)", optional ? Faint : Bad);
                    else if (next.Ordered.Contains(d)) Add(d.Title, Good);
                    else Add(d.Title + (ModChoices.IsOn(d) ? " (can't load)" : " (off)"), optional ? Faint : Bad);
                }
                Add("\n");
            }
            Deps("Needs", p.requires, false);
            Deps("Loads after", p.loadAfter, true);
            Deps("Needed by", ModPacks.All.Where(o => (o.requires ?? new List<string>()).Contains(p.id, StringComparer.OrdinalIgnoreCase)).OrderBy(o => o.Title).Select(o => o.id), true);
            Head("Contents");
            Add(ModPacks.Contents(p) + "\n");
            if (p.scriptFiles.Count > 0)
            {
                Head("Scripts");
                Add(string.Join(", ", p.scriptFiles.Select(Path.GetFileName)) + "\n");
                Add("Scripts run in a sandbox: they reach the game only through the scripting API, never your files or the internet, so they need no OK.\n");
            }
            if (p.pluginFiles.Count > 0)
            {
                Head("Code");
                Add(string.Join(", ", p.pluginFiles.Select(Path.GetFileName)) + "\n");
                Add("This mod runs its own program inside the game, with the same access to your computer as the game itself. Only allow it if you trust where it came from.\n", Warn);
                Add(ModChoices.CodeOk(p) ? "Allowed as it is now; you will be asked again if it changes.\n" : "Not allowed, so the mod doesn't load.\n", ModChoices.CodeOk(p) ? Good : Bad);
            }

            var saves = SavesUsing(p).GroupBy(t => t.save).OrderBy(g => g.Key).Select(g => $"{g.Key} ({string.Join(", ", g.Select(t => t.cls).Distinct())})").ToList();
            if (saves.Count > 0)
            {
                Head("Saves using its classes");
                Add(string.Join(", ", saves) + "\n");
                if (at < 0) Add("While this mod doesn't load, those characters load with that skill slot empty.\n", Warn);
            }

            var bad = files.ScanErrors.Where(f => ModPacks.Mentions(p, f.file, files.ModsDir)).ToList();
            if (bad.Count > 0)
            {
                Add("\nFiles that could not be read:\n", Bad, null, FontStyle.Bold);
                foreach (var (file, error) in bad) Add($"  {Path.GetFileName(file)}: {error}\n", Bad);
            }

            var log = files.Log.Where(l => ModPacks.Mentions(p, l.text, files.ModsDir)).ToList();
            Add("\n");
            if (files.LogTime == null) Add("No game log yet: start the game once to see what the loader says about this mod.\n", Faint);
            else if (log.Count == 0) Add($"Nothing about this mod in the last game start's log ({files.LogTime:g}).\n", Faint);
            else
            {
                Add($"The last game start's log ({files.LogTime:g}) has {ModPacks.Count(log.Count, "line", "lines")} about it:\n", null, null, FontStyle.Bold);
                foreach (var (error, text) in log.Take(30)) Add("  " + (text.Length > 500 ? text.Substring(0, 500) + "…" : text) + "\n", error ? Bad : Warn);
                if (log.Count > 30) Add($"  …and {log.Count - 30} more in {files.LogFile}\n", Faint);
            }
            info.SelectionStart = 0;
            info.ScrollToCaret();
        }

        private static string Ordinal(int n) => n + (n % 100 is int t && t >= 11 && t <= 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" });

        private void Head(string label) => Add(label + ": ", Faint);

        private void Add(string text, Color? color = null, float? size = null, FontStyle style = FontStyle.Regular)
        {
            info.SelectionStart = info.TextLength;
            info.SelectionLength = 0;
            info.SelectionFont = new Font(info.Font.FontFamily, size ?? 10f, style);
            info.SelectionColor = color ?? SystemColors.WindowText;
            info.AppendText(text);
        }

        // ---- self-test hooks

        internal ModPack Selected => selected;
        internal string Said => said.Text;
        internal IReadOnlyList<ModPack> Order => order;
        internal string Details => info.Text;
        internal ListView List => list;
        internal bool NeedsShown => needs.Visible;
        internal bool AllowCodeShown => allowCode.Visible;

        internal void Select(string id)
        {
            selected = ModPacks.Find(id);
            Fill();
        }
    }
}
