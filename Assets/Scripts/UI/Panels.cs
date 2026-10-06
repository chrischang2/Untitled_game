using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UntitledGame.Companion;
using UntitledGame.Core;
using UntitledGame.Economy;
using UntitledGame.Language;
using UntitledGame.Environment;
using UntitledGame.Fishing;
using UntitledGame.GenAI;
using UntitledGame.Progression;

namespace UntitledGame.UI
{
    /// <summary>Pop-up card celebrating a catch.</summary>
    public class CatchCard
    {
        private readonly RectTransform _rt;
        private readonly CanvasGroup _group;
        private readonly Image _icon, _rarityStrip;
        private readonly TextMeshProUGUI _name, _chinese, _details, _blurb, _badge;
        private readonly RectTransform _badgeRt;
        private float _shownAt = -99f;
        private bool _visible;

        public CatchCard(RectTransform root)
        {
            var panel = UIFactory.Panel(root, "CatchCard");
            _rt = panel.rectTransform.Anchor(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -150), new Vector2(640, 250));
            _group = panel.gameObject.AddComponent<CanvasGroup>();
            _group.alpha = 0f;
            _group.blocksRaycasts = false;

            _rarityStrip = UIFactory.Image(panel.transform, "Strip", UITheme.Teal, GameAssets.Instance.roundedRectSmall);
            _rarityStrip.rectTransform.Anchor(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(16, 0), new Vector2(10, 210));

            var iconBg = UIFactory.Image(panel.transform, "IconBg", UITheme.CreamDark, GameAssets.Instance.circle, sliced: false);
            iconBg.rectTransform.Anchor(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(42, 12), new Vector2(170, 170));
            _icon = UIFactory.Image(iconBg.transform, "Icon", Color.white, GameAssets.Instance.fishIcon, sliced: false);
            _icon.rectTransform.Stretch(14, 14, 14, 14);

            _name = UIFactory.Text(panel.transform, "Name", "", 44, UITheme.Ink, TextAlignmentOptions.TopLeft, title: true);
            _name.rectTransform.Anchor(new Vector2(0, 1), new Vector2(0, 1), new Vector2(236, -22), new Vector2(390, 56));
            _chinese = UIFactory.Text(panel.transform, "Chinese", "", 28, UITheme.TealDark, TextAlignmentOptions.TopLeft);
            _chinese.rectTransform.Anchor(new Vector2(0, 1), new Vector2(0, 1), new Vector2(238, -76), new Vector2(390, 38));
            _details = UIFactory.Text(panel.transform, "Details", "", 25, UITheme.InkSoft, TextAlignmentOptions.TopLeft);
            _details.rectTransform.Anchor(new Vector2(0, 1), new Vector2(0, 1), new Vector2(238, -112), new Vector2(400, 44));
            _blurb = UIFactory.Text(panel.transform, "Blurb", "", 22, UITheme.Ink, TextAlignmentOptions.TopLeft);
            _blurb.rectTransform.Anchor(new Vector2(0, 1), new Vector2(0, 1), new Vector2(238, -156), new Vector2(380, 80));
            _blurb.fontStyle = FontStyles.Italic;

            var badge = UIFactory.Panel(panel.transform, "Badge", UITheme.Orange, shadow: false, small: true);
            _badgeRt = badge.rectTransform.Anchor(new Vector2(1, 1), new Vector2(0.5f, 0.5f), new Vector2(-40, -8), new Vector2(170, 46));
            _badgeRt.localRotation = Quaternion.Euler(0, 0, -8);
            _badge = UIFactory.Text(badge.transform, "Text", "NEW!", 26, Color.white, TextAlignmentOptions.Center, title: true);
            _badge.rectTransform.Stretch();
        }

        public void Show(CatchResult r)
        {
            var s = r.species;
            _name.text = s.name;
            int worth = Catalog.FishPrice(s, r.length) * (r.golden ? CatchJournal.GoldenValue : 1);
            _chinese.text = r.inBucket
                ? $"Worth about ¥{worth}  <size=22><color=#8A7563>· bag {Inventory.SlotsUsed}/{Inventory.SlotCapacity} slots</color></size>"
                : "<color=#E0604E>Bucket full: released</color>";
            var rc = FishDatabase.RarityColor(s.rarity);
            string hex = ColorUtility.ToHtmlStringRGB(r.golden ? UITheme.Hex("#C9951E") : rc);
            _name.text = $"<color=#{hex}>{(r.golden ? "Golden " : "")}{s.name}</color>";
            string size = s.IsFish ? $"<size=34><b>{FishDatabase.WeightText(r.length)}</b></size>{(r.perfect ? "  perfect!" : "")}  " : "";
            _details.text = $"{size}<color=#{ColorUtility.ToHtmlStringRGB(rc)}><b>{FishDatabase.RarityLabel(s.rarity)}</b></color>  <size=20>{s.hanzi} {s.pinyin}</size>";
            _blurb.text = s.blurb;
            _rarityStrip.color = rc;
            _icon.color = r.golden ? UITheme.Hex("#E8B83A") : s.IsFish ? s.body : UITheme.InkSoft;
            bool badge = r.isNewSpecies || r.isRecord || r.golden || r.newMedal >= 2;
            _badgeRt.gameObject.SetActive(badge);
            _badge.text = r.golden ? "GOLDEN!" : r.newMedal == 3 ? "GOLD!" : r.newMedal == 2 ? "SILVER!" : r.isNewSpecies ? "NEW!" : "RECORD!";
            _shownAt = Time.time;
            _visible = true;
        }

        /// <summary>Gone at once (a new cast is starting and the card would cover the power bar).</summary>
        public void HideNow()
        {
            _visible = false;
            _group.alpha = 0f;
        }

        public bool Visible => _visible || _group.alpha > 0.01f;

        public void Update(bool landing)
        {
            if (_visible && !landing && Time.time - _shownAt > 4f) _visible = false;
            if (_visible && Time.time - _shownAt > 8f) _visible = false;
            float target = _visible ? 1f : 0f;
            _group.alpha = Mathf.MoveTowards(_group.alpha, target, Time.deltaTime * 4f);
            float t = Mathf.Clamp01((Time.time - _shownAt) * 3f);
            float pop = _visible ? 1f + 0.12f * Mathf.Sin(t * Mathf.PI) : 1f;
            _rt.localScale = Vector3.one * pop;
            _badgeRt.localScale = Vector3.one * (1f + 0.06f * Mathf.Sin(Time.time * 6f));
        }
    }

    /// <summary>Base for centred modal panels that pause gameplay input while open.</summary>
    public abstract class ModalPanel
    {
        protected readonly RectTransform Root;
        protected readonly RectTransform Window;
        private readonly CanvasGroup _group;
        private readonly TMPro.TextMeshProUGUI _titleText;
        private readonly string _titleEnglish;
        private string _titleChinese;
        private int _titleTier;

        public bool IsOpen { get; private set; }

        /// <summary>The window title turns Chinese as HSK tests are passed (see UiText).</summary>
        protected void ChineseTitle(string chinese, int tier)
        {
            _titleChinese = chinese;
            _titleTier = tier;
        }

        protected ModalPanel(RectTransform canvas, string name, Vector2 size, string title)
        {
            var dim = UIFactory.Image(canvas, name, new Color(0.1f, 0.08f, 0.06f, 0.45f));
            dim.raycastTarget = true;
            Root = dim.rectTransform.Stretch();
            _group = dim.gameObject.AddComponent<CanvasGroup>();
            var win = UIFactory.Panel(Root, "Window");
            Window = win.rectTransform.Anchor(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, size);
            var t = UIFactory.Text(Window, "Title", title, 50, UITheme.Ink, TextAlignmentOptions.Center, title: true);
            t.rectTransform.Anchor(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -22), new Vector2(size.x - 80, 64));
            _titleText = t;
            _titleEnglish = title;
            var close = UIFactory.Button(Window, "x", Close, UITheme.InkSoft, 30);
            close.GetComponent<RectTransform>().Anchor(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-22, -22), new Vector2(56, 56));
            Root.gameObject.SetActive(false);
        }

        public void Toggle()
        {
            if (IsOpen) Close(); else Open();
        }

        public virtual void Open()
        {
            IsOpen = true;
            Root.gameObject.SetActive(true);
            Root.SetAsLastSibling();
            InputGate.BlockGameplay(this);
            AudioManager.Instance?.PlaySfx("SFX/rpg_bookOpen", 0.5f);
            if (_titleChinese != null) _titleText.text = UiText.Plain(_titleEnglish, _titleChinese, _titleTier);
            Refresh();
        }

        public virtual void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            Root.gameObject.SetActive(false);
            InputGate.UnblockGameplay(this);
            AudioManager.Instance?.PlaySfx("SFX/rpg_bookClose", 0.5f);
        }

        protected abstract void Refresh();
    }

    /// <summary>The fishing journal: every species with its Chinese name, counts and best size.</summary>
    public class JournalPanel : ModalPanel
    {
        private readonly TextMeshProUGUI _progress;
        private readonly List<(FishSpecies species, Image icon, TextMeshProUGUI name, TextMeshProUGUI info, Image strip)> _cards =
            new List<(FishSpecies, Image, TextMeshProUGUI, TextMeshProUGUI, Image)>();

        private readonly RectTransform _grid, _peopleArea, _people, _transcriptArea, _transcript;
        private bool _showPeople, _showTranscript, _showPhrases;
        private readonly TextMeshProUGUI _fishTab, _peopleTab, _transcriptTab, _phraseTab;

        public JournalPanel(RectTransform canvas) : base(canvas, "Journal", new Vector2(1500, 980), "Journal")
        {
            _progress = UIFactory.Text(Window, "Progress", "", 26, UITheme.InkSoft, TextAlignmentOptions.Center);
            _progress.rectTransform.Anchor(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -84), new Vector2(900, 36));

            // Tabs: fish / people (what you've learned about the shopkeepers).
            var fishTab = UIFactory.Button(Window, "Fish", () => { _showPeople = false; _showTranscript = false; _showPhrases = false; Refresh(); }, UITheme.TealDark, 22);
            _fishTab = fishTab.GetComponentInChildren<TextMeshProUGUI>();
            fishTab.GetComponent<RectTransform>().Anchor(new Vector2(0, 1), new Vector2(0, 1), new Vector2(40, -28), new Vector2(130, 48));
            var peopleTab = UIFactory.Button(Window, "People", () => { _showPeople = true; _showTranscript = false; _showPhrases = false; Refresh(); }, UITheme.Orange, 22);
            _peopleTab = peopleTab.GetComponentInChildren<TextMeshProUGUI>();
            peopleTab.GetComponent<RectTransform>().Anchor(new Vector2(0, 1), new Vector2(0, 1), new Vector2(180, -28), new Vector2(130, 48));
            var phraseTab = UIFactory.Button(Window, "Phrasebook", () => { _showPhrases = true; _showPeople = false; _showTranscript = false; Refresh(); }, UITheme.Teal, 22);
            _phraseTab = phraseTab.GetComponentInChildren<TextMeshProUGUI>();
            phraseTab.GetComponent<RectTransform>().Anchor(new Vector2(0, 1), new Vector2(0, 1), new Vector2(320, -28), new Vector2(170, 48));
            var transcriptTab = UIFactory.Button(Window, "Transcript", () => { _showTranscript = true; _showPeople = false; _showPhrases = false; Refresh(); }, UITheme.InkSoft, 22);
            _transcriptTab = transcriptTab.GetComponentInChildren<TextMeshProUGUI>();
            transcriptTab.GetComponent<RectTransform>().Anchor(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-100, -28), new Vector2(170, 48));
            _peopleArea = UIFactory.Rect("PeopleArea", Window).Stretch(40, 40, 136, 36);
            _people = UIFactory.ScrollList(_peopleArea, 12);
            _peopleArea.gameObject.SetActive(false);
            _transcriptArea = UIFactory.Rect("TranscriptArea", Window).Stretch(40, 40, 136, 36);
            _transcript = UIFactory.ScrollList(_transcriptArea, 6);
            _transcriptArea.gameObject.SetActive(false);

            var grid = UIFactory.Rect("Grid", Window).Stretch(40, 40, 136, 36);
            _grid = grid;
            var gl = grid.gameObject.AddComponent<GridLayoutGroup>();
            gl.cellSize = new Vector2(274, 182);
            gl.spacing = new Vector2(14, 10);
            gl.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            gl.constraintCount = 5;
            gl.childAlignment = TextAnchor.UpperCenter;

            foreach (var s in FishDatabase.All)
            {
                var card = UIFactory.Panel(grid, "Card_" + s.id, UITheme.CreamDark.WithAlpha(0.7f), shadow: false, small: true);
                var strip = UIFactory.Image(card.transform, "Strip", FishDatabase.RarityColor(s.rarity), GameAssets.Instance.roundedRectSmall);
                strip.rectTransform.Anchor(new Vector2(0, 1), new Vector2(0, 1), new Vector2(10, -10), new Vector2(254, 8));
                var icon = UIFactory.Image(card.transform, "Icon", Color.white, GameAssets.Instance.fishIcon, sliced: false);
                icon.rectTransform.Anchor(new Vector2(0, 1), new Vector2(0, 1), new Vector2(10, -20), new Vector2(84, 84));
                var name = UIFactory.Text(card.transform, "Name", "", 24, UITheme.Ink, TextAlignmentOptions.TopLeft, title: true);
                name.rectTransform.Anchor(new Vector2(0, 1), new Vector2(0, 1), new Vector2(100, -24), new Vector2(166, 80));
                var info = UIFactory.Text(card.transform, "Info", "", 15, UITheme.InkSoft, TextAlignmentOptions.TopLeft);
                info.rectTransform.Anchor(new Vector2(0, 1), new Vector2(0, 1), new Vector2(12, -96), new Vector2(256, 84));
                _cards.Add((s, icon, name, info, strip));
            }
        }

        public bool ShowingPeople => _showPeople;

        public void ShowPeople(bool people)
        {
            _showPeople = people;
            _showTranscript = false;
            _showPhrases = false;
            if (IsOpen) Refresh();
        }

        public void ShowPhrasebook()
        {
            _showPhrases = true;
            _showPeople = false;
            _showTranscript = false;
            if (IsOpen) Refresh();
        }

        public void ShowTranscript()
        {
            _showTranscript = true;
            _showPeople = false;
            _showPhrases = false;
            if (IsOpen) Refresh();
        }

        // Phrases for getting to know the shopkeepers (hanzi, English). Pinyin is added by the game.
        private static readonly (string section, (string zh, string en)[] phrases)[] Phrasebook =
        {
            ("Finding a good gift", new[]
            {
                ("你喜欢什么？", "What do you like?"),
                ("你不喜欢什么？", "What don't you like?"),
                ("刘奶奶，老王喜欢什么？", "Granny Liu, what does Old Wang like? (she knows what everyone likes)"),
            }),
            ("Give a gift (one a day for each shopkeeper)", new[]
            {
                ("这是送给你的礼物。", "This is a present for you."),
                ("这是送给你的茶。", "This tea is for you. (say the gift's name)"),
                ("送给你！", "For you!"),
                ("希望你喜欢。", "I hope you like it."),
            }),
            ("Chat (every Chinese sentence counts)", new[]
            {
                ("你好！你今天怎么样？", "Hello! How are you today?"),
                ("今天天气很好。", "The weather is nice today."),
                ("你在做什么？", "What are you doing?"),
                ("我很喜欢你的店。", "I really like your shop."),
                ("谢谢你！再见！", "Thank you! Goodbye!"),
            }),
        };

        private void PhraseLine(string text, float size = 21)
        {
            var t = UIFactory.Text(_transcript, "Line", text, size, UITheme.Ink, TextAlignmentOptions.TopLeft);
            t.rectTransform.SetLayout(t.GetPreferredValues(text, 1380, 2000).y + 6);
        }

        /// <summary>How friendship works, and phrases for getting to know the shopkeepers.</summary>
        private void RefreshPhrasebook()
        {
            foreach (Transform c in _transcript) Object.Destroy(c.gameObject);
            _progress.text = "Phrasebook: making friends at the market";
            PhraseLine("<b>How friendship works</b>", 26);
            PhraseLine("Every shopkeeper works the same way. To reach each friendship level you need three things:\n" +
                       "- <b>Learn about them</b> by asking the questions below (one or two for each level). What they tell you goes on the People page.\n" +
                       "- <b>Give them a gift</b> (one for each level; a gift they don't like doesn't count). Buy gifts at Granny Liu's 礼品店.\n" +
                       "- <b>Pass the HSK test</b>: 朋友 needs HSK 1, 好朋友 HSK 2, 老朋友 HSK 3 (Teacher Gao's test centre).\n" +
                       "Closer friends sell you better things. Each shop window shows what's left for the next level.");
            PhraseLine("\n<b>The questions, level by level</b>  <size=18><color=#8A7563>(the same for everyone)</color></size>", 26);
            for (int l = 1; l <= Affinity.MaxLevel; l++)
            {
                foreach (var f in Affinity.FactsFor[l])
                {
                    var (q, en) = Affinity.FactQuestions[f];
                    string hsk = Affinity.HskNeeded(l) > 0 ? $", HSK {Affinity.HskNeeded(l)}" : "";
                    PhraseLine($"<b>{q}</b>  <color=#2C7F79><i>{Pinyin.Of(q)}</i></color>\n<size=18><color=#8A7563>Ask about {en}  (for {Affinity.LevelHanzi[l]} {Affinity.LevelEnglish[l]}{hsk})</color></size>");
                }
            }
            foreach (var (section, phrases) in Phrasebook)
            {
                PhraseLine($"\n<b>{section}</b>", 26);
                foreach (var (zh, en) in phrases)
                    PhraseLine($"<b>{zh}</b>  <color=#2C7F79><i>{Pinyin.Of(zh)}</i></color>\n<size=18><color=#8A7563>{en}</color></size>");
            }
            PhraseLine("\n<b>What they love talking about</b>  <size=18><color=#8A7563>(don't know a word? Ask Mei)</color></size>", 26);
            foreach (var shop in Catalog.Shops)
            {
                var p = KeeperProfiles.For(shop.id);
                if (p == null) continue;
                string topics = string.Join("、", p.topics.Select(t => $"{t} <color=#2C7F79><i>{Pinyin.Of(t)}</i></color>"));
                PhraseLine($"<b>{shop.keeperName}</b> <size=18>({shop.keeperEnglish})</size>:  {topics}");
            }
        }

        /// <summary>Everything said with Mei and the shopkeepers this session, with pinyin (starts fresh each session).</summary>
        private void RefreshTranscript()
        {
            foreach (Transform c in _transcript) Object.Destroy(c.gameObject);
            var entries = ConversationLog.Entries;
            _progress.text = entries.Count == 0 ? "Nothing said yet this session." : $"This session's conversations ({entries.Count} lines, newest first)";
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                var e = entries[i];
                string who = e.fromPlayer ? "<color=#2C7F79><b>You</b></color>" : $"<color=#C9504A><b>{e.speaker}</b></color>";
                string py = Pinyin.ContainsHanzi(e.text) && !e.fromPlayer ? $"\n<size=17><color=#2C7F79><i>{Pinyin.Annotate(e.text)}</i></color></size>" : "";
                string line = $"{who}  {e.text}{py}";
                var t = UIFactory.Text(_transcript, "Line", line, 21, UITheme.Ink, TextAlignmentOptions.TopLeft);
                t.rectTransform.SetLayout(t.GetPreferredValues(line, 1380, 2000).y + 6);
            }
        }

        /// <summary>
        /// What you've learned about each shopkeeper. Likes and dislikes are written only in Chinese with pinyin
        /// (they're vocabulary to learn); other facts are in English.
        /// </summary>
        private void RefreshPeople()
        {
            foreach (Transform c in _people) Object.Destroy(c.gameObject);
            int known = 0, total = 0;
            foreach (var shop in Catalog.Shops)
            {
                var p = KeeperProfiles.For(shop.id);
                if (p == null) continue;
                var all = p.AllFacts().ToList();
                var learned = all.Where(f => Affinity.Knows(shop.id, f.id)).ToList();
                known += learned.Count;
                total += all.Count;
                int level = Affinity.Level(shop.id);

                string Words(string kind)
                {
                    var list = (kind == "like" ? p.likes : p.dislikes)
                        .Select(h => Affinity.Knows(shop.id, kind + ":" + h) ? $"{h} <color=#2C7F79><i>{Pinyin.Of(h)}</i></color>" : "？").ToList();
                    return string.Join("、", list);
                }
                var sb = new System.Text.StringBuilder();
                sb.Append($"<size=28><b>{shop.keeperName}</b></size> <color=#2C7F79><i>{Pinyin.Of(shop.keeperName)}</i></color>  ·  {shop.keeperEnglish}, {shop.english}\n");
                sb.Append($"<color=#8A7563>Friendship:</color> {Affinity.LevelHanzi[level]} <color=#2C7F79><i>{Pinyin.Of(Affinity.LevelHanzi[level])}</i></color> ({level}/{Affinity.MaxLevel})");
                if (level < Affinity.MaxLevel)
                {
                    var left = Affinity.Requirements(shop.id, level + 1).Where(r => !r.done)
                        .Select(r => r.kind == "fact" ? Affinity.FactQuestions[r.factId].question : r.text).ToList();
                    sb.Append($"   <color=#8A7563>for {Affinity.LevelHanzi[level + 1]}: {(left.Count == 0 ? "all done" : string.Join(", ", left))}</color>");
                }
                sb.Append('\n');
                sb.Append($"喜欢 <color=#2C7F79><i>xǐhuan</i></color>: {Words("like")}     不喜欢 <color=#2C7F79><i>bù xǐhuan</i></color>: {Words("dislike")}\n");
                var facts = learned.Where(f => !f.id.StartsWith("like:") && !f.id.StartsWith("dislike:")).Select(f => "- " + f.english).ToList();
                int unknown = all.Count - learned.Count;
                if (facts.Count > 0) sb.Append(string.Join("\n", facts)).Append('\n');
                if (unknown > 0) sb.Append($"<color=#8A7563><i>{unknown} more thing{(unknown == 1 ? "" : "s")} to find out: ask them (see the Phrasebook).</i></color>");

                var card = UIFactory.Panel(_people, "Person_" + shop.id, UITheme.CreamDark.WithAlpha(0.6f), shadow: false, small: true);
                var t = UIFactory.Text(card.transform, "Text", sb.ToString(), 21, UITheme.Ink, TextAlignmentOptions.TopLeft);
                t.rectTransform.Stretch(20, 20, 12, 10);
                float h = t.GetPreferredValues(sb.ToString(), 1340, 2000).y + 30;
                card.rectTransform.SetLayout(Mathf.Max(120, h));
            }
            _progress.text = $"{known} / {total} things learned about the shopkeepers";
        }

        protected override void Refresh()
        {
            if (_fishTab != null) _fishTab.text = UiText.Plain("Fish", "鱼", 1);
            if (_peopleTab != null) _peopleTab.text = UiText.Plain("People", "朋友", 1);
            if (_transcriptTab != null) _transcriptTab.text = UiText.Plain("Transcript", "说过的话", 2);
            _grid.gameObject.SetActive(!_showPeople && !_showTranscript && !_showPhrases);
            _peopleArea.gameObject.SetActive(_showPeople);
            _transcriptArea.gameObject.SetActive(_showTranscript || _showPhrases);
            if (_showPhrases)
            {
                RefreshPhrasebook();
                return;
            }
            if (_showTranscript)
            {
                RefreshTranscript();
                return;
            }
            if (_showPeople)
            {
                RefreshPeople();
                return;
            }
            _progress.text = $"{CatchJournal.SpeciesDiscovered} / {FishDatabase.All.Count} discovered   ·   {CatchJournal.TotalCatches} catches   ·   medals: {CatchJournal.MedalCount(3)} gold, {CatchJournal.MedalCount(2)} silver+";
            foreach (var (s, icon, name, info, strip) in _cards)
            {
                var rec = CatchJournal.Get(s.id);
                bool known = rec != null && rec.count > 0;
                if (!known && !Progression.PlayerStats.IsDiscovered(s))
                {
                    icon.color = new Color(0.3f, 0.25f, 0.2f, 0.12f);
                    name.text = "???";
                    info.text = "Undiscovered. A book from Teacher Zhou's bookshop would tell you about it.";
                    continue;
                }
                // Read about (or caught): show what it takes to catch one.
                icon.color = known ? (s.IsFish ? s.body : UITheme.InkSoft) : new Color(0.3f, 0.25f, 0.2f, 0.45f);
                name.text = s.name;
                if (!s.IsFish)
                {
                    info.text = known ? $"Found {rec.count}x\n{s.blurb}" : "Washes about anywhere.";
                    continue;
                }
                string medal = known && rec.medal > 0 ? $" <color={(rec.medal == 3 ? "#C9951E" : rec.medal == 2 ? "#8C96A0" : "#A0673A")}><b>{CatchJournal.MedalNames[rec.medal]}</b></color>" : "";
                string golden = known && rec.goldenCount > 0 ? $" <color=#C9951E>({rec.goldenCount} golden)</color>" : "";
                string caught = known ? $"Caught {rec.count}x, heaviest {FishDatabase.WeightText(rec.bestLength)}{medal}{golden}" : $"<color=#{ColorUtility.ToHtmlStringRGB(FishDatabase.RarityColor(s.rarity))}>{FishDatabase.RarityLabel(s.rarity)}</color>, not caught yet";
                info.text = $"{caught}\n{FishDatabase.WeightText(s.minWeight)}-{FishDatabase.WeightText(s.maxWeight)} · {FishDatabase.WhereText(s)} · {FishDatabase.WhenText(s)}\n" +
                            $"Likes: {FishDatabase.BaitText(s)}\nLine: {FishDatabase.LineText(s)}";
            }
        }
    }

    /// <summary>Settings / pause menu.</summary>
    public class SettingsPanel : ModalPanel
    {
        private readonly List<System.Action> _refreshers = new List<System.Action>();
        private readonly CompanionBrain _brain;

        public SettingsPanel(RectTransform canvas, CompanionBrain brain, System.Action openSaves) : base(canvas, "Settings", new Vector2(1040, 1040), "Take a break")
        {
            ChineseTitle("休息一下", 2);
            _brain = brain;
            var list = UIFactory.Rect("Rows", Window).Stretch(60, 60, 100, 120);
            UIFactory.VLayout(list.gameObject, 5, new RectOffset(0, 0, 0, 0));

            var st = SaveSystem.Settings;
            SliderRow(list, "Music", () => st.musicVolume, v => st.musicVolume = v);
            SliderRow(list, "Nature sounds", () => st.ambienceVolume, v => st.ambienceVolume = v);
            SliderRow(list, "Effects", () => st.sfxVolume, v => st.sfxVolume = v);
            SliderRow(list, "Voice volume", () => st.voiceVolume, v => st.voiceVolume = v);
            OptionRow(list, "Voices", () => st.speakReplies ? "On" : "Subtitles only", () => st.speakReplies = !st.speakReplies);
            OptionRow(list, "Mei sounds like", () => MeiVoices.Label(st.meiVoice), () =>
            {
                st.meiVoice = MeiVoices.Next(st.meiVoice);
                MeiVoices.Preview();
            });
            OptionRow(list, "Talking speed", () => st.voiceSpeed < 0.85f ? "Slow" : st.voiceSpeed < 0.95f ? "Relaxed" : "Normal", () =>
            {
                st.voiceSpeed = st.voiceSpeed < 0.85f ? 0.9f : st.voiceSpeed < 0.95f ? 1f : 0.8f;
            });
            OptionRow(list, "Mei's English", () => st.immersion switch
            {
                ImmersionLevel.Beginner => "Lots (beginner)",
                ImmersionLevel.Intermediate => "A little",
                _ => "None (full immersion)",
            }, () => st.immersion = (ImmersionLevel)(((int)st.immersion + 1) % 3));
            OptionRow(list, "Pinyin", () => st.pinyin switch
            {
                PinyinMode.Always => "Always",
                PinyinMode.NewWordsOnly => "New words only",
                _ => "Off",
            }, () => st.pinyin = (PinyinMode)(((int)st.pinyin + 1) % 3));
            OptionRow(list, "Speech recognition", () => st.asrMode switch
            {
                1 => "SenseVoice only (fastest)",
                2 => "Qwen3-ASR (beta, slower)",
                _ => "Auto (Qwen3 for English)",
            }, () =>
            {
                st.asrMode = (st.asrMode + 1) % 3;
                var sp = LocalAIServices.Instance?.Speech;
                if (st.asrMode != 1 && sp != null && !sp.QwenReady)
                    sp.LoadQwen(LocalAIServices.Instance.Config.Resolve(LocalAIServices.Instance.Root, LocalAIServices.Instance.Config.qwenAsrModel), 6);
            });
            OptionRow(list, "Talking", () => st.handsFree ? "Hands-free (beta)" : "Hold V / B to talk", () => st.handsFree = !st.handsFree);
            // The waking day runs 6am-2am (20 game hours).
            OptionRow(list, "Day length", () => $"{Mathf.RoundToInt(st.realSecondsPerHour * 20f / 60f)} min (6am-2am)", () =>
            {
                st.realSecondsPerHour = st.realSecondsPerHour < 100f ? 120f : st.realSecondsPerHour < 150f ? 180f : 90f;
                if (DayNightCycle.Instance != null) DayNightCycle.Instance.RealSecondsPerHour = st.realSecondsPerHour;
            });

            var buttons = UIFactory.Rect("Buttons", Window).Anchor(new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 34), new Vector2(960, 70));
            var hl = UIFactory.HLayout(buttons.gameObject, 20, new RectOffset(0, 0, 0, 0));
            hl.childAlignment = TextAnchor.MiddleCenter;
            hl.childForceExpandWidth = true;
            UIFactory.Button(buttons, "Keep fishing", Close, UITheme.Teal).GetComponent<RectTransform>().SetLayout(64, 240);
            UIFactory.Button(buttons, "Saves & logs", () => { Close(); openSaves?.Invoke(); }, UITheme.TealDark).GetComponent<RectTransform>().SetLayout(64, 240);
            UIFactory.Button(buttons, "New chat", () => { _brain?.ClearConversation(); GameEvents.Toast("Mei's conversation was reset."); Close(); }, UITheme.Orange)
                .GetComponent<RectTransform>().SetLayout(64, 220);
            UIFactory.Button(buttons, "Quit", Quit, UITheme.Red).GetComponent<RectTransform>().SetLayout(64, 180);
        }


        private RectTransform Row(Transform parent, string label)
        {
            var row = UIFactory.Rect("Row_" + label, parent);
            row.SetLayout(52);
            var l = UIFactory.Text(row, "Label", label, 28, UITheme.Ink, TextAlignmentOptions.MidlineLeft);
            l.rectTransform.anchorMin = new Vector2(0, 0);
            l.rectTransform.anchorMax = new Vector2(0.42f, 1);
            l.rectTransform.offsetMin = l.rectTransform.offsetMax = Vector2.zero;
            return row;
        }

        private void SliderRow(Transform parent, string label, System.Func<float> get, System.Action<float> set)
        {
            var row = Row(parent, label);
            var s = UIFactory.Slider(row, get(), v =>
            {
                set(v);
                SaveSystem.Save();
            });
            var rt = s.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.45f, 0.1f);
            rt.anchorMax = new Vector2(1f, 0.9f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            _refreshers.Add(() => s.SetValueWithoutNotify(get()));
        }

        private void OptionRow(Transform parent, string label, System.Func<string> get, System.Action cycle)
        {
            var row = Row(parent, label);
            Button b = null;
            b = UIFactory.Button(row, get(), () =>
            {
                cycle();
                SaveSystem.NotifySettingsChanged();
                b.GetComponentInChildren<TextMeshProUGUI>().text = get();
            }, UITheme.TealDark, 24);
            var rt = b.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.45f, 0.05f);
            rt.anchorMax = new Vector2(1f, 0.95f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            _refreshers.Add(() => b.GetComponentInChildren<TextMeshProUGUI>().text = get());
        }

        protected override void Refresh()
        {
            foreach (var r in _refreshers) r();
        }

        public override void Close()
        {
            base.Close();
            SaveSystem.NotifySettingsChanged();
        }

        private static void Quit()
        {
            SaveSystem.Save();
            ChatAudit.Close();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }

    /// <summary>Scrollable transcript of every conversation (study material), with pinyin. Newest at the top.</summary>
    public class ChatPanel
    {
        private readonly RectTransform _root;
        private readonly RectTransform _content;
        private readonly ScrollRect _scroll;
        private int _shown;
        private bool _dirty = true;

        public bool IsOpen { get; private set; }

        private void MarkDirty() => _dirty = true;

        public void Dispose() => ConversationLog.Changed -= MarkDirty;

        public ChatPanel(RectTransform canvas)
        {
            var panel = UIFactory.Panel(canvas, "ChatLog");
            _root = panel.rectTransform;
            _root.anchorMin = new Vector2(1, 0.18f);
            _root.anchorMax = new Vector2(1, 0.86f);
            _root.pivot = new Vector2(1, 0.5f);
            _root.sizeDelta = new Vector2(600, 0);
            _root.anchoredPosition = new Vector2(-34, 0);

            var title = UIFactory.Text(_root, "Title", "Conversations", 34, UITheme.Ink, TextAlignmentOptions.TopLeft, title: true);
            title.rectTransform.Anchor(new Vector2(0, 1), new Vector2(0, 1), new Vector2(26, -16), new Vector2(400, 46));
            var hint = UIFactory.Text(_root, "Hint", "[C] close", 20, UITheme.InkSoft, TextAlignmentOptions.TopRight);
            hint.rectTransform.Anchor(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-26, -24), new Vector2(160, 30));

            var area = UIFactory.Rect("Area", _root).Stretch(20, 20, 72, 20);
            _content = UIFactory.ScrollList(area, 10);
            _scroll = area.GetComponent<ScrollRect>();
            ConversationLog.Changed += MarkDirty;
            _root.gameObject.SetActive(false);
        }

        public void Toggle()
        {
            if (IsOpen) Close(); else Open();
        }

        public void Open()
        {
            IsOpen = true;
            _root.gameObject.SetActive(true);
            _dirty = true;
            AudioManager.Instance?.PlaySfx("SFX/rpg_bookFlip1", 0.4f);
        }

        public void Close()
        {
            IsOpen = false;
            _root.gameObject.SetActive(false);
        }

        public void Update()
        {
            if (!IsOpen || !_dirty) return;
            _dirty = false;
            var entries = ConversationLog.Entries;
            if (_shown > entries.Count)
            {
                foreach (Transform c in _content) Object.Destroy(c.gameObject);
                _shown = 0;
            }
            bool added = false;
            while (_shown < entries.Count)
            {
                var e = entries[_shown++];
                string color = e.fromPlayer ? "C7713A" : e.speaker == CompanionPersona.Name ? "2C7F79" : "B8612E";
                string py = PinyinDisplay.For(e.text);
                string body = $"<b><color=#{color}>{e.speaker}</color></b>  {e.text}" + (py.Length > 0 ? $"\n<size=18><i><color=#6E8C88>{py}</color></i></size>" : "");
                var line = UIFactory.Text(_content, "Line", body, 23, UITheme.Ink, TextAlignmentOptions.TopLeft);
                line.textWrappingMode = TextWrappingModes.Normal;
                line.transform.SetAsFirstSibling(); // newest first
                added = true;
            }
            if (added)
            {
                Canvas.ForceUpdateCanvases();
                _scroll.verticalNormalizedPosition = 1f;
            }
        }
    }
}
