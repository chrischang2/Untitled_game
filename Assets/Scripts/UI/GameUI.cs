using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UntitledGame.Companion;
using UntitledGame.Core;
using UntitledGame.Economy;
using UntitledGame.Environment;
using UntitledGame.Fishing;
using UntitledGame.GenAI;
using UntitledGame.Home;
using UntitledGame.Language;
using UntitledGame.Progression;

namespace UntitledGame.UI
{
    /// <summary>
    /// Builds and runs the whole HUD from code: clock + money, AI status, control hints, speech bubbles
    /// for every character (with dictionary pinyin), push-to-talk meter, fishing meters, shop offer card,
    /// interaction prompt, world labels, toasts and the menus (journal, bag, word notebook, chat log, settings).
    /// Menus are in English; everything characters say is Mandarin.
    /// </summary>
    public class GameUI : MonoBehaviour
    {
        [SerializeField] private CompanionBrain mei;
        [SerializeField] private Transform playerAnchor;
        [SerializeField] private FishingController fishing;
        [SerializeField] private VoiceChatController voiceChat;
        [SerializeField] private InteractionController interaction;

        private RectTransform _root;
        private RectTransform _hud;
        private Camera _cam;

        private TextMeshProUGUI _clock, _clockSub, _money, _powerText, _castLabelText, _fishGridLabel;
        private RectTransform _fishGrid, _castLabel;
        private Image _fishGridPanel;
        private readonly List<(Image cell, Image icon, TextMeshProUGUI count)> _fishCells = new List<(Image, Image, TextMeshProUGUI)>();
        private string _fishGridKey;
        /// <summary>The HUD's fish grid: one cell per bag slot (the self-test reads it).</summary>
        public int FishGridCells => _fishCells.Count(c => c.cell.gameObject.activeSelf);
        public int FishGridFilled => _fishCells.Count(c => c.cell.gameObject.activeSelf && c.icon.gameObject.activeSelf);
        public string PowerText => _powerText != null ? _powerText.text : "";
        public string FishGridText => _fishGridLabel != null ? _fishGridLabel.text : "";
        public string CastLabel => _castLabel != null && _castLabel.gameObject.activeSelf ? _castLabelText.text : "";
        private Image _sunIcon;
        private TextMeshProUGUI _aiText;
        private Image[] _aiDots;
        private TextMeshProUGUI _hint;
        private RectTransform _hintPanel;

        private readonly List<SpeechBubble> _bubbles = new List<SpeechBubble>();
        private ShopPanel _shop;
        public ShopPanel Shop => _shop;
        private WorldLabels _labels;
        private RectTransform _playerBubble;
        private CanvasGroup _playerBubbleGroup;
        private TextMeshProUGUI _playerBubbleText;
        private float _playerBubbleHideAt;

        private RectTransform _mic;
        private Image _micLevel, _micDot;
        private TextMeshProUGUI _micText;

        private RectTransform _power, _bite, _tension;
        private Image _powerFill, _tensionFill, _tensionFish;
        private TextMeshProUGUI _tensionLabel, _tensionDistance;
        private RectTransform _reelTrack;
        private Image _reelMeter;

        private RectTransform _prompt;
        private TextMeshProUGUI _promptText;

        private RectTransform _offer;
        private TextMeshProUGUI _offerText;
        private ShopkeeperBrain _offerKeeper;

        private RectTransform _typeBar;
        private TMP_InputField _typeField;
        private TextMeshProUGUI _typeLabel;
        private DialogueAgent _typeTarget;
        private int _typingClosedFrame = -1;
        private float _typingOpenedAt;

        private RectTransform _toastRoot;
        private readonly List<(CanvasGroup group, float until)> _toasts = new List<(CanvasGroup, float)>();

        private CanvasGroup _intro;
        private float _introUntil;

        private CatchCard _card;
        private JournalPanel _journal;
        private SettingsPanel _settings;
        private SavesPanel _saves;
        private ChatPanel _chat;
        private InventoryPanel _bag;
        private NotebookPanel _notebook;

        public bool HudHidden { get; private set; }
        public Vector2 RootSize => _root.rect.size;

        public void ToggleJournal(bool people = false)
        {
            _journal.ShowPeople(people);
            _journal.Toggle();
        }
        public void OpenPhrasebook()
        {
            _journal.ShowPhrasebook();
            if (!_journal.IsOpen) _journal.Toggle();
        }
        public void CloseJournal() => _journal.Close();
        public void ToggleSettings() => _settings.Toggle();
        public void OpenSaves() => _saves.Open();
        public void CloseSaves() => _saves.Close();
        public void ToggleChatLog() => _chat.Toggle();
        public void ToggleBag() => _bag.Toggle();
        public void ToggleNotebook() => _notebook.Toggle();

        public void Configure(CompanionBrain b, Transform player, FishingController f, VoiceChatController vc, InteractionController ic)
        {
            mei = b;
            playerAnchor = player;
            fishing = f;
            voiceChat = vc;
            interaction = ic;
        }

        private void Start()
        {
            _cam = Camera.main;
            Pinyin.Init();
            if (FindFirstObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
                es.transform.SetParent(transform, false);
            }
            Build();

            GameEvents.ToastRequested += ShowToast;
            GameEvents.BannerRequested += ShowBanner;
            Progression.Affinity.LevelChanged += OnFriendshipLevel;
            FishingController.FishCaught += OnFishCaught;
            ShopkeeperBrain.TransactionDone += OnTransaction;
            VocabNotebook.WordLearned += OnWordLearned;
            VocabNotebook.WordUsed += OnWordUsed;
            if (mei != null) mei.PlayerSaid += OnPlayerSaid;
            foreach (var k in ShopkeeperBrain.Keepers) k.PlayerSaid += OnPlayerSaid;
        }

        private void OnDestroy()
        {
            GameEvents.ToastRequested -= ShowToast;
            GameEvents.BannerRequested -= ShowBanner;
            _scale?.Dispose();
            Progression.Affinity.LevelChanged -= OnFriendshipLevel;
            FishingController.FishCaught -= OnFishCaught;
            ShopkeeperBrain.TransactionDone -= OnTransaction;
            VocabNotebook.WordLearned -= OnWordLearned;
            VocabNotebook.WordUsed -= OnWordUsed;
            if (mei != null) mei.PlayerSaid -= OnPlayerSaid;
            foreach (var b in _bubbles) b.Dispose();
            _bag?.Dispose();
            _shop?.Dispose();
            _notebook?.Dispose();
            _chat?.Dispose();
        }

        // ------------------------------------------------------------------ build

        private void Build()
        {
            var canvasGo = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.6f;
            _root = (RectTransform)canvasGo.transform;

            _hud = UIFactory.Rect("HUD", _root).Stretch();
            _labels = new WorldLabels(UIFactory.Rect("WorldLabels", _hud).Stretch());
            BuildClock();
            BuildAiStatus();
            BuildHint();
            BuildBubbles();
            BuildMic();
            BuildFishing();
            BuildPromptAndOffer();
            BuildTypeBar();
            _toastRoot = UIFactory.Rect("Toasts", _hud).Anchor(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -40), new Vector2(900, 400));
            var tl = UIFactory.VLayout(_toastRoot.gameObject, 10, new RectOffset(0, 0, 0, 0));
            tl.childAlignment = TextAnchor.UpperCenter;
            tl.childForceExpandWidth = false;
            BuildBanner();
            _scale = new SaleScaleView(_hud);

            _card = new CatchCard(_root);
            _journal = new JournalPanel(_root);
            _bag = new InventoryPanel(_root);
            _notebook = new NotebookPanel(_root);
            _saves = new SavesPanel(_root);
            _settings = new SettingsPanel(_root, mei, () => _saves.Open());
            _chat = new ChatPanel(_root);
            BuildIntro();
        }

        private void BuildClock()
        {
            var panel = UIFactory.Panel(_hud, "Clock", small: true);
            panel.rectTransform.Anchor(new Vector2(0, 1), new Vector2(0, 1), new Vector2(34, -30), new Vector2(300, 92));
            _sunIcon = UIFactory.Image(panel.transform, "Sun", UITheme.Yellow, GameAssets.Instance.circle, sliced: false);
            _sunIcon.rectTransform.Anchor(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(20, 0), new Vector2(48, 48));
            _clock = UIFactory.Text(panel.transform, "Time", "7:30 AM", 36, UITheme.Ink, TextAlignmentOptions.Left, title: true);
            _clock.rectTransform.Anchor(new Vector2(0, 1), new Vector2(0, 1), new Vector2(84, -8), new Vector2(210, 44));
            _clockSub = UIFactory.Text(panel.transform, "Sub", "Day 1 · clear", 21, UITheme.InkSoft);
            _clockSub.rectTransform.Anchor(new Vector2(0, 1), new Vector2(0, 1), new Vector2(86, -50), new Vector2(210, 30));

            var wallet = UIFactory.Panel(_hud, "Wallet", small: true);
            wallet.rectTransform.Anchor(new Vector2(0, 1), new Vector2(0, 1), new Vector2(34, -134), new Vector2(300, 56));
            _money = UIFactory.Text(wallet.transform, "Money", "¥50", 28, UITheme.Ink, TextAlignmentOptions.MidlineLeft, title: true);
            _money.rectTransform.Stretch(22, 20, 4, 4);

            // Energy: fishing uses it; at zero you pass out.
            var energy = UIFactory.Panel(_hud, "Energy", small: true);
            energy.rectTransform.Anchor(new Vector2(0, 1), new Vector2(0, 1), new Vector2(34, -196), new Vector2(300, 46));
            var track = UIFactory.Image(energy.transform, "Track", UITheme.CreamDark, GameAssets.Instance.roundedRectSmall);
            track.rectTransform.Stretch(16, 16, 12, 12);
            _energyFill = UIFactory.Image(track.transform, "Fill", UITheme.Green, GameAssets.Instance.roundedRectSmall);
            _energyFill.rectTransform.anchorMin = Vector2.zero;
            _energyFill.rectTransform.anchorMax = Vector2.one;
            _energyFill.rectTransform.offsetMin = _energyFill.rectTransform.offsetMax = Vector2.zero;
            _energyText = UIFactory.Text(energy.transform, "Text", "Energy", 19, UITheme.Ink, TextAlignmentOptions.Center);
            _energyText.rectTransform.Stretch(16, 16, 4, 4);

            // The bag: a grid of slots under the energy bar, each showing its fish and how many.
            _fishGridPanel = UIFactory.Panel(_hud, "FishGrid", small: true);
            _fishGrid = _fishGridPanel.rectTransform.Anchor(new Vector2(0, 1), new Vector2(0, 1), new Vector2(34, -250), new Vector2(300, 60));
            _fishGridLabel = UIFactory.Text(_fishGrid, "Slots", "", 20, UITheme.InkSoft, TextAlignmentOptions.MidlineRight, title: true);
            _fishGridLabel.rectTransform.Anchor(new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-10, 0), new Vector2(64, 30));
            _fishGridLabel.rectTransform.pivot = new Vector2(1, 0.5f);
        }

        private const int FishGridColumns = 6;
        private const float FishCell = 44f, FishGap = 4f, FishPad = 8f;

        /// <summary>Rebuilds the fish grid when the bag changes (one cell per slot; empty slots are faint).</summary>
        private void UpdateFishGrid()
        {
            var slots = Inventory.Slots();
            int cap = Mathf.Max(Inventory.SlotCapacity, slots.Count);
            string key = cap + "|" + Inventory.BucketFull + "|" + string.Join(",", slots.Select(s => (s.speciesId ?? s.itemId) + ":" + s.count));
            if (key == _fishGridKey) return;
            _fishGridKey = key;
            while (_fishCells.Count < cap)
            {
                var cell = UIFactory.Image(_fishGrid, "Slot", UITheme.CreamDark, GameAssets.Instance.roundedRectSmall);
                cell.raycastTarget = false;
                var icon = UIFactory.Image(cell.transform, "Fish", Color.white, GameAssets.Instance.fishIcon, sliced: false);
                icon.raycastTarget = false;
                icon.rectTransform.Stretch(2, 2, 4, 4);
                var count = UIFactory.Text(cell.transform, "Count", "", 19, UITheme.Ink, TextAlignmentOptions.BottomRight, title: true);
                count.rectTransform.Stretch(2, 4, 0, 0);
                _fishCells.Add((cell, icon, count));
            }
            int rows = Mathf.Max(1, (cap + FishGridColumns - 1) / FishGridColumns), cols = Mathf.Min(cap, FishGridColumns);
            // Exactly as wide as the slots (plus the "used/total" label), so empty panel space never looks like a slot.
            _fishGrid.sizeDelta = new Vector2(FishPad * 2 + 4f + cols * FishCell + (cols - 1) * FishGap + 66f, FishPad * 2 + rows * FishCell + (rows - 1) * FishGap);
            _fishGridLabel.text = $"{slots.Count}/{Inventory.SlotCapacity}";
            _fishGridLabel.color = Inventory.BucketFull ? UITheme.Red : UITheme.InkSoft;
            for (int i = 0; i < _fishCells.Count; i++)
            {
                var (cell, icon, count) = _fishCells[i];
                cell.gameObject.SetActive(i < cap);
                if (i >= cap) continue;
                int r = i / FishGridColumns, c = i % FishGridColumns;
                cell.rectTransform.Anchor(new Vector2(0, 1), new Vector2(0, 1),
                    new Vector2(FishPad + 2f + c * (FishCell + FishGap), -FishPad - r * (FishCell + FishGap)), new Vector2(FishCell, FishCell));
                bool used = i < slots.Count;
                cell.color = used ? UITheme.Hex("#D8C8AD") : UITheme.Hex("#E6DCCB");
                icon.gameObject.SetActive(used);
                count.text = used && slots[i].count > 1 ? slots[i].count.ToString() : "";
                if (!used) continue;
                var species = slots[i].speciesId != null ? FishDatabase.Get(slots[i].speciesId) : null;
                icon.color = species != null ? species.body : UITheme.InkSoft;
            }
            _fishGridPanel.color = Inventory.BucketFull ? UITheme.Hex("#F3C9BE") : UITheme.Cream;
        }

        private Image _energyFill;
        private TextMeshProUGUI _energyText;

        private void BuildAiStatus()
        {
            var panel = UIFactory.Panel(_hud, "AIStatus", small: true);
            panel.rectTransform.Anchor(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-34, -30), new Vector2(380, 92));
            var title = UIFactory.Text(panel.transform, "Title", "Mei", 30, UITheme.Ink, TextAlignmentOptions.Left, title: true);
            title.rectTransform.Anchor(new Vector2(0, 1), new Vector2(0, 1), new Vector2(22, -8), new Vector2(120, 40));
            _aiDots = new Image[3];
            string[] labels = { "think", "hear", "voice" };
            for (int i = 0; i < 3; i++)
            {
                var dot = UIFactory.Image(panel.transform, "Dot" + i, UITheme.InkSoft, GameAssets.Instance.circle, sliced: false);
                dot.rectTransform.Anchor(new Vector2(0, 1), new Vector2(0, 1), new Vector2(110 + i * 88, -20), new Vector2(16, 16));
                _aiDots[i] = dot;
                var l = UIFactory.Text(panel.transform, "L" + i, labels[i], 19, UITheme.InkSoft);
                l.rectTransform.Anchor(new Vector2(0, 1), new Vector2(0, 1), new Vector2(132 + i * 88, -12), new Vector2(70, 30));
            }
            _aiText = UIFactory.Text(panel.transform, "State", "waking up...", 20, UITheme.InkSoft);
            _aiText.rectTransform.Anchor(new Vector2(0, 1), new Vector2(0, 1), new Vector2(22, -50), new Vector2(340, 32));
        }

        private void BuildHint()
        {
            var panel = UIFactory.Panel(_hud, "Hint", UITheme.Ink.WithAlpha(0.72f), shadow: false, small: true);
            _hintPanel = panel.rectTransform.Anchor(new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 28), new Vector2(1500, 52));
            panel.raycastTarget = false;
            _hint = UIFactory.Text(panel.transform, "Text", "", 21, UITheme.Cream, TextAlignmentOptions.Center);
            _hint.rectTransform.Stretch(16, 16, 4, 4);
        }

        private void BuildBubbles()
        {
            var layer = UIFactory.Rect("Bubbles", _hud).Stretch();
            if (mei != null) _bubbles.Add(new SpeechBubble(layer, mei, UITheme.Teal, docked: true));
            _shop = new ShopPanel(_hud);
            foreach (var k in ShopkeeperBrain.Keepers) _bubbles.Add(new SpeechBubble(layer, k, UITheme.Orange));

            var pb = UIFactory.Panel(_hud, "PlayerBubble", UITheme.Teal.WithAlpha(0.95f), small: true);
            _playerBubble = pb.rectTransform;
            _playerBubble.anchorMin = _playerBubble.anchorMax = new Vector2(0.5f, 0f);
            _playerBubble.pivot = new Vector2(0.5f, 0f);
            _playerBubble.sizeDelta = new Vector2(460, 70);
            pb.raycastTarget = false;
            _playerBubbleGroup = pb.gameObject.AddComponent<CanvasGroup>();
            _playerBubbleGroup.alpha = 0f;
            _playerBubbleGroup.blocksRaycasts = false;
            _playerBubbleText = UIFactory.Text(pb.transform, "Text", "", 25, Color.white, TextAlignmentOptions.Center);
            _playerBubbleText.rectTransform.Stretch(18, 18, 10, 10);
        }

        private void BuildMic()
        {
            var panel = UIFactory.Panel(_hud, "Mic", small: true);
            _mic = panel.rectTransform.Anchor(new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 100), new Vector2(520, 78));
            _micDot = UIFactory.Image(panel.transform, "Dot", UITheme.Red, GameAssets.Instance.circle, sliced: false);
            _micDot.rectTransform.Anchor(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(22, 0), new Vector2(34, 34));
            _micText = UIFactory.Text(panel.transform, "Text", "Listening...", 22, UITheme.Ink);
            _micText.rectTransform.Anchor(new Vector2(0, 1), new Vector2(0, 1), new Vector2(72, -8), new Vector2(430, 34));
            var track = UIFactory.Image(panel.transform, "Track", UITheme.CreamDark, GameAssets.Instance.roundedRectSmall);
            track.rectTransform.Anchor(new Vector2(0, 0), new Vector2(0, 0), new Vector2(72, 14), new Vector2(420, 16));
            _micLevel = UIFactory.Image(track.transform, "Level", UITheme.Teal, GameAssets.Instance.roundedRectSmall);
            _micLevel.rectTransform.anchorMin = Vector2.zero;
            _micLevel.rectTransform.anchorMax = new Vector2(0, 1);
            _micLevel.rectTransform.pivot = new Vector2(0, 0.5f);
            _micLevel.rectTransform.offsetMin = _micLevel.rectTransform.offsetMax = Vector2.zero;
            _mic.gameObject.SetActive(false);
        }

        private void BuildFishing()
        {
            var p = UIFactory.Panel(_hud, "Power", small: true);
            _power = p.rectTransform;
            _power.anchorMin = _power.anchorMax = new Vector2(0.5f, 0.5f);
            _power.pivot = new Vector2(0.5f, 0.5f);
            _power.sizeDelta = new Vector2(300, 40);
            var track = UIFactory.Image(p.transform, "Track", UITheme.CreamDark, GameAssets.Instance.roundedRectSmall);
            track.rectTransform.Stretch(8, 88, 9, 9);
            _powerText = UIFactory.Text(p.transform, "Distance", "", 22, UITheme.Ink, TextAlignmentOptions.MidlineRight, title: true);
            _powerText.rectTransform.Stretch(214, 12, 2, 2);

            // How far the bobber went (and how far out from the shore that is: fish need distance).
            var cl = UIFactory.Panel(_hud, "CastDistance", small: true);
            _castLabel = cl.rectTransform;
            _castLabel.anchorMin = _castLabel.anchorMax = new Vector2(0.5f, 0.5f);
            _castLabel.pivot = new Vector2(0.5f, 0f);
            _castLabel.sizeDelta = new Vector2(220, 34);
            _castLabelText = UIFactory.Text(cl.transform, "Text", "", 19, UITheme.Ink, TextAlignmentOptions.Center);
            _castLabelText.rectTransform.Stretch(8, 8, 2, 2);
            cl.raycastTarget = false;
            _castLabel.gameObject.SetActive(false);
            _powerFill = UIFactory.Image(track.transform, "Fill", UITheme.Orange, GameAssets.Instance.roundedRectSmall);
            _powerFill.rectTransform.anchorMin = Vector2.zero;
            _powerFill.rectTransform.anchorMax = new Vector2(0, 1);
            _powerFill.rectTransform.pivot = new Vector2(0, 0.5f);
            _powerFill.rectTransform.offsetMin = _powerFill.rectTransform.offsetMax = Vector2.zero;
            _power.gameObject.SetActive(false);

            var b = UIFactory.Panel(_hud, "Bite", UITheme.Orange, small: true);
            _bite = b.rectTransform;
            _bite.anchorMin = _bite.anchorMax = new Vector2(0.5f, 0.5f);
            _bite.pivot = new Vector2(0.5f, 0f);
            _bite.sizeDelta = new Vector2(220, 74);
            var bt = UIFactory.Text(b.transform, "Text", "! CLICK !", 40, Color.white, TextAlignmentOptions.Center, title: true);
            bt.rectTransform.Stretch();
            _bite.gameObject.SetActive(false);

            // Reeling minigame (Stardew-style): a vertical track with the green bar, the fish, and the catch meter beside it.
            var t = UIFactory.Panel(_hud, "Reel");
            _tension = t.rectTransform.Anchor(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(330, 10), new Vector2(170, 600));
            _tensionLabel = UIFactory.Text(t.transform, "Label", "Reel!", 28, UITheme.Ink, TextAlignmentOptions.Center, title: true);
            _tensionLabel.rectTransform.Anchor(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -10), new Vector2(160, 40));
            _tensionDistance = UIFactory.Text(t.transform, "Hint", "hold LMB", 18, UITheme.InkSoft, TextAlignmentOptions.Center);
            _tensionDistance.rectTransform.Anchor(new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 8), new Vector2(160, 28));
            var tt = UIFactory.Image(t.transform, "Track", UITheme.Hex("#5B8DB0"), GameAssets.Instance.roundedRectSmall);
            tt.rectTransform.Anchor(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-18, 0), new Vector2(72, 480));
            _reelTrack = tt.rectTransform;
            _tensionFill = UIFactory.Image(tt.transform, "Bar", UITheme.Green.WithAlpha(0.85f), GameAssets.Instance.roundedRectSmall);
            _tensionFill.rectTransform.anchorMin = new Vector2(0, 0);
            _tensionFill.rectTransform.anchorMax = new Vector2(1, 0.3f);
            _tensionFill.rectTransform.offsetMin = new Vector2(4, 0);
            _tensionFill.rectTransform.offsetMax = new Vector2(-4, 0);
            _tensionFish = UIFactory.Image(tt.transform, "Fish", UITheme.Ink, GameAssets.Instance.fishIcon, sliced: false);
            _tensionFish.rectTransform.Anchor(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(46, 46));
            var meter = UIFactory.Image(t.transform, "Meter", UITheme.CreamDark, GameAssets.Instance.roundedRectSmall);
            meter.rectTransform.Anchor(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(42, 0), new Vector2(20, 480));
            _reelMeter = UIFactory.Image(meter.transform, "Fill", UITheme.Green, GameAssets.Instance.roundedRectSmall);
            _reelMeter.rectTransform.anchorMin = Vector2.zero;
            _reelMeter.rectTransform.anchorMax = new Vector2(1, 0.3f);
            _reelMeter.rectTransform.offsetMin = new Vector2(3, 3);
            _reelMeter.rectTransform.offsetMax = new Vector2(-3, -3);
            _tension.gameObject.SetActive(false);
        }

        private void BuildPromptAndOffer()
        {
            var prompt = UIFactory.Panel(_hud, "Prompt", UITheme.Cream.WithAlpha(0.95f), small: true);
            _prompt = prompt.rectTransform.Anchor(new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 96), new Vector2(520, 50));
            _promptText = UIFactory.Text(prompt.transform, "Text", "", 23, UITheme.Ink, TextAlignmentOptions.Center);
            _promptText.rectTransform.Stretch(14, 14, 4, 4);
            _prompt.gameObject.SetActive(false);

            var offer = UIFactory.Panel(_hud, "Offer");
            _offer = offer.rectTransform.Anchor(new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 170), new Vector2(760, 96));
            _offerText = UIFactory.Text(offer.transform, "Text", "", 25, UITheme.Ink, TextAlignmentOptions.MidlineLeft);
            _offerText.rectTransform.Stretch(26, 26, 8, 8);
            // No buttons: every deal with a shopkeeper is made by speaking (要 / 不要).
            _offer.gameObject.SetActive(false);
        }

        private void BuildTypeBar()
        {
            var panel = UIFactory.Panel(_hud, "TypeBar", small: true);
            _typeBar = panel.rectTransform.Anchor(new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 100), new Vector2(900, 76));
            _typeLabel = UIFactory.Text(panel.transform, "Label", "To Mei:", 24, UITheme.Ink, TextAlignmentOptions.MidlineLeft, title: true);
            _typeLabel.rectTransform.Anchor(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(22, 0), new Vector2(150, 50));
            _typeField = UIFactory.InputField(panel.transform, "Type (Chinese or English), Enter to send · Tab: switch to Mei · Esc: cancel");
            _typeField.GetComponent<RectTransform>().Stretch(170, 14, 12, 12);
            _typeField.onSubmit.AddListener(SubmitTyped);
            _typeBar.gameObject.SetActive(false);
        }

        private void BuildIntro()
        {
            var bg = UIFactory.Image(_root, "Intro", new Color(0.08f, 0.1f, 0.16f, 0.55f));
            bg.rectTransform.Stretch();
            _intro = bg.gameObject.AddComponent<CanvasGroup>();
            _intro.blocksRaycasts = false;
            var here = UntitledGame.Environment.Regions.Here;
            var title = UIFactory.Text(bg.transform, "Title", $"{here.english}  <size=70>{here.hanzi}</size>", 120, UITheme.Cream, TextAlignmentOptions.Center, title: true);
            title.rectTransform.Anchor(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 70), new Vector2(1400, 160));
            var sub = UIFactory.Text(bg.transform, "Sub", "fish, learn Mandarin with Mei, and make friends at the market\n<size=26><color=#E8D8C0>hold <b>V</b> to talk · <b>B</b> to ask Mei · hold the mouse to cast</color></size>", 36, UITheme.Cream, TextAlignmentOptions.Center);
            sub.rectTransform.Anchor(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -40), new Vector2(1400, 120));
            _introUntil = Time.time + 4.5f;
        }

        // ------------------------------------------------------------------ events

        private void OnPlayerSaid(string text)
        {
            string py = PinyinDisplay.For(text);
            _playerBubbleText.text = "<b>You:</b> “" + text + "”" + (py.Length > 0 ? $"\n<size=20><i>{py}</i></size>" : "");
            _playerBubbleHideAt = Time.time + Mathf.Clamp(2.5f + text.Length * 0.08f, 3.5f, 8f);
            Vector2 pref = _playerBubbleText.GetPreferredValues(_playerBubbleText.text, 900, 400);
            _playerBubble.sizeDelta = new Vector2(Mathf.Clamp(pref.x + 50, 220, 960), Mathf.Clamp(pref.y + 26, 60, 200));
        }

        private void OnFishCaught(CatchResult r) => _card.Show(r);
        /// <summary>Shows the catch card (the self-test uses it to check the presentation).</summary>
        public void ShowCatchCard(CatchResult r) => _card.Show(r);

        private void OnTransaction(ShopkeeperBrain k, string english, string chinese) => ShowToast(english, 3.5f);

        private void OnWordLearned(VocabEntry v) => ShowToast($"New word in your notebook [N]: <b>{v.hanzi}</b> {v.pinyin} — {v.meaning}", 4.5f);

        private void OnWordUsed(VocabEntry v)
        {
            ShowToast($"Nice! You used <b>{v.hanzi}</b> ({v.pinyin})!", 3f);
            AudioManager.Instance?.PlaySfx("SFX/ui_confirmation_002", 0.4f, 0f);
        }

        // ------------------------------------------------------------------ banner (big results, e.g. "HSK 1 PASSED!")

        private SaleScaleView _scale;
        /// <summary>Auntie Chen's scale animation (the self-test watches it).</summary>
        public SaleScaleView Scale => _scale;
        private Image _banner;
        private TextMeshProUGUI _bannerTitle, _bannerText;
        private CanvasGroup _bannerGroup;
        private float _bannerUntil;

        private void BuildBanner()
        {
            _banner = UIFactory.Panel(_hud, "Banner", UITheme.Cream.WithAlpha(0.97f));
            _banner.rectTransform.Anchor(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-180, 120), new Vector2(980, 300));
            _bannerGroup = _banner.gameObject.AddComponent<CanvasGroup>();
            _bannerGroup.blocksRaycasts = false;
            _bannerTitle = UIFactory.Text(_banner.transform, "Title", "", 60, UITheme.Ink, TextAlignmentOptions.Center, title: true);
            _bannerTitle.rectTransform.Anchor(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -24), new Vector2(920, 80));
            _bannerText = UIFactory.Text(_banner.transform, "Text", "", 24, UITheme.Ink, TextAlignmentOptions.Top);
            _bannerText.rectTransform.Stretch(30, 30, 110, 20);
            _banner.gameObject.SetActive(false);
        }

        /// <summary>A big message in the middle of the screen for a few seconds (click or any key... it just fades).</summary>
        public void ShowBanner(string title, string details, bool good)
        {
            _bannerTitle.text = title;
            _bannerTitle.color = good ? UITheme.TealDark : UITheme.Red;
            _bannerText.text = details;
            float h = 130f + _bannerText.GetPreferredValues(details, 920, 0).y + 30f;
            _banner.rectTransform.sizeDelta = new Vector2(980, Mathf.Max(220, h));
            _banner.gameObject.SetActive(true);
            _bannerGroup.alpha = 1f;
            _bannerUntil = Time.time + 8f;
            AudioManager.Instance?.PlaySfx(good ? "SFX/rpg_handleCoins" : "SFX/ui_error_004", 0.6f);
        }

        private void OnFriendshipLevel(string shopId, int before, int now)
        {
            if (now <= before) return;
            var shop = Catalog.Shop(shopId);
            if (shop == null) return;
            var unlocked = shop.items.Select(Catalog.Get).Where(i => i != null && i.minAffinity == now && i.minHsk <= Progression.Hsk.Level).Select(i => i.english).ToList();
            string next = now < Progression.Affinity.MaxLevel
                ? $"\nNext, for {Progression.Affinity.LevelHanzi[now + 1]}: " + string.Join(", ", Progression.Affinity.Requirements(shopId, now + 1)
                    .Select(r => r.kind == "fact" ? Progression.Affinity.FactQuestions[r.factId].question : r.text))
                : "";
            ShowBanner($"{shop.keeperName} and you: {Progression.Affinity.LevelHanzi[now]}!",
                $"{shop.keeperEnglish} is now your {Progression.Affinity.LevelEnglish[now]}." +
                (unlocked.Count > 0 ? $"\nNew in the {shop.english.ToLower()}: {string.Join(", ", unlocked)}" : "") + next, true);
        }

        private void UpdateBanner()
        {
            if (_banner == null || !_banner.gameObject.activeSelf) return;
            if (Time.time > _bannerUntil) _bannerGroup.alpha = Mathf.MoveTowards(_bannerGroup.alpha, 0f, Time.deltaTime * 1.5f);
            if (_bannerGroup.alpha <= 0f) _banner.gameObject.SetActive(false);
        }

        public void ShowToast(string message, float seconds)
        {
            var panel = UIFactory.Panel(_toastRoot, "Toast", UITheme.Ink.WithAlpha(0.85f), shadow: false, small: true);
            var t = UIFactory.Text(panel.transform, "Text", message, 24, UITheme.Cream, TextAlignmentOptions.Center);
            t.rectTransform.Stretch(22, 22, 10, 10);
            float width = Mathf.Clamp(t.GetPreferredValues(message, 1200, 40).x + 50, 260, 1000);
            float height = Mathf.Max(54f, t.GetPreferredValues(message, width - 44f, 0f).y + 22f);
            panel.rectTransform.SetLayout(height, width);
            var g = panel.gameObject.AddComponent<CanvasGroup>();
            _toasts.Add((g, Time.time + seconds));
            while (_toasts.Count > 3)
            {
                Destroy(_toasts[0].group.gameObject);
                _toasts.RemoveAt(0);
            }
        }

        // ------------------------------------------------------------------ typing

        private void SubmitTyped(string text)
        {
            var target = _typeTarget;
            CloseTyping();
            if (!string.IsNullOrWhiteSpace(text) && target != null) target.HandlePlayerUtterance(text);
        }

        private void OpenTyping()
        {
            if (Time.frameCount == _typingClosedFrame) return; // the Enter that just sent a message
            _typeTarget = mei; // shopkeepers are only ever spoken to; typing is for Mei
            UpdateTypeLabel();
            _typeBar.gameObject.SetActive(true);
            _typeField.text = "";
            InputGate.TextEntryActive = true;
            _typingOpenedAt = Time.unscaledTime;
            StartCoroutine(FocusTypingNextFrame());
        }

        private void UpdateTypeLabel() => _typeLabel.text = $"To {(_typeTarget != null ? _typeTarget.DisplayName : "Mei")}:";

        private System.Collections.IEnumerator FocusTypingNextFrame()
        {
            yield return null;
            _typeField.text = "";
            _typeField.ActivateInputField();
            _typeField.Select();
        }

        private void CloseTyping()
        {
            _typingClosedFrame = Time.frameCount;
            _typeField.DeactivateInputField();
            _typeBar.gameObject.SetActive(false);
            InputGate.TextEntryActive = false;
            EventSystem.current?.SetSelectedGameObject(null);
        }

        // ------------------------------------------------------------------ update

        private void Update()
        {
            if (_cam == null) _cam = Camera.main;
            HandleHotkeys();
            UpdateBanner();
            _scale?.Update();
            UpdateClock();
            UpdateAiStatus();
            UpdateHint();
            foreach (var b in _bubbles) b.Update(this);
            SpeechBubble.Separate(_bubbles, this);
            foreach (var b in _bubbles) b.Apply();
            UpdatePlayerBubble();
            _labels.Update(this, playerAnchor);
            UpdateMic();
            UpdateFishing();
            UpdatePromptAndOffer();
            _shop?.Update(ShopConversation.Active, HudHidden);
            UpdateToasts();
            if (fishing != null && fishing.State == FishingState.Charging) _card.HideNow();
            _card.Update(fishing != null && fishing.State == FishingState.Landing);
            _chat.Update();

            if (_intro != null)
            {
                float a = Mathf.Clamp01((_introUntil - Time.time) / 1.2f);
                _intro.alpha = a;
                if (a <= 0f)
                {
                    Destroy(_intro.gameObject);
                    _intro = null;
                }
            }
        }

        private void HandleHotkeys()
        {
            if (_typeBar.gameObject.activeSelf)
            {
                bool lostFocus = !_typeField.isFocused && Time.unscaledTime - _typingOpenedAt > 0.4f;
                if (Input.GetKeyDown(KeyCode.Escape) || lostFocus) CloseTyping();
                return;
            }
            if (Time.frameCount == _typingClosedFrame || PlacementController.Active) return;

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (_saves.IsOpen) { _saves.Close(); _settings.Open(); }
                else if (_journal.IsOpen) _journal.Close();
                else if (_bag.IsOpen) _bag.Close();
                else if (_notebook.IsOpen) _notebook.Close();
                else if (_chat.IsOpen) _chat.Close();
                else _settings.Toggle();
                return;
            }
            if (_settings.IsOpen || _saves.IsOpen) return;

            if (Input.GetKeyDown(KeyCode.F5))
            {
                SaveSystem.Save();
                ChatAudit.Write("SAVE", $"quick save (F5) to slot {SaveSystem.ActiveSlot}");
                ShowToast($"Saved (slot {SaveSystem.ActiveSlot}).", 2f);
            }

            // HSK test: confirm (Y) or throw away (N) what was heard before it's graded.
            if (HskSchool.Current?.pending != null)
            {
                if (Input.GetKeyDown(KeyCode.Y)) { HskSchool.SubmitPending(); return; }
                if (Input.GetKeyDown(KeyCode.N)) { HskSchool.DiscardPending(); return; }
            }
            if (Input.GetKeyDown(KeyCode.J)) { _bag.Close(); _notebook.Close(); _journal.Toggle(); }
            if (Input.GetKeyDown(KeyCode.I)) { _journal.Close(); _notebook.Close(); _bag.Toggle(); }
            if (Input.GetKeyDown(KeyCode.N)) { _journal.Close(); _bag.Close(); _notebook.Toggle(); }
            if (_journal.IsOpen || _bag.IsOpen || _notebook.IsOpen) return;

            if (Input.GetKeyDown(KeyCode.C)) _chat.Toggle();
            if (Input.GetKeyDown(KeyCode.T) || Input.GetKeyDown(KeyCode.Return)) OpenTyping();
            if (Input.GetKeyDown(KeyCode.H)) HudHidden = !HudHidden;
            _hud.gameObject.SetActive(!HudHidden);
        }

        private void UpdateClock()
        {
            var dn = DayNightCycle.Instance;
            if (dn == null) return;
            // The HUD turns Chinese as HSK tests are passed (UiText).
            int hsk = Progression.Hsk.Level;
            _clock.text = hsk >= 1 ? UiText.ChineseClock(dn.TimeOfDay) : dn.ClockText;
            bool raining = Weather.Instance != null && Weather.Instance.IsRaining;
            string weather = raining ? UiText.Plain("rainy", "下雨", 0) : UiText.Plain(dn.PhaseDescription, PhaseHanzi(dn.Phase), 0);
            _clockSub.text = $"{UiText.Plain($"Day {dn.Day}", $"第{dn.Day}天", 0)} · {weather}";
            _sunIcon.color = dn.Darkness > 0.5f ? UITheme.Hex("#BFD3F2") : (dn.Phase == DayPhase.Evening || dn.Phase == DayPhase.Dawn ? UITheme.Orange : UITheme.Yellow);
            _money.text = hsk >= 1 ? $"{Inventory.Money}块" : $"¥{Inventory.Money}";
            UpdateFishGrid();
            float ef = Progression.Energy.Fraction;
            _energyFill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(ef), 1);
            _energyFill.color = ef > 0.4f ? UITheme.Green : ef > 0.15f ? UITheme.Yellow : UITheme.Red;
            _energyText.text = $"Energy {Progression.Energy.Current:0} / {Progression.Energy.Max:0}";
        }

        private static string PhaseHanzi(DayPhase p) => p switch
        {
            DayPhase.Dawn => "早上",
            DayPhase.Morning => "上午",
            DayPhase.Day => "下午",
            _ => "晚上",
        };

        private static Color StatusColor(ServiceStatus s) => s switch
        {
            ServiceStatus.Ready => UITheme.Green,
            ServiceStatus.Starting => UITheme.Yellow,
            ServiceStatus.Failed => UITheme.Red,
            _ => UITheme.InkSoft.WithAlpha(0.5f),
        };

        private void UpdateAiStatus()
        {
            var s = LocalAIServices.Instance;
            if (s == null) return;
            float pulse = 0.6f + 0.4f * Mathf.Sin(Time.time * 5f);
            var sts = new[] { s.LlmStatus, s.SttStatus, s.TtsStatus };
            for (int i = 0; i < 3; i++)
            {
                var c = StatusColor(sts[i]);
                if (sts[i] == ServiceStatus.Starting) c.a = pulse;
                _aiDots[i].color = c;
            }
            string text;
            if (s.Root == null) text = "offline: run the setup script";
            else if (s.LlmStatus == ServiceStatus.Starting) text = "waking up...";
            else if (s.LlmStatus == ServiceStatus.Failed) text = "sleepy (see LocalAI/logs)";
            else if (s.LlmStatus == ServiceStatus.NotInstalled) text = "no model installed";
            else if (mei == null) text = "";
            else text = mei.State switch
            {
                CompanionState.Listening => "listening...",
                CompanionState.Transcribing => "hmm?",
                CompanionState.Thinking => "thinking...",
                CompanionState.Speaking => "talking",
                _ => s.SttStatus != ServiceStatus.Ready ? "press T to type" : SaveSystem.Settings.handsFree ? "listening for you" : "hold B to ask me",
            };
            _aiText.text = text;
        }

        private void UpdateHint()
        {
            if (PlacementController.Active)
            {
                _hint.text = $"Placing <b>{PlacementController.Instance.ItemEnglish}</b>:  <b>[LMB]</b> Place   <b>[R / scroll]</b> Rotate   <b>[RMB / Esc]</b> Cancel";
                return;
            }
            var shopping = ShopConversation.Active;
            if (shopping != null)
            {
                string speak = SaveSystem.Settings.handsFree
                    ? UiText.T($"Just talk (to {shopping.DisplayName})", $"和{shopping.DisplayName}说话", 2)
                    : "<b>[Hold V]</b> " + UiText.T($"Speak to {shopping.DisplayName}", $"和{shopping.DisplayName}说话", 2);
                _hint.text = $"{speak}   <b>[Hold B]</b> {UiText.T("Ask Mei quietly", "问美", 2)}   <b>[T]</b> {UiText.T("Type to Mei", "给美写", 2)}   <b>[E]</b> {UiText.T("Leave", "再见", 1)}";
                return;
            }
            string toMei = UiText.T("Talk to Mei", "和美说话", 2), type = UiText.T("Type", "写", 2);
            string talk = SaveSystem.Settings.handsFree ? $"{UiText.T("Just talk (to Mei)", "和美说话", 2)}   <b>[T]</b> {type}" : $"<b>[V]</b> {toMei}   <b>[T]</b> {type}";
            if (Minigames.PitchPotGame.Active != null)
            {
                _hint.text = "投壶 tóuhú  ·  <b>[A]/[D]</b> aim  ·  hold <b>LMB</b>, let go to throw  ·  <b>[E]</b> stop";
                return;
            }
            string text = fishing == null ? talk : fishing.State switch
            {
                FishingState.Idle => $"<b>[Hold LMB]</b> {UiText.T("Cast", "钓鱼", 2)}   {talk}   <b>[I]</b> {UiText.T("Bag", "包", 3)}   <b>[N]</b> {UiText.T("Words", "词语", 3)}   <b>[J]</b> Journal   <b>[Esc]</b> {UiText.T("Menu", "菜单", 3)}",
                FishingState.Charging => "Release to cast!   <b>[A]/[D]</b> aim left/right (or turn the camera with <b>RMB</b>): the ring shows where it lands",
                FishingState.Casting => "Wheee...",
                FishingState.Waiting => $"Watch the bobber... click when it dives!   <b>[E]</b> Reel in   {talk}",
                FishingState.Bite => $"<b>{UiText.T("CLICK NOW!", "快！", 1)}</b>",
                FishingState.Reeling => "Hold <b>LMB</b> to lift the green bar, let go to drop it: keep the fish inside until the meter fills!",
                FishingState.Landing => UiText.T("Nice catch! Click to put it in your bucket", "太好了！点一下，放到包里", 3),
                _ => "",
            };
            _hint.text = text;
        }

        public bool CatchCardVisible => _card.Visible;
        /// <summary>The HUD layer (minigames add their panels here).</summary>
        public RectTransform HudRoot => _hud;

        public bool ToCanvas(Vector3 world, out Vector2 local, out bool behind)
        {
            local = Vector2.zero;
            behind = false;
            if (_cam == null) return false;
            Vector3 sp = _cam.WorldToScreenPoint(world);
            behind = sp.z < 0f;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_root, sp, null, out local);
            return true;
        }

        public Vector2 ClampToScreen(Vector2 local, Vector2 size, Vector2 pivot)
        {
            Vector2 half = _root.rect.size * 0.5f;
            float minX = -half.x + size.x * pivot.x + 20, maxX = half.x - size.x * (1 - pivot.x) - 20;
            // Stay above the offer card when one is showing (Mei's side-chat bubble used to hide under it).
            float bottom = _offer != null && _offer.gameObject.activeSelf ? 330f : 100f;
            float minY = -half.y + size.y * pivot.y + bottom, maxY = half.y - size.y * (1 - pivot.y) - 140;
            return new Vector2(Mathf.Clamp(local.x, minX, maxX), Mathf.Clamp(local.y, minY, maxY));
        }

        private void UpdatePlayerBubble()
        {
            bool show = Time.time < _playerBubbleHideAt;
            _playerBubbleGroup.alpha = Mathf.MoveTowards(_playerBubbleGroup.alpha, show ? 1f : 0f, Time.deltaTime * 4f);
            float baseY = _tension.gameObject.activeSelf ? 250f : _offer.gameObject.activeSelf ? 280f : _mic.gameObject.activeSelf || _prompt.gameObject.activeSelf ? 190f : 100f;
            _playerBubble.anchoredPosition = new Vector2(0f, baseY);
        }

        private void UpdateMic()
        {
            var mic = voiceChat != null ? voiceChat.Mic : null;
            bool on = mic != null && (mic.IsRecording || voiceChat.IsTranscribing);
            _mic.gameObject.SetActive(on && !_tension.gameObject.activeSelf);
            if (!on) return;
            var target = voiceChat.RecordingTarget;
            string who = target != null ? target.DisplayName : "Mei";
            if (mic.IsRecording)
            {
                _micText.text = SaveSystem.Settings.handsFree ? $"Listening... (to {who})" : $"Talking to {who}... release to send";
                _micDot.color = UITheme.Red.WithAlpha(0.6f + 0.4f * Mathf.Sin(Time.time * 8f));
                _micLevel.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(mic.Level), 1);
            }
            else
            {
                _micText.text = "Working out what you said...";
                _micDot.color = UITheme.Yellow;
                _micLevel.rectTransform.anchorMax = new Vector2(0.5f + 0.5f * Mathf.Sin(Time.time * 4f), 1);
            }
        }

        private void UpdateFishing()
        {
            if (fishing == null) return;
            var st = fishing.State;

            _power.gameObject.SetActive(st == FishingState.Charging);
            if (st == FishingState.Charging && ToCanvas(playerAnchor.position + Vector3.up * 2.1f, out var p, out _))
            {
                _power.anchoredPosition = p;
                _powerFill.rectTransform.anchorMax = new Vector2(fishing.Power, 1);
                _powerFill.color = Color.Lerp(UITheme.Yellow, UITheme.Orange, fishing.Power);
                bool perfect = fishing.ChargeIsPerfect;
                _powerText.text = perfect ? "<color=#E0604E>PERFECT</color>" : $"{fishing.ChargeDistance:0.0} m";
                _castLabel.gameObject.SetActive(true);
                _castLabelText.text = fishing.PredictedOnWater ? $"lands {fishing.PredictedShoreDistance:0} m out" : "<color=#E0604E>that's land!</color>";
                _castLabel.anchoredPosition = p + new Vector2(0f, 28f);
                if (perfect) _powerFill.color = UITheme.Hex("#E0604E");
            }

            bool showCast = st == FishingState.Waiting || st == FishingState.Bite;
            if (st != FishingState.Charging) _castLabel.gameObject.SetActive(showCast);
            if (showCast && ToCanvas(fishing.BobberWorld + Vector3.up * 0.7f, out var cp, out bool behind) && !behind)
            {
                _castLabel.anchoredPosition = cp;
                _castLabelText.text = $"{fishing.BobberDistance:0.0} m  ·  {fishing.ShoreDistance:0} m out";
            }
            else if (showCast) _castLabel.gameObject.SetActive(false);

            _bite.gameObject.SetActive(st == FishingState.Bite);
            if (st == FishingState.Bite && ToCanvas(playerAnchor.position + Vector3.up * 2.2f, out var b, out _))
            {
                _bite.anchoredPosition = b + Vector2.up * (Mathf.Abs(Mathf.Sin(Time.time * 14f)) * 14f);
                _bite.localScale = Vector3.one * (1f + 0.08f * Mathf.Sin(Time.time * 20f));
            }

            bool reeling = st == FishingState.Reeling;
            _tension.gameObject.SetActive(reeling);
            if (reeling)
            {
                _tensionFill.rectTransform.anchorMin = new Vector2(0, fishing.BarPos);
                _tensionFill.rectTransform.anchorMax = new Vector2(1, fishing.BarPos + fishing.BarSize);
                _tensionFill.color = (fishing.FishInBar ? UITheme.Green : UITheme.Hex("#A8D48A")).WithAlpha(0.9f);
                // The fish vibrates in the meter, more for lively fish (and wriggles hard when it's outside the bar).
                float v = fishing.Vibration;
                _tensionFish.rectTransform.anchorMin = _tensionFish.rectTransform.anchorMax =
                    new Vector2(0.5f + Random.Range(-v, v) * 4f, Mathf.Clamp01(fishing.FishPos + Random.Range(-v, v)));
                float wiggle = (fishing.FishInBar ? 6f : 16f) * Mathf.Min(1.6f, fishing.MoveRate);
                _tensionFish.rectTransform.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(Time.time * (24f + 12f * fishing.MoveRate)) * wiggle);
                _reelMeter.rectTransform.anchorMax = new Vector2(1, Mathf.Max(0.02f, fishing.Progress));
                _reelMeter.color = Color.Lerp(UITheme.Red, UITheme.Green, fishing.Progress);
                _tensionLabel.text = fishing.FishInBar ? "Reel!" : "<color=#E0604E>Catch it!</color>";
                _tensionDistance.text = fishing.Perfect ? "<color=#2C7F79>perfect so far</color>" : "hold LMB";
                _tension.localScale = Vector3.one * (fishing.FishInBar ? 1f : 1f + 0.01f * Mathf.Sin(Time.time * 40f));
            }
        }

        private void UpdatePromptAndOffer()
        {
            // Shop offer waiting for the player's answer.
            _offerKeeper = null;
            if (playerAnchor != null)
            {
                foreach (var k in ShopkeeperBrain.Keepers)
                {
                    if (k.PendingOffer != null && k.DistanceToPlayer < k.ServiceRadius * 1.6f)
                    {
                        _offerKeeper = k;
                        break;
                    }
                }
            }
            bool offer = _offerKeeper != null && (fishing == null || fishing.State == FishingState.Idle);
            _offer.gameObject.SetActive(offer);
            // Slide left of the shop window when it's open.
            _offer.anchoredPosition = new Vector2(_shop != null && _shop.IsOpen ? -250f : 0f, 170f);
            if (offer)
            {
                var o = _offerKeeper.PendingOffer;
                _offerText.text = $"<b>{_offerKeeper.DisplayName}</b> <size=20><color=#8A7563>({_offerKeeper.DisplayNameEnglish}) is waiting for your answer. Say it out loud: 要 / 不要</color></size>\n{o.english}  <size=20><color=#8A7563>· you have ¥{Inventory.Money}</color></size>";
            }

            var cur = interaction != null ? interaction.Current : null;
            var keeper = ShopConversation.Instance != null ? ShopConversation.Instance.Candidate : null;
            string promptText = keeper != null ? $"[E] Talk to {keeper.DisplayName} ({keeper.DisplayNameEnglish}) and see the {keeper.Shop?.english.ToLower()}" : cur?.Prompt;
            bool prompt = promptText != null && !offer && (fishing == null || fishing.State == FishingState.Idle);
            _prompt.gameObject.SetActive(prompt);
            if (prompt)
            {
                _promptText.text = promptText;
                _prompt.sizeDelta = new Vector2(Mathf.Clamp(_promptText.GetPreferredValues(promptText, 900, 40).x + 50, 260, 900), 50);
            }
        }

        private void UpdateToasts()
        {
            for (int i = _toasts.Count - 1; i >= 0; i--)
            {
                var (g, until) = _toasts[i];
                if (g == null)
                {
                    _toasts.RemoveAt(i);
                    continue;
                }
                float remaining = until - Time.time;
                g.alpha = Mathf.Clamp01(remaining / 0.4f);
                if (remaining <= 0f)
                {
                    Destroy(g.gameObject);
                    _toasts.RemoveAt(i);
                }
            }
        }
    }
}
