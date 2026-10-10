using System;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using UntitledGame.Core;
using Object = UnityEngine.Object;

namespace UntitledGame.UI
{
    /// <summary>
    /// Save slots, restore points and the chat logs. Anything that replaces a game asks for a second click,
    /// and a restore point is taken first, so nothing is ever lost for good.
    /// </summary>
    public class SavesPanel : ModalPanel
    {
        private readonly RectTransform _content;
        private readonly TextMeshProUGUI _footer;

        public SavesPanel(RectTransform canvas) : base(canvas, "Saves", new Vector2(1180, 960), "Saves & chat logs")
        {
            ChineseTitle("存档", 3);
            var area = UIFactory.Rect("Area", Window).Stretch(50, 40, 100, 176);
            _content = UIFactory.ScrollList(area, 10);

            _footer = UIFactory.Text(Window, "Footer", "", 19, UITheme.InkSoft, TextAlignmentOptions.Center);
            _footer.rectTransform.Anchor(new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 108), new Vector2(1080, 36));

            var buttons = UIFactory.Rect("Buttons", Window).Anchor(new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 30), new Vector2(1080, 64));
            var hl = UIFactory.HLayout(buttons.gameObject, 16, new RectOffset(0, 0, 0, 0));
            hl.childAlignment = TextAnchor.MiddleCenter;
            UIFactory.Button(buttons, "Open chat logs", () => OpenFolder(ChatAudit.SessionFolder ?? ChatAudit.Folder), UITheme.TealDark, 22)
                .GetComponent<RectTransform>().SetLayout(60, 250);
            UIFactory.Button(buttons, "Open saves folder", () => OpenFolder(SaveSystem.SavesFolder), UITheme.TealDark, 22)
                .GetComponent<RectTransform>().SetLayout(60, 260);
            Button rec = null;
            rec = UIFactory.Button(buttons, RecordingLabel(), () =>
            {
                SaveSystem.Settings.keepVoiceRecordings = !SaveSystem.Settings.keepVoiceRecordings;
                SaveSystem.NotifySettingsChanged();
                rec.GetComponentInChildren<TextMeshProUGUI>().text = RecordingLabel();
                ChatAudit.Write("SETTINGS", "voice recordings " + (SaveSystem.Settings.keepVoiceRecordings ? "kept" : "off"));
            }, UITheme.InkSoft, 22);
            rec.GetComponent<RectTransform>().SetLayout(60, 330);
        }

        private static string RecordingLabel() => SaveSystem.Settings.keepVoiceRecordings ? "Voice recordings: kept" : "Voice recordings: off";

        protected override void Refresh()
        {
            foreach (Transform c in _content) Object.Destroy(c.gameObject);

            for (int slot = 1; slot <= SaveSystem.SlotCount; slot++) SlotRow(SaveSystem.Describe(slot));

            int active = SaveSystem.ActiveSlot;
            var header = UIFactory.Text(_content, "RestoreHeader", $"<b>Restore points for slot {active}</b>  <size=19><color=#8A7563>taken automatically at the start of each session, each new day, and before anything is overwritten</color></size>",
                24, UITheme.Ink, TextAlignmentOptions.BottomLeft);
            header.rectTransform.SetLayout(62);
            var points = SaveSystem.RestorePoints(active);
            if (points.Count == 0)
            {
                var none = UIFactory.Text(_content, "None", "None yet - one is made each time you start playing.", 22, UITheme.InkSoft, TextAlignmentOptions.MidlineLeft);
                none.rectTransform.SetLayout(44);
            }
            foreach (var rp in points)
            {
                var row = Row(64);
                Label(row, $"{rp.time:ddd d MMM, HH:mm}  <color=#8A7563>· {rp.reason}</color>\n<size=19><color=#8A7563>Day {rp.day} · ¥{rp.money} · {rp.words} words</color></size>", 22, 760);
                ConfirmButton.Add(RightButtons(row, 300), "Go back to this", "Replace current game?", UITheme.Orange, 280, () => GameBootstrap.Instance?.RestoreTo(rp));
            }

            string where = ChatAudit.SessionFile != null ? ChatAudit.SessionFile : ChatAudit.Folder;
            _footer.text = $"This session's chat log: <color=#4A3A30>{where}</color>";
        }

        private void SlotRow(SlotInfo info)
        {
            bool current = info.slot == SaveSystem.ActiveSlot;
            var row = Row(108, current ? UITheme.Teal.WithAlpha(0.18f) : UITheme.CreamDark.WithAlpha(0.6f));
            string title = $"<b>Slot {info.slot}</b>" + (current ? "  <color=#2C7F79>· playing now</color>" : "");
            string body = info.exists
                ? $"Day {info.day} · ¥{info.money} · {info.words} words · {info.catches} catch{(info.catches == 1 ? "" : "es")} · {GameBootstrap.FormatDuration(info.playSeconds)} played" +
                  (info.lastPlayed > DateTime.MinValue ? $"\n<size=19>last played {info.lastPlayed:ddd d MMM, HH:mm}</size>" : "")
                : "Empty";
            Label(row, $"{title}\n<size=21><color=#8A7563>{body}</color></size>", 25, 640);

            var buttons = RightButtons(row, 430);

            int slot = info.slot;
            if (current)
            {
                Simple(buttons, "Save now", UITheme.Teal, 170, () =>
                {
                    SaveSystem.Save();
                    ChatAudit.Write("SAVE", $"saved slot {slot} by hand");
                    GameEvents.Toast($"Saved to slot {slot}.");
                    Refresh();
                });
                ConfirmButton.Add(buttons, "Start over", "Really? Click again", UITheme.Red, 230, () => GameBootstrap.Instance?.LoadSlot(slot, fresh: true));
            }
            else if (info.exists)
            {
                Simple(buttons, "Load", UITheme.Teal, 130, () => GameBootstrap.Instance?.LoadSlot(slot, fresh: false));
                ConfirmButton.Add(buttons, "Save here", "Overwrite slot?", UITheme.Orange, 270, () => SaveInto(slot));
            }
            else
            {
                Simple(buttons, "New game", UITheme.Teal, 170, () => GameBootstrap.Instance?.LoadSlot(slot, fresh: true));
                Simple(buttons, "Save here", UITheme.Orange, 170, () => SaveInto(slot));
            }
        }

        private void SaveInto(int slot)
        {
            SaveSystem.SaveInto(slot);
            ChatAudit.Banner($"Saved into slot {slot}; now playing slot {slot}");
            GameEvents.Toast($"Saved into slot {slot}. You're now playing slot {slot}.", 3.5f);
            Refresh();
        }

        /// <summary>A right-aligned strip of buttons inside a row.</summary>
        private static RectTransform RightButtons(RectTransform row, float width)
        {
            var buttons = UIFactory.Rect("Buttons", row).Anchor(new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-16, 0), new Vector2(width, 60));
            var hl = UIFactory.HLayout(buttons.gameObject, 10, new RectOffset(0, 0, 0, 0));
            hl.childAlignment = TextAnchor.MiddleRight;
            hl.childForceExpandWidth = false;
            return buttons;
        }

        private RectTransform Row(float height, Color? color = null)
        {
            var row = UIFactory.Panel(_content, "Row", color ?? UITheme.CreamDark.WithAlpha(0.6f), shadow: false, small: true);
            row.rectTransform.SetLayout(height);
            return row.rectTransform;
        }

        private static void Label(RectTransform row, string text, float size, float width)
        {
            var t = UIFactory.Text(row, "Label", text, size, UITheme.Ink, TextAlignmentOptions.MidlineLeft);
            t.rectTransform.Anchor(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(22, 0), new Vector2(width, row.rect.height > 0 ? row.rect.height : 100));
            t.rectTransform.anchorMin = new Vector2(0, 0);
            t.rectTransform.anchorMax = new Vector2(0, 1);
            t.rectTransform.sizeDelta = new Vector2(width, 0);
        }

        private static void Simple(Transform parent, string label, Color color, float width, UnityAction onClick)
        {
            UIFactory.Button(parent, label, onClick, color, 22).GetComponent<RectTransform>().SetLayout(56, width);
        }

        private static void OpenFolder(string path)
        {
            try
            {
                Directory.CreateDirectory(path);
                Application.OpenURL("file:///" + path.Replace('\\', '/'));
            }
            catch (Exception e)
            {
                GameEvents.Toast("Couldn't open the folder: " + e.Message, 4f);
            }
        }
    }

    /// <summary>A button that needs a second click within a few seconds (for anything that replaces a game).</summary>
    public class ConfirmButton : MonoBehaviour
    {
        private string _label, _confirm;
        private UnityAction _action;
        private TextMeshProUGUI _text;
        private float _armedUntil;

        public static void Add(Transform parent, string label, string confirm, Color color, float width, UnityAction action)
        {
            ConfirmButton cb = null;
            var b = UIFactory.Button(parent, label, () => cb.Click(), color, 22);
            b.GetComponent<RectTransform>().SetLayout(56, width);
            cb = b.gameObject.AddComponent<ConfirmButton>();
            cb._label = label;
            cb._confirm = confirm;
            cb._action = action;
            cb._text = b.GetComponentInChildren<TextMeshProUGUI>();
        }

        private void Click()
        {
            if (Time.unscaledTime < _armedUntil)
            {
                _armedUntil = 0f;
                _text.text = _label;
                _action?.Invoke();
                return;
            }
            _armedUntil = Time.unscaledTime + 3f;
            _text.text = _confirm;
        }

        private void Update()
        {
            if (_armedUntil > 0f && Time.unscaledTime >= _armedUntil)
            {
                _armedUntil = 0f;
                _text.text = _label;
            }
        }
    }
}
