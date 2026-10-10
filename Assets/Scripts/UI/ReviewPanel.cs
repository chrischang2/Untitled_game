using System.Linq;
using TMPro;
using UnityEngine;
using UntitledGame.Companion;
using UntitledGame.Core;

namespace UntitledGame.UI
{
    /// <summary>
    /// What a failed lesson or test leaves on screen: the pass/fail message with the review of every question beneath it
    /// (what was asked, the answer, and whether you got it right). It stays until you close it yourself (the button, or
    /// X), so you can read it at your own pace, even after you've walked away from the test centre.
    /// </summary>
    public class ReviewPanel
    {
        private readonly RectTransform _root;
        private readonly TextMeshProUGUI _title, _subtitle;
        private TextMeshProUGUI _closeLabel;
        private readonly RectTransform _content;

        public bool IsOpen => _root.gameObject.activeSelf;
        public int RowCount { get; private set; }
        public string Title => _title.text;

        public ReviewPanel(RectTransform parent)
        {
            var panel = UIFactory.Panel(parent, "Review", UITheme.Cream.WithAlpha(0.98f));
            // Where the pass/fail banner sits, but tall: the message on top, the review under it.
            _root = panel.rectTransform.Anchor(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-180, 10), new Vector2(980, 780));
            _title = UIFactory.Text(_root, "Title", "", 56, UITheme.Red, TextAlignmentOptions.Center, title: true);
            _title.rectTransform.Anchor(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -20), new Vector2(920, 76));
            _subtitle = UIFactory.Text(_root, "Subtitle", "", 24, UITheme.Ink, TextAlignmentOptions.Top);
            _subtitle.rectTransform.Anchor(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -100), new Vector2(920, 60));
            var area = UIFactory.Rect("List", _root).Stretch(30, 30, 176, 100);
            _content = UIFactory.ScrollList(area, 8);
            var close = UIFactory.Button(_root, "Close review  [X]", Close, UITheme.Teal, 26);
            _closeLabel = close.GetComponentInChildren<TextMeshProUGUI>();
            close.GetComponent<RectTransform>().Anchor(new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 28), new Vector2(380, 60));
            _root.gameObject.SetActive(false);
        }

        /// <summary>Opens the review for a lesson or test that wasn't passed.</summary>
        public void Show(LessonSession r)
        {
            foreach (Transform c in _content) Object.Destroy(c.gameObject);
            if (_closeLabel != null) _closeLabel.text = UiText.Plain("Close review", "关上", 3) + "  [X]";
            bool test = r.kind == LessonKind.Test;
            _title.text = r.kind == LessonKind.Lesson ? "Lesson not passed" : $"HSK {r.level} test: NOT PASSED";
            string need = test ? $"you need {Progression.Hsk.PassMarkFor(r.questions.Count)}" : $"you need {Mathf.Min(Progression.Hsk.LessonPassMark, Mathf.CeilToInt(r.RecallAsked * 0.75f))}";
            string score = r.kind == LessonKind.Lesson ? $"quiz {r.RecallRight}/{r.RecallAsked} right" : $"{r.Right}/{r.questions.Count} right";
            _subtitle.text = $"{r.Title}\n{score}  ·  {need}";

            var asked = (r.kind == LessonKind.Lesson ? r.questions.Where(q => q.recall) : r.questions).Where(q => q.answered).ToList();
            RowCount = asked.Count;
            Line($"<b>Review</b>  <size=18><color=#8A7563>{asked.Count(q => q.correct)} right, {asked.Count(q => !q.correct)} to work on</color></size>");
            int n = 0;
            foreach (var q in asked)
            {
                n++;
                string tag = q.correct ? (q.attempts == 0 ? "<color=#2C7F79>right</color>" : $"<color=#2C7F79>try {q.attempts + 1}</color>")
                    : q.skipped ? "<color=#E0604E>skipped</color>" : "<color=#E0604E>wrong</color>";
                string said = !q.correct && !q.skipped && !string.IsNullOrEmpty(q.heard) ? $"\n<size=17><color=#8A7563>you said: {q.heard}</color></size>" : "";
                Row($"<size=19><color=#8A7563>{n}.</color></size> <b>{q.word.meaning}</b>  →  <b>{q.word.hanzi}</b> <size=19>{q.word.pinyin}</size>{said}", tag, said.Length > 0 ? 72 : 56);
            }
            _root.gameObject.SetActive(true);
            _root.SetAsLastSibling();
            AudioManager.Instance?.PlaySfx("SFX/rpg_bookOpen", 0.5f);
        }

        private static string Hsk_TestNeed() => $"you need {Progression.Hsk.TestPassMark}";

        public void Close()
        {
            if (!IsOpen) return;
            _root.gameObject.SetActive(false);
            AudioManager.Instance?.PlaySfx("SFX/rpg_bookClose", 0.5f);
        }

        private void Line(string text)
        {
            var t = UIFactory.Text(_content, "Line", text, 22, UITheme.Ink, TextAlignmentOptions.MidlineLeft);
            t.rectTransform.SetLayout(44);
        }

        private void Row(string text, string tag, float height)
        {
            var row = UIFactory.Panel(_content, "Row", UITheme.CreamDark.WithAlpha(0.55f), shadow: false, small: true);
            row.rectTransform.SetLayout(height);
            var t = UIFactory.Text(row.transform, "Text", text, 24, UITheme.Ink, TextAlignmentOptions.MidlineLeft);
            t.rectTransform.anchorMin = Vector2.zero;
            t.rectTransform.anchorMax = Vector2.one;
            t.rectTransform.offsetMin = new Vector2(16, 4);
            t.rectTransform.offsetMax = new Vector2(-150, -4);
            var l = UIFactory.Text(row.transform, "Tag", tag, 22, UITheme.TealDark, TextAlignmentOptions.MidlineRight);
            l.rectTransform.Anchor(new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-14, 0), new Vector2(130, 46));
        }
    }
}
