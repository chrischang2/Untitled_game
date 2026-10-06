using UnityEngine;
using UntitledGame.Core;
using UntitledGame.Home;
using UntitledGame.Language;
using UntitledGame.Progression;

namespace UntitledGame.Environment
{
    /// <summary>
    /// A games stall on the seafront (投壶 pitch-pot, 毽子 jianzi, 麻将 mahjong). Each stays a fenced-off building site
    /// (施工中) until its HSK test is passed, then the finished stall appears. Pitch-pot is playable
    /// (Minigames.PitchPotGame); the other two are coming soon.
    /// </summary>
    public class MinigameStall : Interactable
    {
        public string gameId, hanzi, english, blurb;
        public int hsk;

        private Transform _built, _site;
        private bool? _shownOpen;

        public bool Open => Hsk.Level >= hsk;

        private void Awake()
        {
            radius = 3.2f;
            _built = transform.Find("Built");
            _site = transform.Find("Construction");
        }

        private void Start()
        {
            // Signs need the fonts and pinyin, so they're made at runtime: the name over the finished stall, and a
            // red 施工中 ("under construction") board on the site's fence with the name above the scaffolding.
            Vector3 front = transform.forward, pos = transform.position;
            if (_built != null) ShopSign.CreateAt(_built, pos + front * 0.95f + Vector3.up * ShopSign.Height, front, hanzi);
            if (_site != null)
            {
                ShopSign.CreateAt(_site, pos + front * 2.4f + Vector3.up * 1.15f, front, "施工中", new Color(0.85f, 0.3f, 0.26f));
                ShopSign.CreateAt(_site, pos + Vector3.up * 2.4f, front, hanzi);
            }
        }

        private void Update()
        {
            if (_shownOpen == Open) return;
            _shownOpen = Open;
            if (_built != null) _built.gameObject.SetActive(Open);
            if (_site != null) _site.gameObject.SetActive(!Open);
        }

        public override bool Available => Minigames.PitchPotGame.Active == null;

        public override string Prompt => Open
            ? $"[F] Play {hanzi} {english}" + (gameId == "pitchpot" ? "" : " (coming soon)")
            : $"{hanzi} {english}: under construction (施工中) until you pass HSK {hsk}";

        public override void Interact()
        {
            if (!Open)
            {
                GameEvents.Toast($"The {english.ToLower()} stall ({hanzi}) is still being built. It opens when you pass the HSK {hsk} test.", 4f);
                return;
            }
            if (gameId == "pitchpot")
            {
                Minigames.PitchPotGame.Begin(this);
                return;
            }
            GameEvents.Toast($"{hanzi} {Pinyin.Of(hanzi)}: {blurb} The game itself is coming soon!", 4.5f);
            AudioManager.Instance?.PlaySfx("SFX/rpg_bookFlip1", 0.4f);
        }
    }
}
