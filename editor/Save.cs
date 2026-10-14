using System;
using System.Collections.Generic;
using System.IO;

namespace FFXIISaveEditor
{
    // One ordered inventory list: u16 IDs ending in 0xFFFF plus a u32 count.
    // Magicks, technicks, gambits and key items also have an ownership bitfield (bit = id - FirstId).
    class InvList
    {
        public readonly string Name;
        public readonly int ListOffset, Capacity, CountOffset, FirstId, LastId, BitsOffset;
        public readonly string[] Names;

        public InvList(string name, int list, int capacity, int count, int first, int last, int bits, string[] names)
        {
            Name = name; ListOffset = list; Capacity = capacity; CountOffset = count;
            FirstId = first; LastId = last; BitsOffset = bits; Names = names;
        }

        public bool Contains(int id) { return id >= FirstId && id <= LastId; }
        public bool HasQuantity { get { return BitsOffset < 0; } }

        // Names for item IDs, indexed by id - (base of the name table).
        public string NameOf(int id)
        {
            int baseId = this == Save.Weapons || this == Save.Armor || this == Save.Accessories || this == Save.Ammo ? 0x1000 : FirstId;
            int i = id - baseId;
            return i >= 0 && i < Names.Length ? Names[i] : "";
        }
    }

    class Save
    {
        // Offsets are into the decoded save. 0x0-0xE200 is the PS2-style save block (CRC protected).
        // The game also keeps a second copy of the characters, quantities and ownership bits
        // Mirror bytes further on; both copies are written.
        public const int CrcStart = 0x8, CrcEnd = 0xE200;
        public const int Mirror = 0x4C48C;
        public const int Gil = 0x8;
        public const int PartySummary = 0x20;     // load screen: 5 x (u8 character, u8 level, u16)
        public const int Settings = 0x8208;
        public const int ClanPoints = 0xBA68;
        public const int CharBase = 0x2210, CharSize = 0x1C8, CharCount = 17;
        public const int ItemQty = 0x6948, EquipQty = 0x69C8, LootQty = 0x6D10;
        public const int EquipInInventory = 420;  // 0x1000-0x11A3 can be owned
        public const int MaxQuantity = 99;
        static readonly int[,] MirroredRanges = { { CharBase, CharBase + CharCount * CharSize }, { ItemQty, 0x7180 } };

        public static readonly InvList Items = new InvList("Items", 0xBACC, 64, 0xC9B8, 0x0000, 0x003F, -1, Tables.Items);
        public static readonly InvList Weapons = new InvList("Weapons", 0xBB4C, 200, 0xC9BC, 0x1000, 0x10C7, -1, Tables.Equipment);
        public static readonly InvList Armor = new InvList("Armor", 0xBCDC, 140, 0xC9C0, 0x10C8, 0x1153, -1, Tables.Equipment);
        public static readonly InvList Accessories = new InvList("Accessories", 0xBDF4, 48, 0xC9C4, 0x1154, 0x1183, -1, Tables.Equipment);
        public static readonly InvList Ammo = new InvList("Ammo", 0xBE54, 32, 0xC9C8, 0x1184, 0x11A3, -1, Tables.Equipment);
        public static readonly InvList Gambits = new InvList("Gambits", 0xBEE4, 256, 0xC9D0, 0x6000, 0x60FF, 0x7120, Tables.Gambits);
        public static readonly InvList Technicks = new InvList("Technicks", 0xC0E4, 24, 0xC9D8, 0x4000, 0x4017, 0x711C, Tables.Technicks);
        public static readonly InvList Magicks = new InvList("Magicks", 0xC114, 81, 0xC9DC, 0x3000, 0x3050, 0x7110, Tables.Magicks);
        public static readonly InvList KeyItems = new InvList("Key items", 0xC1B6, 512, 0xC9E0, 0x8000, 0x81FF, 0x7140, Tables.KeyItems);
        public static readonly InvList Loot = new InvList("Loot", 0xC5B6, 512, 0xC9E4, 0x2000, 0x21FF, -1, Tables.Loot);
        public static readonly InvList[] AllLists = { Items, Weapons, Armor, Accessories, Ammo, Loot, Magicks, Technicks, Gambits, KeyItems };

        public readonly string FilePath;
        public readonly byte[] Data;
        readonly byte[] original;
        readonly int keyRotation;
        public readonly Character[] Characters = new Character[CharCount];

        public Save(string path)
        {
            FilePath = path;
            Data = Codec.Decode(File.ReadAllBytes(path), out keyRotation);
            if (Data.Length < Mirror + 0xA000)
                throw new InvalidDataException("Unexpected save size " + Data.Length + ".");
            if (Codec.Crc32(Data, CrcStart, CrcEnd) != U32(0))
                throw new InvalidDataException("Save checksum is wrong; the file may be damaged.");
            original = (byte[])Data.Clone();
            for (int i = 0; i < CharCount; i++) Characters[i] = new Character(this, i);
        }

        public int Slot { get { return Data[0x54]; } }
        public long SaveCount { get { return U32(0x50); } }

        public bool Dirty
        {
            get
            {
                for (int i = 0; i < Data.Length; i++) if (Data[i] != original[i]) return true;
                return false;
            }
        }

        public int ChangedBytes()
        {
            int n = 0;
            for (int i = 0; i < Data.Length; i++) if (Data[i] != original[i]) n++;
            return n;
        }

        // ---- primitive access ----
        public int U8(int o) { return Data[o]; }
        public void SetU8(int o, int v) { Data[o] = (byte)v; }
        public int U16(int o) { return Data[o] | Data[o + 1] << 8; }
        public void SetU16(int o, int v) { Data[o] = (byte)v; Data[o + 1] = (byte)(v >> 8); }
        public long U32(int o) { return (uint)BitConverter.ToInt32(Data, o); }
        public void SetU32(int o, long v) { BitConverter.GetBytes((uint)v).CopyTo(Data, o); }
        public float F32(int o) { return BitConverter.ToSingle(Data, o); }
        public void SetF32(int o, float v) { BitConverter.GetBytes(v).CopyTo(Data, o); }
        public bool Bit(int o, int bit) { return (Data[o + bit / 8] >> (bit % 8) & 1) != 0; }
        public void SetBit(int o, int bit, bool on)
        {
            if (on) Data[o + bit / 8] |= (byte)(1 << bit % 8);
            else Data[o + bit / 8] &= (byte)~(1 << bit % 8);
        }

        // ---- general ----
        public long GilAmount { get { return U32(Gil); } set { SetU32(Gil, Math.Max(0, Math.Min(99999999, value))); } }

        public long Clan
        {
            get { return U32(ClanPoints); }
            set
            {
                value = Math.Max(0, Math.Min(99999999, value));
                // The header keeps a copy for the load screen (0x40); update it only if it matched.
                if (U32(0x40) == U32(ClanPoints)) SetU32(0x40, value);
                SetU32(ClanPoints, value);
            }
        }

        public bool Subtitles { get { return !Bit(Settings, 12); } set { SetBit(Settings, 12, !value); } }
        public bool ActiveBattle { get { return Bit(Settings, 3); } set { SetBit(Settings, 3, value); } }
        public bool JapaneseVoices { get { return Bit(Settings + 4, 10); } set { SetBit(Settings + 4, 10, value); } }
        public int MusicVolume { get { return U16(Settings + 8); } set { SetU16(Settings + 8, Clamp(value, 0, 100)); } }
        public int SfxVolume { get { return U16(Settings + 0xA); } set { SetU16(Settings + 0xA, Clamp(value, 0, 100)); } }
        public int VoiceVolume { get { return U16(Settings + 0xC); } set { SetU16(Settings + 0xC, Clamp(value, 0, 100)); } }
        public int MusicTrack { get { return U16(Settings + 0xE); } set { SetU16(Settings + 0xE, Clamp(value, 0, 2)); } }

        public static int Clamp(int v, int lo, int hi) { return v < lo ? lo : v > hi ? hi : v; }

        // ---- inventory ----
        public static InvList ListFor(int id)
        {
            foreach (var l in AllLists) if (l.Contains(id)) return l;
            return null;
        }

        public static string NameOf(int id)
        {
            var l = ListFor(id);
            string n = l != null ? l.NameOf(id)
                : id >= 0x1000 && id < 0x1000 + Tables.Equipment.Length ? Tables.Equipment[id - 0x1000] : "";
            return n != "" ? n : "#" + id.ToString("X4");
        }

        public static bool IsEquipment(int id) { return id >= 0x1000 && id < 0x1000 + EquipInInventory; }

        static int QuantityOffset(int id)
        {
            if (id >= 0 && id < 64) return ItemQty + 2 * id;
            if (IsEquipment(id)) return EquipQty + 2 * (id - 0x1000);
            if (id >= 0x2000 && id < 0x2200) return LootQty + 2 * (id - 0x2000);
            return -1;
        }

        public List<int> ListIds(InvList l)
        {
            var ids = new List<int>();
            for (int i = 0; i < l.Capacity; i++)
            {
                int v = U16(l.ListOffset + 2 * i);
                if (v == 0xFFFF) break;
                ids.Add(v);
            }
            return ids;
        }

        // Quantity owned, including copies worn by party members.
        public int Quantity(int id)
        {
            int o = QuantityOffset(id);
            if (o < 0) return Owned(id) ? 1 : 0;
            int v = U16(o);
            return IsEquipment(id) ? v & 0x3FF : v;
        }

        // How many copies party members are wearing (upper 6 bits of the equipment quantity).
        public int Equipped(int id) { return IsEquipment(id) ? U16(QuantityOffset(id)) >> 10 : 0; }

        public void SetQuantity(int id, int q)
        {
            var l = ListFor(id);
            int o = QuantityOffset(id);
            if (l == null || o < 0) throw new ArgumentException("No quantity for " + NameOf(id));
            q = Clamp(q, 0, MaxQuantity);
            if (IsEquipment(id))
            {
                int worn = Equipped(id);
                q = Math.Max(q, worn);
                SetU16(o, worn << 10 | q);
            }
            else SetU16(o, q);
            SetListed(l, id, q > 0);
        }

        public bool Owned(int id)
        {
            var l = ListFor(id);
            if (l == null) return false;
            if (l.HasQuantity) return Quantity(id) > 0;
            return Bit(l.BitsOffset, id - l.FirstId);
        }

        public void SetOwned(int id, bool own)
        {
            var l = ListFor(id);
            if (l == null) throw new ArgumentException("Unknown ID " + id);
            if (l.HasQuantity)
            {
                if (own != (Quantity(id) > 0)) SetQuantity(id, own ? 1 : 0);
                return;
            }
            SetBit(l.BitsOffset, id - l.FirstId, own);
            SetListed(l, id, own);
        }

        void SetListed(InvList l, int id, bool present)
        {
            var ids = ListIds(l);
            int at = ids.IndexOf(id);
            if (present && at < 0)
            {
                if (ids.Count >= l.Capacity) throw new InvalidOperationException(l.Name + " list is full.");
                SetU16(l.ListOffset + 2 * ids.Count, id);   // new entries go to the end, like the game
                SetU32(l.CountOffset, ids.Count + 1);
            }
            else if (!present && at >= 0)
            {
                ids.RemoveAt(at);
                for (int i = at; i < ids.Count; i++) SetU16(l.ListOffset + 2 * i, ids[i]);
                SetU16(l.ListOffset + 2 * ids.Count, 0xFFFF);
                SetU32(l.CountOffset, ids.Count);
            }
        }

        // Called when a party member's equipment changes so worn counts and ownership stay consistent.
        internal void Wear(int oldId, int newId)
        {
            if (oldId == newId) return;
            if (IsEquipment(oldId))
            {
                int o = QuantityOffset(oldId), v = U16(o), worn = v >> 10;
                if (worn > 0) SetU16(o, (worn - 1) << 10 | (v & 0x3FF));
            }
            if (IsEquipment(newId))
            {
                int o = QuantityOffset(newId), v = U16(o), worn = (v >> 10) + 1, q = Math.Max(v & 0x3FF, worn);
                SetU16(o, worn << 10 | q);
                SetListed(ListFor(newId), newId, true);
            }
        }

        // Lists problems with the save's own bookkeeping (not caused by the editor).
        public List<string> CheckConsistency()
        {
            var problems = new List<string>();
            foreach (var l in AllLists)
            {
                var ids = ListIds(l);
                if (U32(l.CountOffset) != ids.Count)
                    problems.Add(string.Format("{0}: count says {1} but the list has {2} entries.", l.Name, U32(l.CountOffset), ids.Count));
                foreach (int id in ids)
                    if (!l.Contains(id)) problems.Add(string.Format("{0}: unexpected ID {1:X4} in list.", l.Name, id));
                    else if (!Owned(id)) problems.Add(string.Format("{0}: {1} is listed but not owned.", l.Name, NameOf(id)));
                for (int id = l.FirstId; id <= l.LastId; id++)
                    if ((l.HasQuantity ? QuantityOffset(id) >= 0 : true) && Owned(id) && !ids.Contains(id))
                        problems.Add(string.Format("{0}: {1} is owned but not listed.", l.Name, NameOf(id)));
            }
            var worn = new int[EquipInInventory];
            foreach (var c in Characters)
                if (c.InParty)
                    for (int s = 0; s < 5; s++)
                        if (IsEquipment(c.Equipment(s))) worn[c.Equipment(s) - 0x1000]++;
            for (int i = 0; i < EquipInInventory; i++)
                if (worn[i] != Equipped(0x1000 + i))
                    problems.Add(string.Format("{0}: worn count {1}, party wears {2}.", NameOf(0x1000 + i), Equipped(0x1000 + i), worn[i]));
            return problems;
        }

        // ---- writing ----
        // Returns the encoded file without touching disk, after mirroring, fixing checksums and re-decoding to verify.
        public byte[] Build()
        {
            for (int r = 0; r < MirroredRanges.GetLength(0); r++)
                for (int i = MirroredRanges[r, 0]; i < MirroredRanges[r, 1]; i++)
                    if (Data[i] != original[i]) Data[i + Mirror] = Data[i];

            foreach (var c in Characters)
                for (int e = 0; e < 5; e++)
                {
                    int o = PartySummary + 4 * e;
                    if (Data[o] == c.Index) Data[o + 1] = (byte)c.Level;
                }

            uint crc = Codec.Crc32(Data, CrcStart, CrcEnd);
            SetU32(0, crc);
            SetU32(CrcEnd, crc);

            byte[] blob = Codec.Encode(Data, keyRotation);
            int r2;
            byte[] check = Codec.Decode(blob, out r2);
            if (check.Length != Data.Length) throw new InvalidDataException("Verification failed (size).");
            for (int i = 0; i < check.Length; i++)
                if (check[i] != Data[i]) throw new InvalidDataException("Verification failed at offset " + i + ".");
            return blob;
        }

        public void MarkSaved() { Array.Copy(Data, original, Data.Length); }
    }

    class Character
    {
        public static readonly string[] Names = {
            "Vaan", "Ashe", "Fran", "Balthier", "Basch", "Penelo", "Reks", "Amalia", "Basch (shirtless)",
            "Basch (dark suit)", "Lamont", "Vossler (Imperial)", "Vossler (Resistance)", "Larsa", "Reddas",
            "Basch (Resistance)", "Reddas (duplicate)" };
        public static readonly string[] Jobs = {
            "White Mage", "Uhlan", "Machinist", "Red Battlemage", "Knight", "Monk", "Time Battlemage",
            "Foebreaker", "Archer", "Black Mage", "Bushi", "Shikari" };
        public static readonly string[] Slots = { "Weapon", "Shield / ammo", "Head", "Body", "Accessory" };
        public const int GambitSlots = 12, LicenseCount = 361;

        readonly Save s;
        public readonly int Index;
        readonly int b;

        public Character(Save save, int index) { s = save; Index = index; b = Save.CharBase + index * Save.CharSize; }

        public string Name { get { return Names[Index]; } }
        public bool InParty { get { return s.U8(b + 0x7D) == 1; } }

        public int Level { get { return s.U8(b + 0x1BA); } set { s.SetU8(b + 0x1BA, Save.Clamp(value, 1, 99)); } }
        public long Exp { get { return s.U32(b + 0x184); } set { s.SetU32(b + 0x184, Math.Max(0, Math.Min(99999999, value))); } }
        public long Lp { get { return s.U32(b + 0x188); } set { s.SetU32(b + 0x188, Math.Max(0, Math.Min(99999, value))); } }
        public long MaxHp { get { return s.U32(b + 0x1C); } set { s.SetU32(b + 0x1C, Math.Max(1, Math.Min(99999, value))); } }
        public long Hp { get { return s.U32(b + 0x40); } set { s.SetU32(b + 0x40, Math.Max(0, Math.Min(99999, value))); } }
        public int MaxMp { get { return s.U16(b + 0x20); } set { s.SetU16(b + 0x20, Save.Clamp(value, 0, 9999)); } }
        public int Mp { get { return s.U16(b + 0x44); } set { s.SetU16(b + 0x44, Save.Clamp(value, 0, 9999)); } }

        // Base Strength, Magick Power, Vitality, Speed (floats); the game adds equipment and licenses on top.
        public float BaseStat(int i) { return s.F32(b + 8 + 4 * i); }
        public void SetBaseStat(int i, float v) { s.SetF32(b + 8 + 4 * i, Math.Max(0, Math.Min(99, v))); }
        // Totals as last computed by the game: Str, Mag, Vit, Spd, Attack, Defense, Magick Resist, Evade.
        public int ShownStat(int i) { return s.U8(b + new[] { 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x28, 0x2D }[i]); }

        public int Job1 { get { return s.U8(b + 0x1BB); } set { SetJob(0x1BB, value); } }
        public int Job2 { get { return s.U8(b + 0x1BC); } set { SetJob(0x1BC, value); } }

        void SetJob(int off, int job)
        {
            if (off == 0x1BB && s.U8(b + 0x1BD) == s.U8(b + 0x1BB)) s.SetU8(b + 0x1BD, job);  // +0x1BD tracks the first job
            s.SetU8(b + off, job);
        }

        public int Equipment(int slot) { return s.U16(b + 0x48 + 2 * slot); }

        public void SetEquipment(int slot, int id)
        {
            int old = Equipment(slot);
            if (old == id) return;
            if (InParty) s.Wear(old, id);
            s.SetU16(b + 0x48 + 2 * slot, id);
        }

        public int GambitTarget(int i) { return s.U16(b + 0x80 + 4 * i); }
        public int GambitAction(int i) { return s.U16(b + 0x82 + 4 * i); }
        public bool GambitOn(int i) { return s.Bit(b + 0xB0, i); }

        public void SetGambit(int i, int target, int action, bool on)
        {
            if (target == 0xFFFF || action == 0xFFFF) { target = action = 0xFFFF; on = false; }
            s.SetU16(b + 0x80 + 4 * i, target);
            s.SetU16(b + 0x82 + 4 * i, action);
            s.SetBit(b + 0xB0, i, on);
            // The game only lets you pick gambits, magicks and technicks you own.
            if (target != 0xFFFF && Save.Gambits.Contains(target)) s.SetOwned(target, true);
            if (action >= 0 && action <= 80) s.SetOwned(0x3000 + action, true);
            if (action >= 158 && action <= 181) s.SetOwned(0x4000 + action - 158, true);
        }

        public bool License(int id) { return s.Bit(b + 0x18C, id); }
        public void SetLicense(int id, bool on) { s.SetBit(b + 0x18C, id, on); }
    }
}
