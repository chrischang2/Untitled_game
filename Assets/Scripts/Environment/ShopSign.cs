using TMPro;
using UnityEngine;
using UntitledGame.Companion;
using UntitledGame.Core;
using UntitledGame.Language;

namespace UntitledGame.Environment
{
    /// <summary>
    /// A painted wooden sign over each stall front with the shop's name in Chinese (and pinyin), facing the plaza.
    /// Replaces the floating name bubbles.
    /// </summary>
    public static class ShopSign
    {
        public const float Height = 2.55f;

        /// <summary>A free-standing sign (the games stalls), facing <paramref name="front"/>.</summary>
        public static GameObject CreateAt(Transform parent, Vector3 pos, Vector3 front, string hanzi, Color? board = null)
        {
            var sign = new GameObject("Sign");
            sign.transform.SetParent(parent, true);
            sign.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(-front));
            Box(sign.transform, "Board", new Vector3(0f, 0f, 0f), new Vector3(1.7f, 0.62f, 0.07f), board ?? new Color(0.47f, 0.31f, 0.18f));
            Box(sign.transform, "Face", new Vector3(0f, 0f, -0.04f), new Vector3(1.56f, 0.5f, 0.01f), new Color(0.96f, 0.91f, 0.8f));
            bool pinyin = SaveSystem.Settings.pinyin != PinyinMode.Off;
            Text(sign.transform, "Name", hanzi, UI.UITheme.Body, 3.2f, new Vector3(0f, pinyin ? 0.06f : 0f, -0.05f), new Color(0.25f, 0.16f, 0.1f), FontStyles.Bold);
            if (pinyin)
            {
                var py = Text(sign.transform, "Pinyin", Pinyin.Ready ? Pinyin.Of(hanzi) : "", UI.UITheme.Body, 1.3f, new Vector3(0f, -0.17f, -0.05f), new Color(0.17f, 0.5f, 0.47f), FontStyles.Italic);
                if (!Pinyin.Ready) py.gameObject.AddComponent<SignPinyin>().hanzi = hanzi;
            }
            return sign;
        }

        public static GameObject Create(ShopkeeperBrain keeper)
        {
            var shop = keeper.Shop;
            var stall = keeper.transform.parent;
            if (shop == null || stall == null || shop.busDriver) return null; // the bus stop puts up its own sign (BusStop)
            if (stall.Find("ShopSign") != null) return stall.Find("ShopSign").gameObject;

            // The keeper stands behind the counter, looking out: the sign faces the same way.
            Vector3 front = keeper.transform.forward;
            front.y = 0f;
            front = front.sqrMagnitude > 0.01f ? front.normalized : Vector3.forward;

            var sign = new GameObject("ShopSign");
            sign.transform.SetParent(stall, true);
            sign.transform.SetPositionAndRotation(stall.position + front * 0.95f + Vector3.up * Height, Quaternion.LookRotation(-front));

            Box(sign.transform, "Board", new Vector3(0f, 0f, 0f), new Vector3(1.7f, 0.62f, 0.07f), new Color(0.47f, 0.31f, 0.18f));
            Box(sign.transform, "Face", new Vector3(0f, 0f, -0.04f), new Vector3(1.56f, 0.5f, 0.01f), new Color(0.96f, 0.91f, 0.8f));
            Box(sign.transform, "PostL", new Vector3(-0.7f, -0.55f, 0.02f), new Vector3(0.06f, 0.55f, 0.06f), new Color(0.4f, 0.27f, 0.16f));
            Box(sign.transform, "PostR", new Vector3(0.7f, -0.55f, 0.02f), new Vector3(0.06f, 0.55f, 0.06f), new Color(0.4f, 0.27f, 0.16f));

            bool pinyin = SaveSystem.Settings.pinyin != PinyinMode.Off;
            Text(sign.transform, "Name", shop.hanzi, UI.UITheme.Body, 3.2f, new Vector3(0f, pinyin ? 0.06f : 0f, -0.05f), new Color(0.25f, 0.16f, 0.1f), FontStyles.Bold);
            if (pinyin)
            {
                // The pinyin dictionary loads in the background: fill the line in once it's ready.
                var py = Text(sign.transform, "Pinyin", Pinyin.Ready ? Pinyin.Of(shop.hanzi) : "", UI.UITheme.Body, 1.3f, new Vector3(0f, -0.17f, -0.05f), new Color(0.17f, 0.5f, 0.47f), FontStyles.Italic);
                if (!Pinyin.Ready) py.gameObject.AddComponent<SignPinyin>().hanzi = shop.hanzi;
            }
            return sign;
        }

        public static void Box(Transform parent, string name, Vector3 pos, Vector3 size, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(go.GetComponent<Collider>());
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = size;
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = GameAssets.Instance.bobberWhite; // a URP-lit material that ships with the build
            var mpb = new MaterialPropertyBlock();
            mpb.SetColor("_BaseColor", color);
            r.SetPropertyBlock(mpb);
        }

        private static TextMeshPro Text(Transform parent, string name, string text, TMP_FontAsset font, float size, Vector3 pos, Color color, FontStyles style)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            var t = go.AddComponent<TextMeshPro>();
            if (font != null) t.font = font;
            t.text = text;
            t.fontSize = size;
            t.fontStyle = style;
            t.color = color;
            t.alignment = TextAlignmentOptions.Center;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.rectTransform.sizeDelta = new Vector2(1.5f, 0.4f);
            return t;
        }
    }

    /// <summary>Writes a sign's pinyin as soon as the pinyin dictionary has loaded, then removes itself.</summary>
    public class SignPinyin : MonoBehaviour
    {
        public string hanzi;

        private void Update()
        {
            if (!Pinyin.Ready) return;
            var t = GetComponent<TextMeshPro>();
            if (t != null) t.text = Pinyin.Of(hanzi);
            Destroy(this);
        }
    }
}
