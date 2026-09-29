using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UntitledGame.Companion;
using UntitledGame.Core;
using UntitledGame.Environment;
using UntitledGame.Fishing;
using UntitledGame.GenAI;

namespace UntitledGame.UI
{
    /// <summary>
    /// Builds and runs the whole HUD from code: clock, AI status, control hints, Mei's speech bubble,
    /// push-to-talk meter, fishing meters, catch card, toasts, and the journal / chat / settings panels.
    /// </summary>
    public class GameUI : MonoBehaviour
    {
        [SerializeField] private CompanionBrain brain;
        [SerializeField] private CompanionVoice voice;
        [SerializeField] private Transform meiAnchor;
        [SerializeField] private Transform playerAnchor;
        [SerializeField] private FishingController fishing;
        [SerializeField] private VoiceChatController voiceChat;

        private RectTransform _root;
        private RectTransform _hud;
        private Camera _cam;

        // HUD
        private TextMeshProUGUI _clock, _clockSub;
        private Image _sunIcon;
        private TextMeshProUGUI _aiText;
        private Image[] _aiDots;
        private TextMeshProUGUI _hint;

        // Speech bubbles
        private RectTransform _bubble;
        private CanvasGroup _bubbleGroup;
        private TextMeshProUGUI _bubbleText, _bubbleName;
        private string _bubbleFull = "";
        private float _bubbleReveal;
        private float _bubbleHideAt;
        private RectTransform _playerBubble;
        private CanvasGroup _playerBubbleGroup;
        private TextMeshProUGUI _playerBubbleText;
        private float _playerBubbleHideAt;

        // Mic
        private RectTransform _mic;
        private Image _micLevel, _micDot;
        private TextMeshProUGUI _micText;

        // Fishing
        private RectTransform _power;
        private Image _powerFill;
        private RectTransform _bite;
        private RectTransform _tension;
        private Image _tensionFill, _tensionFish;
        private TextMeshProUGUI _tensionLabel, _tensionDistance;

        // Typing
        private RectTransform _typeBar;
        private TMP_InputField _typeField;

        // Toasts
        private RectTransform _toastRoot;
        private readonly List<(CanvasGroup group, float until)> _toasts = new List<(CanvasGroup, float)>();

        private int _typingClosedFrame = -1;
        private float _typingOpenedAt;

        // Intro
        private CanvasGroup _intro;
        private float _introUntil;

        private CatchCard _card;
        private JournalPanel _journal;
        private SettingsPanel _settings;
        private ChatPanel _chat;

        public bool HudHidden { get; private set; }

        public void ToggleJournal() => _journal.Toggle();
        public void ToggleSettings() => _settings.Toggle();
        public void ToggleChatLog() => _chat.Toggle();

        public void Configure(CompanionBrain b, CompanionVoice v, Transform mei, Transform player, FishingController f, VoiceChatController vc)
        {
            brain = b;
            voice = v;
            meiAnchor = mei;
            playerAnchor = player;
            fishing = f;
            voiceChat = vc;
        }

        private void Start()
        {
            _cam = Camera.main;
            if (FindFirstObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
                es.transform.SetParent(transform, false);
            }
            Build();

            GameEvents.ToastRequested += ShowToast;
            if (brain != null)
            {
                brain.SentenceSpoken += OnMeiSentence;
                brain.PlayerSaid += OnPlayerSaid;
                brain.StateChanged += OnBrainState;
            }
            FishingController.FishCaught += OnFishCaught;
        }

        private void OnDestroy()
        {
            GameEvents.ToastRequested -= ShowToast;
            if (brain != null)
            {
                brain.SentenceSpoken -= OnMeiSentence;
                brain.PlayerSaid -= OnPlayerSaid;
                brain.StateChanged -= OnBrainState;
            }
            FishingController.FishCaught -= OnFishCaught;
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
            BuildClock();
            BuildAiStatus();
            BuildHint();
            BuildBubbles();
            BuildMic();
            BuildFishing();
            BuildTypeBar();
            _toastRoot = UIFactory.Rect("Toasts", _hud).Anchor(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -40), new Vector2(900, 400));
            var tl = UIFactory.VLayout(_toastRoot.gameObject, 10, new RectOffset(0, 0, 0, 0));
            tl.childAlignment = TextAnchor.UpperCenter;
            tl.childForceExpandWidth = false;

            _card = new CatchCard(_root);
            _journal = new JournalPanel(_root);
            _settings = new SettingsPanel(_root, brain);
            _chat = new ChatPanel(_root, brain);
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
            panel.rectTransform.Anchor(new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 28), new Vector2(1100, 54));
            panel.raycastTarget = false;
            _hint = UIFactory.Text(panel.transform, "Text", "", 23, UITheme.Cream, TextAlignmentOptions.Center);
            _hint.rectTransform.Stretch(16, 16, 4, 4);
        }

        private void BuildBubbles()
        {
            var bubble = UIFactory.Panel(_hud, "MeiBubble");
            _bubble = bubble.rectTransform;
            _bubble.anchorMin = _bubble.anchorMax = new Vector2(0.5f, 0.5f);
            _bubble.pivot = new Vector2(0.5f, 0f);
            _bubble.sizeDelta = new Vector2(560, 120);
            bubble.raycastTarget = false;
            _bubbleGroup = bubble.gameObject.AddComponent<CanvasGroup>();
            _bubbleGroup.alpha = 0f;
            _bubbleGroup.blocksRaycasts = false;
            var tail = UIFactory.Image(bubble.transform, "Tail", UITheme.Cream.WithAlpha(0.96f), GameAssets.Instance.roundedRectSmall);
            tail.rectTransform.Anchor(new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f), new Vector2(0, 2), new Vector2(26, 26));
            tail.rectTransform.localRotation = Quaternion.Euler(0, 0, 45);
            var name = UIFactory.Panel(bubble.transform, "NameTag", UITheme.Teal, shadow: false, small: true);
            name.rectTransform.Anchor(new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(26, 0), new Vector2(92, 38));
            _bubbleName = UIFactory.Text(name.transform, "Name", "Mei", 24, Color.white, TextAlignmentOptions.Center, title: true);
            _bubbleName.rectTransform.Stretch();
            _bubbleText = UIFactory.Text(bubble.transform, "Text", "", 27, UITheme.Ink, TextAlignmentOptions.TopLeft);
            _bubbleText.rectTransform.Stretch(26, 26, 26, 18);

            // What the player said: a subtitle above the hint bar (never covers Mei's bubble).
            var pb = UIFactory.Panel(_hud, "PlayerBubble", UITheme.Teal.WithAlpha(0.95f), small: true);
            _playerBubble = pb.rectTransform;
            _playerBubble.anchorMin = _playerBubble.anchorMax = new Vector2(0.5f, 0f);
            _playerBubble.pivot = new Vector2(0.5f, 0f);
            _playerBubble.sizeDelta = new Vector2(460, 70);
            pb.raycastTarget = false;
            _playerBubbleGroup = pb.gameObject.AddComponent<CanvasGroup>();
            _playerBubbleGroup.alpha = 0f;
            _playerBubbleGroup.blocksRaycasts = false;
            _playerBubbleText = UIFactory.Text(pb.transform, "Text", "", 24, Color.white, TextAlignmentOptions.Center);
            _playerBubbleText.rectTransform.Stretch(18, 18, 10, 10);
        }

        private void BuildMic()
        {
            var panel = UIFactory.Panel(_hud, "Mic", small: true);
            _mic = panel.rectTransform.Anchor(new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 104), new Vector2(460, 78));
            _micDot = UIFactory.Image(panel.transform, "Dot", UITheme.Red, GameAssets.Instance.circle, sliced: false);
            _micDot.rectTransform.Anchor(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(22, 0), new Vector2(34, 34));
            _micText = UIFactory.Text(panel.transform, "Text", "Listening... release V to send", 22, UITheme.Ink);
            _micText.rectTransform.Anchor(new Vector2(0, 1), new Vector2(0, 1), new Vector2(72, -8), new Vector2(370, 34));
            var track = UIFactory.Image(panel.transform, "Track", UITheme.CreamDark, GameAssets.Instance.roundedRectSmall);
            track.rectTransform.Anchor(new Vector2(0, 0), new Vector2(0, 0), new Vector2(72, 14), new Vector2(360, 16));
            _micLevel = UIFactory.Image(track.transform, "Level", UITheme.Teal, GameAssets.Instance.roundedRectSmall);
            _micLevel.rectTransform.anchorMin = Vector2.zero;
            _micLevel.rectTransform.anchorMax = new Vector2(0, 1);
            _micLevel.rectTransform.pivot = new Vector2(0, 0.5f);
            _micLevel.rectTransform.offsetMin = _micLevel.rectTransform.offsetMax = Vector2.zero;
            _mic.gameObject.SetActive(false);
        }

        private void BuildFishing()
        {
            // Cast power (follows player on screen).
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

            // Bite alert.
            var b = UIFactory.Panel(_hud, "Bite", UITheme.Orange, small: true);
            _bite = b.rectTransform;
            _bite.anchorMin = _bite.anchorMax = new Vector2(0.5f, 0.5f);
            _bite.pivot = new Vector2(0.5f, 0f);
            _bite.sizeDelta = new Vector2(220, 74);
            var bt = UIFactory.Text(b.transform, "Text", "! CLICK !", 40, Color.white, TextAlignmentOptions.Center, title: true);
            bt.rectTransform.Stretch();
            _bite.gameObject.SetActive(false);

            // Tension meter.
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

        private void BuildTypeBar()
        {
            var panel = UIFactory.Panel(_hud, "TypeBar", small: true);
            _typeBar = panel.rectTransform.Anchor(new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 100), new Vector2(820, 76));
            var label = UIFactory.Text(panel.transform, "Label", "Say:", 26, UITheme.Ink, TextAlignmentOptions.MidlineLeft, title: true);
            label.rectTransform.Anchor(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(22, 0), new Vector2(70, 50));
            _typeField = UIFactory.InputField(panel.transform, "Type to Mei and press Enter (Esc to cancel)");
            _typeField.GetComponent<RectTransform>().Stretch(96, 14, 12, 12);
            _typeField.onSubmit.AddListener(SubmitTyped);
            _typeBar.gameObject.SetActive(false);
        }

        private void BuildIntro()
        {
            var bg = UIFactory.Image(_root, "Intro", new Color(0.08f, 0.1f, 0.16f, 0.55f));
            bg.rectTransform.Stretch();
            _intro = bg.gameObject.AddComponent<CanvasGroup>();
            _intro.blocksRaycasts = false;
            var title = UIFactory.Text(bg.transform, "Title", "Willow Lake", 120, UITheme.Cream, TextAlignmentOptions.Center, title: true);
            title.rectTransform.Anchor(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 70), new Vector2(1400, 160));
            var sub = UIFactory.Text(bg.transform, "Sub", "a cozy fishing trip with Mei\n<size=26><color=#E8D8C0>hold <b>V</b> to talk · hold the mouse to cast</color></size>", 38, UITheme.Cream, TextAlignmentOptions.Center);
            sub.rectTransform.Anchor(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -40), new Vector2(1400, 120));
            _introUntil = Time.time + 4.5f;
        }

        // ------------------------------------------------------------------ events

        private void OnMeiSentence(string sentence)
        {
            _bubbleFull = sentence;
            _bubbleReveal = 0f;
            _bubbleText.text = sentence;
            _bubbleText.maxVisibleCharacters = 0;
            _bubbleHideAt = float.MaxValue;
        }

        private void OnPlayerSaid(string text)
        {
            _playerBubbleText.text = "<b>You:</b> “" + text + "”";
            _playerBubbleHideAt = Time.time + Mathf.Clamp(2.5f + text.Length * 0.05f, 3f, 7f);
            Vector2 pref = _playerBubbleText.GetPreferredValues(_playerBubbleText.text, 840, 400);
            _playerBubble.sizeDelta = new Vector2(Mathf.Clamp(pref.x + 50, 220, 900), Mathf.Clamp(pref.y + 26, 60, 160));
        }

        private void OnBrainState(CompanionState s)
        {
            if (s == CompanionState.Idle && _bubbleFull.Length > 0) _bubbleHideAt = Time.time + 4f;
        }

        private void OnFishCaught(CatchResult r) => _card.Show(r);

        public void ShowToast(string message, float seconds)
        {
            var panel = UIFactory.Panel(_toastRoot, "Toast", UITheme.Ink.WithAlpha(0.85f), shadow: false, small: true);
            var t = UIFactory.Text(panel.transform, "Text", message, 24, UITheme.Cream, TextAlignmentOptions.Center);
            t.rectTransform.Stretch(22, 22, 10, 10);
            float width = Mathf.Clamp(t.GetPreferredValues(message, 1200, 40).x + 50, 260, 880);
            panel.rectTransform.SetLayout(54, width);
            var g = panel.gameObject.AddComponent<CanvasGroup>();
            _toasts.Add((g, Time.time + seconds));
            while (_toasts.Count > 3)
            {
                Destroy(_toasts[0].group.gameObject);
                _toasts.RemoveAt(0);
            }
        }

        private void SubmitTyped(string text)
        {
            CloseTyping();
            if (!string.IsNullOrWhiteSpace(text) && brain != null) brain.SendPlayerMessage(text);
        }

        private void OpenTyping()
        {
            if (Time.frameCount == _typingClosedFrame) return; // the Enter that just sent a message
            _typeBar.gameObject.SetActive(true);
            _typeField.text = "";
            InputGate.TextEntryActive = true;
            _typingOpenedAt = Time.unscaledTime;
            StartCoroutine(FocusTypingNextFrame());
        }

        private System.Collections.IEnumerator FocusTypingNextFrame()
        {
            // Wait a frame so the T/Enter key that opened the bar isn't typed into it.
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
            UpdateBubbles();
            UpdateMic();
            UpdateFishing();
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
                bool lostFocus = !_typeField.isFocused && Time.unscaledTime - _typingOpenedAt > 0.4f;
                if (Input.GetKeyDown(KeyCode.Escape) || lostFocus) CloseTyping();
                return;
            }
            if (Time.frameCount == _typingClosedFrame) return;

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (_journal.IsOpen) _journal.Close();
                else if (_chat.IsOpen) _chat.Close();
                else _settings.Toggle();
                return;
            }
            if (_settings.IsOpen) return;

            if (Input.GetKeyDown(KeyCode.J) || Input.GetKeyDown(KeyCode.Tab)) _journal.Toggle();
            if (_journal.IsOpen) return;
            if (Input.GetKeyDown(KeyCode.C)) _chat.Toggle();
            if (Input.GetKeyDown(KeyCode.T) || Input.GetKeyDown(KeyCode.Return)) OpenTyping();
            if (Input.GetKeyDown(KeyCode.H)) HudHidden = !HudHidden;
            _hud.gameObject.SetActive(!HudHidden);
        }

        private void UpdateClock()
        {
            var dn = DayNightCycle.Instance;
            if (dn == null) return;
            _clock.text = dn.ClockText;
            string weather = Weather.Instance != null && Weather.Instance.IsRaining ? "rainy" : dn.PhaseDescription;
            int today = CatchJournal.Session.Count;
            _clockSub.text = today > 0 ? $"Day {dn.Day} · {weather} · {today} caught" : $"Day {dn.Day} · {weather}";
            _sunIcon.color = dn.Darkness > 0.5f ? UITheme.Hex("#BFD3F2") : (dn.Phase == DayPhase.Evening || dn.Phase == DayPhase.Dawn ? UITheme.Orange : UITheme.Yellow);
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
            else if (brain == null) text = "";
            else text = brain.State switch
            {
                CompanionState.Listening => "listening...",
                CompanionState.Transcribing => "hmm?",
                CompanionState.Thinking => "thinking...",
                CompanionState.Speaking => "talking",
                _ => s.SttStatus != ServiceStatus.Ready ? "press T to type" : SaveSystem.Settings.handsFree ? "listening for you" : "hold V to talk",
            };
            _aiText.text = text;
        }

        private void UpdateHint()
        {
            string ptt = SaveSystem.Settings.handsFree ? "Just talk   <b>[T]</b> Type" : "<b>[V]</b> Talk   <b>[T]</b> Type";
            string text = fishing == null ? ptt : fishing.State switch
            {
                FishingState.Idle => $"<b>[Hold LMB]</b> Cast   {ptt}   <b>[J]</b> Journal   <b>[C]</b> Chat log   <b>[Esc]</b> Menu",
                FishingState.Charging => "Release to cast! Aim with the camera (hold <b>RMB</b> to look around)",
                FishingState.Casting => "Wheee...",
                FishingState.Waiting => $"Watch the bobber... click when it dives!   <b>[E]</b> Reel in   {ptt}",
                FishingState.Bite => "<b>CLICK NOW!</b>",
                FishingState.Reeling => "Hold <b>LMB</b> to reel · let go when the fish pulls!",
                FishingState.Landing => "Nice catch! Click to put it in your bucket",
                _ => "",
            };
            _hint.text = text;
        }

        private bool ToCanvas(Vector3 world, out Vector2 local, out bool behind)
        {
            local = Vector2.zero;
            behind = false;
            if (_cam == null) return false;
            Vector3 sp = _cam.WorldToScreenPoint(world);
            behind = sp.z < 0f;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_root, sp, null, out local);
            return true;
        }

        private Vector2 ClampToScreen(Vector2 local, Vector2 size, Vector2 pivot)
        {
            Vector2 half = _root.rect.size * 0.5f;
            float minX = -half.x + size.x * pivot.x + 20, maxX = half.x - size.x * (1 - pivot.x) - 20;
            float minY = -half.y + size.y * pivot.y + 100, maxY = half.y - size.y * (1 - pivot.y) - 140;
            return new Vector2(Mathf.Clamp(local.x, minX, maxX), Mathf.Clamp(local.y, minY, maxY));
        }

        private void UpdateBubbles()
        {
            // Mei.
            bool thinking = brain != null && (brain.State == CompanionState.Thinking || brain.State == CompanionState.Transcribing);
            bool listening = brain != null && brain.State == CompanionState.Listening;
            bool speaking = voice != null && voice.IsSpeaking && _bubbleFull.Length > 0;
            bool show = speaking || thinking || listening || Time.time < _bubbleHideAt;

            if (thinking && !speaking)
            {
                int dots = 1 + (int)(Time.time * 3f) % 3;
                _bubbleText.text = "<color=#8A7563>" + new string('.', dots) + "</color>";
                _bubbleText.maxVisibleCharacters = 99;
                _bubble.sizeDelta = new Vector2(180, 90);
            }
            else if (listening && !speaking)
            {
                _bubbleText.text = "<i><color=#8A7563>listening...</color></i>";
                _bubbleText.maxVisibleCharacters = 99;
                _bubble.sizeDelta = new Vector2(250, 90);
            }
            else if (_bubbleFull.Length > 0)
            {
                _bubbleReveal += Time.deltaTime * 28f;
                _bubbleText.text = _bubbleFull;
                _bubbleText.maxVisibleCharacters = Mathf.FloorToInt(_bubbleReveal);
                Vector2 pref = _bubbleText.GetPreferredValues(_bubbleFull, 520, 1000);
                _bubble.sizeDelta = new Vector2(Mathf.Clamp(pref.x + 60, 220, 580), Mathf.Clamp(pref.y + 50, 90, 320));
            }

            _bubbleGroup.alpha = Mathf.MoveTowards(_bubbleGroup.alpha, show ? 1f : 0f, Time.deltaTime * 4f);
            if (meiAnchor != null && ToCanvas(meiAnchor.position + Vector3.up * 1.55f, out var mp, out bool behind))
            {
                if (behind) mp = new Vector2(0, -_root.rect.height * 0.5f + 240);
                _bubble.anchoredPosition = Vector2.Lerp(_bubble.anchoredPosition, ClampToScreen(mp, _bubble.sizeDelta, _bubble.pivot), 1f - Mathf.Exp(-14f * Time.deltaTime));
            }

            // Player.
            bool pshow = Time.time < _playerBubbleHideAt;
            _playerBubbleGroup.alpha = Mathf.MoveTowards(_playerBubbleGroup.alpha, pshow ? 1f : 0f, Time.deltaTime * 4f);
            float baseY = _tension.gameObject.activeSelf ? 250f : _mic.gameObject.activeSelf ? 196f : 100f;
            _playerBubble.anchoredPosition = new Vector2(0f, baseY);
        }

        private void UpdateMic()
        {
            var mic = voiceChat != null ? voiceChat.Mic : null;
            bool on = mic != null && (mic.IsRecording || voiceChat.IsTranscribing);
            _mic.gameObject.SetActive(on && !_tension.gameObject.activeSelf);
            if (!on) return;
            if (mic.IsRecording)
            {
                _micText.text = SaveSystem.Settings.handsFree ? "Listening..." : "Listening... release <b>V</b> to send";
                _micDot.color = UITheme.Red.WithAlpha(0.6f + 0.4f * Mathf.Sin(Time.time * 8f));
                _micLevel.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(mic.Level), 1);
            }
            else
            {
                _micText.text = "Mei is working out what you said...";
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
