using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using UntitledGame.Core;
using UntitledGame.Economy;
using UntitledGame.Home;
using UntitledGame.Language;
using UntitledGame.Progression;

namespace UntitledGame.UI
{
    /// <summary>Your bag: money, bucket, rods, bait, gear, things for the camp and Tangyuan's supplies.</summary>
    public class InventoryPanel : ModalPanel
    {
        private readonly RectTransform _content;

        private readonly System.Action _onChanged;

        public void Dispose() => Inventory.Changed -= _onChanged;

        public InventoryPanel(RectTransform canvas) : base(canvas, "Bag", new Vector2(1100, 900), "Bag")
        {
            ChineseTitle("包", 3);
            var area = UIFactory.Rect("Area", Window).Stretch(50, 40, 100, 36);
            _content = UIFactory.ScrollList(area, 8);
            _onChanged = () => { if (IsOpen) Refresh(); };
            Inventory.Changed += _onChanged;
        }

        protected override void Refresh()
        {
            foreach (Transform c in _content) Object.Destroy(c.gameObject);

            Header($"Money: ¥{Inventory.Money}");
            Line($"<b>Fishing knowledge: {PlayerStats.Knowledge} book{(PlayerStats.Knowledge == 1 ? "" : "s")}</b>  <color=#8A7563>({PlayerStats.DiscoveredFishCount}/{PlayerStats.TotalFishCount} kinds of fish discovered; buy books from Teacher Zhou)</color>");

            // Everything you carry takes a slot (fish stack 20 of a kind, other things 99).
            var slots = Inventory.Slots();
            Header($"Bag  {slots.Count}/{Inventory.SlotCapacity} slots" + (Inventory.BucketCount > 0 ? $"  ·  fish worth about ¥{Inventory.BucketValue}" : ""));
            for (int i = 0; i < Inventory.SlotCapacity || i < slots.Count; i++)
            {
                if (i < slots.Count)
                {
                    var s = slots[i];
                    string extra = s.speciesId != null
                        ? $"heaviest {Fishing.FishDatabase.WeightText(Inventory.Bucket.Where(b => b.species.id == s.speciesId).Max(b => b.length))}, ~¥{s.value}"
                        : $"worth ~¥{s.value}";
                    Line($"{i + 1}.  <b>{s.label}</b> x {s.count}  <color=#8A7563>({extra})</color>" + (i >= Inventory.SlotCapacity ? "  <color=#E0604E>(over capacity)</color>" : ""));
                }
                else Line($"{i + 1}.  <color=#8A7563>empty</color>");
            }
            Line("<color=#8A7563><i>If you pass out, you keep only your 3 most valuable slots. Old Wang sells bigger buckets for more slots.</i></color>");
            Line($"<b>Energy {Progression.Energy.Current:0}/{Progression.Energy.Max:0}</b>  <color=#8A7563>({Progression.Energy.Bed?.english}: {Progression.Energy.BedEnergy[Progression.Energy.BedLevel]}, " +
                 $"home comfort level {Progression.Energy.ComfortLevel}: +{Progression.Energy.ComfortLevel * Progression.Energy.EnergyPerComfortLevel}" +
                 (Progression.Energy.NextComfortThreshold > 0 ? $"; {Progression.Energy.ComfortPoints}/{Progression.Energy.NextComfortThreshold} comfort to the next level" : "") + ")</color>");

            Header("Stats  <size=20><color=#8A7563>(train with Coach Wu)</color></size>");
            foreach (var stat in PlayerStats.Stats)
                Line($"{PlayerStats.StatName(stat)}  <b>{PlayerStats.Level(stat)}/{PlayerStats.MaxLevel}</b>  <color=#8A7563>{PlayerStats.StatValue(stat)}</color>");

            var cosmetics = Inventory.Owned().Where(x => x.def.category == ItemCategory.Cosmetic).Select(x => x.def).ToList();
            if (cosmetics.Count > 0)
            {
                Header("Cosmetics");
                foreach (var def in cosmetics)
                {
                    bool on = Cosmetics.Chosen(def.cosmeticFor) == def.id;
                    Row($"{def.english}  <color=#8A7563>({def.cosmeticFor})</color>", on ? "Take off" : "Use",
                        () => Cosmetics.Choose(def.cosmeticFor, on ? null : def.id), null);
                }
            }

            Header("Line");
            Line($"<b>{Inventory.LineName}</b>  <color=#8A7563>(your strongest line is always on the rod; Old Wang sells stronger ones)</color>");

            Header("Bait");
            var baits = Inventory.Owned().Where(x => x.def.category == ItemCategory.Bait).ToList();
            var current = Inventory.Bait;
            Row("Plain hook", current == null ? null : "Use", () => Inventory.SelectBait(""), current == null ? "using" : null);
            foreach (var (def, count) in baits)
            {
                bool using_ = current != null && current.id == def.id;
                Row($"{def.english} × {count}  <color=#8A7563>{def.description}</color>", using_ ? null : "Use", () => Inventory.SelectBait(def.id), using_ ? "using" : null);
            }

            var books = Inventory.Owned().Where(x => x.def.category == ItemCategory.Book).ToList();
            if (books.Count > 0)
            {
                Header("Books");
                foreach (var (def, _) in books)
                {
                    bool read = PlayerStats.HasRead(def.id);
                    var fish = (def.teachesFish ?? new string[0]).Select(Fishing.FishDatabase.Get).Where(f => f != null).Select(f => f.name);
                    Row($"{def.english}  <color=#8A7563>{(read ? "about " + string.Join(", ", fish) : def.description)}</color>",
                        read ? null : "Read", () => ReadBook(def.id), read ? "read" : null);
                }
            }

            var giftItems = Inventory.Owned().Where(x => x.def.category == ItemCategory.Gift && x.count > 0).ToList();
            if (giftItems.Count > 0)
            {
                Header("Gifts");
                foreach (var (def, count) in giftItems) Line($"{def.english} x {count}  <color=#8A7563>{def.description}</color>");
                Line("<color=#8A7563><i>Give one by telling a shopkeeper, in Chinese, that it's for them (e.g. 这是送给你的…). One gift a day each.</i></color>");
            }
            if (Inventory.Owns("boat")) Line("<b>Old Wang's rowboat</b>  <color=#8A7563>Press F at the boat by the dock to row out. Big fish live far from shore.</color>");

            var gear = Inventory.Owned().Where(x => x.def.category == ItemCategory.Accessory).ToList();
            if (gear.Count > 0)
            {
                Header("Gear");
                foreach (var (def, _) in gear) Line($"{def.english}  <color=#8A7563>{def.description}</color>");
            }

            Header("For your camp");
            var placeable = Inventory.Owned().Where(x => x.def.placeable).ToList();
            var placed = SaveSystem.Data.placed.ToList();
            if (placeable.Count == 0 && placed.Count == 0) Line("<color=#8A7563><i>Buy furniture and cat things at the market, then place them here.</i></color>");
            foreach (var (def, count) in placeable)
            {
                Row($"{def.english} × {count}", "Place", () =>
                {
                    Close();
                    PlacementController.Instance?.Begin(def.id);
                });
            }
            foreach (var p in placed)
            {
                var def = Catalog.Get(p.id);
                if (def == null) continue;
                Row($"{def.english}  <color=#8A7563>(placed)</color>", "Move", () =>
                {
                    Close();
                    HomeItems.Instance?.StartMoving(p);
                }, button2: "Put away", onClick2: () =>
                {
                    HomeItems.Instance?.PutAway(p);
                    Refresh();
                });
            }

            Header("Tangyuan");
            var pet = SaveSystem.Data.pet;
            string hunger = pet.hunger > 0.75f ? "<color=#E0604E>very hungry</color>" : pet.hunger > 0.45f ? "a bit hungry" : "full";
            string mood = pet.happiness > 0.75f ? "very happy" : pet.happiness > 0.4f ? "content" : "<color=#E0604E>lonely</color>";
            Line($"Tummy: {hunger}  ·  Mood: {mood}{(pet.collar ? "  ·  wearing her bell collar" : "")}");
            Line($"Cat food × {Inventory.Count("cat_food")}   ·   Dried fish treats × {Inventory.Count("cat_treat")}");
            Line("<color=#8A7563><i>F near Tangyuan to pet her (or give a treat). F near her bowl to fill it.</i></color>");
        }

        private static void ReadBook(string id)
        {
            var found = PlayerStats.ReadBook(id);
            AudioManager.Instance?.PlaySfx("SFX/rpg_bookFlip1", 0.6f);
            GameEvents.Toast(found.Count == 0
                ? "You read it again. Nothing new, but it's a good read."
                : $"You discovered {found.Count} new fish: {string.Join(", ", found.Select(f => f.name))}. They can bite now!", 5f);
        }

        private void Header(string text)
        {
            var t = UIFactory.Text(_content, "Header", text, 30, UITheme.Ink, TextAlignmentOptions.BottomLeft, title: true);
            t.rectTransform.SetLayout(50);
        }

        private void Line(string text)
        {
            var t = UIFactory.Text(_content, "Line", text, 23, UITheme.Ink, TextAlignmentOptions.MidlineLeft);
            t.rectTransform.SetLayout(36);
        }

        private void Row(string text, string button, UnityAction onClick, string tag = null, string button2 = null, UnityAction onClick2 = null)
        {
            var row = UIFactory.Rect("Row", _content);
            row.SetLayout(48);
            var t = UIFactory.Text(row, "Text", text, 23, UITheme.Ink, TextAlignmentOptions.MidlineLeft);
            t.rectTransform.anchorMin = Vector2.zero;
            t.rectTransform.anchorMax = new Vector2(1, 1);
            t.rectTransform.offsetMin = new Vector2(20, 0);
            t.rectTransform.offsetMax = new Vector2(button2 != null ? -370 : -190, 0);
            if (button != null)
            {
                var b = UIFactory.Button(row, button, () => { onClick(); if (IsOpen) Refresh(); }, UITheme.TealDark, 20);
                b.GetComponent<RectTransform>().Anchor(new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(button2 != null ? -186 : -6, 0), new Vector2(170, 42));
            }
            if (button2 != null)
            {
                var b2 = UIFactory.Button(row, button2, () => { onClick2?.Invoke(); if (IsOpen) Refresh(); }, UITheme.InkSoft, 20);
                b2.GetComponent<RectTransform>().Anchor(new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-6, 0), new Vector2(170, 42));
            }
            else if (tag != null)
            {
                var l = UIFactory.Text(row, "Tag", tag, 20, UITheme.TealDark, TextAlignmentOptions.MidlineRight);
                l.rectTransform.Anchor(new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-10, 0), new Vector2(170, 42));
            }
        }
    }

    /// <summary>Every word Mei has taught, with pinyin, meaning and how often you've used it.</summary>
    public class NotebookPanel : ModalPanel
    {
        private readonly RectTransform _content;
        private readonly TextMeshProUGUI _count;

        private readonly System.Action<VocabEntry> _onLearned;

        public void Dispose() => VocabNotebook.WordLearned -= _onLearned;

        public NotebookPanel(RectTransform canvas) : base(canvas, "Notebook", new Vector2(1100, 900), "Words Mei taught you")
        {
            ChineseTitle("美教你的词语", 3);
            _count = UIFactory.Text(Window, "Count", "", 24, UITheme.InkSoft, TextAlignmentOptions.Center);
            _count.rectTransform.Anchor(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -84), new Vector2(900, 34));
            var area = UIFactory.Rect("Area", Window).Stretch(50, 40, 130, 36);
            _content = UIFactory.ScrollList(area, 6);
            _onLearned = _ => { if (IsOpen) Refresh(); };
            VocabNotebook.WordLearned += _onLearned;
        }

        protected override void Refresh()
        {
            foreach (Transform c in _content) Object.Destroy(c.gameObject);
            var entries = VocabNotebook.Entries;
            int used = entries.Count(e => e.said > 0);
            _count.text = entries.Count == 0 ? "" : $"{entries.Count} words · you've used {used} of them yourself";
            if (entries.Count == 0)
            {
                var t = UIFactory.Text(_content, "Empty", "Nothing yet! Ask Mei (hold <b>B</b>) how to say things. Every word she teaches lands here.", 26, UITheme.InkSoft, TextAlignmentOptions.Center);
                t.rectTransform.SetLayout(120);
                return;
            }
            foreach (var e in entries.Reverse())
            {
                var row = UIFactory.Panel(_content, "Word", UITheme.CreamDark.WithAlpha(0.6f), shadow: false, small: true);
                row.rectTransform.SetLayout(74);
                string py = string.IsNullOrEmpty(e.pinyin) ? Pinyin.Of(e.hanzi) : e.pinyin;
                var hz = UIFactory.Text(row.transform, "Hanzi", e.hanzi, 36, UITheme.Ink, TextAlignmentOptions.MidlineLeft);
                hz.rectTransform.Anchor(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(20, 0), new Vector2(260, 64));
                var mid = UIFactory.Text(row.transform, "Meaning", $"<color=#2C7F79><i>{py}</i></color>\n{e.meaning}", 22, UITheme.Ink, TextAlignmentOptions.MidlineLeft);
                mid.rectTransform.Anchor(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(290, 0), new Vector2(520, 70));
                string usedText = e.said > 0 ? $"<color=#2C7F79>used {e.said}×</color>" : "not used yet";
                var stats = UIFactory.Text(row.transform, "Stats", $"heard {e.heard}×\n{usedText}", 19, UITheme.InkSoft, TextAlignmentOptions.MidlineRight);
                stats.rectTransform.Anchor(new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-20, 0), new Vector2(200, 64));
            }
        }
    }
}
