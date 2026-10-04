using System.Linq;
using TMPro;
using UnityEngine;
using UntitledGame.Companion;
using UntitledGame.Core;
using UntitledGame.Economy;
using UntitledGame.Language;
using UntitledGame.Progression;

namespace UntitledGame.UI
{
    /// <summary>
    /// The shop window on the right while you're talking to a shopkeeper (E): your friendship with them, and their
    /// wares in English with prices (at the fish market, your catch). It has no buttons on purpose: every deal is
    /// made by speaking Mandarin to the keeper. Goods that need a closer friendship show which level unlocks them.
    /// Not modal: V / B / walking keep working.
    /// </summary>
    public class ShopPanel
    {
        private readonly RectTransform _root;
        private readonly CanvasGroup _group;
        private readonly TextMeshProUGUI _title, _subtitle, _friendship, _question;
        private readonly RectTransform _content;
        private ShopkeeperBrain _keeper;
        private bool _dirty;
        private string _shownQuestion;

        public bool IsOpen => _keeper != null;
        public ShopkeeperBrain Keeper => _keeper;

        public ShopPanel(RectTransform parent)
        {
            var panel = UIFactory.Panel(parent, "ShopPanel");
            _root = panel.rectTransform.Anchor(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-34, -150), new Vector2(600, 770));
            _group = panel.gameObject.AddComponent<CanvasGroup>();
            _title = UIFactory.Text(_root, "Title", "", 34, UITheme.Ink, TextAlignmentOptions.TopLeft, title: true);
            _title.rectTransform.Anchor(new Vector2(0, 1), new Vector2(0, 1), new Vector2(30, -18), new Vector2(540, 44));
            _subtitle = UIFactory.Text(_root, "Subtitle", "", 20, UITheme.InkSoft, TextAlignmentOptions.TopLeft);
            _subtitle.rectTransform.Anchor(new Vector2(0, 1), new Vector2(0, 1), new Vector2(30, -62), new Vector2(540, 30));
            _friendship = UIFactory.Text(_root, "Friendship", "", 21, UITheme.Ink, TextAlignmentOptions.TopLeft);
            _friendship.rectTransform.Anchor(new Vector2(0, 1), new Vector2(0, 1), new Vector2(30, -92), new Vector2(540, 56));
            var area = UIFactory.Rect("Area", _root).Stretch(22, 16, 154, 150);
            _content = UIFactory.ScrollList(area, 6);
            _question = UIFactory.Text(_root, "Question", "", 20, UITheme.TealDark, TextAlignmentOptions.Center);
            _question.rectTransform.Anchor(new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 80), new Vector2(560, 64));
            var foot = UIFactory.Text(_root, "Footer",
                "Everything here is done by <b>talking</b>: hold <b>V</b> and say it in Chinese.\n<b>Hold B</b> to ask Mei how  ·  <b>E</b> to leave",
                18, UITheme.InkSoft, TextAlignmentOptions.Center);
            foot.rectTransform.Anchor(new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 14), new Vector2(560, 60));
            _root.gameObject.SetActive(false);

            Inventory.Changed += MarkDirty;
            PlayerStats.Changed += MarkDirty;
            Affinity.Changed += MarkDirty;
            Hsk.Changed += MarkDirty;
            HskSchool.Changed += MarkDirty;
            ShopkeeperBrain.OfferChanged += OnOfferChanged;
        }

        public void Dispose()
        {
            Inventory.Changed -= MarkDirty;
            PlayerStats.Changed -= MarkDirty;
            Affinity.Changed -= MarkDirty;
            Hsk.Changed -= MarkDirty;
            HskSchool.Changed -= MarkDirty;
            ShopkeeperBrain.OfferChanged -= OnOfferChanged;
        }

        private void MarkDirty() => _dirty = true;
        private void OnOfferChanged(ShopkeeperBrain k) { if (k == _keeper) _dirty = true; }

        /// <summary>Called every frame with the keeper you're talking to (null closes the window).</summary>
        public void Update(ShopkeeperBrain active, bool hidden)
        {
            if (active != _keeper)
            {
                _keeper = active;
                _root.gameObject.SetActive(_keeper != null);
                if (_keeper != null)
                {
                    _group.alpha = 0f;
                    Rebuild();
                    AudioManager.Instance?.PlaySfx("SFX/ui_open_002", 0.3f);
                }
            }
            if (_keeper == null) return;
            _group.alpha = Mathf.MoveTowards(_group.alpha, hidden ? 0f : 1f, Time.unscaledDeltaTime * 6f);
            if (_keeper.PendingQuestion != _shownQuestion) UpdateQuestion();
            if (_dirty) Rebuild();
        }

        private void UpdateQuestion()
        {
            _shownQuestion = _keeper.PendingQuestion;
            _question.text = string.IsNullOrEmpty(_shownQuestion) ? ""
                : $"{_keeper.DisplayName} asked you a question. Answer in Chinese for extra friendship\n<size=17><color=#8A7563>Not sure what they said? Hold B and ask Mei.</color></size>";
        }

        private void Rebuild()
        {
            _dirty = false;
            foreach (Transform c in _content) Object.Destroy(c.gameObject);
            var shop = _keeper.Shop;
            if (shop == null) return;
            _title.text = $"{shop.english}  <size=22><color=#8A7563>{_keeper.DisplayName} ({_keeper.DisplayNameEnglish})</color></size>";
            string stats = shop.id == "tackle" ? $"  ·  line {Inventory.LineKg:0} kg"
                : shop.id == "books" ? $"  ·  {PlayerStats.DiscoveredFishCount}/{PlayerStats.TotalFishCount} fish discovered" : "";
            if (shop.school) stats = $"  ·  {UiText.T("passed", "通过了", 2)} <b>HSK {Hsk.Level}</b>";
            _subtitle.text = UiText.T($"You have <b>¥{Inventory.Money}</b>", $"你有<b>{Inventory.Money}</b>块", 1) + stats;

            int level = Affinity.Level(shop.id);
            int points = Affinity.Points(shop.id);
            int next = Affinity.NextThreshold(shop.id);
            string hearts = $"{level}/{Affinity.Thresholds.Length - 1}";
            _friendship.text = $"Friendship <color=#E0604E>{hearts}</color>  <b>{Affinity.LevelLabel(level)}</b>\n" +
                               $"<size=17><color=#8A7563>{(next > 0 ? $"{points}/{next} to {Affinity.LevelHanzi[level + 1]}: chat, answer, bring gifts (see J, Phrasebook)" : "As close as can be!")}</color></size>";
            UpdateQuestion();

            if (shop.school)
            {
                if (HskSchool.Current == null) GiftRow();
                BuildSchool();
                return;
            }
            GiftRow();
            if (shop.buysFish)
            {
                BuildFishRows(level);
                foreach (var def in shop.items.Select(Catalog.Get).Where(d => d != null))
                    ItemRow(def, level);
                return;
            }
            foreach (var def in shop.items.Select(Catalog.Get).Where(d => d != null))
                ItemRow(def, level);
        }

        private void ItemRow(ItemDef def, int level)
        {
            bool friendLocked = def.minAffinity > level;
            bool hskLocked = def.minHsk > Hsk.Level;
            bool locked = friendLocked || hskLocked;
            string state = null;
            if (locked)
                state = friendLocked && hskLocked ? $"{Affinity.LevelHanzi[def.minAffinity]}\n+ HSK {def.minHsk}"
                    : friendLocked ? $"locked: {Affinity.LevelHanzi[def.minAffinity]}" : $"locked: HSK {def.minHsk}";
            else if (def.category == ItemCategory.Upgrade && PlayerStats.Level(def.stat) >= PlayerStats.MaxLevel) state = "max level";
            else if (def.category == ItemCategory.Upgrade && PlayerStats.Level(def.stat) >= PlayerStats.TrainingCap(level)) state = $"locked: {Affinity.LevelHanzi[Mathf.Min(level + 1, 4)]}";
            else if (def.category == ItemCategory.Line && Inventory.LineKg >= def.lineKg) state = Inventory.Owns(def.id) ? "owned" : "weaker";
            else if (def.category == ItemCategory.Book && PlayerStats.HasRead(def.id)) state = "read";
            else if (def.unique && Inventory.Owns(def.id)) state = def.category == ItemCategory.Book ? "in your bag" : "owned";
            else if (_keeper.PendingOffer != null && _keeper.PendingOffer.itemId == def.id) state = "offered";

            string detail = def.description ?? "";
            if (def.category == ItemCategory.Book && def.teachesFish != null) detail = $"Teaches about {def.teachesFish.Length} kinds of fish. {detail}";
            if (def.category == ItemCategory.Upgrade) detail = $"Level {PlayerStats.Level(def.stat)}/{PlayerStats.MaxLevel} ({PlayerStats.StatValue(def.stat)}). {detail}";
            if (def.packSize > 1) detail = $"Pack of {def.packSize}. {detail}";
            if (locked)
            {
                var needs = new System.Collections.Generic.List<string>();
                if (friendLocked) needs.Add($"be {Affinity.LevelEnglish[def.minAffinity]}s ({Affinity.LevelHanzi[def.minAffinity]})");
                if (hskLocked) needs.Add($"pass HSK {def.minHsk} at the test centre");
                detail = $"Needs you to {string.Join(" and ", needs)}. {detail}";
            }
            int cost = Catalog.PriceOf(def);
            string price = Inventory.Money >= cost ? $"¥{cost}" : $"<color=#E0604E>¥{cost}</color>";
            string name = locked ? $"<color=#8A7563>{def.english}</color>" : $"<b>{def.english}</b>";
            Row($"{name}  {price}\n<size=18><color=#8A7563>{detail}</color></size>", state, locked);
        }

        /// <summary>How to give this keeper a gift, with the gifts you're carrying.</summary>
        private void GiftRow()
        {
            var shopId = _keeper.Shop.id;
            var gifts = Inventory.Owned().Where(x => x.def.category == ItemCategory.Gift && x.count > 0).Select(x => x.def).ToList();
            if (Affinity.GaveGiftToday(shopId))
            {
                Line($"<size=18><color=#8A7563>You gave {_keeper.DisplayName} a gift today (one a day).</color></size>");
                return;
            }
            if (gifts.Count == 0)
            {
                Line("<size=18><color=#8A7563>Gifts: buy some at Granny Liu's 礼品店, then come back and give one (+friendship).</color></size>");
                return;
            }
            var g = gifts[0];
            string phrase = $"这是送给你的{g.hanzi}";
            string carried = string.Join("、", gifts.Select(x => $"{x.hanzi} ({x.english.ToLower()})"));
            Row($"<b>Give a gift</b> <size=18>(once a day): say</size> <b>{phrase}</b>\n<size=17><color=#8A7563>{Pinyin.Of(phrase)}  ·  you have {carried}</color></size>", null, false);
        }

        private void BuildSchool()
        {
            var session = HskSchool.Current;
            if (session != null)
            {
                BuildSession(session);
                return;
            }
            ResultRows();
            int next = Hsk.NextTest;
            Line($"<b>Say to Teacher Gao</b> (hold V):");
            Row("<b>我想上课</b> <size=18>wǒ xiǎng shàngkè</size>\n<size=18><color=#8A7563>Take the next lesson. Passing it the first time pays money.</color></size>", null, false);
            Row("<b>我想练习</b> <size=18>wǒ xiǎng liànxí</size>\n<size=18><color=#8A7563>Free practice with random words (no money).</color></size>", null, false);
            Row(next > 0
                ? $"<b>我想考试</b> <size=18>wǒ xiǎng kǎoshì</size>\n<size=18><color=#8A7563>Take the HSK {next} test: {Hsk.TestQuestions} words, {Hsk.TestPassMark} to pass. Take it as often as you like.</color></size>"
                : "<b>HSK 3 passed!</b>\n<size=18><color=#8A7563>Every test is done. Lessons and practice are still open.</color></size>", null, false);
            Line("<size=18><color=#8A7563>In a lesson: repeat each word after her, then say words from English. 不知道 skips, 不学了 stops.</color></size>");
            if (Hsk.Level < Hsk.MaxLevel) LessonList(Mathf.Min(Hsk.Level + 1, Hsk.MaxLevel));
            for (int l = 1; l <= Hsk.MaxLevel; l++)
            {
                bool open = l <= Hsk.Level + 1;
                string passed = Hsk.Level >= l ? " <color=#2C7F79>passed</color>" : "";
                Row($"<b>HSK {l}</b>{passed}  <size=18>lessons {Hsk.LessonsDone(l)}/{Hsk.LessonCounts[l]} · ¥{Hsk.LessonReward[l]} each</size>\n" +
                    $"<size=16><color=#8A7563>Unlocks {Hsk.Unlocks[l]}.</color></size>", open ? null : $"pass HSK {l - 1}", !open, 104);
            }
        }

        /// <summary>The last lesson or test's result, big and clear, with the words that were missed.</summary>
        private void ResultRows()
        {
            var r = HskSchool.LastFinished;
            if (r == null) return;
            bool test = r.kind == LessonKind.Test;
            string score = r.kind == LessonKind.Lesson ? $"quiz {r.RecallRight}/{r.RecallAsked}" : $"{r.Right}/{r.questions.Count} right";
            string verdict = r.kind == LessonKind.Practice ? "<color=#2C7F79><b>FINISHED</b></color>"
                : HskSchool.LastPassed ? "<color=#2C7F79><b>PASSED</b></color>" : "<color=#E0604E><b>NOT PASSED</b></color>";
            string need = test ? $" (need {Hsk.TestPassMark})" : "";
            var missed = r.questions.Where(q => q.answered && !q.correct && (q.recall || r.kind != LessonKind.Lesson)).ToList();
            string missedText = missed.Count == 0 ? "No mistakes!" :
                "Missed: " + string.Join("  ", missed.Take(12).Select(q => $"{q.word.hanzi} {q.word.pinyin} ({q.word.meaning})"));
            Row($"<size=24>{r.Title}: {verdict}</size>  <size=19>{score}{need}</size>\n<size=16><color=#8A7563>{missedText}</color></size>", null, false, 112);
        }

        /// <summary>The lessons of the level you're working on, with their themes and which are passed.</summary>
        private void LessonList(int level)
        {
            var (nl, nn) = Hsk.NextLesson();
            var parts = new System.Collections.Generic.List<string>();
            for (int n = 1; n <= Hsk.LessonCounts[level]; n++)
            {
                var (en, zh) = Hsk.LessonTitle(level, n);
                string name = zh.Length > 0 ? $"{n}. {en} {zh}" : $"{n}";
                parts.Add(Hsk.LessonDone(level, n) ? $"<color=#2C7F79>{name}  passed</color>" : nl == level && nn == n ? $"<b>{name}  (next)</b>" : name);
            }
            var t = UIFactory.Text(_content, "Lessons", $"<b>HSK {level} lessons</b> <size=16><color=#8A7563>(say 第三课 for lesson 3)</color></size>\n<size=17>{string.Join("\n", parts)}</size>", 20, UITheme.Ink, TextAlignmentOptions.TopLeft);
            t.rectTransform.SetLayout(t.GetPreferredValues(t.text, 540, 2000).y + 8);
        }

        private void BuildSession(LessonSession session)
        {
            int total = session.questions.Count;
            string score = session.kind == LessonKind.Lesson ? $"quiz {session.RecallRight}/{session.RecallAsked}" : $"{session.Right} right";
            Line($"<b>{session.Title}</b>   <size=18>question {Mathf.Min(session.index + 1, total)}/{total} · {score}</size>");
            var q = session.Current;
            if (q != null)
            {
                string body = q.recall
                    ? $"<size=19><color=#8A7563>Say in Chinese:</color></size>\n<size=34><b>{q.word.meaning}</b></size>"
                    : $"<size=19><color=#8A7563>Listen and repeat:</color></size>\n<size=44><b>{q.word.hanzi}</b></size>  <size=26>{q.word.pinyin}</size>\n<size=20>{q.word.meaning}</size>";
                Row(body, null, false, q.recall ? 120 : 150);
            }
            if (session.pending != null)
            {
                string py = Pinyin.ContainsHanzi(session.pending) ? Pinyin.Of(session.pending) : "";
                Row($"<size=19><color=#8A7563>I heard:</color></size>  <size=34><b>{session.pending}</b></size>  <size=22>{py}</size>\n" +
                    "<size=18><b>[Y]</b> that's what I said: submit  ·  <b>[N]</b> or hold V: say it again</size>", null, false, 110);
                Line("<size=17><color=#8A7563>不知道 / skip = skip  ·  不考了 = stop  ·  E = leave (ends the test)</color></size>");
                return;
            }
            var last = session.last;
            if (last != null)
            {
                string mark = last.correct ? "<color=#2C7F79><b>Right!</b></color>" : "<color=#E0604E><b>Not quite</b></color>";
                Row($"{mark}  <b>{last.word.hanzi}</b> {last.word.pinyin}  <size=18>{last.word.meaning}</size>\n" +
                    $"<size=17><color=#8A7563>You said: {(string.IsNullOrEmpty(last.heard) ? "-" : last.heard)}</color></size>", null, false);
            }
            Line(session.kind == LessonKind.Test
                ? "<size=17><color=#8A7563>Answer, then check what I heard and press Y  ·  不知道 / skip = skip  ·  不考了 = stop</color></size>"
                : "<size=17><color=#8A7563>不知道 / skip = skip  ·  不学了 = stop  ·  E = leave (ends the lesson)</color></size>");
        }

        private void BuildFishRows(int level)
        {
            if (level > 0) Line($"<color=#8A7563>As a {Affinity.LevelEnglish[level]}, Auntie Chen pays you {level * 5}% more.</color>");
            Line(Hsk.FishBonus > 0
                ? $"<color=#2C7F79>HSK {Hsk.Level} passed: every fish sells for {Hsk.FishBonus * 100f:0}% more (prices below include it).</color>"
                : "<color=#8A7563>Passing HSK tests makes every fish sell for more (+10% / +20% / +35%).</color>");
            if (Inventory.BucketCount == 0)
            {
                Line("<color=#8A7563>Your bucket is empty. Auntie Chen buys any fish you catch: bigger and rarer ones pay more.</color>");
                return;
            }
            float bonus = 1f + 0.05f * level;
            foreach (var g in Inventory.Bucket.GroupBy(b => b.species.id))
            {
                var s = g.First().species;
                int value = Mathf.RoundToInt(g.Sum(x => Catalog.FishPrice(x.species, x.length)) * bonus);
                Row($"<b>{g.Count()} x {s.name}</b>  ~¥{value}\n<size=18><color=#8A7563>heaviest {Fishing.FishDatabase.WeightText(g.Max(x => x.length))}</color></size>", null, false);
            }
            Row($"<b>Everything</b>  ~¥{Mathf.RoundToInt(Inventory.BucketValue * bonus)}\n<size=18><color=#8A7563>{Inventory.BucketCount} catches. Tell Auntie Chen in Chinese that you want to sell.</color></size>", null, false);
        }

        private void Line(string text)
        {
            var t = UIFactory.Text(_content, "Line", text, 20, UITheme.Ink, TextAlignmentOptions.MidlineLeft);
            t.rectTransform.SetLayout(56);
        }

        private void Row(string text, string tag, bool dim, float height = 76)
        {
            var row = UIFactory.Panel(_content, "Row", UITheme.CreamDark.WithAlpha(dim ? 0.3f : 0.55f), shadow: false, small: true);
            row.rectTransform.SetLayout(height);
            var t = UIFactory.Text(row.transform, "Text", text, 22, UITheme.Ink, TextAlignmentOptions.MidlineLeft);
            t.rectTransform.anchorMin = Vector2.zero;
            t.rectTransform.anchorMax = Vector2.one;
            t.rectTransform.offsetMin = new Vector2(16, 4);
            t.rectTransform.offsetMax = new Vector2(tag != null ? -130 : -12, -4);
            if (tag != null)
            {
                var l = UIFactory.Text(row.transform, "Tag", tag, 19, dim ? UITheme.InkSoft : UITheme.TealDark, TextAlignmentOptions.MidlineRight);
                l.rectTransform.Anchor(new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-14, 0), new Vector2(120, 46));
            }
        }
    }
}
