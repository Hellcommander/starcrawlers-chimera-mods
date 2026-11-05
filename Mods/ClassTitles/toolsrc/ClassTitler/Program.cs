using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ClassTitler
{
    /// <summary>
    /// The Class Titler: pick up to seven classes with their descriptions in view, have a local Ollama write titles for
    /// that exact combination, and save the one you like. It runs while the game is closed, so Ollama never shares video
    /// memory with the game. Saved titles go into Mods/YourClassTitles, a data mod the game loads like any other.
    /// </summary>
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            string game = FindGame();
            // --check id1 id2 ...: write the title the game would give these classes to stdout and exit (for testing)
            if (args.Length > 0 && args[0] == "--check")
            {
                var cat = Catalog.Load(CatalogFolder(game));
                var picked = args.Skip(1).Select(id => cat.Classes.FirstOrDefault(c => c.id == id)).Where(c => c != null).ToList();
                string t = cat.GameTitle(picked, out bool saved);
                File.WriteAllText(Path.Combine(Path.GetTempPath(), "classtitler_check.txt"), $"{picked.Count} classes: {t}{(saved ? " (saved)" : "")}\n{cat.Classes.Count} classes in the catalog");
                return 0;
            }
            // --ask id1 id2 ...: Ollama's titles for these classes to classtitler_ask.txt (for testing)
            if (args.Length > 1 && args[0] == "--ask")
            {
                var cat = Catalog.Load(CatalogFolder(game));
                var picked = args.Skip(1).Select(id => cat.Classes.FirstOrDefault(c => c.id == id)).Where(c => c != null).ToList();
                var sw = Stopwatch.StartNew();
                string outText;
                try { outText = string.Join("\n", Ollama.Titles(cat.OllamaUrl, cat.OllamaModel, picked)); }
                catch (Exception e) { outText = "ERROR " + e.GetBaseException().Message; }
                File.WriteAllText(Path.Combine(Path.GetTempPath(), "classtitler_ask.txt"), $"{picked.Count} classes, {sw.Elapsed.TotalSeconds:0.0}s\n{outText}\n---\n{Ollama.Prompt(picked, 8)}");
                return 0;
            }
            // --forge id1 id2 ...: the wordsmith's titles for these classes to classtitler_forge.txt (for testing)
            if (args.Length > 1 && args[0] == "--forge")
            {
                var cat = Catalog.Load(CatalogFolder(game));
                var picked = args.Skip(1).Select(id => cat.Classes.FirstOrDefault(c => c.id == id)).Where(c => c != null).ToList();
                var lines = cat.Forge(picked).Select(t => $"{t.text}  ({t.covered}/{picked.Count}: {string.Join(", ", t.parts.Select(kv => kv.Key.TrimStart('-') + "=" + string.Join("/", kv.Value)))})");
                File.WriteAllText(Path.Combine(Path.GetTempPath(), "classtitler_forge.txt"), $"{picked.Count} classes, max {cat.WordsmithLength}\n" + string.Join("\n", lines));
                return 0;
            }
            // --smith id1 id2 ...: Ollama's identity-word titles, rerolling until one fits, to classtitler_smith.txt (for testing)
            if (args.Length > 1 && args[0] == "--smith")
            {
                var cat = Catalog.Load(CatalogFolder(game));
                var picked = args.Skip(1).Select(id => cat.Classes.FirstOrDefault(c => c.id == id)).Where(c => c != null).ToList();
                var sw = Stopwatch.StartNew();
                string outText;
                try
                {
                    string known = cat.GameTitle(picked, out bool isSaved);
                    var r = Idea.Roll(cat, cat.OllamaUrl, cat.OllamaModel, picked, Idea.DefaultMin(picked.Count), new List<string>(), 5, null, out int rolls, isSaved ? known : null);
                    outText = $"{rolls} rolls\n" + string.Join("\n", r.Select(i => i.ToString() + "  [" + string.Join(", ", i.Names) + "]"));
                }
                catch (Exception e) { outText = "ERROR " + e.GetBaseException().Message; }
                File.WriteAllText(Path.Combine(Path.GetTempPath(), "classtitler_smith.txt"), $"{picked.Count} classes, {sw.Elapsed.TotalSeconds:0.0}s, fits = names {Idea.DefaultMin(picked.Count)}+ in {cat.WordsmithLength} letters\n{outText}\n---\n{Ollama.SmithPrompt(picked, 8, cat.WordsmithLength, null, Idea.DefaultMin(picked.Count), cat.Forge(picked, 1).FirstOrDefault()?.text)}");
                return 0;
            }
            // --save "Title" id1 id2 ... / --forget id1 id2 ...: what the Save and Delete buttons do (for testing)
            if (args.Length > 1 && (args[0] == "--save" || args[0] == "--forget"))
            {
                var store = new SavedTitles(Path.Combine(game, "Mods", "YourClassTitles"));
                bool save = args[0] == "--save";
                string key = Catalog.Signature(args.Skip(save ? 2 : 1));
                if (save) store.Combos[key] = Ollama.Cap(Ollama.Tidy(args[1])); else store.Combos.Remove(key);
                store.Save();
                return 0;
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (game == null)
            {
                MessageBox.Show("Put the Class Titler in the game's Mods\\ClassTitles\\tools\\ClassTitler folder (it finds the game from there).", "Class Titler");
                return 1;
            }
            if (!File.Exists(Path.Combine(CatalogFolder(game), "classes.json")))
            {
                MessageBox.Show("No class list yet. Start the game once (with the Breachwork Loader) and close it: it writes the list this tool reads.", "Class Titler");
                return 1;
            }
            // --pick id1 id2 ...: open with these classes picked
            Application.Run(new TitlerForm(game, Catalog.Load(CatalogFolder(game)), args.Length > 1 && args[0] == "--pick" ? args.Skip(1) : Enumerable.Empty<string>()));
            return 0;
        }

        internal static string CatalogFolder(string game) => Path.Combine(game, "BepInEx", "cache", "BreachworkClassCatalog");

        /// <summary>The game folder: the nearest folder above this exe holding BepInEx and Mods.</summary>
        private static string FindGame()
        {
            for (var d = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory); d != null; d = d.Parent)
                if (Directory.Exists(Path.Combine(d.FullName, "BepInEx")) && Directory.Exists(Path.Combine(d.FullName, "Mods"))) return d.FullName;
            return null;
        }
    }

    internal class TitlerForm : Form
    {
        private static readonly Color Back = Color.FromArgb(9, 18, 26), Panel = Color.FromArgb(16, 32, 44), Line = Color.FromArgb(40, 70, 88),
            Text_ = Color.FromArgb(214, 230, 238), Dim = Color.FromArgb(140, 170, 186), Gold = Color.FromArgb(254, 191, 89);
        private const int MaxClasses = 7;

        private readonly string game;
        private readonly Catalog cat;
        private readonly SavedTitles saved;
        private readonly List<ClassInfo> picked = new List<ClassInfo>();
        private readonly ImageList icons = new ImageList { ImageSize = new Size(40, 40), ColorDepth = ColorDepth.Depth32Bit };
        private readonly Dictionary<string, Image> bigIcons = new Dictionary<string, Image>();

        private TextBox search, titleBox, url;
        private ListView list;
        private FlowLayoutPanel cards;
        private Label pickedHeader, gameTitle, status, forged, forgedWhy, titleNames;
        private ComboBox model;
        private ListBox ideas, savedList;
        private Button ask, reroll, save, forgeNext, forgeUse;
        private CheckBox smithMode;
        private NumericUpDown minNames;
        private int forgeRoll;
        /// <summary>Every title Ollama wrote for the picked classes so far: a reroll asks for new ones.</summary>
        private readonly List<string> rolled = new List<string>();

        public TitlerForm(string game, Catalog cat, IEnumerable<string> pick)
        {
            this.game = game;
            this.cat = cat;
            saved = new SavedTitles(cat.SaveTo != null && Directory.Exists(Path.GetDirectoryName(cat.SaveTo)) ? cat.SaveTo : Path.Combine(game, "Mods", "YourClassTitles"));
            Text = "Class Titler - Breachwork";
            BackColor = Back;
            ForeColor = Text_;
            Font = new Font("Segoe UI", 9.5f);
            ClientSize = new Size(1360, 820);
            MinimumSize = new Size(1100, 640);
            StartPosition = FormStartPosition.CenterScreen;
            LoadIcons();
            Build();
            Fill();
            RefreshSaved();
            foreach (string id in pick)
            {
                var c = cat.Classes.FirstOrDefault(x => x.id.Equals(id, StringComparison.OrdinalIgnoreCase));
                if (c != null && !picked.Contains(c) && picked.Count < MaxClasses) picked.Add(c);
            }
            foreach (ListViewItem i in list.Items) Mark(i);
            RefreshPicked();
            Shown += (_, __) => { WarnIfGameRuns(); LoadModels(); };
        }

        // ---- layout -------------------------------------------------------------------------------------------------

        private Label Lbl(string text, float size = 9.5f, FontStyle style = FontStyle.Regular, Color? color = null) =>
            new Label { Text = text, AutoSize = true, Font = new Font("Segoe UI", size, style), ForeColor = color ?? Text_, BackColor = Color.Transparent };

        private Button Btn(string text, EventHandler click)
        {
            var b = new Button { Text = text, AutoSize = true, FlatStyle = FlatStyle.Flat, BackColor = Panel, ForeColor = Text_, Padding = new Padding(8, 3, 8, 3) };
            b.FlatAppearance.BorderColor = Line;
            b.Click += click;
            return b;
        }

        private void Build()
        {
            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 2, Padding = new Padding(10), BackColor = Back };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 330));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 400));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(grid);

            // left: every class
            var left = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Margin = new Padding(0, 0, 10, 0) };
            left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            left.Controls.Add(Lbl($"Classes ({cat.Classes.Count})", 12f, FontStyle.Bold, Gold));
            search = new TextBox { Dock = DockStyle.Fill, BackColor = Panel, ForeColor = Text_, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(0, 6, 0, 6) };
            search.TextChanged += (_, __) => Fill();
            left.Controls.Add(search);
            list = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HeaderStyle = ColumnHeaderStyle.None, SmallImageList = icons,
                BackColor = Panel, ForeColor = Text_, BorderStyle = BorderStyle.None, MultiSelect = false, ShowItemToolTips = true };
            list.Columns.Add("Class", 290);
            list.ItemActivate += (_, __) => Toggle(list.SelectedItems.Count > 0 ? list.SelectedItems[0].Tag as ClassInfo : null);
            list.MouseClick += (_, e) => { var hit = list.HitTest(e.Location).Item; if (hit != null) Toggle(hit.Tag as ClassInfo); };
            left.Controls.Add(list);
            grid.Controls.Add(left, 0, 0);

            // middle: the classes you picked, in full
            var mid = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
            mid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            pickedHeader = Lbl("", 12f, FontStyle.Bold, Gold);
            mid.Controls.Add(pickedHeader);
            cards = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Back };
            cards.Resize += (_, __) => FitCards();
            mid.Controls.Add(cards);
            grid.Controls.Add(mid, 1, 0);

            // right: the title
            var right = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(10, 0, 0, 0), AutoScroll = true };
            right.Controls.Add(Lbl("In game now", 9f, FontStyle.Regular, Dim));
            gameTitle = Lbl("", 13f, FontStyle.Italic, Gold);
            gameTitle.MaximumSize = new Size(380, 0);
            right.Controls.Add(gameTitle);

            right.Controls.Add(Spacer(10));
            right.Controls.Add(Lbl("Wordsmith (TitleSource = Wordsmith in game)", 9f, FontStyle.Regular, Dim));
            forged = Lbl("", 12f, FontStyle.Italic, Gold);
            forged.MaximumSize = new Size(380, 0);
            right.Controls.Add(forged);
            forgedWhy = Lbl("", 8.5f, FontStyle.Regular, Dim);
            forgedWhy.MaximumSize = new Size(380, 0);
            right.Controls.Add(forgedWhy);
            var frow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            forgeNext = Btn("Next", (_, __) => { forgeRoll++; ShowForged(); });
            forgeUse = Btn("Use this title", (_, __) => { if (forged.Tag is string t) titleBox.Text = t; });
            frow.Controls.Add(forgeNext);
            frow.Controls.Add(forgeUse);
            right.Controls.Add(frow);

            right.Controls.Add(Spacer(14));
            right.Controls.Add(Lbl("Ollama", 12f, FontStyle.Bold, Gold));
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            url = new TextBox { Text = cat.OllamaUrl, Width = 190, BackColor = Panel, ForeColor = Text_, BorderStyle = BorderStyle.FixedSingle };
            model = new ComboBox { Width = 170, DropDownStyle = ComboBoxStyle.DropDown, BackColor = Panel, ForeColor = Text_, FlatStyle = FlatStyle.Flat, Text = cat.OllamaModel };
            row.Controls.Add(url);
            row.Controls.Add(model);
            right.Controls.Add(row);
            smithMode = new CheckBox { Text = "Build from the classes' identity words", Checked = true, AutoSize = true, ForeColor = Text_ };
            smithMode.CheckedChanged += (_, __) => { minNames.Enabled = smithMode.Checked; };
            right.Controls.Add(smithMode);
            var mrow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            mrow.Controls.Add(Lbl("Fits when it names at least", 9f, FontStyle.Regular, Dim));
            minNames = new NumericUpDown { Minimum = 1, Maximum = 7, Value = 5, Width = 44, BackColor = Panel, ForeColor = Text_ };
            mrow.Controls.Add(minNames);
            mrow.Controls.Add(Lbl($"classes in {cat.WordsmithLength} letters", 9f, FontStyle.Regular, Dim));
            right.Controls.Add(mrow);
            var arow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            ask = Btn("Write titles", (_, __) => Ask(false));
            reroll = Btn("Reroll until one fits", (_, __) => Ask(true));
            arow.Controls.Add(ask);
            arow.Controls.Add(reroll);
            right.Controls.Add(arow);
            ideas = new ListBox { Width = 380, Height = 170, BackColor = Panel, ForeColor = Text_, BorderStyle = BorderStyle.None, Font = new Font("Segoe UI", 10.5f, FontStyle.Italic) };
            ideas.SelectedIndexChanged += (_, __) => { if (ideas.SelectedItem is Idea i) titleBox.Text = i.Title; else if (ideas.SelectedItem is string s && !s.EndsWith("...")) titleBox.Text = s; };
            right.Controls.Add(ideas);
            right.Controls.Add(Lbl("Title (edit it if you like)", 9f, FontStyle.Regular, Dim));
            titleBox = new TextBox { Width = 380, BackColor = Panel, ForeColor = Gold, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Segoe UI", 11f, FontStyle.Italic) };
            titleBox.TextChanged += (_, __) => { save.Enabled = picked.Count >= 2 && titleBox.Text.Trim() != ""; ShowTitleNames(); };
            right.Controls.Add(titleBox);
            titleNames = Lbl("", 8.5f, FontStyle.Regular, Dim);
            titleNames.MaximumSize = new Size(380, 0);
            right.Controls.Add(titleNames);
            save = Btn("Save for these classes", (_, __) => Save());
            right.Controls.Add(save);

            right.Controls.Add(Spacer(14));
            right.Controls.Add(Lbl("Your saved titles", 12f, FontStyle.Bold, Gold));
            savedList = new ListBox { Width = 380, Height = 170, BackColor = Panel, ForeColor = Text_, BorderStyle = BorderStyle.None, HorizontalScrollbar = true };
            savedList.DoubleClick += (_, __) => OpenSaved();
            right.Controls.Add(savedList);
            var srow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            srow.Controls.Add(Btn("Pick these classes", (_, __) => OpenSaved()));
            srow.Controls.Add(Btn("Delete", (_, __) => DeleteSaved()));
            right.Controls.Add(srow);
            grid.Controls.Add(right, 2, 0);

            status = Lbl("", 9f, FontStyle.Regular, Dim);
            grid.Controls.Add(status, 0, 1);
            grid.SetColumnSpan(status, 3);
            status.Text = $"Class list from the game, {cat.Written.ToLocalTime():g}. Click a class to add or remove it (up to {MaxClasses}). Saved titles go to {saved.Folder}.";
        }

        private static Control Spacer(int h) => new Panel { Height = h, Width = 10, BackColor = Color.Transparent };

        private void LoadIcons()
        {
            icons.Images.Add("none", new Bitmap(40, 40));
            foreach (ClassInfo c in cat.Classes)
            {
                if (c.icon == null) continue;
                string path = Path.Combine(cat.Folder, c.icon.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(path)) continue;
                try
                {
                    using (var fs = File.OpenRead(path)) bigIcons[c.id] = new Bitmap(Image.FromStream(fs));
                    icons.Images.Add(c.id, bigIcons[c.id]);
                }
                catch { }
            }
        }

        // ---- the class list -------------------------------------------------------------------------------------

        private void Fill()
        {
            string q = search.Text.Trim();
            list.BeginUpdate();
            list.Items.Clear();
            foreach (ClassInfo c in cat.Classes)
            {
                string hay = c.name + " " + c.mod + " " + c.description + " " + string.Join(" ", c.skills) + " " + string.Join(" ", c.themes);
                if (q != "" && hay.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0) continue;
                var item = new ListViewItem(c.name, icons.Images.ContainsKey(c.id) ? c.id : "none") { Tag = c, ToolTipText = c.mod + " - " + string.Join(", ", c.themes) };
                Mark(item);
                list.Items.Add(item);
            }
            list.EndUpdate();
        }

        private void Mark(ListViewItem item)
        {
            bool on = picked.Contains(item.Tag as ClassInfo);
            item.BackColor = on ? Color.FromArgb(46, 74, 48) : Panel;
            item.Text = (on ? "✓ " : "") + ((ClassInfo)item.Tag).name;
        }

        private void Toggle(ClassInfo c)
        {
            if (c == null) return;
            if (picked.Contains(c)) picked.Remove(c);
            else if (picked.Count >= MaxClasses) { status.Text = $"Seven classes is the most a character has: remove one first."; return; }
            else picked.Add(c);
            foreach (ListViewItem i in list.Items) Mark(i);
            RefreshPicked();
        }

        // ---- picked classes -----------------------------------------------------------------------------------------

        private void RefreshPicked()
        {
            pickedHeader.Text = $"Your classes ({picked.Count}/{MaxClasses})";
            cards.SuspendLayout();
            cards.Controls.Clear();
            foreach (ClassInfo c in picked) cards.Controls.Add(Card(c));
            if (picked.Count == 0) cards.Controls.Add(Lbl("Click classes on the left. Their descriptions show here, and the title on the right is the one the game gives this exact set.", 10f, FontStyle.Regular, Dim));
            cards.ResumeLayout();
            // the scroll bar may have just appeared: every card fits the column
            FitCards();
            string t = cat.GameTitle(picked, out bool isSaved);
            if (!isSaved && saved.Combos.TryGetValue(Catalog.Signature(picked.Select(p => p.id)), out string mine)) { t = mine; isSaved = true; }
            gameTitle.Text = picked.Count == 0 ? "-" : t + (isSaved ? "   (saved)" : "");
            ideas.Items.Clear();
            rolled.Clear();
            forgeRoll = 0;
            ShowForged();
            ShowTitleNames();
            minNames.Value = Math.Max(1, Math.Min(7, Idea.DefaultMin(Math.Max(1, picked.Count))));
            ask.Enabled = reroll.Enabled = picked.Count >= 1;
            save.Enabled = picked.Count >= 2 && titleBox.Text.Trim() != "";
            var scores = Catalog.ThemeScores(picked);
            if (picked.Count > 0) status.Text = "Themes: " + string.Join(", ", scores.Select(kv => kv.Key + " " + kv.Value));
        }

        /// <summary>Every card as wide as the column (auto-size would shrink a short one).</summary>
        private void FitCards()
        {
            int w = Math.Max(200, cards.ClientSize.Width - 24);
            foreach (Control c in cards.Controls) if (c is TableLayoutPanel) { c.MinimumSize = new Size(w, 0); c.MaximumSize = new Size(w, 0); c.Width = w; }
        }

        private Control Card(ClassInfo c)
        {
            var card = new TableLayoutPanel { ColumnCount = 3, RowCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Panel,
                Width = cards.ClientSize.Width - 24, Margin = new Padding(0, 0, 0, 8), Padding = new Padding(8) };
            card.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72));
            card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            card.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            card.Controls.Add(new PictureBox { Size = new Size(64, 64), SizeMode = PictureBoxSizeMode.Zoom, Image = bigIcons.TryGetValue(c.id, out Image im) ? im : null }, 0, 0);
            var text = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Dock = DockStyle.Fill };
            text.Controls.Add(Lbl(c.name, 11.5f, FontStyle.Bold, Gold));
            text.Controls.Add(Lbl($"{c.archetype} - {c.mod} - plays as {string.Join(", ", c.themes.DefaultIfEmpty("?"))}", 8.5f, FontStyle.Regular, Dim));
            var desc = Lbl(Catalog.StatNames(c.description), 9.5f);
            desc.MaximumSize = new Size(Math.Max(300, cards.ClientSize.Width - 170), 0);
            text.Controls.Add(desc);
            if (c.skills.Count > 0)
            {
                var sk = Lbl("Skills: " + string.Join(", ", c.skills), 8.5f, FontStyle.Italic, Dim);
                sk.MaximumSize = desc.MaximumSize;
                text.Controls.Add(sk);
            }
            card.Controls.Add(text, 1, 0);
            card.Controls.Add(Btn("Remove", (_, __) => Toggle(c)), 2, 0);
            return card;
        }

        // ---- Ollama ---------------------------------------------------------------------------------------------

        private void WarnIfGameRuns()
        {
            if (Process.GetProcessesByName("StarCrawlers Chimera").Length > 0)
                status.Text = "The game is running: close it first, so Ollama has the video memory to itself.";
        }

        private void LoadModels()
        {
            string u = url.Text;
            Task.Run(() => Ollama.Models(u)).ContinueWith(t => BeginInvoke((Action)(() =>
            {
                if (t.Exception != null) { status.Text = $"Ollama isn't answering at {u}: start it (ollama serve), or fix the address. Picking and saving work without it."; return; }
                string current = model.Text;
                model.Items.Clear();
                foreach (string m in t.Result) model.Items.Add(m);
                if (!t.Result.Contains(current) && t.Result.Count > 0) model.Text = t.Result[0];
            })));
        }

        // ---- the wordsmith ---------------------------------------------------------------------------------------

        private void ShowForged()
        {
            var list = picked.Count == 0 ? new List<Breachwork.Titles.WordsmithTitle>() : cat.Forge(picked);
            forgeNext.Enabled = forgeUse.Enabled = list.Count > 0;
            if (list.Count == 0) { forged.Text = "-"; forged.Tag = null; forgedWhy.Text = ""; return; }
            var t = list[forgeRoll % list.Count];
            forged.Text = t.text;
            forged.Tag = t.text;
            forgedWhy.Text = $"Names {t.covered} of {picked.Count}: " + string.Join(", ", t.parts.Select(kv => kv.Key.TrimStart('-') + " (" + string.Join(", ", kv.Value) + ")"))
                + $"   [{forgeRoll % list.Count + 1}/{list.Count}]";
        }

        /// <summary>Which of the picked classes the title in the box names.</summary>
        private void ShowTitleNames()
        {
            if (titleNames == null) return;
            string t = titleBox.Text.Trim();
            if (t == "" || picked.Count == 0) { titleNames.Text = ""; return; }
            var named = Catalog.Named(t, picked);
            titleNames.Text = $"Names {named.Count} of {picked.Count}" + (named.Count > 0 ? ": " + string.Join(", ", named.Select(c => c.name)) : "")
                + (t.Length > cat.WordsmithLength ? $"   ({t.Length} letters, over {cat.WordsmithLength})" : "");
        }

        /// <summary>
        /// Asks Ollama. With identity words on, every title is scored by the classes it names and a title fits when it
        /// names enough of them in WordsmithLength letters; <paramref name="untilFits"/> rerolls (new seed, earlier
        /// titles barred) until one fits or five rolls are used. Earlier rolls stay in the list.
        /// </summary>
        private void Ask(bool untilFits)
        {
            if (picked.Count == 0) return;
            WarnIfGameRuns();
            var classes = picked.ToList();
            string u = url.Text, m = model.Text;
            bool smith = smithMode.Checked;
            int min = (int)minNames.Value;
            var avoid = rolled.ToList();
            string known = cat.GameTitle(classes, out bool isSaved);
            if (!isSaved && saved.Combos.TryGetValue(Catalog.Signature(classes.Select(c => c.id)), out string mine)) { known = mine; isSaved = true; }
            string example = isSaved ? known : null;
            ask.Enabled = reroll.Enabled = false;
            var before = ideas.Items.OfType<Idea>().ToList();
            ideas.Items.Clear();
            ideas.Items.Add($"{m} is writing... (the first one loads the model)");
            Action<int> progress = n => BeginInvoke((Action)(() => { if (ideas.Items.Count > 0 && ideas.Items[0] is string) ideas.Items[0] = $"{m} is writing... roll {n}, none fits yet"; }));
            Task.Run(() =>
            {
                if (!smith) return Ollama.Titles(u, m, classes).Select(x => new Idea { Title = x, Names = new List<string>(), Fits = true }).ToList();
                return Idea.Roll(cat, u, m, classes, min, avoid, untilFits ? 5 : 1, progress, out _, example);
            }).ContinueWith(t => BeginInvoke((Action)(() =>
            {
                ask.Enabled = reroll.Enabled = true;
                ideas.Items.Clear();
                if (t.Exception != null) { ideas.Items.Add("No titles: " + t.Exception.GetBaseException().Message); foreach (var i in before) ideas.Items.Add(i); return; }
                if (!classes.SequenceEqual(picked)) { status.Text = "The classes changed while Ollama was writing: ask again."; return; }
                rolled.AddRange(t.Result.Select(i => i.Title));
                var all = t.Result.Concat(before).GroupBy(i => i.Title, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
                    .OrderByDescending(i => i.Fits).ThenByDescending(i => i.Names.Count).ThenBy(i => i.Title.Length).ToList();
                foreach (var i in all) ideas.Items.Add(i);
                if (all.Count == 0) ideas.Items.Add("Ollama's answer had no usable titles: ask again.");
                int fit = t.Result.Count(i => i.Fits);
                status.Text = !smith ? "Pick one (or edit it), then save it for these classes. Ask again for more."
                    : fit > 0 ? $"{fit} new title(s) fit (✓). Pick one, edit it if you like, then save it for these classes."
                    : "None fits yet: Reroll asks for new ones, or lower how many classes a title must name.";
            })));
        }

        // ---- saving ---------------------------------------------------------------------------------------------

        private void Save()
        {
            string title = Ollama.Cap(Ollama.Tidy(titleBox.Text));
            if (picked.Count < 2 || title == "") return;
            saved.Combos[Catalog.Signature(picked.Select(p => p.id))] = title;
            try { saved.Save(); }
            catch (Exception e) { MessageBox.Show("Could not save: " + e.Message, "Class Titler"); return; }
            RefreshSaved();
            RefreshPicked();
            status.Text = $"Saved \"{title}\". The game uses it for a character with exactly these {picked.Count} classes (next launch).";
        }

        private void RefreshSaved()
        {
            savedList.Items.Clear();
            foreach (var kv in saved.Combos)
            {
                var names = kv.Key.Split('+').Select(id => cat.Classes.FirstOrDefault(c => c.id.Equals(id, StringComparison.OrdinalIgnoreCase))?.name ?? id);
                savedList.Items.Add(new SavedItem { Key = kv.Key, Text = $"{kv.Value}  -  {string.Join(", ", names)}" });
            }
        }

        private class SavedItem { public string Key, Text; public override string ToString() => Text; }

        private void OpenSaved()
        {
            if (!(savedList.SelectedItem is SavedItem s)) return;
            picked.Clear();
            foreach (string id in s.Key.Split('+'))
            {
                var c = cat.Classes.FirstOrDefault(x => x.id.Equals(id, StringComparison.OrdinalIgnoreCase));
                if (c != null) picked.Add(c);
            }
            titleBox.Text = saved.Combos[s.Key];
            foreach (ListViewItem i in list.Items) Mark(i);
            RefreshPicked();
        }

        private void DeleteSaved()
        {
            if (!(savedList.SelectedItem is SavedItem s)) return;
            saved.Combos.Remove(s.Key);
            try { saved.Save(); } catch (Exception e) { MessageBox.Show("Could not save: " + e.Message, "Class Titler"); }
            RefreshSaved();
            RefreshPicked();
        }
    }

    /// <summary>One title Ollama wrote, with the classes it names through their identity words.</summary>
    internal class Idea
    {
        public string Title;
        public List<string> Names = new List<string>();
        public bool Fits;
        public int Of;

        public override string ToString() => (Fits ? "✓ " : "   ") + Title + (Of > 0 ? $"   - names {Names.Count}/{Of}" : "");

        /// <summary>How many classes a title must name to fit, by default: all of up to three, else two fewer.</summary>
        public static int DefaultMin(int classes) => classes <= 3 ? classes : classes - 2;

        /// <summary>
        /// Asks Ollama's identity-word titles up to <paramref name="maxRolls"/> times, each roll with a new seed and the
        /// titles so far barred, until one names at least <paramref name="min"/> classes within WordsmithLength letters.
        /// </summary>
        public static List<Idea> Roll(Catalog cat, string url, string model, List<ClassInfo> classes, int min, List<string> avoid, int maxRolls, Action<int> progress, out int rolls, string example = null)
        {
            var found = new List<Idea>();
            var barred = new List<string>(avoid ?? new List<string>());
            var rng = new Random();
            // a title already saved for these classes is the best guide to the kind of name wanted; else the wordsmith's
            example = example ?? cat.Forge(classes, 1).FirstOrDefault()?.text;
            List<string> missing = null;
            rolls = 0;
            while (rolls < Math.Max(1, maxRolls))
            {
                rolls++;
                progress?.Invoke(rolls);
                foreach (string t in Ollama.SmithTitles(url, model, classes, cat.WordsmithLength, barred, rng.Next(), min, example, missing))
                {
                    if (barred.Contains(t, StringComparer.OrdinalIgnoreCase)) continue;
                    barred.Add(t);
                    var named = Catalog.Named(t, classes);
                    found.Add(new Idea { Title = t, Names = named.Select(c => c.name).ToList(), Of = classes.Count, Fits = named.Count >= min && t.Length <= cat.WordsmithLength });
                }
                if (found.Any(i => i.Fits)) break;
                // the next roll hears which classes the best title so far left out
                var best = found.Where(i => i.Title.Length <= cat.WordsmithLength).OrderByDescending(i => i.Names.Count).FirstOrDefault();
                if (best != null) missing = classes.Select(c => c.name).Where(n => !best.Names.Contains(n)).ToList();
            }
            return found.OrderByDescending(i => i.Fits).ThenByDescending(i => i.Names.Count).ThenBy(i => i.Title.Length).ToList();
        }
    }
}
