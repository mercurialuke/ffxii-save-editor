using System;
using System.Collections.Generic;
using System.IO;

namespace FFXIISaveEditor
{
    // Console checks for the save model. Usage: SelfTest.exe <save copy> <output dir>
    // Never point it at the live save folder: it writes edited copies to <output dir>.
    static class SelfTest
    {
        static int failures;

        static void Check(bool ok, string what)
        {
            Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what);
            if (!ok) failures++;
        }

        static int Main(string[] args)
        {
            if (args.Length != 2) { Console.WriteLine("usage: SelfTest <save copy> <output dir>"); return 2; }
            string src = args[0], outDir = args[1];
            Directory.CreateDirectory(outDir);

            var save = new Save(src);
            Console.WriteLine("{0}: slot {1}, gil {2}, {3}", Path.GetFileName(src), save.Slot, save.GilAmount, save.Characters[0].Name + " Lv" + save.Characters[0].Level);
            var problems = save.CheckConsistency();
            foreach (var p in problems) Console.WriteLine("  note: " + p);
            Check(problems.Count == 0, "original save is self-consistent");

            // Round trip without edits.
            byte[] before = (byte[])save.Data.Clone();
            byte[] blob = save.Build();
            string same = Path.Combine(outDir, "unchanged");
            File.WriteAllBytes(same, blob);
            var again = new Save(same);
            Check(Equal(again.Data, before), "unchanged round trip gives identical data");

            // Edits.
            var vaan = save.Characters[0];
            int oldWeapon = vaan.Equipment(0);
            int potionQty = save.Quantity(0);
            int listItems = save.ListIds(Save.Items).Count;
            save.GilAmount = 123456;
            save.SetQuantity(0, 99);                      // Potion
            save.SetQuantity(0x3E, 5);                     // new item at the end of the list
            save.SetQuantity(0x1001, 2);                   // Broadsword
            save.SetQuantity(0x2010, 7);                   // some loot
            save.SetOwned(0x3010, true);                   // a magick
            save.SetOwned(0x4001, true);                   // a technick
            save.SetOwned(0x6050, true);                   // a gambit
            save.SetOwned(0x8002, true);                   // a key item
            vaan.SetEquipment(0, 0x1002);                  // Longsword, not owned yet
            vaan.Level = 50;
            vaan.Lp = 5000;
            vaan.SetBaseStat(0, 40.5f);
            vaan.SetLicense(100, true);
            vaan.SetGambit(2, 0x6001, 18, true);           // Foe: nearest visible -> Fire
            save.Subtitles = false;
            save.MusicVolume = 70;

            blob = save.Build();
            string edited = Path.Combine(outDir, "edited");
            File.WriteAllBytes(edited, blob);
            var e = new Save(edited);
            var ev = e.Characters[0];
            Check(e.GilAmount == 123456, "gil");
            Check(e.Quantity(0) == 99 && potionQty > 0, "potion quantity");
            Check(e.Quantity(0x3E) == 5 && e.ListIds(Save.Items)[listItems] == 0x3E, "new item appended to list");
            Check(e.U32(Save.Items.CountOffset) == listItems + 1, "item count incremented");
            Check(e.Quantity(0x1001) == 2 && e.ListIds(Save.Weapons).Contains(0x1001), "weapon added");
            Check(e.Quantity(0x2010) == 7 && e.ListIds(Save.Loot).Contains(0x2010), "loot added");
            Check(e.Owned(0x3010) && e.ListIds(Save.Magicks).Contains(0x3010), "magick owned and listed");
            Check(e.Owned(0x4001) && e.Owned(0x6050) && e.Owned(0x8002), "technick, gambit, key item owned");
            Check(ev.Equipment(0) == 0x1002 && e.Quantity(0x1002) == 1 && e.Equipped(0x1002) == 1, "new weapon worn and owned");
            Check(e.Equipped(oldWeapon) == 0 && e.Quantity(oldWeapon) >= 1, "old weapon unequipped but kept");
            Check(ev.Level == 50 && e.U8(Save.PartySummary + 1) == 50, "level and load-screen level");
            Check(ev.Lp == 5000 && Math.Abs(ev.BaseStat(0) - 40.5f) < 0.001, "LP and base strength");
            Check(ev.License(100), "license bit");
            Check(ev.GambitTarget(2) == 0x6001 && ev.GambitAction(2) == 18 && ev.GambitOn(2), "gambit slot");
            Check(e.Owned(0x3000 + 18), "gambit action magick owned");
            Check(!e.Subtitles && e.MusicVolume == 70, "settings");
            Check(Equal(e.Data, Save.CharBase, e.Data, Save.CharBase + Save.Mirror, Save.CharCount * Save.CharSize), "character mirror matches");
            Check(Equal(e.Data, Save.ItemQty, e.Data, Save.ItemQty + Save.Mirror, 0x7180 - Save.ItemQty), "inventory mirror matches");
            problems = e.CheckConsistency();
            foreach (var p in problems) Console.WriteLine("  note: " + p);
            Check(problems.Count == 0, "edited save is self-consistent");

            // Removing things again.
            e.SetQuantity(0x3E, 0);
            e.SetOwned(0x3010, false);
            e.Characters[0].SetEquipment(0, oldWeapon);
            e.SetQuantity(0x1002, 0);
            blob = e.Build();
            string removed = Path.Combine(outDir, "removed");
            File.WriteAllBytes(removed, blob);
            var r = new Save(removed);
            Check(!r.ListIds(Save.Items).Contains(0x3E) && r.U32(Save.Items.CountOffset) == listItems, "item removed from list");
            Check(!r.Owned(0x3010) && !r.ListIds(Save.Magicks).Contains(0x3010), "magick removed");
            Check(r.Characters[0].Equipment(0) == oldWeapon && r.Equipped(oldWeapon) == 1, "old weapon re-equipped");
            Check(r.Quantity(0x1002) == 0 && !r.ListIds(Save.Weapons).Contains(0x1002), "longsword gone");
            problems = r.CheckConsistency();
            foreach (var p in problems) Console.WriteLine("  note: " + p);
            Check(problems.Count == 0, "save after removals is self-consistent");

            // Only expected regions differ from the original.
            var regions = new List<string>();
            for (int i = 0; i < before.Length; i++)
                if (before[i] != e.Data[i])
                {
                    int a = i;
                    while (i + 1 < before.Length && before[i + 1] != e.Data[i + 1]) i++;
                    regions.Add(a.ToString("X") + "-" + i.ToString("X"));
                }
            Console.WriteLine("  changed ranges: " + string.Join(" ", regions));

            Console.WriteLine(failures == 0 ? "ALL PASSED" : failures + " FAILED");
            return failures == 0 ? 0 : 1;
        }

        static bool Equal(byte[] a, byte[] b) { return a.Length == b.Length && Equal(a, 0, b, 0, a.Length); }

        static bool Equal(byte[] a, int ao, byte[] b, int bo, int n)
        {
            for (int i = 0; i < n; i++) if (a[ao + i] != b[bo + i]) return false;
            return true;
        }
    }
}
