using System.Collections.Generic;
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

        private TextMeshProUGUI _clock, _clockSub, _money, _bucket;
        private Image _sunIcon;
        private TextMeshProUGUI _aiText;
        private Image[] _aiDots;
        private TextMeshProUGUI _hint;
        private RectTransform _hintPanel;

        private readonly List<SpeechBubble> _bubbles = new List<SpeechBubble>();
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

        public void ToggleJournal() => _journal.Toggle();
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
            FishingController.FishCaught -= OnFishCaught;
            ShopkeeperBrain.TransactionDone -= OnTransaction;
            VocabNotebook.WordLearned -= OnWordLearned;
            VocabNotebook.WordUsed -= OnWordUsed;
            if (mei != null) mei.PlayerSaid -= OnPlayerSaid;
            foreach (var b in _bubbles) b.Dispose();
            _bag?.Dispose();
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
            _money.rectTransform.Stretch(22, 150, 4, 4);
            _bucket = UIFactory.Text(wallet.transform, "Bucket", "Bucket 0/8", 21, UITheme.InkSoft, TextAlignmentOptions.MidlineRight);
            _bucket.rectTransform.Stretch(120, 20, 4, 4);
        }

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
            if (mei != null) _bubbles.Add(new SpeechBubble(layer, mei, UITheme.Teal));
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
            _power.sizeDelta = new Vector2(220, 36);
            var track = UIFactory.Image(p.transform, "Track", UITheme.CreamDark, GameAssets.Instance.roundedRectSmall);
            track.rectTransform.Stretch(8, 8, 8, 8);
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

            var t = UIFactory.Panel(_hud, "Tension");
            _tension = t.rectTransform.Anchor(new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 110), new Vector2(720, 120));
            _tensionLabel = UIFactory.Text(t.transform, "Label", "Reel!", 32, UITheme.Ink, TextAlignmentOptions.Left, title: true);
            _tensionLabel.rectTransform.Anchor(new Vector2(0, 1), new Vector2(0, 1), new Vector2(26, -10), new Vector2(420, 44));
            _tensionDistance = UIFactory.Text(t.transform, "Distance", "10.0 m", 26, UITheme.InkSoft, TextAlignmentOptions.Right);
            _tensionDistance.rectTransform.Anchor(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-26, -14), new Vector2(240, 40));
            var tt = UIFactory.Image(t.transform, "Track", UITheme.CreamDark, GameAssets.Instance.roundedRectSmall);
            tt.rectTransform.Anchor(new Vector2(0, 0), new Vector2(0, 0), new Vector2(26, 22), new Vector2(668, 34));
            var danger = UIFactory.Image(tt.transform, "Danger", UITheme.Red.WithAlpha(0.25f), GameAssets.Instance.roundedRectSmall);
            danger.rectTransform.anchorMin = new Vector2(0.8f, 0);
            danger.rectTransform.anchorMax = Vector2.one;
            danger.rectTransform.offsetMin = danger.rectTransform.offsetMax = Vector2.zero;
            _tensionFill = UIFactory.Image(tt.transform, "Fill", UITheme.Green, GameAssets.Instance.roundedRectSmall);
            _tensionFill.rectTransform.anchorMin = Vector2.zero;
            _tensionFill.rectTransform.anchorMax = new Vector2(0, 1);
            _tensionFill.rectTransform.pivot = new Vector2(0, 0.5f);
            _tensionFill.rectTransform.offsetMin = _tensionFill.rectTransform.offsetMax = Vector2.zero;
            _tensionFish = UIFactory.Image(tt.transform, "Fish", UITheme.Ink, GameAssets.Instance.fishIcon, sliced: false);
            _tensionFish.rectTransform.Anchor(new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(56, 56));
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
            _offerText.rectTransform.Stretch(26, 330, 8, 8);
            var yes = UIFactory.Button(offer.transform, "Accept [Y]", () => AnswerOffer(true), UITheme.Teal, 22);
            yes.GetComponent<RectTransform>().Anchor(new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-170, 0), new Vector2(150, 56));
            var no = UIFactory.Button(offer.transform, "No [X]", () => AnswerOffer(false), UITheme.InkSoft, 22);
            no.GetComponent<RectTransform>().Anchor(new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-18, 0), new Vector2(140, 56));
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
            var title = UIFactory.Text(bg.transform, "Title", "Willow Lake  <size=70>柳湖</size>", 120, UITheme.Cream, TextAlignmentOptions.Center, title: true);
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

        private void OnTransaction(ShopkeeperBrain k, string english, string chinese) => ShowToast(english, 3.5f);

        private void OnWordLearned(VocabEntry v) => ShowToast($"New word in your notebook [N]: <b>{v.hanzi}</b> {v.pinyin} — {v.meaning}", 4.5f);

        private void OnWordUsed(VocabEntry v)
        {
            ShowToast($"Nice! You used <b>{v.hanzi}</b> ({v.pinyin})!", 3f);
            AudioManager.Instance?.PlaySfx("SFX/ui_confirmation_002", 0.4f, 0f);
        }

        public void ShowToast(string message, float seconds)
        {
            var panel = UIFactory.Panel(_toastRoot, "Toast", UITheme.Ink.WithAlpha(0.85f), shadow: false, small: true);
            var t = UIFactory.Text(panel.transform, "Text", message, 24, UITheme.Cream, TextAlignmentOptions.Center);
            t.rectTransform.Stretch(22, 22, 10, 10);
            float width = Mathf.Clamp(t.GetPreferredValues(message, 1200, 40).x + 50, 260, 1000);
            panel.rectTransform.SetLayout(54, width);
            var g = panel.gameObject.AddComponent<CanvasGroup>();
            _toasts.Add((g, Time.time + seconds));
            while (_toasts.Count > 3)
            {
                Destroy(_toasts[0].group.gameObject);
                _toasts.RemoveAt(0);
            }
        }

        private void AnswerOffer(bool yes)
        {
            if (_offerKeeper == null) return;
            _offerKeeper.AnswerOffer(yes);
            AudioManager.Instance?.PlaySfx(yes ? "SFX/ui_confirmation_002" : "SFX/ui_close_002", 0.4f);
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
            _typeTarget = voiceChat != null ? voiceChat.CurrentTarget : mei;
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
            UpdateToasts();
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
                if (Input.GetKeyDown(KeyCode.Tab) && mei != null)
                {
                    _typeTarget = _typeTarget == (DialogueAgent)mei ? voiceChat.CurrentTarget : mei;
                    UpdateTypeLabel();
                    _typeField.ActivateInputField();
                }
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

            if (Input.GetKeyDown(KeyCode.J)) { _bag.Close(); _notebook.Close(); _journal.Toggle(); }
            if (Input.GetKeyDown(KeyCode.I)) { _journal.Close(); _notebook.Close(); _bag.Toggle(); }
            if (Input.GetKeyDown(KeyCode.N)) { _journal.Close(); _bag.Close(); _notebook.Toggle(); }
            if (_journal.IsOpen || _bag.IsOpen || _notebook.IsOpen) return;

            if (Input.GetKeyDown(KeyCode.C)) _chat.Toggle();
            if (Input.GetKeyDown(KeyCode.T) || Input.GetKeyDown(KeyCode.Return)) OpenTyping();
            if (Input.GetKeyDown(KeyCode.H)) HudHidden = !HudHidden;
            if (_offer.gameObject.activeSelf)
            {
                if (Input.GetKeyDown(KeyCode.Y)) AnswerOffer(true);
                if (Input.GetKeyDown(KeyCode.X)) AnswerOffer(false);
            }
            _hud.gameObject.SetActive(!HudHidden);
        }

        private void UpdateClock()
        {
            var dn = DayNightCycle.Instance;
            if (dn == null) return;
            _clock.text = dn.ClockText;
            string weather = Weather.Instance != null && Weather.Instance.IsRaining ? "rainy" : dn.PhaseDescription;
            _clockSub.text = $"Day {dn.Day} · {weather}";
            _sunIcon.color = dn.Darkness > 0.5f ? UITheme.Hex("#BFD3F2") : (dn.Phase == DayPhase.Evening || dn.Phase == DayPhase.Dawn ? UITheme.Orange : UITheme.Yellow);
            _money.text = $"¥{Inventory.Money}";
            _bucket.text = $"Bucket {Inventory.BucketCount}/{Inventory.BucketCapacity}";
            _bucket.color = Inventory.BucketFull ? UITheme.Red : UITheme.InkSoft;
        }

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
            var target = voiceChat != null ? voiceChat.CurrentTarget : mei;
            string who = target != null && target != (DialogueAgent)mei ? target.DisplayName : "Mei";
            string talk = SaveSystem.Settings.handsFree
                ? $"Just talk (to {who})   <b>[T]</b> Type"
                : who == "Mei" ? "<b>[V]</b> Talk to Mei   <b>[T]</b> Type" : $"<b>[V]</b> Talk to {who}   <b>[B]</b> Ask Mei   <b>[T]</b> Type";
            string text = fishing == null ? talk : fishing.State switch
            {
                FishingState.Idle => $"<b>[Hold LMB]</b> Cast   {talk}   <b>[I]</b> Bag   <b>[N]</b> Words   <b>[J]</b> Journal   <b>[Esc]</b> Menu",
                FishingState.Charging => "Release to cast! Aim with the camera (hold <b>RMB</b> to look around)",
                FishingState.Casting => "Wheee...",
                FishingState.Waiting => $"Watch the bobber... click when it dives!   <b>[E]</b> Reel in   {talk}",
                FishingState.Bite => "<b>CLICK NOW!</b>",
                FishingState.Reeling => "Hold <b>LMB</b> to reel · let go when the fish pulls!",
                FishingState.Landing => "Nice catch! Click to put it in your bucket",
                _ => "",
            };
            _hint.text = text;
        }

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
            float minY = -half.y + size.y * pivot.y + 100, maxY = half.y - size.y * (1 - pivot.y) - 140;
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
            }

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
                float t = fishing.Tension;
                _tensionFill.rectTransform.anchorMax = new Vector2(t, 1);
                _tensionFill.color = t < 0.55f ? UITheme.Green : t < 0.8f ? UITheme.Yellow : UITheme.Red;
                _tensionFish.rectTransform.anchorMin = _tensionFish.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(t), 0.5f);
                _tensionFish.rectTransform.localRotation = Quaternion.Euler(0, 0, fishing.FishPulling ? Mathf.Sin(Time.time * 30f) * 12f : 0f);
                _tensionLabel.text = fishing.FishPulling ? "<color=#E0604E>It's pulling! Ease off!</color>" : "Reel it in!";
                _tensionDistance.text = $"{fishing.FishDistance:0.0} m";
                _tension.localScale = Vector3.one * (fishing.FishPulling ? 1f + 0.01f * Mathf.Sin(Time.time * 40f) : 1f);
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
            if (offer)
            {
                var o = _offerKeeper.PendingOffer;
                _offerText.text = $"<b>{_offerKeeper.DisplayName}</b> <size=20><color=#8A7563>({_offerKeeper.DisplayNameEnglish})</color></size>\n{o.english}  <size=20><color=#8A7563>· you have ¥{Inventory.Money}</color></size>";
            }

            var cur = interaction != null ? interaction.Current : null;
            bool prompt = cur != null && !offer && (fishing == null || fishing.State == FishingState.Idle);
            _prompt.gameObject.SetActive(prompt);
            if (prompt)
            {
                _promptText.text = cur.Prompt;
                _prompt.sizeDelta = new Vector2(Mathf.Clamp(_promptText.GetPreferredValues(cur.Prompt, 900, 40).x + 50, 260, 900), 50);
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
