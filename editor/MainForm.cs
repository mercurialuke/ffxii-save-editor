using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace FFXIISaveEditor
{
    static class Program
    {
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

        [STAThread]
        static void Main(string[] args)
        {
            try { SetProcessDPIAware(); } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += (s, e) => ShowError(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => ShowError(e.ExceptionObject as Exception);
            // Development aid: --shots <save> <dir> renders every tab to PNGs and exits. Never writes the save.
            if (args.Length == 3 && args[0] == "--shots")
            {
                var f = new MainForm(args[1]);
                f.Shown += delegate { f.CaptureTabs(args[2]); f.Close(); };
                Application.Run(f);
                return;
            }
            Application.Run(new MainForm(args.Length > 0 ? args[0] : null));
        }

        static void ShowError(Exception e)
        {
            MessageBox.Show("Something went wrong in the editor. Nothing has been written unless you saw \"Saved\".\n\n" + e,
                "FFXII save editor", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // A combo box / list entry carrying an ID.
    class Entry
    {
        public readonly int Id;
        public readonly string Text;
        public Entry(int id, string text) { Id = id; Text = text; }
        public override string ToString() { return Text; }
    }

    class MainForm : Form
    {
        static readonly string SaveRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "My Games", "FINAL FANTASY XII THE ZODIAC AGE");
        static string BackupRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FFXII Save Editor", "Backups");
        const int None = 0xFFFF;
        static string GameProcess = "FFXII_TZA";

        Save save;
        bool loading;
        readonly float scale;

        ComboBox saveBox;
        Button saveButton;
        TabControl tabs;
        ToolStripStatusLabel statusText, problemText;

        public MainForm(string path)
        {
            Text = "FFXII: The Zodiac Age save editor";
            Font = SystemFonts.MessageBoxFont;
            AutoScaleMode = AutoScaleMode.None;
            using (var g = CreateGraphics()) scale = g.DpiX / 96f;
            ClientSize = new Size(S(960), S(680));
            MinimumSize = new Size(S(760), S(520));
            StartPosition = FormStartPosition.CenterScreen;

            BuildTop();
            BuildTabs();
            var status = new StatusStrip();
            statusText = new ToolStripStatusLabel { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
            problemText = new ToolStripStatusLabel { ForeColor = Color.DarkRed };
            status.Items.Add(statusText);
            status.Items.Add(problemText);
            Controls.Add(status);

            FillSaveList(path);
        }

        int S(int px) { return (int)Math.Round(px * scale); }

        internal void CaptureTabs(string dir)
        {
            Directory.CreateDirectory(dir);
            int n = 0;
            Action<string> shot = name =>
            {
                Application.DoEvents();
                using (var bmp = new Bitmap(Width, Height))
                {
                    DrawToBitmap(bmp, new Rectangle(0, 0, Width, Height));
                    bmp.Save(Path.Combine(dir, string.Format("{0:00}-{1}.png", n++, name)));
                }
            };
            var subTabs = new Dictionary<int, TabControl> { { 1, charTabs }, { 2, invTabs }, { 3, ownTabs } };
            for (int t = 0; t < tabs.TabCount; t++)
            {
                tabs.SelectedIndex = t;
                TabControl sub;
                if (!subTabs.TryGetValue(t, out sub)) { shot(tabs.TabPages[t].Text); continue; }
                for (int i = 0; i < sub.TabCount; i++)
                {
                    sub.SelectedIndex = i;
                    shot(tabs.TabPages[t].Text + "-" + sub.TabPages[i].Text);
                }
            }
            save = null;   // skip the unsaved-changes prompt on close
        }

        // ------------------------------------------------------------------ save selection

        void BuildTop()
        {
            var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(S(6)), WrapContents = false };
            bar.Controls.Add(new Label { Text = "Save:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, S(7), S(4), 0) });
            saveBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = S(440), DropDownWidth = S(700) };
            saveBox.SelectedIndexChanged += delegate { OnSaveSelected(); };
            bar.Controls.Add(saveBox);
            bar.Controls.Add(MakeButton("Open file…", delegate { OpenOther(); }));
            bar.Controls.Add(MakeButton("Reload", delegate { if (save != null && ConfirmDiscard()) LoadSave(save.FilePath); }));
            saveButton = MakeButton("Save", delegate { WriteSave(); });
            saveButton.Font = new Font(Font, FontStyle.Bold);
            bar.Controls.Add(saveButton);
            bar.Controls.Add(MakeButton("Save as slot…", delegate { SaveAs(); }));
            bar.Controls.Add(MakeButton("Backups", delegate
            {
                Directory.CreateDirectory(BackupRoot);
                Process.Start("explorer.exe", "\"" + BackupRoot + "\"");
            }));
            Controls.Add(bar);
        }

        Button MakeButton(string text, EventHandler click)
        {
            var b = new Button { Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(S(6), S(1), S(6), S(1)) };
            b.Click += click;
            return b;
        }

        class SaveChoice
        {
            public string Path, Label, Suffix = "";
            public override string ToString() { return Label; }
        }

        void FillSaveList(string select)
        {
            var choices = new List<SaveChoice>();
            if (Directory.Exists(SaveRoot))
                foreach (var dir in Directory.GetDirectories(SaveRoot))
                    foreach (var f in Directory.GetFiles(dir))
                        if (Regex.IsMatch(Path.GetFileName(f), @"^FFXII_\d{3}$"))
                            choices.Add(new SaveChoice { Path = f, Label = Describe(f) });
            if (select != null && !choices.Any(c => SamePath(c.Path, select)))
            {
                string suffix = "  ·  " + Path.GetDirectoryName(Path.GetFullPath(select));
                choices.Add(new SaveChoice { Path = select, Label = Describe(select) + suffix, Suffix = suffix });
            }
            choices = choices.OrderBy(c => c.Path, StringComparer.OrdinalIgnoreCase).ToList();

            loading = true;
            saveBox.Items.Clear();
            foreach (var c in choices) saveBox.Items.Add(c);
            loading = false;
            if (choices.Count == 0)
            {
                statusText.Text = "No saves found in " + SaveRoot + ". Use Open file… to pick one.";
                SetEnabled(false);
                return;
            }
            var pick = select != null ? choices.First(c => SamePath(c.Path, select))
                : choices.OrderByDescending(c => File.GetLastWriteTime(c.Path)).First();
            saveBox.SelectedItem = pick;
        }

        static bool SamePath(string a, string b)
        {
            return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
        }

        static string Describe(string path)
        {
            string name = Path.GetFileName(path);
            var m = Regex.Match(name, @"^FFXII_(\d{3})$");
            string slot = m.Success ? "Slot " + (int.Parse(m.Groups[1].Value) + 1) : name;
            try
            {
                var s = new Save(path);
                var leader = s.U8(Save.PartySummary) < Save.CharCount ? s.Characters[s.U8(Save.PartySummary)] : s.Characters[0];
                return string.Format("{0}  ·  {1} Lv {2}  ·  {3:N0} gil  ·  saved {4:yyyy-MM-dd HH:mm}  ·  {5}",
                    slot, leader.Name, leader.Level, s.GilAmount, File.GetLastWriteTime(path), name);
            }
            catch (Exception e)
            {
                return slot + "  ·  unreadable (" + e.Message + ")  ·  " + name;
            }
        }

        void OnSaveSelected()
        {
            if (loading) return;
            var c = saveBox.SelectedItem as SaveChoice;
            if (c == null || (save != null && SamePath(save.FilePath, c.Path))) return;
            if (!ConfirmDiscard())
            {
                loading = true;
                saveBox.SelectedItem = saveBox.Items.Cast<SaveChoice>().FirstOrDefault(x => SamePath(x.Path, save.FilePath));
                loading = false;
                return;
            }
            LoadSave(c.Path);
        }

        void OpenOther()
        {
            using (var d = new OpenFileDialog { Title = "Open an FFXII save", Filter = "FFXII saves (FFXII_*)|FFXII_*|All files|*.*" })
            {
                if (Directory.Exists(SaveRoot)) d.InitialDirectory = SaveRoot;
                if (d.ShowDialog(this) == DialogResult.OK && ConfirmDiscard())
                {
                    save = null;
                    FillSaveList(d.FileName);
                }
            }
        }

        void LoadSave(string path)
        {
            try
            {
                save = new Save(path);
            }
            catch (Exception e)
            {
                save = null;
                SetEnabled(false);
                MessageBox.Show(this, "Couldn't read " + path + ":\n\n" + e.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            SetEnabled(true);
            var problems = save.CheckConsistency();
            problemText.Text = problems.Count == 0 ? "" : problems.Count + " oddities in this save (hover)";
            problemText.ToolTipText = string.Join("\n", problems.Take(20));
            ReadIni();
            PopulateAll();
            UpdateStatus();
        }

        void SetEnabled(bool on)
        {
            tabs.Enabled = on;
            saveButton.Enabled = on;
        }

        bool ConfirmDiscard()
        {
            if (save == null || (!save.Dirty && !IniDirty)) return true;
            var r = MessageBox.Show(this, "Save changes to " + Path.GetFileName(save.FilePath) + " first?", Text,
                MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (r == DialogResult.Cancel) return false;
            if (r == DialogResult.Yes) return WriteSave();
            return true;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!ConfirmDiscard()) e.Cancel = true;
            base.OnFormClosing(e);
        }

        void Changed()
        {
            if (!loading) UpdateStatus();
        }

        void UpdateStatus()
        {
            if (save == null) return;
            bool dirty = save.Dirty || IniDirty;
            statusText.Text = (dirty ? "Unsaved changes  ·  " : "") + save.FilePath;
            saveButton.Text = dirty ? "Save*" : "Save";
        }

        // ------------------------------------------------------------------ writing

        bool WriteSave()
        {
            if (save == null) return true;
            if (!ReadyToWrite()) return false;
            try
            {
                string path = save.FilePath;
                string backupDir = Path.Combine(BackupRoot, DateTime.Now.ToString("yyyyMMdd-HHmmss"), Path.GetFileName(Path.GetDirectoryName(path)));
                Directory.CreateDirectory(backupDir);
                if (save.Dirty)
                {
                    byte[] blob = save.Build();
                    File.Copy(path, Path.Combine(backupDir, Path.GetFileName(path)), true);
                    string tmp = path + ".editor-tmp";
                    File.WriteAllBytes(tmp, blob);
                    File.Copy(tmp, path, true);
                    File.Delete(tmp);
                    var check = new Save(path);
                    if (!check.Data.SequenceEqual(save.Data))
                        throw new IOException("The file on disk doesn't match after writing. Restore it from " + backupDir + ".");
                    save.MarkSaved();
                }
                if (IniDirty) WriteIni(backupDir);
                statusText.Text = "Saved. Backup in " + backupDir;
                saveButton.Text = "Save";
                RefreshSaveLabel();
                return true;
            }
            catch (Exception e)
            {
                MessageBox.Show(this, "Saving failed:\n\n" + e.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        bool ReadyToWrite()
        {
            foreach (var p in invPages) p.Grid.EndEdit();
            ValidateChildren();
            if (Process.GetProcessesByName(GameProcess).Length == 0) return true;
            return MessageBox.Show(this,
                "FFXII is running.\n\nWrite the save before you open the Load menu (the safest is with the game closed). " +
                "Saving in-game to this slot afterwards will overwrite the edit, and the confirm-button setting only applies with the game closed.\n\nWrite the save now?",
                Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) == DialogResult.OK;
        }

        // Writes the edited save to another slot (e.g. an empty one, to try changes safely) and switches to it.
        // The file it was loaded from is left untouched.
        void SaveAs()
        {
            if (save == null) return;
            string dir = Path.GetDirectoryName(save.FilePath);
            int free = 0;
            while (free < 99 && File.Exists(Path.Combine(dir, "FFXII_" + free.ToString("000")))) free++;
            string target;
            using (var d = new SaveFileDialog { Title = "Save to slot (FFXII_000 is slot 1)", InitialDirectory = dir, FileName = "FFXII_" + free.ToString("000"),
                Filter = "FFXII saves (FFXII_*)|FFXII_*|All files|*.*", OverwritePrompt = true, AddExtension = false })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                target = d.FileName;
            }
            SaveTo(target);
        }

        void SaveTo(string target)
        {
            string dir = Path.GetDirectoryName(save.FilePath);
            var m = Regex.Match(Path.GetFileName(target), @"^FFXII_(\d{3})$");
            if (!m.Success)
            {
                MessageBox.Show(this, "Name it FFXII_000, FFXII_001, … so the game finds it (FFXII_000 is slot 1).", Text);
                return;
            }
            if (SamePath(target, save.FilePath)) { WriteSave(); return; }
            if (!ReadyToWrite()) return;
            string source = save.FilePath;
            try
            {
                string backupDir = Path.Combine(BackupRoot, DateTime.Now.ToString("yyyyMMdd-HHmmss"), Path.GetFileName(dir));
                Directory.CreateDirectory(backupDir);
                if (File.Exists(target)) File.Copy(target, Path.Combine(backupDir, Path.GetFileName(target)), true);
                save.SetU8(0x54, int.Parse(m.Groups[1].Value));
                byte[] blob = save.Build();
                File.WriteAllBytes(target, blob);
                var check = new Save(target);
                if (!check.Data.SequenceEqual(save.Data))
                    throw new IOException("The new file doesn't match after writing.");
                if (IniDirty) WriteIni(backupDir);
                save = null;
                FillSaveList(target);
                statusText.Text = "Saved as " + Path.GetFileName(target) + " (slot " + (int.Parse(m.Groups[1].Value) + 1) + "); now editing that file.";
            }
            catch (Exception e)
            {
                MessageBox.Show(this, "Saving failed:\n\n" + e.Message + "\n\n" + Path.GetFileName(source) + " was not changed.", Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                save = null;
                FillSaveList(source);
            }
        }

        void RefreshSaveLabel()
        {
            var c = saveBox.SelectedItem as SaveChoice;
            if (c == null) return;
            c.Label = Describe(c.Path) + c.Suffix;
            loading = true;
            saveBox.Items[saveBox.SelectedIndex] = c;
            loading = false;
        }

        // ------------------------------------------------------------------ GameSetting.ini (confirm button)

        string iniPath;
        string iniDecide, iniDecideLoaded;
        bool IniDirty { get { return iniDecide != iniDecideLoaded; } }
        ComboBox confirmBox;

        void ReadIni()
        {
            iniPath = Path.Combine(Path.GetDirectoryName(save.FilePath), "GameSetting.ini");
            iniDecide = iniDecideLoaded = null;
            if (!File.Exists(iniPath)) return;
            foreach (var line in File.ReadAllLines(iniPath))
            {
                var m = Regex.Match(line, @"^\s*DecideButton\s*=\s*(\w+)");
                if (m.Success) iniDecide = iniDecideLoaded = m.Groups[1].Value;
            }
        }

        void WriteIni(string backupDir)
        {
            File.Copy(iniPath, Path.Combine(backupDir, "GameSetting.ini"), true);
            var lines = File.ReadAllLines(iniPath).Select(l =>
                Regex.IsMatch(l, @"^\s*DecideButton\s*=") ? "DecideButton=" + iniDecide : l).ToArray();
            File.WriteAllLines(iniPath, lines);
            iniDecideLoaded = iniDecide;
        }

        // ------------------------------------------------------------------ tabs

        void BuildTabs()
        {
            tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(S(10), S(4)) };
            tabs.TabPages.Add(BuildGeneralTab());
            tabs.TabPages.Add(BuildCharactersTab());
            tabs.TabPages.Add(BuildInventoryTab());
            tabs.TabPages.Add(BuildAbilitiesTab());
            tabs.SelectedIndexChanged += delegate { PopulateAll(); };
            Controls.Add(tabs);
            tabs.BringToFront();
        }

        void PopulateAll()
        {
            if (save == null) return;
            loading = true;
            try
            {
                PopulateGeneral();
                PopulateCharacterList();
                PopulateCharacter();
                PopulateInventory();
                PopulateAbilities();
            }
            finally { loading = false; }
        }

        static TableLayoutPanel Table(int columns)
        {
            var t = new TableLayoutPanel { ColumnCount = columns, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
            for (int i = 0; i < columns; i++) t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            return t;
        }

        static Label Caption(string text)
        {
            return new Label { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 6, 8, 3) };
        }

        Label Note(string text, int width)
        {
            return new Label { Text = text, AutoSize = true, MaximumSize = new Size(S(width), 0), ForeColor = SystemColors.GrayText, Margin = new Padding(3, S(8), 3, 3) };
        }

        NumericUpDown Number(decimal max, int decimals, Action<decimal> set)
        {
            var n = new NumericUpDown { Maximum = max, DecimalPlaces = decimals, Width = S(110), ThousandsSeparator = decimals == 0 && max > 9999, TextAlign = HorizontalAlignment.Right };
            if (decimals > 0) n.Increment = 0.25m;
            n.ValueChanged += delegate { if (!loading && save != null) { set(n.Value); Changed(); } };
            return n;
        }

        static void SetNumber(NumericUpDown n, decimal v) { n.Value = Math.Max(n.Minimum, Math.Min(n.Maximum, v)); }

        ComboBox Combo(int width, Action<ComboBox> changed)
        {
            var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = S(width), MaxDropDownItems = 25 };
            c.SelectedIndexChanged += delegate { if (!loading && save != null) { changed(c); Changed(); } };
            return c;
        }

        static void SelectId(ComboBox c, int id) { SelectId(c, id, Save.NameOf(id)); }

        static void SelectId(ComboBox c, int id, string fallback)
        {
            foreach (Entry e in c.Items)
                if (e.Id == id) { c.SelectedItem = e; return; }
            var extra = new Entry(id, fallback);
            c.Items.Add(extra);
            c.SelectedItem = extra;
        }

        static int SelectedId(ComboBox c)
        {
            var e = c.SelectedItem as Entry;
            return e == null ? None : e.Id;
        }

        // ------------------------------------------------------------------ General

        NumericUpDown gilBox, clanBox, musicBox, sfxBox, voiceBox;
        CheckBox subtitlesBox, japaneseBox;
        ComboBox trackBox, battleBox;

        TabPage BuildGeneralTab()
        {
            var page = new TabPage("General") { AutoScroll = true, Padding = new Padding(S(10)) };
            var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };

            var party = Table(2);
            gilBox = Number(99999999, 0, v => save.GilAmount = (long)v);
            clanBox = Number(99999999, 0, v => save.Clan = (long)v);
            party.Controls.Add(Caption("Gil")); party.Controls.Add(gilBox);
            party.Controls.Add(Caption("Clan points")); party.Controls.Add(clanBox);
            flow.Controls.Add(Group("Party", party));

            var settings = Table(2);
            subtitlesBox = new CheckBox { Text = "Subtitles", AutoSize = true };
            subtitlesBox.CheckedChanged += delegate { if (!loading) { save.Subtitles = subtitlesBox.Checked; Changed(); } };
            japaneseBox = new CheckBox { Text = "Japanese voices", AutoSize = true };
            japaneseBox.CheckedChanged += delegate { if (!loading) { save.JapaneseVoices = japaneseBox.Checked; Changed(); } };
            musicBox = Number(100, 0, v => save.MusicVolume = (int)v);
            sfxBox = Number(100, 0, v => save.SfxVolume = (int)v);
            voiceBox = Number(100, 0, v => save.VoiceVolume = (int)v);
            trackBox = Combo(220, c => save.MusicTrack = c.SelectedIndex);
            trackBox.Items.AddRange(new object[] { "Original", "Reorchestrated", "Original soundtrack (OST)" });
            battleBox = Combo(220, c => save.ActiveBattle = c.SelectedIndex == 1);
            battleBox.Items.AddRange(new object[] { "Wait", "Active" });
            settings.Controls.Add(subtitlesBox); settings.SetColumnSpan(subtitlesBox, 2);
            settings.Controls.Add(japaneseBox); settings.SetColumnSpan(japaneseBox, 2);
            settings.Controls.Add(Caption("Music volume")); settings.Controls.Add(musicBox);
            settings.Controls.Add(Caption("Sound effects volume")); settings.Controls.Add(sfxBox);
            settings.Controls.Add(Caption("Voice volume")); settings.Controls.Add(voiceBox);
            settings.Controls.Add(Caption("Music")); settings.Controls.Add(trackBox);
            settings.Controls.Add(Caption("Battle mode")); settings.Controls.Add(battleBox);
            flow.Controls.Add(Group("Options stored in this save", settings));

            var ini = Table(2);
            confirmBox = Combo(260, c => iniDecide = c.SelectedIndex == 0 ? "Cross" : "Circle");
            confirmBox.Items.AddRange(new object[] { "Type A (A / Cross confirms)", "Type B (B / Circle confirms)" });
            ini.Controls.Add(Caption("Confirm button")); ini.Controls.Add(confirmBox);
            var iniNote = Note("Stored in GameSetting.ini next to the saves and shared by all slots. Change it with the game closed, because the game rewrites that file.", 420);
            ini.Controls.Add(iniNote); ini.SetColumnSpan(iniNote, 2);
            flow.Controls.Add(Group("Game settings (GameSetting.ini)", ini));

            page.Controls.Add(flow);
            return page;
        }

        GroupBox Group(string title, Control content)
        {
            var g = new GroupBox { Text = title, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(S(8)), Margin = new Padding(0, 0, 0, S(10)) };
            content.Dock = DockStyle.Fill;
            g.Controls.Add(content);
            return g;
        }

        void PopulateGeneral()
        {
            SetNumber(gilBox, save.GilAmount);
            SetNumber(clanBox, save.Clan);
            subtitlesBox.Checked = save.Subtitles;
            japaneseBox.Checked = save.JapaneseVoices;
            SetNumber(musicBox, save.MusicVolume);
            SetNumber(sfxBox, save.SfxVolume);
            SetNumber(voiceBox, save.VoiceVolume);
            trackBox.SelectedIndex = Math.Min(save.MusicTrack, 2);
            battleBox.SelectedIndex = save.ActiveBattle ? 1 : 0;
            confirmBox.Enabled = iniDecide != null;
            confirmBox.SelectedIndex = iniDecide == null ? -1 : iniDecide == "Circle" ? 1 : 0;
        }

        // ------------------------------------------------------------------ Characters

        ListBox charList;
        TabControl charTabs;
        NumericUpDown levelBox, expBox, lpBox, hpBox, maxHpBox, mpBox, maxMpBox;
        NumericUpDown[] statBoxes = new NumericUpDown[4];
        Label shownStats, partyNote;
        ComboBox job1Box, job2Box;
        ComboBox[] equipBoxes = new ComboBox[5];
        CheckBox[] gambitOn = new CheckBox[Character.GambitSlots];
        ComboBox[] gambitTarget = new ComboBox[Character.GambitSlots], gambitAction = new ComboBox[Character.GambitSlots];
        TextBox licenseFilter;
        CheckedListBox licenseList;
        Label licenseCount;

        Character Current { get { return save == null || charList.SelectedIndex < 0 ? null : save.Characters[charList.SelectedIndex]; } }

        TabPage BuildCharactersTab()
        {
            var page = new TabPage("Characters") { Padding = new Padding(S(6)) };
            charList = new ListBox { Dock = DockStyle.Left, Width = S(260), IntegralHeight = false };
            charList.SelectedIndexChanged += delegate { if (!loading) { loading = true; try { PopulateCharacter(); } finally { loading = false; } } };
            charTabs = new TabControl { Dock = DockStyle.Fill };
            charTabs.TabPages.Add(BuildStatsPage());
            charTabs.TabPages.Add(BuildEquipmentPage());
            charTabs.TabPages.Add(BuildGambitsPage());
            charTabs.TabPages.Add(BuildLicensesPage());
            page.Controls.Add(charTabs);
            page.Controls.Add(new Panel { Dock = DockStyle.Left, Width = S(6) });
            page.Controls.Add(charList);
            return page;
        }

        TabPage BuildStatsPage()
        {
            var page = new TabPage("Stats") { AutoScroll = true, Padding = new Padding(S(10)) };
            var t = Table(4);
            levelBox = Number(99, 0, v => { Current.Level = (int)v; RefreshCharacterName(); });
            levelBox.Minimum = 1;
            expBox = Number(99999999, 0, v => Current.Exp = (long)v);
            lpBox = Number(99999, 0, v => Current.Lp = (long)v);
            hpBox = Number(99999, 0, v => Current.Hp = (long)v);
            maxHpBox = Number(99999, 0, v => Current.MaxHp = (long)v);
            maxHpBox.Minimum = 1;
            mpBox = Number(9999, 0, v => Current.Mp = (int)v);
            maxMpBox = Number(9999, 0, v => Current.MaxMp = (int)v);
            string[] statNames = { "Strength", "Magick Power", "Vitality", "Speed" };
            for (int i = 0; i < 4; i++)
            {
                int stat = i;
                statBoxes[i] = Number(99, 2, v => Current.SetBaseStat(stat, (float)v));
            }
            AddPair(t, "Level", levelBox, "EXP", expBox);
            AddPair(t, "LP", lpBox, null, null);
            AddPair(t, "HP", hpBox, "Max HP", maxHpBox);
            AddPair(t, "MP", mpBox, "Max MP", maxMpBox);
            AddPair(t, "Base " + statNames[0], statBoxes[0], "Base " + statNames[1], statBoxes[1]);
            AddPair(t, "Base " + statNames[2], statBoxes[2], "Base " + statNames[3], statBoxes[3]);
            shownStats = new Label { AutoSize = true, Margin = new Padding(3, S(10), 3, 3) };
            t.Controls.Add(shownStats); t.SetColumnSpan(shownStats, 4);
            var note = Note("Base stats are the character's own values; the game adds equipment and licenses on top and recalculates " +
                "the totals above. Level and EXP are independent: if EXP is below what the level needs, the next level-up just comes later. " +
                "Raising Level doesn't add the HP and stats a real level-up would, so set those too if you want them.", 560);
            t.Controls.Add(note); t.SetColumnSpan(note, 4);
            partyNote = Note("", 560);
            t.Controls.Add(partyNote); t.SetColumnSpan(partyNote, 4);
            page.Controls.Add(t);
            return page;
        }

        static void AddPair(TableLayoutPanel t, string a, Control ac, string b, Control bc)
        {
            t.Controls.Add(Caption(a)); t.Controls.Add(ac);
            if (b != null) { var cb = Caption(b); cb.Margin = new Padding(24, 6, 8, 3); t.Controls.Add(cb); t.Controls.Add(bc); }
            else { t.Controls.Add(new Label { AutoSize = true }); t.Controls.Add(new Label { AutoSize = true }); }
        }

        TabPage BuildEquipmentPage()
        {
            var page = new TabPage("Jobs & equipment") { AutoScroll = true, Padding = new Padding(S(10)) };
            var t = Table(2);
            job1Box = Combo(240, c => Current.Job1 = SelectedId(c));
            job2Box = Combo(240, c => Current.Job2 = SelectedId(c));
            foreach (var box in new[] { job1Box, job2Box })
            {
                box.Items.Add(new Entry(0xFF, "(none)"));
                for (int j = 0; j < Character.Jobs.Length; j++) box.Items.Add(new Entry(j, Character.Jobs[j]));
            }
            t.Controls.Add(Caption("First job")); t.Controls.Add(job1Box);
            t.Controls.Add(Caption("Second job")); t.Controls.Add(job2Box);
            var spacer = new Label { AutoSize = true, Height = S(8) };
            t.Controls.Add(spacer); t.SetColumnSpan(spacer, 2);
            for (int s = 0; s < 5; s++)
            {
                int slot = s;
                equipBoxes[s] = Combo(340, c => { Current.SetEquipment(slot, SelectedId(c)); });
                t.Controls.Add(Caption(Character.Slots[s])); t.Controls.Add(equipBoxes[s]);
            }
            var note = Note("Jobs choose the license boards (in the game a second job needs the second-board unlock). " +
                "Equipment changes on party members also update the inventory: the item is added if you don't own one, " +
                "and the worn count stays correct. The editor doesn't check licenses or two-handed weapons.", 520);
            t.Controls.Add(note); t.SetColumnSpan(note, 2);
            page.Controls.Add(t);
            return page;
        }

        static IEnumerable<Entry> EquipChoices(int slot)
        {
            int[][] ranges = {
                new[] { 0x1000, 0x10C7 },
                new[] { 0x10C8, 0x10DB, 0x1184, 0x11A3 },
                new[] { 0x10DC, 0x1117 },
                new[] { 0x1118, 0x1153 },
                new[] { 0x1154, 0x1183 } };
            yield return new Entry(None, "(none)");
            var r = ranges[slot];
            for (int k = 0; k < r.Length; k += 2)
                for (int id = r[k]; id <= r[k + 1]; id++)
                {
                    string name = Tables.Equipment[id - 0x1000], type = Tables.EquipmentType[id - 0x1000];
                    if (name != "") yield return new Entry(id, type != "" && type != "Weapon" ? name + "  (" + type + ")" : name);
                }
        }

        TabPage BuildGambitsPage()
        {
            var page = new TabPage("Gambits") { AutoScroll = true, Padding = new Padding(S(10)) };
            var t = Table(4);
            t.Controls.Add(new Label { Text = "", AutoSize = true });
            t.Controls.Add(new Label { Text = "On", AutoSize = true });
            t.Controls.Add(new Label { Text = "Target", AutoSize = true });
            t.Controls.Add(new Label { Text = "Action", AutoSize = true });
            var targets = new List<Entry> { new Entry(None, "(empty)") };
            for (int i = 0; i < 256; i++)
                if (Tables.Gambits[i] != "") targets.Add(new Entry(0x6000 + i, Tables.Gambits[i]));
            var actions = new List<Entry> { new Entry(None, "(empty)") };
            foreach (var kv in Tables.GambitActions.OrderBy(kv => ActionGroup(kv.Key)).ThenBy(kv => kv.Key))
                actions.Add(new Entry(kv.Key, kv.Value));
            for (int i = 0; i < Character.GambitSlots; i++)
            {
                int slot = i;
                t.Controls.Add(new Label { Text = (i + 1).ToString(), AutoSize = true, Anchor = AnchorStyles.Left });
                gambitOn[i] = new CheckBox { AutoSize = true, Anchor = AnchorStyles.Left };
                gambitOn[i].CheckedChanged += delegate { if (!loading) StoreGambit(slot); };
                gambitTarget[i] = Combo(290, c => StoreGambit(slot));
                gambitAction[i] = Combo(220, c => StoreGambit(slot));
                gambitTarget[i].Items.AddRange(targets.ToArray());
                gambitAction[i].Items.AddRange(actions.ToArray());
                t.Controls.Add(gambitOn[i]); t.Controls.Add(gambitTarget[i]); t.Controls.Add(gambitAction[i]);
            }
            var note = Note("Picking a gambit, magick or technick the party doesn't own yet also adds it to the party's collection, " +
                "since the game only offers owned ones. The character still needs the license to use a magick or technick.", 560);
            t.Controls.Add(note); t.SetColumnSpan(note, 4);
            page.Controls.Add(t);
            return page;
        }

        static int ActionGroup(int id)
        {
            if (id == 150 || id == 146 || id == 275) return 0;    // Attack, Do nothing, Dismiss
            if (id <= 80) return 1;                                // magicks
            if (id >= 158 && id <= 181) return 2;                  // technicks
            if (id >= 82 && id <= 145) return 3;                   // items
            return 4;
        }

        void StoreGambit(int i)
        {
            int target = SelectedId(gambitTarget[i]), action = SelectedId(gambitAction[i]);
            // A slot with only one half picked is stored as empty until the other half is chosen.
            Current.SetGambit(i, target, action, gambitOn[i].Checked);
            Changed();
        }

        TabPage BuildLicensesPage()
        {
            var page = new TabPage("Licenses") { Padding = new Padding(S(8)) };
            var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false };
            top.Controls.Add(Caption("Filter"));
            licenseFilter = new TextBox { Width = S(200) };
            licenseFilter.TextChanged += delegate { loading = true; try { PopulateLicenses(); } finally { loading = false; } };
            top.Controls.Add(licenseFilter);
            top.Controls.Add(MakeButton("Check shown", delegate { SetShownLicenses(true); }));
            top.Controls.Add(MakeButton("Uncheck shown", delegate { SetShownLicenses(false); }));
            licenseCount = new Label { AutoSize = true, Margin = new Padding(S(10), S(7), 0, 0) };
            top.Controls.Add(licenseCount);
            licenseList = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false, MultiColumn = true, ColumnWidth = S(230) };
            licenseList.ItemCheck += (s, e) =>
            {
                if (loading) return;
                Current.SetLicense(((Entry)licenseList.Items[e.Index]).Id, e.NewValue == CheckState.Checked);
                BeginInvoke(new Action(() => { UpdateLicenseCount(); Changed(); }));
            };
            var note = Note("Licenses are stored as one bit each. Learning them here doesn't spend LP or apply their bonuses " +
                "(HP+, Battle Lore…) to stored values; the game applies passive bonuses when it recalculates stats.", 700);
            note.Dock = DockStyle.Bottom;
            page.Controls.Add(licenseList);
            page.Controls.Add(note);
            page.Controls.Add(top);
            return page;
        }

        void PopulateLicenses()
        {
            var c = Current;
            licenseList.BeginUpdate();
            licenseList.Items.Clear();
            if (c != null)
            {
                string f = licenseFilter.Text.Trim();
                for (int id = 0; id < Character.LicenseCount; id++)
                {
                    string name = Tables.Licenses[id];
                    if (name == "" || (f != "" && name.IndexOf(f, StringComparison.OrdinalIgnoreCase) < 0)) continue;
                    licenseList.Items.Add(new Entry(id, name), c.License(id));
                }
            }
            licenseList.EndUpdate();
            UpdateLicenseCount();
        }

        void UpdateLicenseCount()
        {
            var c = Current;
            if (c == null) { licenseCount.Text = ""; return; }
            int n = 0;
            for (int id = 0; id < Character.LicenseCount; id++) if (c.License(id)) n++;
            licenseCount.Text = n + " learned";
        }

        void SetShownLicenses(bool on)
        {
            var c = Current;
            if (c == null) return;
            foreach (Entry e in licenseList.Items) c.SetLicense(e.Id, on);
            loading = true;
            try { PopulateLicenses(); } finally { loading = false; }
            Changed();
        }

        void PopulateCharacterList()
        {
            int sel = Math.Max(0, charList.SelectedIndex);
            charList.BeginUpdate();
            charList.Items.Clear();
            foreach (var c in save.Characters) charList.Items.Add(CharacterLabel(c));
            charList.EndUpdate();
            charList.SelectedIndex = sel;
        }

        static string CharacterLabel(Character c)
        {
            return c.Name + "  ·  Lv " + c.Level + (c.InParty ? "" : "  (not in party)");
        }

        void RefreshCharacterName()
        {
            var c = Current;
            bool was = loading;
            loading = true;
            charList.Items[c.Index] = CharacterLabel(c);
            charList.SelectedIndex = c.Index;
            loading = was;
        }

        void PopulateCharacter()
        {
            var c = Current;
            if (c == null) return;
            SetNumber(levelBox, c.Level);
            SetNumber(expBox, c.Exp);
            SetNumber(lpBox, c.Lp);
            SetNumber(hpBox, c.Hp);
            SetNumber(maxHpBox, c.MaxHp);
            SetNumber(mpBox, c.Mp);
            SetNumber(maxMpBox, c.MaxMp);
            for (int i = 0; i < 4; i++) SetNumber(statBoxes[i], (decimal)Math.Round(c.BaseStat(i), 2));
            shownStats.Text = string.Format("Totals as last calculated by the game: Strength {0}, Magick {1}, Vitality {2}, Speed {3}, " +
                "Attack {4}, Defense {5}, Magick Resist {6}, Evade {7}",
                c.ShownStat(0), c.ShownStat(1), c.ShownStat(2), c.ShownStat(3), c.ShownStat(4), c.ShownStat(5), c.ShownStat(6), c.ShownStat(7));
            partyNote.Text = c.InParty ? "" : c.Name + " isn't in the active party in this save (a guest, someone who left, or someone who hasn't joined yet).";

            SelectId(job1Box, c.Job1);
            SelectId(job2Box, c.Job2);
            for (int s = 0; s < 5; s++)
            {
                var box = equipBoxes[s];
                box.BeginUpdate();
                box.Items.Clear();
                box.Items.AddRange(EquipChoices(s).ToArray());
                SelectId(box, c.Equipment(s));
                box.EndUpdate();
            }
            for (int i = 0; i < Character.GambitSlots; i++)
            {
                SelectId(gambitTarget[i], c.GambitTarget(i));
                SelectId(gambitAction[i], c.GambitAction(i), "Action #" + c.GambitAction(i));
                gambitOn[i].Checked = c.GambitOn(i);
            }
            PopulateLicenses();
        }

        // ------------------------------------------------------------------ Inventory

        class InvPage
        {
            public InvList List;
            public DataGridView Grid;
            public CheckBox OwnedOnly;
            public TextBox Filter;
        }

        readonly List<InvPage> invPages = new List<InvPage>();
        TabControl invTabs;

        TabPage BuildInventoryTab()
        {
            var page = new TabPage("Inventory") { Padding = new Padding(S(6)) };
            invTabs = new TabControl { Dock = DockStyle.Fill };
            foreach (var l in new[] { Save.Items, Save.Weapons, Save.Armor, Save.Accessories, Save.Ammo, Save.Loot })
                invTabs.TabPages.Add(BuildInventoryPage(l));
            invTabs.SelectedIndexChanged += delegate { PopulateInventory(); };
            page.Controls.Add(invTabs);
            return page;
        }

        TabPage BuildInventoryPage(InvList list)
        {
            var p = new InvPage { List = list };
            var page = new TabPage(list.Name) { Padding = new Padding(S(6)) };
            var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false };
            p.OwnedOnly = new CheckBox { Text = "Owned only", AutoSize = true, Margin = new Padding(3, S(6), S(12), 3) };
            p.OwnedOnly.CheckedChanged += delegate { PopulateInventoryPage(p); };
            top.Controls.Add(p.OwnedOnly);
            top.Controls.Add(Caption("Filter"));
            p.Filter = new TextBox { Width = S(180) };
            p.Filter.TextChanged += delegate { PopulateInventoryPage(p); };
            top.Controls.Add(p.Filter);
            top.Controls.Add(MakeButton("Owned → 99", delegate { Bulk(p, false); }));
            top.Controls.Add(MakeButton("All shown → 99", delegate { Bulk(p, true); }));

            var g = new DataGridView
            {
                Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false,
                RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.CellSelect, MultiSelect = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None, BackgroundColor = SystemColors.Window,
                EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2,
            };
            g.RowTemplate.Height = S(22);
            g.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "Name", ReadOnly = true, Width = S(260) });
            g.Columns.Add(new DataGridViewTextBoxColumn { Name = "Type", HeaderText = "Type", ReadOnly = true, Width = S(130) });
            g.Columns.Add(new DataGridViewTextBoxColumn { Name = "Qty", HeaderText = "Owned", Width = S(80), DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight, BackColor = Color.FromArgb(255, 255, 235) } });
            g.Columns.Add(new DataGridViewTextBoxColumn { Name = "Worn", HeaderText = "Worn", ReadOnly = true, Width = S(70), DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight } });
            g.Columns.Add(new DataGridViewTextBoxColumn { Name = "Id", HeaderText = "ID", ReadOnly = true, Width = S(70) });
            g.Columns["Type"].Visible = Save.IsEquipment(list.FirstId);
            g.Columns["Worn"].Visible = Save.IsEquipment(list.FirstId);
            g.CellValidating += (s, e) =>
            {
                if (g.Columns[e.ColumnIndex].Name != "Qty") return;
                int v;
                if (!int.TryParse(Convert.ToString(e.FormattedValue), NumberStyles.Integer, CultureInfo.CurrentCulture, out v) || v < 0 || v > Save.MaxQuantity)
                {
                    e.Cancel = true;
                    statusText.Text = "Enter a number from 0 to " + Save.MaxQuantity + ".";
                }
            };
            g.CellEndEdit += (s, e) =>
            {
                var row = g.Rows[e.RowIndex];
                int id = (int)row.Tag;
                save.SetQuantity(id, int.Parse(Convert.ToString(row.Cells["Qty"].Value), CultureInfo.CurrentCulture));
                FillRow(row, id);
                Changed();
            };
            p.Grid = g;
            page.Controls.Add(g);
            page.Controls.Add(top);
            invPages.Add(p);
            return page;
        }

        void FillRow(DataGridViewRow row, int id)
        {
            row.Cells["Qty"].Value = save.Quantity(id);
            row.Cells["Worn"].Value = Save.IsEquipment(id) && save.Equipped(id) > 0 ? save.Equipped(id).ToString() : "";
            row.DefaultCellStyle.ForeColor = save.Quantity(id) > 0 ? SystemColors.WindowText : SystemColors.GrayText;
        }

        IEnumerable<int> ShownIds(InvPage p)
        {
            string f = p.Filter.Text.Trim();
            for (int id = p.List.FirstId; id <= p.List.LastId; id++)
            {
                string name = p.List.NameOf(id);
                bool owned = save.Quantity(id) > 0;
                if (name == "" && !owned) continue;
                if (p.OwnedOnly.Checked && !owned) continue;
                if (f != "" && Save.NameOf(id).IndexOf(f, StringComparison.OrdinalIgnoreCase) < 0) continue;
                yield return id;
            }
        }

        void PopulateInventory()
        {
            if (save == null || invTabs.SelectedIndex < 0) return;
            PopulateInventoryPage(invPages[invTabs.SelectedIndex]);
        }

        void PopulateInventoryPage(InvPage p)
        {
            if (save == null) return;
            var g = p.Grid;
            int first = g.FirstDisplayedScrollingRowIndex;
            g.SuspendLayout();
            g.Rows.Clear();
            foreach (int id in ShownIds(p))
            {
                int i = g.Rows.Add(Save.NameOf(id), Save.IsEquipment(id) ? Tables.EquipmentType[id - 0x1000] : "", 0, "", id.ToString("X4"));
                g.Rows[i].Tag = id;
                FillRow(g.Rows[i], id);
            }
            g.ResumeLayout();
            if (first >= 0 && first < g.Rows.Count) g.FirstDisplayedScrollingRowIndex = first;
        }

        void Bulk(InvPage p, bool everything)
        {
            var ids = ShownIds(p).Where(id => everything || save.Quantity(id) > 0).ToList();
            if (everything && ids.Count > 20 &&
                MessageBox.Show(this, "Set " + ids.Count + " entries to 99?", Text, MessageBoxButtons.OKCancel) != DialogResult.OK) return;
            try
            {
                foreach (int id in ids) save.SetQuantity(id, Save.MaxQuantity);
            }
            catch (InvalidOperationException e)
            {
                MessageBox.Show(this, e.Message, Text);
            }
            PopulateInventoryPage(p);
            Changed();
        }

        // ------------------------------------------------------------------ Magicks, technicks, gambits, key items

        class OwnPage
        {
            public InvList List;
            public CheckedListBox Box;
            public TextBox Filter;
            public Label Count;
        }

        readonly List<OwnPage> ownPages = new List<OwnPage>();
        TabControl ownTabs;

        TabPage BuildAbilitiesTab()
        {
            var page = new TabPage("Magicks, gambits & key items") { Padding = new Padding(S(6)) };
            ownTabs = new TabControl { Dock = DockStyle.Fill };
            ownTabs.TabPages.Add(BuildOwnPage(Save.Magicks, "Owning a magick makes it available to everyone with its license."));
            ownTabs.TabPages.Add(BuildOwnPage(Save.Technicks, "Owning a technick makes it available to everyone with its license."));
            ownTabs.TabPages.Add(BuildOwnPage(Save.Gambits, "Gambits the party has bought or found."));
            ownTabs.TabPages.Add(BuildOwnPage(Save.KeyItems,
                "Key items drive story and quest progress. Adding one early or removing one you still need can break a quest or a story event, so change these with care."));
            ownTabs.SelectedIndexChanged += delegate { PopulateAbilities(); };
            page.Controls.Add(ownTabs);
            return page;
        }

        TabPage BuildOwnPage(InvList list, string help)
        {
            var p = new OwnPage { List = list };
            var page = new TabPage(list.Name) { Padding = new Padding(S(6)) };
            var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false };
            top.Controls.Add(Caption("Filter"));
            p.Filter = new TextBox { Width = S(180) };
            p.Filter.TextChanged += delegate { PopulateOwnPage(p); };
            top.Controls.Add(p.Filter);
            top.Controls.Add(MakeButton("Check shown", delegate { SetShownOwned(p, true); }));
            top.Controls.Add(MakeButton("Uncheck shown", delegate { SetShownOwned(p, false); }));
            p.Count = new Label { AutoSize = true, Margin = new Padding(S(10), S(7), 0, 0) };
            top.Controls.Add(p.Count);
            p.Box = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false, MultiColumn = true, ColumnWidth = S(250) };
            p.Box.ItemCheck += (s, e) =>
            {
                if (loading) return;
                try
                {
                    save.SetOwned(((Entry)p.Box.Items[e.Index]).Id, e.NewValue == CheckState.Checked);
                }
                catch (InvalidOperationException ex)
                {
                    e.NewValue = e.CurrentValue;
                    MessageBox.Show(this, ex.Message, Text);
                }
                BeginInvoke(new Action(() => { UpdateOwnCount(p); Changed(); }));
            };
            var note = Note(help, 700);
            note.Dock = DockStyle.Bottom;
            page.Controls.Add(p.Box);
            page.Controls.Add(note);
            page.Controls.Add(top);
            ownPages.Add(p);
            return page;
        }

        void PopulateAbilities()
        {
            if (save == null || ownTabs.SelectedIndex < 0) return;
            PopulateOwnPage(ownPages[ownTabs.SelectedIndex]);
        }

        void PopulateOwnPage(OwnPage p)
        {
            if (save == null) return;
            bool was = loading;
            loading = true;
            p.Box.BeginUpdate();
            p.Box.Items.Clear();
            string f = p.Filter.Text.Trim();
            for (int id = p.List.FirstId; id <= p.List.LastId; id++)
            {
                string name = p.List.NameOf(id);
                bool owned = save.Owned(id);
                if (name == "" && !owned) continue;
                if (name == "") name = Save.NameOf(id);
                else if (Array.IndexOf(p.List.Names, name) != Array.LastIndexOf(p.List.Names, name)) name += "  (" + id.ToString("X4") + ")";
                if (f != "" && name.IndexOf(f, StringComparison.OrdinalIgnoreCase) < 0) continue;
                p.Box.Items.Add(new Entry(id, name), owned);
            }
            p.Box.EndUpdate();
            loading = was;
            UpdateOwnCount(p);
        }

        void UpdateOwnCount(OwnPage p)
        {
            p.Count.Text = save.ListIds(p.List).Count + " owned";
        }

        void SetShownOwned(OwnPage p, bool own)
        {
            if (own && p.List == Save.KeyItems &&
                MessageBox.Show(this, "Give every shown key item? This can break quests and story events.", Text,
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
            try
            {
                foreach (Entry e in p.Box.Items) save.SetOwned(e.Id, own);
            }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show(this, ex.Message, Text);
            }
            PopulateOwnPage(p);
            Changed();
        }
    }
}
