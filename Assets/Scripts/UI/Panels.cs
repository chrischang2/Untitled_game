using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UntitledGame.Companion;
using UntitledGame.Core;
using UntitledGame.Environment;
using UntitledGame.Fishing;

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
            _details.rectTransform.Anchor(new Vector2(0, 1), new Vector2(0, 1), new Vector2(238, -116), new Vector2(390, 36));
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
            _chinese.text = $"{s.hanzi}  <size=22><color=#8A7563>{s.pinyin}</color></size>";
            string size = s.IsFish ? $"{r.length:0.#} cm · " : "";
            var rc = FishDatabase.RarityColor(s.rarity);
            _details.text = $"{size}<color=#{ColorUtility.ToHtmlStringRGB(rc)}>{FishDatabase.RarityLabel(s.rarity)}</color>";
            _blurb.text = s.blurb;
            _rarityStrip.color = rc;
            _icon.color = s.IsFish ? s.body : UITheme.InkSoft;
            bool badge = r.isNewSpecies || r.isRecord;
            _badgeRt.gameObject.SetActive(badge);
            _badge.text = r.isNewSpecies ? "NEW!" : "RECORD!";
            _shownAt = Time.time;
            _visible = true;
        }

        public void Update(bool landing)
        {
            if (_visible && !landing && Time.time - _shownAt > 1.5f) _visible = false;
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

        public bool IsOpen { get; private set; }

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

        public JournalPanel(RectTransform canvas) : base(canvas, "Journal", new Vector2(1500, 900), "Fishing Journal")
        {
            _progress = UIFactory.Text(Window, "Progress", "", 26, UITheme.InkSoft, TextAlignmentOptions.Center);
            _progress.rectTransform.Anchor(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -84), new Vector2(900, 36));

            var grid = UIFactory.Rect("Grid", Window).Stretch(40, 40, 136, 36);
            var gl = grid.gameObject.AddComponent<GridLayoutGroup>();
            gl.cellSize = new Vector2(274, 172);
            gl.spacing = new Vector2(14, 14);
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
                var info = UIFactory.Text(card.transform, "Info", "", 18, UITheme.InkSoft, TextAlignmentOptions.TopLeft);
                info.rectTransform.Anchor(new Vector2(0, 1), new Vector2(0, 1), new Vector2(12, -106), new Vector2(252, 64));
                _cards.Add((s, icon, name, info, strip));
            }
        }

        protected override void Refresh()
        {
            _progress.text = $"{CatchJournal.SpeciesDiscovered} / {FishDatabase.All.Count} discovered   ·   {CatchJournal.TotalCatches} total catches";
            foreach (var (s, icon, name, info, strip) in _cards)
            {
                var rec = CatchJournal.Get(s.id);
                bool known = rec != null && rec.count > 0;
                icon.color = known ? (s.IsFish ? s.body : UITheme.InkSoft) : new Color(0.3f, 0.25f, 0.2f, 0.25f);
                name.text = known ? $"{s.name}\n<size=20><font-weight=400><color=#2C7F79>{s.hanzi}</color> <color=#8A7563>{s.pinyin}</color></size>" : "???";
                string when = $"{FishDatabase.WhenText(s)} · {FishDatabase.WhereText(s)}";
                info.text = known
                    ? (s.IsFish ? $"Caught {rec.count}× · best {rec.bestLength:0.#} cm\n{when}" : $"Found {rec.count}×\n{s.blurb}")
                    : $"<color=#{ColorUtility.ToHtmlStringRGB(FishDatabase.RarityColor(s.rarity))}>{FishDatabase.RarityLabel(s.rarity)}</color>\nHint: {when}";
            }
        }
    }

    /// <summary>Settings / pause menu.</summary>
    public class SettingsPanel : ModalPanel
    {
        private readonly List<System.Action> _refreshers = new List<System.Action>();
        private readonly CompanionBrain _brain;

        public SettingsPanel(RectTransform canvas, CompanionBrain brain) : base(canvas, "Settings", new Vector2(980, 960), "Take a break")
        {
            _brain = brain;
            var list = UIFactory.Rect("Rows", Window).Stretch(60, 60, 110, 130);
            UIFactory.VLayout(list.gameObject, 12, new RectOffset(0, 0, 0, 0));

            var st = SaveSystem.Settings;
            SliderRow(list, "Music", () => st.musicVolume, v => st.musicVolume = v);
            SliderRow(list, "Nature sounds", () => st.ambienceVolume, v => st.ambienceVolume = v);
            SliderRow(list, "Effects", () => st.sfxVolume, v => st.sfxVolume = v);
            SliderRow(list, "Mei's voice", () => st.voiceVolume, v => st.voiceVolume = v);
            OptionRow(list, "Mei speaks aloud", () => st.speakReplies ? "On" : "Subtitles only", () => st.speakReplies = !st.speakReplies);
            OptionRow(list, "Mei's voice", () => VoiceLabel(st.voiceName), () =>
            {
                st.voiceName = st.voiceName == "en_US-kristin-medium" ? "en_US-ljspeech-medium" : "en_US-kristin-medium";
            });
            OptionRow(list, "Talking speed", () => st.voiceSpeed < 0.95f ? "Relaxed" : st.voiceSpeed > 1.05f ? "Brisk" : "Normal", () =>
            {
                st.voiceSpeed = st.voiceSpeed < 0.95f ? 1f : st.voiceSpeed < 1.05f ? 1.12f : 0.9f;
            });
            OptionRow(list, "Talking", () => st.handsFree ? "Hands-free (beta)" : "Hold V to talk", () => st.handsFree = !st.handsFree);
            OptionRow(list, "Mei starts chats", () => st.companionChatter ? "Sometimes" : "Only when asked", () => st.companionChatter = !st.companionChatter);
            OptionRow(list, "Language", () => st.language == LanguageMode.English ? "English" : "Mandarin practice (beta)", () =>
            {
                st.language = st.language == LanguageMode.English ? LanguageMode.MandarinPractice : LanguageMode.English;
            });
            OptionRow(list, "Day length", () => $"{Mathf.RoundToInt(st.realSecondsPerHour * 24f / 60f)} minutes", () =>
            {
                st.realSecondsPerHour = st.realSecondsPerHour <= 30f ? 60f : st.realSecondsPerHour <= 60f ? 120f : 30f;
                if (DayNightCycle.Instance != null) DayNightCycle.Instance.RealSecondsPerHour = st.realSecondsPerHour;
            });

            var buttons = UIFactory.Rect("Buttons", Window).Anchor(new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 40), new Vector2(860, 70));
            var hl = UIFactory.HLayout(buttons.gameObject, 20, new RectOffset(0, 0, 0, 0));
            hl.childAlignment = TextAnchor.MiddleCenter;
            hl.childForceExpandWidth = true;
            UIFactory.Button(buttons, "Keep fishing", Close, UITheme.Teal).GetComponent<RectTransform>().SetLayout(64, 260);
            UIFactory.Button(buttons, "New chat", () => { _brain?.ClearConversation(); GameEvents.Toast("Mei's conversation was reset."); Close(); }, UITheme.Orange)
                .GetComponent<RectTransform>().SetLayout(64, 220);
            UIFactory.Button(buttons, "Quit", Quit, UITheme.Red).GetComponent<RectTransform>().SetLayout(64, 180);
        }

        private static string VoiceLabel(string voice) => voice switch
        {
            "en_US-ljspeech-medium" => "Linda (LJ)",
            _ => "Kristin",
        };

        private RectTransform Row(Transform parent, string label)
        {
            var row = UIFactory.Rect("Row_" + label, parent);
            row.SetLayout(60);
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
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }

    /// <summary>Scrollable transcript of the conversation (handy for language practice later).</summary>
    public class ChatPanel
    {
        private readonly RectTransform _root;
        private readonly RectTransform _content;
        private readonly ScrollRect _scroll;
        private readonly CompanionBrain _brain;
        private int _shown;

        public bool IsOpen { get; private set; }

        public ChatPanel(RectTransform canvas, CompanionBrain brain)
        {
            _brain = brain;
            var panel = UIFactory.Panel(canvas, "ChatLog");
            _root = panel.rectTransform;
            _root.anchorMin = new Vector2(1, 0.18f);
            _root.anchorMax = new Vector2(1, 0.86f);
            _root.pivot = new Vector2(1, 0.5f);
            _root.sizeDelta = new Vector2(560, 0);
            _root.anchoredPosition = new Vector2(-34, 0);

            var title = UIFactory.Text(_root, "Title", "Chat with Mei", 34, UITheme.Ink, TextAlignmentOptions.TopLeft, title: true);
            title.rectTransform.Anchor(new Vector2(0, 1), new Vector2(0, 1), new Vector2(26, -16), new Vector2(400, 46));
            var hint = UIFactory.Text(_root, "Hint", "[C] close", 20, UITheme.InkSoft, TextAlignmentOptions.TopRight);
            hint.rectTransform.Anchor(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-26, -24), new Vector2(160, 30));

            var viewport = UIFactory.Rect("Viewport", _root).Stretch(20, 20, 72, 20);
            viewport.gameObject.AddComponent<RectMask2D>();
            var vpImg = viewport.gameObject.AddComponent<Image>();
            vpImg.color = new Color(1, 1, 1, 0.01f);
            _content = UIFactory.Rect("Content", viewport);
            _content.anchorMin = new Vector2(0, 1);
            _content.anchorMax = new Vector2(1, 1);
            _content.pivot = new Vector2(0.5f, 1);
            _content.sizeDelta = Vector2.zero;
            UIFactory.VLayout(_content.gameObject, 10, new RectOffset(6, 6, 6, 6));
            var fitter = _content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _scroll = _root.gameObject.AddComponent<ScrollRect>();
            _scroll.viewport = viewport;
            _scroll.content = _content;
            _scroll.horizontal = false;
            _scroll.vertical = true;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.scrollSensitivity = 40f;

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
            AudioManager.Instance?.PlaySfx("SFX/rpg_bookFlip1", 0.4f);
        }

        public void Close()
        {
            IsOpen = false;
            _root.gameObject.SetActive(false);
        }

        public void Update()
        {
            if (_brain == null || !IsOpen) return;
            if (_shown > _brain.Log.Count)
            {
                foreach (Transform c in _content) Object.Destroy(c.gameObject);
                _shown = 0;
            }
            bool added = false;
            while (_shown < _brain.Log.Count)
            {
                var (speaker, text) = _brain.Log[_shown++];
                bool mei = speaker == CompanionPersona.Name;
                var line = UIFactory.Text(_content, "Line", $"<b><color=#{(mei ? "2C7F79" : "C7713A")}>{speaker}</color></b>  {text}", 23, UITheme.Ink, TextAlignmentOptions.TopLeft);
                line.textWrappingMode = TextWrappingModes.Normal;
                added = true;
            }
            if (added)
            {
                Canvas.ForceUpdateCanvases();
                _scroll.verticalNormalizedPosition = 0f;
            }
        }
    }
}
