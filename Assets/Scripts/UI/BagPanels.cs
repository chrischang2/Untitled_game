using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using UntitledGame.Core;
using UntitledGame.Economy;
using UntitledGame.Home;
using UntitledGame.Language;

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
            var area = UIFactory.Rect("Area", Window).Stretch(50, 40, 100, 36);
            _content = UIFactory.ScrollList(area, 8);
            _onChanged = () => { if (IsOpen) Refresh(); };
            Inventory.Changed += _onChanged;
        }

        protected override void Refresh()
        {
            foreach (Transform c in _content) Object.Destroy(c.gameObject);

            Header($"Money: ¥{Inventory.Money}");

            Header($"Fish bucket  {Inventory.BucketCount}/{Inventory.BucketCapacity}" + (Inventory.BucketCount > 0 ? $"  ·  worth about ¥{Inventory.BucketValue}" : ""));
            if (Inventory.BucketCount == 0) Line("Empty. Go catch something!");
            foreach (var g in Inventory.Bucket.GroupBy(b => b.species.id))
            {
                var s = g.First().species;
                Line($"{g.Count()} × {s.name}  <color=#8A7563>(best {g.Max(x => x.length):0} cm, ~¥{g.Sum(x => Catalog.FishPrice(x.species, x.length))})</color>");
            }
            if (Inventory.BucketCount > 0) Line("<color=#8A7563><i>Sell them at the fish shop at the market. The shopkeeper only speaks Mandarin, so ask Mei how.</i></color>");

            Header("Rods");
            foreach (var rod in Inventory.OwnedRods())
            {
                bool equipped = Inventory.Rod.id == rod.id;
                Row($"{rod.english}  <color=#8A7563>cast {rod.castDistance:0} m · reel ×{rod.reelSpeed:0.##}</color>",
                    equipped ? null : "Equip", () => Inventory.EquipRod(rod.id), equipped ? "equipped" : null);
            }

            Header("Bait");
            var baits = Inventory.Owned().Where(x => x.def.category == ItemCategory.Bait).ToList();
            var current = Inventory.Bait;
            Row("Plain hook", current == null ? null : "Use", () => Inventory.SelectBait(""), current == null ? "using" : null);
            foreach (var (def, count) in baits)
            {
                bool using_ = current != null && current.id == def.id;
                Row($"{def.english} × {count}  <color=#8A7563>{def.description}</color>", using_ ? null : "Use", () => Inventory.SelectBait(def.id), using_ ? "using" : null);
            }

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
                Row($"{def.english}  <color=#8A7563>(placed)</color>", "Put away", () =>
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

        private void Row(string text, string button, UnityAction onClick, string tag = null)
        {
            var row = UIFactory.Rect("Row", _content);
            row.SetLayout(48);
            var t = UIFactory.Text(row, "Text", text, 23, UITheme.Ink, TextAlignmentOptions.MidlineLeft);
            t.rectTransform.anchorMin = Vector2.zero;
            t.rectTransform.anchorMax = new Vector2(1, 1);
            t.rectTransform.offsetMin = new Vector2(20, 0);
            t.rectTransform.offsetMax = new Vector2(-190, 0);
            if (button != null)
            {
                var b = UIFactory.Button(row, button, () => { onClick(); if (IsOpen) Refresh(); }, UITheme.TealDark, 20);
                b.GetComponent<RectTransform>().Anchor(new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-6, 0), new Vector2(170, 42));
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
