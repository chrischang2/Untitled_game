using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using UntitledGame.Core;

namespace UntitledGame.UI
{
    /// <summary>Colours and fonts for the cozy UI.</summary>
    public static class UITheme
    {
        public static readonly Color Cream = Hex("#FFF7EA");
        public static readonly Color CreamDark = Hex("#F3E4CC");
        public static readonly Color Ink = Hex("#4A3A30");
        public static readonly Color InkSoft = Hex("#8A7563");
        public static readonly Color Teal = Hex("#3AA79F");
        public static readonly Color TealDark = Hex("#2C7F79");
        public static readonly Color Orange = Hex("#F2994A");
        public static readonly Color Red = Hex("#E0604E");
        public static readonly Color Green = Hex("#7DBB5B");
        public static readonly Color Yellow = Hex("#F2C94C");
        public static readonly Color Shadow = new Color(0.16f, 0.1f, 0.06f, 0.35f);

        private static TMP_FontAsset _body, _title;

        public static TMP_FontAsset Body => _body != null ? _body : (_body = MakeFont("VarelaRound-Regular"));
        public static TMP_FontAsset Title => _title != null ? _title : (_title = MakeFont("LilitaOne-Regular"));

        private static TMP_FontAsset MakeFont(string resource)
        {
            var font = Resources.Load<Font>(resource);
            TMP_FontAsset fa = font != null ? TMP_FontAsset.CreateFontAsset(font) : TMP_Settings.defaultFontAsset;
            if (fa == null) return TMP_Settings.defaultFontAsset;
            fa.fallbackFontAssetTable ??= new List<TMP_FontAsset>();
            foreach (var cjk in CjkFallbacks()) fa.fallbackFontAssetTable.Add(cjk);
            return fa;
        }

        private static List<TMP_FontAsset> _cjk;

        /// <summary>System fonts with Chinese glyphs, so Mandarin (and anything else) renders.</summary>
        private static List<TMP_FontAsset> CjkFallbacks()
        {
            if (_cjk != null) return _cjk;
            _cjk = new List<TMP_FontAsset>();
            foreach (var family in new[] { "Microsoft YaHei", "Microsoft YaHei UI", "SimHei", "DengXian", "PingFang SC", "Noto Sans CJK SC" })
            {
                try
                {
                    var fa = TMP_FontAsset.CreateFontAsset(family, "Regular");
                    if (fa != null)
                    {
                        _cjk.Add(fa);
                        break;
                    }
                }
                catch (Exception)
                {
                    // Font not on this system; try the next.
                }
            }
            return _cjk;
        }

        public static Color Hex(string hex) => ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.magenta;

        public static Color WithAlpha(this Color c, float a) => new Color(c.r, c.g, c.b, a);
    }

    /// <summary>Helpers for building uGUI hierarchies from code.</summary>
    public static class UIFactory
    {
        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        public static RectTransform Anchor(this RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        public static RectTransform Stretch(this RectTransform rt, float left = 0, float right = 0, float top = 0, float bottom = 0)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
            return rt;
        }

        public static Image Image(Transform parent, string name, Color color, Sprite sprite = null, bool sliced = true)
        {
            var rt = Rect(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.type = sprite != null && sliced ? UnityEngine.UI.Image.Type.Sliced : UnityEngine.UI.Image.Type.Simple;
            img.raycastTarget = false;
            return img;
        }

        /// <summary>Rounded cream panel with a soft drop shadow.</summary>
        public static Image Panel(Transform parent, string name, Color? color = null, bool shadow = true, bool small = false)
        {
            var ga = GameAssets.Instance;
            var root = Rect(name, parent);
            if (shadow)
            {
                var sh = Image(root, "Shadow", UITheme.Shadow, ga.softShadow);
                sh.rectTransform.Stretch(-22, -22, -18, -30);
                sh.pixelsPerUnitMultiplier = 1f;
            }
            var bg = root.gameObject.AddComponent<Image>();
            bg.sprite = small ? ga.roundedRectSmall : ga.roundedRect;
            bg.type = UnityEngine.UI.Image.Type.Sliced;
            bg.color = color ?? UITheme.Cream.WithAlpha(0.96f);
            bg.raycastTarget = true;
            bg.pixelsPerUnitMultiplier = small ? 1f : 1.6f;
            return bg;
        }

        public static TextMeshProUGUI Text(Transform parent, string name, string text, float size, Color color,
            TextAlignmentOptions align = TextAlignmentOptions.Left, bool title = false)
        {
            var rt = Rect(name, parent);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.font = title ? UITheme.Title : UITheme.Body;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.raycastTarget = false;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.richText = true;
            return t;
        }

        public static Button Button(Transform parent, string label, UnityAction onClick, Color? bg = null, float fontSize = 26)
        {
            var img = Panel(parent, "Button_" + label, bg ?? UITheme.Teal, shadow: false, small: true);
            var btn = img.gameObject.AddComponent<Button>();
            var colors = btn.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.08f, 1.08f, 1.08f);
            colors.pressedColor = new Color(0.85f, 0.85f, 0.85f);
            colors.fadeDuration = 0.08f;
            btn.colors = colors;
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => AudioManager.Instance?.PlaySfx("SFX/ui_click_002", 0.4f));
            if (onClick != null) btn.onClick.AddListener(onClick);
            var t = Text(img.transform, "Label", label, fontSize, Color.white, TextAlignmentOptions.Center, title: true);
            t.rectTransform.Stretch(8, 8, 4, 4);
            return btn;
        }

        public static Slider Slider(Transform parent, float value, UnityAction<float> onChange)
        {
            var ga = GameAssets.Instance;
            var root = Rect("Slider", parent);
            var slider = root.gameObject.AddComponent<Slider>();

            var bg = Image(root, "Background", UITheme.CreamDark, ga.roundedRectSmall);
            bg.rectTransform.Stretch(0, 0, 10, 10);
            bg.raycastTarget = true;

            var fillArea = Rect("Fill Area", root).Stretch(6, 6, 10, 10);
            var fill = Image(fillArea, "Fill", UITheme.Teal, ga.roundedRectSmall);
            fill.rectTransform.Stretch();

            var handleArea = Rect("Handle Area", root).Stretch(12, 12, 0, 0);
            var handle = Image(handleArea, "Handle", Color.white, ga.circle, sliced: false);
            handle.rectTransform.sizeDelta = new Vector2(30, 0);
            handle.raycastTarget = true;
            var ring = Image(handle.transform, "Ring", UITheme.TealDark, ga.ring, sliced: false);
            ring.rectTransform.Stretch();

            slider.fillRect = fill.rectTransform;
            slider.handleRect = handle.rectTransform;
            slider.targetGraphic = handle;
            slider.direction = UnityEngine.UI.Slider.Direction.LeftToRight;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.value = value;
            if (onChange != null) slider.onValueChanged.AddListener(onChange);
            return slider;
        }

        public static TMP_InputField InputField(Transform parent, string placeholder, float fontSize = 26)
        {
            var bg = Panel(parent, "Input", Color.white, shadow: false, small: true);
            var field = bg.gameObject.AddComponent<TMP_InputField>();
            var viewport = Rect("Viewport", bg.transform).Stretch(18, 18, 8, 8);
            viewport.gameObject.AddComponent<RectMask2D>();
            var text = Text(viewport, "Text", "", fontSize, UITheme.Ink, TextAlignmentOptions.MidlineLeft);
            text.rectTransform.Stretch();
            text.textWrappingMode = TextWrappingModes.NoWrap;
            var ph = Text(viewport, "Placeholder", placeholder, fontSize, UITheme.InkSoft.WithAlpha(0.7f), TextAlignmentOptions.MidlineLeft);
            ph.rectTransform.Stretch();
            ph.fontStyle = FontStyles.Italic;
            field.textViewport = viewport;
            field.textComponent = text;
            field.placeholder = ph;
            field.fontAsset = UITheme.Body;
            field.pointSize = fontSize;
            field.caretColor = UITheme.Teal;
            field.customCaretColor = true;
            field.selectionColor = UITheme.Teal.WithAlpha(0.3f);
            field.lineType = TMP_InputField.LineType.SingleLine;
            field.characterLimit = 300;
            return field;
        }

        public static void SetLayout(this RectTransform rt, float preferredHeight, float preferredWidth = -1)
        {
            var le = rt.GetComponent<LayoutElement>() ?? rt.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = preferredHeight;
            if (preferredWidth > 0) le.preferredWidth = preferredWidth;
        }

        public static VerticalLayoutGroup VLayout(GameObject go, float spacing, RectOffset padding, bool expandWidth = true)
        {
            var v = go.AddComponent<VerticalLayoutGroup>();
            v.spacing = spacing;
            v.padding = padding;
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = expandWidth;
            v.childForceExpandHeight = false;
            return v;
        }

        public static HorizontalLayoutGroup HLayout(GameObject go, float spacing, RectOffset padding)
        {
            var h = go.AddComponent<HorizontalLayoutGroup>();
            h.spacing = spacing;
            h.padding = padding;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = false;
            h.childAlignment = TextAnchor.MiddleLeft;
            return h;
        }
    }
}
