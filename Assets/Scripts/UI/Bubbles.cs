using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UntitledGame.Companion;
using UntitledGame.Core;
using UntitledGame.Economy;
using UntitledGame.Language;

namespace UntitledGame.UI
{
    /// <summary>Pinyin line for display, following the player's Pinyin setting.</summary>
    public static class PinyinDisplay
    {
        private static readonly Regex Gloss = new Regex(@"([一-鿿]{1,12})\s*[\[【]", RegexOptions.Compiled);

        public static string For(string text)
        {
            if (!Pinyin.ContainsHanzi(text)) return "";
            switch (SaveSystem.Settings.pinyin)
            {
                case PinyinMode.Always:
                    return Pinyin.Annotate(text);
                case PinyinMode.NewWordsOnly:
                    var sb = new StringBuilder();
                    foreach (Match m in Gloss.Matches(text))
                    {
                        if (sb.Length > 0) sb.Append("  ·  ");
                        sb.Append(m.Groups[1].Value).Append(' ').Append(Pinyin.Of(m.Groups[1].Value));
                    }
                    return sb.ToString();
                default:
                    return "";
            }
        }
    }

    /// <summary>
    /// A speech bubble that floats above one character's head, or (docked) sits at the left edge of the
    /// screen: Mei's is docked so she never covers the middle of the screen.
    /// </summary>
    public class SpeechBubble
    {
        public readonly DialogueAgent Agent;
        public readonly bool Docked;
        private readonly RectTransform _rt;
        private readonly CanvasGroup _group;
        private readonly TextMeshProUGUI _text, _pinyin;
        private string _full = "";
        private string _pinyinLine = "";
        private float _reveal;
        private float _hideAt;

        public SpeechBubble(RectTransform parent, DialogueAgent agent, Color tagColor, bool docked = false)
        {
            Agent = agent;
            Docked = docked;
            var bubble = UIFactory.Panel(parent, "Bubble_" + agent.DisplayNameEnglish);
            _rt = bubble.rectTransform;
            _rt.anchorMin = _rt.anchorMax = new Vector2(0.5f, 0.5f);
            _rt.pivot = new Vector2(0.5f, 0f);
            _rt.sizeDelta = new Vector2(560, 120);
            bubble.raycastTarget = false;
            _group = bubble.gameObject.AddComponent<CanvasGroup>();
            _group.alpha = 0f;
            _group.blocksRaycasts = false;
            var tail = UIFactory.Image(bubble.transform, "Tail", UITheme.Cream.WithAlpha(0.96f), GameAssets.Instance.roundedRectSmall);
            tail.rectTransform.Anchor(new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f), new Vector2(0, 2), new Vector2(26, 26));
            tail.rectTransform.localRotation = Quaternion.Euler(0, 0, 45);
            tail.gameObject.SetActive(!docked);
            var name = UIFactory.Panel(bubble.transform, "NameTag", tagColor, shadow: false, small: true);
            name.rectTransform.Anchor(new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(26, 0), new Vector2(Mathf.Max(92, agent.DisplayName.Length * 30 + 30), 38));
            var nameText = UIFactory.Text(name.transform, "Name", agent.DisplayName, 24, Color.white, TextAlignmentOptions.Center, title: true);
            nameText.rectTransform.Stretch();
            _text = UIFactory.Text(bubble.transform, "Text", "", 28, UITheme.Ink, TextAlignmentOptions.TopLeft);
            _text.rectTransform.anchorMin = new Vector2(0, 1);
            _text.rectTransform.anchorMax = new Vector2(1, 1);
            _text.rectTransform.pivot = new Vector2(0.5f, 1);
            _text.rectTransform.offsetMin = new Vector2(26, 0);
            _text.rectTransform.offsetMax = new Vector2(-26, -26);
            _pinyin = UIFactory.Text(bubble.transform, "Pinyin", "", 21, UITheme.TealDark, TextAlignmentOptions.TopLeft);
            _pinyin.fontStyle = FontStyles.Italic;
            _pinyin.rectTransform.anchorMin = new Vector2(0, 0);
            _pinyin.rectTransform.anchorMax = new Vector2(1, 0);
            _pinyin.rectTransform.pivot = new Vector2(0.5f, 0);
            _pinyin.rectTransform.offsetMin = new Vector2(26, 16);
            _pinyin.rectTransform.offsetMax = new Vector2(-26, 16);

            agent.SentenceSpoken += OnSentence;
            agent.StateChanged += OnState;
        }

        public void Dispose()
        {
            Agent.SentenceSpoken -= OnSentence;
            Agent.StateChanged -= OnState;
        }

        private void OnSentence(string s)
        {
            _full = s;
            _pinyinLine = PinyinDisplay.For(s);
            _reveal = 0f;
            _hideAt = float.MaxValue;
        }

        private void OnState(CompanionState s)
        {
            if (s == CompanionState.Idle && _full.Length > 0) _hideAt = Time.time + 4.5f;
        }

        public void Update(GameUI ui)
        {
            var st = Agent.State;
            bool thinking = st == CompanionState.Thinking || st == CompanionState.Transcribing;
            bool listening = st == CompanionState.Listening;
            bool speaking = Agent.Voice != null && Agent.Voice.IsSpeaking && _full.Length > 0;
            bool near = Agent.DistanceToPlayer < 30f;
            bool show = near && (speaking || thinking || listening || Time.time < _hideAt);

            string pinyin = "";
            if (thinking && !speaking)
            {
                int dots = 1 + (int)(Time.time * 3f) % 3;
                _text.text = "<color=#8A7563>" + new string('.', dots) + "</color>";
                _text.maxVisibleCharacters = 99;
                _rt.sizeDelta = new Vector2(180, 90);
            }
            else if (listening && !speaking)
            {
                _text.text = "<i><color=#8A7563>listening...</color></i>";
                _text.maxVisibleCharacters = 99;
                _rt.sizeDelta = new Vector2(250, 90);
            }
            else if (_full.Length > 0)
            {
                _reveal += Time.deltaTime * (Pinyin.ContainsHanzi(_full) ? 9f : 28f);
                _text.text = _full;
                _text.maxVisibleCharacters = Mathf.FloorToInt(_reveal);
                pinyin = _pinyinLine;
                float maxWidth = Docked ? 470 : 580;
                Vector2 pref = _text.GetPreferredValues(_full, maxWidth - 52, 1000);
                Vector2 prefPy = pinyin.Length > 0 ? _pinyin.GetPreferredValues(pinyin, maxWidth - 52, 1000) : Vector2.zero;
                // Italic pinyin measures a little narrow; leave slack, then re-measure at the final width.
                float w = Mathf.Clamp(Mathf.Max(pref.x, prefPy.x * 1.08f) + 64, 220, maxWidth);
                float inner = w - 52;
                float textH = _text.GetPreferredValues(_full, inner, 1000).y;
                float pyH = pinyin.Length > 0 ? _pinyin.GetPreferredValues(pinyin, inner, 1000).y : 0f;
                _pinyin.rectTransform.sizeDelta = new Vector2(_pinyin.rectTransform.sizeDelta.x, pyH);
                float h = 26 + textH + (pyH > 0 ? pyH + 12 : 0) + 22;
                _rt.sizeDelta = new Vector2(w, Mathf.Clamp(h, 90, 460));
            }
            _pinyin.text = pinyin;

            _group.alpha = Mathf.MoveTowards(_group.alpha, show ? 1f : 0f, Time.deltaTime * 4f);
            Visible = false;
            if (Docked)
            {
                // Left edge, under the clock and money panels.
                Vector2 half = ui.RootSize * 0.5f;
                Target = new Vector2(-half.x + 30f + _rt.sizeDelta.x * 0.5f, half.y - 290f - _rt.sizeDelta.y);
                Visible = _group.alpha > 0.001f;
                if (Visible && _rt.anchoredPosition.y > half.y) _rt.anchoredPosition = Target;
            }
            else if (_group.alpha > 0.001f && ui.ToCanvas(Agent.transform.position + Vector3.up * 1.6f, out var p, out bool behind))
            {
                if (behind) p = new Vector2(0, -ui.RootSize.y * 0.5f + 260);
                Target = ui.ClampToScreen(p, _rt.sizeDelta, _rt.pivot);
                Visible = true;
            }
        }

        public bool Visible { get; private set; }
        public Vector2 Target;
        public Vector2 Size => _rt.sizeDelta;

        public void Apply()
        {
            if (!Visible) return;
            // Docked bubbles don't glide (they used to slide over the energy bar after waking up).
            _rt.anchoredPosition = Docked ? Target : Vector2.Lerp(_rt.anchoredPosition, Target, 1f - Mathf.Exp(-14f * Time.deltaTime));
        }

        /// <summary>Pushes overlapping bubbles apart vertically (e.g. two speakers clamped to the same screen edge).</summary>
        public static void Separate(List<SpeechBubble> bubbles, GameUI ui)
        {
            var vis = bubbles.FindAll(b => b.Visible && !b.Docked);
            vis.Sort((a, b) => b.Target.y.CompareTo(a.Target.y));
            for (int i = 0; i < vis.Count; i++)
            {
                for (int j = i + 1; j < vis.Count; j++)
                {
                    var a = vis[i];
                    var b = vis[j];
                    bool overlapX = Mathf.Abs(a.Target.x - b.Target.x) < (a.Size.x + b.Size.x) * 0.5f + 8f;
                    bool overlapY = b.Target.y + b.Size.y + 10f > a.Target.y && b.Target.y < a.Target.y + a.Size.y + 10f;
                    if (overlapX && overlapY)
                    {
                        float below = a.Target.y - b.Size.y - 14f;
                        float minY = -ui.RootSize.y * 0.5f + 100f;
                        b.Target.y = below >= minY ? below : a.Target.y + a.Size.y + 14f;
                    }
                }
            }
        }
    }

    /// <summary>Floating labels in the world: shop signs (in Chinese) and price tags on displayed goods.</summary>
    public class WorldLabels
    {
        private readonly RectTransform _parent;
        private readonly List<(RectTransform rt, TextMeshProUGUI text)> _pool = new List<(RectTransform, TextMeshProUGUI)>();

        public WorldLabels(RectTransform parent) => _parent = parent;

        private (RectTransform rt, TextMeshProUGUI text) Get(int i)
        {
            while (_pool.Count <= i)
            {
                var panel = UIFactory.Panel(_parent, "Label", UITheme.Ink.WithAlpha(0.78f), shadow: false, small: true);
                panel.raycastTarget = false;
                var rt = panel.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0f);
                var t = UIFactory.Text(rt, "Text", "", 22, UITheme.Cream, TextAlignmentOptions.Center);
                t.rectTransform.Stretch(10, 10, 4, 4);
                _pool.Add((rt, t));
            }
            return _pool[i];
        }

        public void Update(GameUI ui, Transform player)
        {
            int n = 0;
            _placed.Clear();
            if (player != null)
            {
                foreach (var k in ShopkeeperBrain.Keepers)
                {
                    float d = Vector3.Distance(player.position, k.transform.position);
                    if (d > 22f) continue;
                    string py = SaveSystem.Settings.pinyin == PinyinMode.Off ? "" : $"\n<size=17><color=#9FE3DA><i>{Pinyin.Of(k.Shop.hanzi)}</i></color></size>";
                    Show(ref n, ui, k.transform.position + Vector3.up * 2.9f, $"<size=30><b>{k.Shop.hanzi}</b></size>{py}", new Vector2(170, py.Length > 0 ? 78 : 52));
                }
                // (No price bubbles over the goods: they were clutter. Prices are in the shop window when you talk.)
            }
            for (int i = n; i < _pool.Count; i++) _pool[i].rt.gameObject.SetActive(false);
        }

        private readonly List<Rect> _placed = new List<Rect>();

        private void Show(ref int n, GameUI ui, Vector3 world, string text, Vector2 size)
        {
            if (!ui.ToCanvas(world, out var p, out bool behind) || behind) return;
            Vector2 half = ui.RootSize * 0.5f;
            if (Mathf.Abs(p.x) > half.x + size.x || p.y < -half.y - size.y || p.y > half.y) return; // off screen
            // Stack upwards instead of overlapping earlier labels.
            var r = new Rect(p.x - size.x * 0.5f, p.y, size.x, size.y);
            for (int guard = 0; guard < 6 && _placed.Exists(o => o.Overlaps(r)); guard++) r.y += size.y + 4f;
            _placed.Add(r);
            p.y = r.y;
            var (rt, t) = Get(n++);
            rt.gameObject.SetActive(true);
            rt.sizeDelta = size;
            rt.anchoredPosition = p;
            t.text = text;
        }
    }
}
