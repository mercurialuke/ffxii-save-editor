using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace FFXIISaveEditor
{
    // Drives the real MainForm in-process: changes controls (firing their normal event handlers), then saves.
    // Usage: UiTest.exe <save copy> <backup dir>. Only ever point it at a copy.
    static class UiTest
    {
        static MainForm form;
        static int failures;

        static T Get<T>(string field) where T : class
        {
            var f = typeof(MainForm).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            return (T)f.GetValue(f.IsStatic ? null : form);
        }

        static void Check(bool ok, string what)
        {
            Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what);
            if (!ok) failures++;
        }

        [STAThread]
        static int Main(string[] args)
        {
            string path = Path.GetFullPath(args[0]);
            typeof(MainForm).GetField("BackupRoot", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, Path.GetFullPath(args[1]));
            typeof(MainForm).GetField("GameProcess", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, "no-such-process");
            Application.EnableVisualStyles();
            form = new MainForm(path);
            form.Shown += delegate
            {
                try { Run(path); }
                catch (Exception e) { Console.WriteLine("EXCEPTION " + e); failures++; }
                typeof(MainForm).GetField("save", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(form, null);
                form.Close();
            };
            Application.Run(form);
            Console.WriteLine(failures == 0 ? "UI TEST PASSED" : failures + " FAILED");
            return failures == 0 ? 0 : 1;
        }

        static void Run(string path)
        {
            var tabs = Get<TabControl>("tabs");
            Get<NumericUpDown>("gilBox").Value = 50000;
            Get<NumericUpDown>("musicBox").Value = 35;
            Get<CheckBox>("subtitlesBox").Checked = false;

            tabs.SelectedIndex = 1;
            var chars = Get<ListBox>("charList");
            chars.SelectedIndex = 2;                                    // Fran
            Application.DoEvents();
            Get<NumericUpDown>("levelBox").Value = 20;
            Get<NumericUpDown>("lpBox").Value = 999;
            Check(chars.Items[2].ToString().Contains("Lv 20"), "list label follows level");
            chars.SelectedIndex = 0;                                    // Vaan
            Application.DoEvents();
            var equip = Get<ComboBox[]>("equipBoxes");
            equip[4].SelectedItem = equip[4].Items.Cast<Entry>().First(e => e.Text.StartsWith("Bangle"));
            var targets = Get<ComboBox[]>("gambitTarget");
            var actions = Get<ComboBox[]>("gambitAction");
            var on = Get<CheckBox[]>("gambitOn");
            targets[2].SelectedItem = targets[2].Items.Cast<Entry>().First(e => e.Id == 0x6001);
            actions[2].SelectedItem = actions[2].Items.Cast<Entry>().First(e => e.Text == "Fire");
            on[2].Checked = true;
            Get<TextBox>("licenseFilter").Text = "Swords 1";
            var lic = Get<CheckedListBox>("licenseList");
            int sw = lic.Items.Cast<Entry>().ToList().FindIndex(e => e.Text == "Swords 1");
            Check(sw >= 0 && lic.Items.Count <= 3, "license filter");
            lic.SetItemChecked(sw, true);
            Application.DoEvents();

            tabs.SelectedIndex = 2;
            var invTabs = Get<TabControl>("invTabs");
            invTabs.SelectedIndex = 0;
            Application.DoEvents();
            var pages = Get<System.Collections.IList>("invPages");
            var grid = (DataGridView)pages[0].GetType().GetField("Grid").GetValue(pages[0]);
            var row = grid.Rows.Cast<DataGridViewRow>().First(r => (int)r.Tag == 2);   // X-Potion
            grid.CurrentCell = row.Cells["Qty"];
            grid.BeginEdit(true);
            grid.EditingControl.Text = "12";
            grid.EndEdit();
            Check(Convert.ToInt32(row.Cells["Qty"].Value) == 12, "grid edit committed");

            tabs.SelectedIndex = 3;
            var ownTabs = Get<TabControl>("ownTabs");
            ownTabs.SelectedIndex = 1;                                  // technicks
            Application.DoEvents();
            var own = Get<System.Collections.IList>("ownPages");
            var box = (CheckedListBox)own[1].GetType().GetField("Box").GetValue(own[1]);
            int stamp = box.Items.Cast<Entry>().ToList().FindIndex(e => e.Text == "Stamp");
            box.SetItemChecked(stamp, true);
            Application.DoEvents();

            var write = typeof(MainForm).GetMethod("WriteSave", BindingFlags.NonPublic | BindingFlags.Instance);
            Check((bool)write.Invoke(form, null), "WriteSave returned true");

            var s = new Save(path);
            Check(s.GilAmount == 50000 && s.MusicVolume == 35 && !s.Subtitles, "general edits on disk");
            Check(s.Characters[2].Level == 20 && s.Characters[2].Lp == 999, "Fran level and LP on disk");
            Check(s.Characters[0].Equipment(4) == Tables.Equipment.ToList().IndexOf("Bangle") + 0x1000, "Vaan accessory on disk");
            Check(s.Characters[0].GambitTarget(2) == 0x6001 && s.Characters[0].GambitAction(2) == 18 && s.Characters[0].GambitOn(2), "gambit on disk");
            Check(s.Characters[0].License(Tables.Licenses.ToList().IndexOf("Swords 1")), "license on disk");
            Check(s.Quantity(2) == 12, "X-Potion quantity on disk");
            Check(s.Owned(0x4003), "Stamp owned on disk");
            var problems = s.CheckConsistency();
            foreach (var p in problems) Console.WriteLine("  note: " + p);
            Check(problems.Count == 0, "saved file is self-consistent");
            Check(Directory.GetFiles(Path.GetFullPath(Get<string>("BackupRoot")), Path.GetFileName(path), SearchOption.AllDirectories).Length > 0, "backup written");

            // Save as another slot: the source stays as it is, the copy gets the new slot number.
            byte[] sourceBefore = File.ReadAllBytes(path);
            Get<NumericUpDown>("gilBox").Value = 777;
            string copy = Path.Combine(Path.GetDirectoryName(path), "FFXII_009");
            File.Delete(copy);
            typeof(MainForm).GetMethod("SaveTo", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(form, new object[] { copy });
            var c = new Save(copy);
            Check(c.Slot == 9 && c.GilAmount == 777 && c.Characters[2].Level == 20, "save as slot 10");
            Check(File.ReadAllBytes(path).SequenceEqual(sourceBefore), "source file untouched by save as");
            Check(c.CheckConsistency().Count == 0, "copy is self-consistent");
        }
    }
}
