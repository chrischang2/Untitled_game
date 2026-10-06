using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UntitledGame.Core;
using UntitledGame.Economy;
using UntitledGame.Fishing;

namespace UntitledGame.UI
{
    /// <summary>
    /// 陈阿姨's scale, played when you sell fish: the fish drop onto the tray one by one, and the bar fills smoothly
    /// with their points (rarity and size, see FishSale), a tone rising in pitch as it goes. Every bar filled chimes,
    /// flashes and raises the multiplier, and the next bar starts. Purely a show: the money was already paid.
    /// </summary>
    public class SaleScaleView
    {
        /// <summary>Seconds the bar takes to fill once (bigger sales speed up a little after the fourth bar).</summary>
        public const float SecondsPerBar = 1.2f;

        private readonly RectTransform _root, _tray, _pile, _bar, _barTrack;
        private readonly CanvasGroup _group;
        private readonly TextMeshProUGUI _count, _price, _stage, _mult, _title;
        private readonly Image _barFill;
        private readonly List<Image> _icons = new List<Image>();
        private readonly AudioSource _tone;
        private static AudioClip _toneClip, _chimeClip;

        private FishSale.Quote _q;
        private float _t, _perFish, _flash, _doneAt;
        private int _landed, _fillsShown;
        private float _target, _shown, _value;

        public bool Playing => _q != null;
        /// <summary>The fills shown so far (the self-test checks the animation counts up to the quote).</summary>
        public int FillsShown => _fillsShown;
        /// <summary>The tone's pitch right now (the self-test checks it rises as the bar fills).</summary>
        public float TonePitch => _tone != null ? _tone.pitch : 1f;
        /// <summary>Whether the tone is sounding now (true even when sound effects are muted in the settings).</summary>
        public bool ToneOn => _toneWanted;
        private bool _toneWanted;
        /// <summary>How full the current bar looks (0..1).</summary>
        public float BarShown => _q == null ? 0f : FishSale.FillFraction(_shown);

        public SaleScaleView(RectTransform parent)
        {
            var panel = UIFactory.Panel(parent, "SaleScale", UITheme.Cream.WithAlpha(0.97f));
            _root = panel.rectTransform.Anchor(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-250, 30), new Vector2(620, 420));
            _group = panel.gameObject.AddComponent<CanvasGroup>();
            _group.blocksRaycasts = false;

            _title = UIFactory.Text(_root, "Title", "Auntie Chen's scale", 30, UITheme.Ink, TextAlignmentOptions.Top, title: true);
            _title.rectTransform.Anchor(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -16), new Vector2(560, 44));

            // The scale: a tray on a post.
            var post = UIFactory.Image(_root, "Post", UITheme.InkSoft, GameAssets.Instance.roundedRectSmall);
            post.rectTransform.Anchor(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -30), new Vector2(18, 80));
            var foot = UIFactory.Image(_root, "Foot", UITheme.InkSoft, GameAssets.Instance.roundedRectSmall);
            foot.rectTransform.Anchor(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -72), new Vector2(160, 16));
            var tray = UIFactory.Image(_root, "Tray", UITheme.Hex("#B9C3C9"), GameAssets.Instance.roundedRectSmall);
            _tray = tray.rectTransform.Anchor(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 12), new Vector2(320, 16));
            _pile = UIFactory.Rect("Pile", _root).Anchor(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 20), new Vector2(320, 200));

            _count = UIFactory.Text(_root, "Count", "", 34, UITheme.Ink, TextAlignmentOptions.Left, title: true);
            _count.rectTransform.Anchor(new Vector2(0, 1), new Vector2(0, 1), new Vector2(30, -66), new Vector2(260, 50));
            _price = UIFactory.Text(_root, "Price", "", 40, UITheme.TealDark, TextAlignmentOptions.Right, title: true);
            _price.rectTransform.Anchor(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-30, -66), new Vector2(260, 50));

            // The bar.
            var track = UIFactory.Image(_root, "BarTrack", UITheme.Hex("#CDBBA0"), GameAssets.Instance.roundedRectSmall);
            _barTrack = track.rectTransform.Anchor(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-40, 58), new Vector2(440, 30));
            _barFill = UIFactory.Image(track.transform, "Fill", UITheme.Orange, GameAssets.Instance.roundedRectSmall);
            _bar = _barFill.rectTransform;
            _bar.anchorMin = Vector2.zero;
            _bar.anchorMax = new Vector2(0f, 1f);
            _bar.offsetMin = _bar.offsetMax = Vector2.zero;
            _mult = UIFactory.Text(_root, "Mult", "", 34, UITheme.Orange, TextAlignmentOptions.Center, title: true);
            _mult.rectTransform.Anchor(new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-62, 58), new Vector2(110, 44));
            _stage = UIFactory.Text(_root, "Stage", "", 18, UITheme.InkSoft, TextAlignmentOptions.Center);
            _stage.rectTransform.Anchor(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 22), new Vector2(580, 28));

            // The rising tone: a soft looping hum whose pitch follows the bar.
            _tone = _root.gameObject.AddComponent<AudioSource>();
            _tone.playOnAwake = false;
            _tone.loop = true;
            _tone.spatialBlend = 0f;
            _tone.clip = _toneClip ??= MakeTone();
            _chimeClip ??= MakeChime();

            _root.gameObject.SetActive(false);
            FishSale.Weighed += Play;
        }

        public void Dispose() => FishSale.Weighed -= Play;

        public void Play(FishSale.Quote q)
        {
            if (q == null || q.fish.Count == 0) return;
            _q = q;
            _t = 0f;
            _landed = 0;
            _fillsShown = 0;
            _shown = _value = 0f;
            _target = q.points; // the bar heads for the whole sale from the start, so it fills in one smooth run
            _flash = 0f;
            _doneAt = -1f;
            // The fish land over about three seconds however many there are; the bar follows at its own pace.
            _perFish = Mathf.Clamp(3f / q.fish.Count, 0.08f, 0.45f);
            foreach (var i in _icons) i.gameObject.SetActive(false);
            _root.gameObject.SetActive(true);
            _root.SetAsLastSibling();
            _group.alpha = 1f;
            _tone.volume = 0f;
            _tone.pitch = 1f;
            _toneWanted = false;
            Refresh();
        }

        public void Update()
        {
            if (_q == null) return;
            float dt = Time.unscaledDeltaTime;
            _t += dt;
            _flash = Mathf.Max(0f, _flash - dt * 2.5f);

            // Fish land one after another on the tray.
            while (_landed < _q.fish.Count && _t >= (_landed + 1) * _perFish)
            {
                _landed++;
                AudioManager.Instance?.PlaySfx("SFX/rpg_dropLeather", 0.22f);
            }

            // The bar fills smoothly towards the points landed: one bar per SecondsPerBar.
            if (_shown < _target)
            {
                float speedUp = 1f + 0.35f * Mathf.Max(0, _fillsShown - 3);
                _shown = Mathf.Min(_target, _shown + FishSale.BarCapacity(_fillsShown) / SecondsPerBar * speedUp * dt);
                int fills = FishSale.Fills(_shown);
                if (fills > _fillsShown)
                {
                    _fillsShown = fills;
                    _flash = 1f;
                    Chime(fills);
                }
            }
            _value = _target > 0f ? _q.baseValue * Mathf.Clamp01(_shown / _target) : _q.baseValue;
            bool filling = _shown < _target - 0.0001f || _landed < _q.fish.Count;
            UpdateTone(filling, dt);
            AnimateIcons();
            Refresh();

            if (!filling && _doneAt < 0f) _doneAt = _t;
            if (_doneAt >= 0f && _t > _doneAt + 2.6f) _group.alpha = Mathf.MoveTowards(_group.alpha, 0f, dt * 1.6f);
            if (_doneAt >= 0f && _t > _doneAt + 3.4f)
            {
                _tone.Stop();
                _root.gameObject.SetActive(false);
                _q = null;
            }
        }

        private void UpdateTone(bool filling, float dt)
        {
            _toneWanted = filling && _shown < _target;
            float vol = _toneWanted ? 0.22f * SaveSystem.Settings.sfxVolume : 0f;
            _tone.volume = Mathf.MoveTowards(_tone.volume, vol, dt * 2.5f);
            if (_tone.volume > 0.001f && !_tone.isPlaying) _tone.Play();
            if (_tone.volume <= 0.001f && _tone.isPlaying) _tone.Stop();
            // An octave up across each bar; every new bar starts two semitones higher than the last.
            float semis = FishSale.FillFraction(_shown) * 12f + Mathf.Min(_fillsShown, 6) * 2f;
            _tone.pitch = Mathf.Pow(2f, semis / 12f);
        }

        private void Chime(int fills)
        {
            AudioManager.Instance?.PlayClip(_chimeClip, 0.55f, 0f);
            AudioManager.Instance?.PlaySfx("SFX/rpg_handleCoins", 0.4f);
        }

        private void AnimateIcons()
        {
            int shown = Mathf.Min(_q.fish.Count, 40);
            for (int i = 0; i < shown; i++)
            {
                var icon = Icon(i);
                float start = i * _perFish * ((float)_q.fish.Count / shown);
                float k = Mathf.Clamp01((_t - start) / Mathf.Max(0.1f, _perFish));
                icon.gameObject.SetActive(_t >= start);
                if (_t < start) continue;
                var f = _q.fish[Mathf.Min(_q.fish.Count - 1, i * _q.fish.Count / shown)];
                icon.color = f.golden ? UITheme.Hex("#E8B83A") : f.species.body;
                // A little pile: alternate left and right, rising row by row.
                int row = i / 8, col = i % 8;
                float x = (col - 3.5f) * 34f + (row % 2) * 17f;
                float restY = 22f + row * 20f; // on top of the tray (the pile is centred just above it)
                float y = Mathf.Lerp(restY + 170f, restY, 1f - (1f - k) * (1f - k));
                float bounce = k >= 1f ? Mathf.Max(0f, Mathf.Sin(Mathf.Clamp01((_t - start - _perFish) * 6f) * Mathf.PI) * 6f) : 0f;
                icon.rectTransform.anchoredPosition = new Vector2(x, y + bounce);
                icon.rectTransform.localRotation = Quaternion.Euler(0, 0, (col % 2 == 0 ? 8f : -8f) * k);
                float size = Mathf.Lerp(42f, 70f, Mathf.InverseLerp(0.02f, 20f, f.kg));
                icon.rectTransform.sizeDelta = new Vector2(size, size);
            }
            // The tray dips a little as the pile grows.
            _tray.anchoredPosition = new Vector2(0, 12f - Mathf.Min(10f, _landed * 0.6f));
        }

        private Image Icon(int i)
        {
            while (_icons.Count <= i)
            {
                var img = UIFactory.Image(_pile, "Fish", Color.white, GameAssets.Instance.fishIcon, sliced: false);
                img.raycastTarget = false;
                _icons.Add(img);
            }
            return _icons[i];
        }

        private void Refresh()
        {
            float mult = 1f + _fillsShown * _q.perFill;
            bool done = _landed >= _q.fish.Count && _shown >= _target - 0.0001f;
            int price = done ? _q.total : Mathf.RoundToInt(_value * (1f + _q.friendshipPct / 100f) * mult);
            _count.text = $"{_landed} fish";
            _price.text = $"¥{price}";
            _bar.anchorMax = new Vector2(FishSale.FillFraction(_shown), 1f);
            _barFill.color = Color.Lerp(UITheme.Orange, UITheme.Hex("#FFE07A"), _flash);
            _barTrack.localScale = Vector3.one * (1f + 0.08f * _flash);
            _mult.text = $"x{mult:0.00}";
            _mult.rectTransform.localScale = Vector3.one * (1f + 0.35f * _flash);
            var (n, rarity) = FishSale.BarRecipe(_fillsShown);
            float frac = FishSale.FillFraction(_shown);
            _stage.text = $"bar {_fillsShown + 1}: <b>{Mathf.FloorToInt(frac * 100f)}%</b>  ·  full at ~{n} {FishDatabase.RarityLabel(rarity).ToLower()} fish  ·  +{_q.perFill * 100f:0}% a bar" +
                          (_q.friendshipPct > 0 ? $"   ·   friendship +{_q.friendshipPct}%" : "");
        }

        // ---------------------------------------------------------------- generated sounds

        /// <summary>A soft hum (A4 with a little second harmonic and shimmer), one second, loops seamlessly.</summary>
        private static AudioClip MakeTone()
        {
            const int rate = 44100;
            var data = new float[rate];
            for (int i = 0; i < rate; i++)
            {
                float t = i / (float)rate;
                float s = Mathf.Sin(2f * Mathf.PI * 440f * t) + 0.25f * Mathf.Sin(2f * Mathf.PI * 880f * t) + 0.08f * Mathf.Sin(2f * Mathf.PI * 1320f * t);
                float shimmer = 0.85f + 0.15f * Mathf.Sin(2f * Mathf.PI * 6f * t);
                data[i] = s * shimmer * 0.45f;
            }
            var clip = AudioClip.Create("ScaleTone", rate, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>A little bell (a major triad that rings out), for each bar filled.</summary>
        private static AudioClip MakeChime()
        {
            const int rate = 44100;
            int n = (int)(rate * 0.9f);
            var data = new float[n];
            float[] freqs = { 1046.5f, 1318.5f, 1568f };
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                float env = Mathf.Exp(-t * 5f) * Mathf.Clamp01(t * 200f);
                float s = 0f;
                for (int k = 0; k < freqs.Length; k++) s += Mathf.Sin(2f * Mathf.PI * freqs[k] * t) * (k == 0 ? 1f : 0.6f) * Mathf.Exp(-t * k * 1.5f);
                data[i] = s * env * 0.32f;
            }
            var clip = AudioClip.Create("ScaleChime", n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
