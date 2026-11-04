using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Media;
using System.Security.Cryptography;
using System.Windows.Forms;
using Newtonsoft.Json.Linq;

namespace ChimeraModManager
{
    /// <summary>
    /// Owner tool (not in the player build): a mod's sealed sounds, opened with the owner's private key file, to play,
    /// export, replace, add or remove, then reseal. When a sound also exists as a loose file on the master copy (the
    /// files sfx_archive.py seals from), a replacement is written there too, so the next reseal from loose files keeps it,
    /// and that sound's .meta.json records the hand edit.
    /// </summary>
    internal sealed class SoundEditorForm : Form
    {
        private readonly ModFiles files;
        private readonly string modFolder;
        private readonly ListView list;
        private readonly ComboBox paks;
        private readonly Label state;
        private readonly Button play, stop, export, exportAll, replace, add, remove, save, keyFile, sealLoose;
        private List<RSACryptoServiceProvider> keys;
        private string keySource;
        private SoundArchive arc;
        private SoundPlayer player;

        internal SoundEditorForm(ModFiles files, string modFolder, string title)
        {
            this.files = files;
            this.modFolder = modFolder;
            Text = $"Sounds: {title}";
            Font = new Font("Segoe UI", 9f);
            float s; using (Graphics g = CreateGraphics()) s = g.DpiX / 96f;
            int Px(int v) => (int)(v * s);
            Size = new Size(Px(980), Px(620));
            MinimumSize = new Size(Px(700), Px(400));
            StartPosition = FormStartPosition.CenterParent;

            var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(Px(8)), WrapContents = false };
            top.Controls.Add(new Label { Text = "Archive:", AutoSize = true, Margin = new Padding(0, Px(6), Px(4), 0) });
            paks = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = Px(220) };
            paks.SelectedIndexChanged += (o, e) => OpenSelected();
            top.Controls.Add(paks);
            keyFile = Btn("Key file…", PickKey, Px); top.Controls.Add(keyFile);
            sealLoose = Btn("Seal loose files", SealLoose, Px); top.Controls.Add(sealLoose);
            state = new Label { AutoSize = true, Margin = new Padding(Px(10), Px(6), 0, 0) };
            top.Controls.Add(state);

            list = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = true, HideSelection = false };
            list.Columns.Add("Sound", Px(420));
            list.Columns.Add("Kind", Px(60));
            list.Columns.Add("Size", Px(80), HorizontalAlignment.Right);
            list.Columns.Add("Loose file", Px(110));
            list.Columns.Add("Changed", Px(220));
            list.SelectedIndexChanged += (o, e) => UpdateButtons();
            list.DoubleClick += (o, e) => Play();

            var side = new FlowLayoutPanel { Dock = DockStyle.Right, FlowDirection = FlowDirection.TopDown, Width = Px(150), Padding = new Padding(Px(8)) };
            play = Btn("▶  Play", Play, Px); stop = Btn("■  Stop", Stop, Px);
            export = Btn("Export…", Export, Px); exportAll = Btn("Export all…", ExportAll, Px);
            replace = Btn("Replace…", Replace, Px); add = Btn("Add…", Add, Px); remove = Btn("Remove", Remove, Px);
            save = Btn("Save && reseal", Save, Px); save.Font = new Font(Font, FontStyle.Bold);
            foreach (var b in new[] { play, stop, export, exportAll, replace, add, remove, save }) { b.Width = Px(130); side.Controls.Add(b); }

            Controls.Add(list);
            Controls.Add(side);
            Controls.Add(top);
            FormClosing += (o, e) =>
            {
                if (arc != null && arc.Dirty && MessageBox.Show(this, "There are changes that are not resealed. Close anyway?", Text,
                        MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) e.Cancel = true;
                else Stop();
            };

            keys = SoundArchive.OwnerKeys(files.GameDir, null, out keySource);
            foreach (string p in Directory.GetFiles(modFolder, "*.pak", SearchOption.TopDirectoryOnly).OrderBy(p => p))
                paks.Items.Add(Path.GetFileName(p));
            if (paks.Items.Count > 0) paks.SelectedIndex = 0; else ShowState();
        }

        private static Button Btn(string text, Action click, Func<int, int> px)
        {
            var b = new Button { Text = text, AutoSize = true, Height = px(30), Margin = new Padding(0, 0, px(6), px(6)) };
            b.Click += (o, e) => click();
            return b;
        }

        private void Fail(string what, Exception ex) =>
            MessageBox.Show(this, $"{what}:\n{ex.Message}", Text, MessageBoxButtons.OK, MessageBoxIcon.Error);

        private void ShowState(string extra = null)
        {
            if (keys.Count == 0)
                state.Text = $"No owner key: {keySource} is missing. This copy can seal sounds but not open them.";
            else if (arc == null)
                state.Text = paks.Items.Count == 0 ? "This mod has no sound archive yet: \"Seal loose files\" makes one." : extra ?? "";
            else
                state.Text = extra ?? $"{arc.Items.Count} sounds, sealed for loader key {arc.KeyId}" + (arc.Dirty ? " — changes not resealed yet" : "");
            UpdateButtons();
        }

        private void UpdateButtons()
        {
            bool open = arc != null, one = open && list.SelectedItems.Count == 1, any = open && list.SelectedItems.Count > 0;
            play.Enabled = one; export.Enabled = any; replace.Enabled = one; remove.Enabled = any;
            exportAll.Enabled = open; add.Enabled = open; save.Enabled = open && arc.Dirty;
            sealLoose.Enabled = Directory.GetFiles(modFolder, "*", SearchOption.AllDirectories).Any(f => SealAudio.Kinds.Contains(Path.GetExtension(f).ToLowerInvariant()));
        }

        private string LoosePath(SoundArchive.Item i) => Path.Combine(modFolder, i.Name.Replace('/', Path.DirectorySeparatorChar));

        private void Fill()
        {
            list.BeginUpdate();
            list.Items.Clear();
            if (arc != null)
                foreach (var i in arc.Items)
                {
                    string loose = LoosePath(i);
                    string looseState = !File.Exists(loose) ? "—" : new FileInfo(loose).Length == i.Data.Length && File.ReadAllBytes(loose).SequenceEqual(i.Data) ? "same" : "differs (wins in game)";
                    list.Items.Add(new ListViewItem(new[] { i.Name, i.Kind, $"{i.Data.Length / 1024.0:F0} KB", looseState, i.Changed ? "from " + Path.GetFileName(i.From) : "" }) { Tag = i });
                }
            list.EndUpdate();
            ShowState();
        }

        private void OpenSelected()
        {
            Stop();
            arc = null;
            if (keys.Count == 0 || paks.SelectedItem == null) { Fill(); return; }
            try { arc = SoundArchive.Open(Path.Combine(modFolder, (string)paks.SelectedItem), keys); }
            catch (Exception ex) { Fill(); ShowState($"Could not open {paks.SelectedItem}: {ex.Message}"); return; }
            Fill();
        }

        private void PickKey()
        {
            using (var dlg = new OpenFileDialog { Title = "The loader's private key file (loader_private.b64)", Filter = "Key file|*.b64|All files|*.*" })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                var k = SoundArchive.OwnerKeys(files.GameDir, dlg.FileName, out string src);
                if (k.Count == 0) { MessageBox.Show(this, "That file holds no private key.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                keys = k; keySource = src;
                OpenSelected();
            }
        }

        private SoundArchive.Item One => list.SelectedItems.Count == 1 ? (SoundArchive.Item)list.SelectedItems[0].Tag : null;
        private IEnumerable<SoundArchive.Item> Picked => list.SelectedItems.Cast<ListViewItem>().Select(v => (SoundArchive.Item)v.Tag).ToList();

        private void Play()
        {
            var i = One;
            if (i == null) return;
            Stop();
            if (i.Kind != "WAV") { MessageBox.Show(this, $"{i.Kind} plays in the game but not here; export it to listen.", Text); return; }
            try { player = new SoundPlayer(new MemoryStream(i.Data)); player.Play(); }
            catch (Exception ex) { Fail("Could not play it", ex); }
        }

        private void Stop()
        {
            try { player?.Stop(); } catch { }
            player?.Dispose(); player = null;
        }

        private static string Ext(SoundArchive.Item i) =>
            i.Kind == "FLAC" ? ".flac" : i.Kind == "OGG" ? ".ogg" : i.Kind == "MP3" ? ".mp3" : Path.GetExtension(i.Name);

        private void Export()
        {
            var picked = Picked.ToList();
            if (picked.Count == 1)
            {
                var i = picked[0];
                using (var dlg = new SaveFileDialog { FileName = Path.GetFileNameWithoutExtension(i.Name) + Ext(i), Filter = "Sound|*" + Ext(i) })
                    if (dlg.ShowDialog(this) == DialogResult.OK) { File.WriteAllBytes(dlg.FileName, i.Data); ShowState($"Exported {i.Name}."); }
                return;
            }
            ExportTo(picked);
        }

        private void ExportAll() => ExportTo(arc.Items);

        private void ExportTo(IEnumerable<SoundArchive.Item> items)
        {
            using (var dlg = new FolderBrowserDialog { Description = "Export the sounds here (their archive folders are kept)" })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                int n = 0;
                foreach (var i in items)
                {
                    string to = Path.Combine(dlg.SelectedPath, Path.ChangeExtension(i.Name.Replace('/', Path.DirectorySeparatorChar), Ext(i)));
                    Directory.CreateDirectory(Path.GetDirectoryName(to));
                    File.WriteAllBytes(to, i.Data); n++;
                }
                ShowState($"Exported {n} sounds to {dlg.SelectedPath}.");
            }
        }

        private const string SoundFilter = "Sounds|*.wav;*.ogg;*.mp3;*.flac|All files|*.*";

        private void Replace()
        {
            var i = One;
            if (i == null) return;
            using (var dlg = new OpenFileDialog { Title = $"Replace {i.Name} with", Filter = SoundFilter })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    Stop();
                    arc.Replace(i, dlg.FileName);
                    SyncLoose(i);
                    Fill();
                }
                catch (Exception ex) { Fail("Could not replace it", ex); }
            }
        }

        /// <summary>Keep the master copy's loose file in step (the reseal from loose files would otherwise undo the edit),
        /// and say in the sound's sidecar that it was edited by hand, from which file.</summary>
        private void SyncLoose(SoundArchive.Item i)
        {
            string loose = LoosePath(i);
            if (File.Exists(loose)) File.WriteAllBytes(loose, i.Data);
            string meta = Path.ChangeExtension(loose, null) + ".meta.json";
            if (!File.Exists(meta)) return;
            var j = JObject.Parse(File.ReadAllText(meta));
            j["hand_edited"] = new JObject { ["date"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm"), ["from"] = Path.GetFileName(i.From), ["via"] = "mod manager sound editor" };
            File.WriteAllText(meta, j.ToString());
        }

        private void Add()
        {
            using (var dlg = new OpenFileDialog { Title = "Add a sound", Filter = SoundFilter })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                string name = Microsoft.VisualBasic.Interaction.InputBox("Path inside the mod (as the mod's files name it):", Text, "sfx/" + Path.GetFileName(dlg.FileName));
                if (string.IsNullOrWhiteSpace(name)) return;
                try { var it = arc.Add(name.Trim(), dlg.FileName); SyncLoose(it); Fill(); }
                catch (Exception ex) { Fail("Could not add it", ex); }
            }
        }

        private void Remove()
        {
            var picked = Picked.ToList();
            if (picked.Count == 0) return;
            bool loose = picked.Any(i => File.Exists(LoosePath(i)));
            if (MessageBox.Show(this, $"Remove {picked.Count} sound(s) from the archive?" + (loose ? "\n\nTheir loose files stay (and still play, and would be sealed again by a reseal from loose files)." : ""),
                    Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            Stop();
            foreach (var i in picked) arc.Remove(i);
            Fill();
        }

        private void Save()
        {
            try
            {
                Stop();
                string backups = Path.Combine(files.GameDir, "_modding", "audio", "pak_backups", Path.GetFileName(modFolder));
                string key = arc.Save(keys, backups, out string backup);
                string name = Path.GetFileName(arc.Pak);
                arc = SoundArchive.Open(arc.Pak, keys);
                Fill();
                ShowState($"Resealed {name}: {arc.Items.Count} sounds for loader key {key}, read back and checked"
                          + (backup != null ? $"; the old one is in {backup.Substring(files.GameDir.Length + 1)}." : "."));
            }
            catch (Exception ex) { Fail("Could not reseal", ex); }
        }

        private void SealLoose()
        {
            if (MessageBox.Show(this, "Seal every loose sound in this mod into sounds.pak (replacing it)?", Text, MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            var log = new List<string>();
            int rc = SealAudio.Run(modFolder, false, log.Add);
            MessageBox.Show(this, string.Join("\n", log), Text, MessageBoxButtons.OK, rc == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            paks.Items.Clear();
            foreach (string p in Directory.GetFiles(modFolder, "*.pak", SearchOption.TopDirectoryOnly).OrderBy(p => p)) paks.Items.Add(Path.GetFileName(p));
            if (paks.Items.Count > 0) paks.SelectedIndex = 0;
        }
    }
}
