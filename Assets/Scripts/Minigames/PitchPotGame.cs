using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UntitledGame.CameraControl;
using UntitledGame.Core;
using UntitledGame.Economy;
using UntitledGame.Environment;
using UntitledGame.Player;
using UntitledGame.UI;

namespace UntitledGame.Minigames
{
    /// <summary>
    /// 投壶 tóuhú, pitch-pot: an old Chinese party game. Stand at the line and throw eight arrows into the narrow
    /// mouth of a bronze pot. A/D aim (each arrow starts a little off, there's a breeze), hold the mouse to swing the
    /// power up and down and let go to throw. An arrow in the mouth is 中 (a hit); clipping the rim is 差一点 (so
    /// close). The calls are in Chinese with pinyin. The first round each day with 4+ hits wins a little prize.
    /// </summary>
    public class PitchPotGame : MonoBehaviour
    {
        public static PitchPotGame Active { get; private set; }

        public const int Arrows = 8, PrizeHits = 4;
        public const float ThrowDistance = 3.4f, MouthRadius = 0.13f, RimWidth = 0.05f, BodyRadius = 0.2f, MouthHeight = 0.86f;
        public const float Elevation = 42f, SpeedMin = 4.6f, SpeedMax = 6.4f, PowerRate = 0.6f, AimSpeed = 12f, Gravity = 9.81f;

        private MinigameStall _stall;
        private Transform _pot;
        private PlayerController _player;
        private CameraRig _cam;

        public int Thrown { get; private set; }
        public int Hits { get; private set; }
        public float AimOffset { get; set; }  // degrees off the straight line to the pot
        public float Power { get; private set; }
        public bool Charging { get; private set; }
        public bool Flying => _arrow != null && _arrowFlying;
        public string LastCall { get; private set; } = "";
        public bool Finished { get; private set; }

        private float _chargeT, _baseYaw, _feedbackUntil;
        private GameObject _arrow;
        private bool _arrowFlying;
        private Vector3 _arrowPos, _arrowVel;
        private readonly List<GameObject> _spent = new List<GameObject>();
        private GameObject _aimLine;
        private float _camYaw, _camPitch, _camDist;

        // UI
        private RectTransform _panel, _powerFill;
        private TextMeshProUGUI _status, _call, _help;

        public Vector3 MouthCenter => _pot.position + Vector3.up * MouthHeight;
        public Vector3 ThrowSpot
        {
            get
            {
                Vector3 f = _stall.transform.forward;
                Vector3 p = _pot.position + f * ThrowDistance;
                p.y = WorldShape.TerrainHeight(p.x, p.z) + 0.05f;
                return p;
            }
        }
        public Vector3 LaunchPoint => _player.transform.position + Vector3.up * 1.3f + Direction * 0.3f;
        public Vector3 Direction => Quaternion.Euler(0f, _baseYaw + AimOffset, 0f) * Vector3.forward;

        // ------------------------------------------------------------------ start / stop

        public static void Begin(MinigameStall stall)
        {
            if (Active != null) return;
            var pot = stall.transform.Find("Built/Pot");
            if (pot == null) { GameEvents.Toast("The pitch-pot isn't set up yet.", 3f); return; }
            var game = stall.GetComponent<PitchPotGame>() ?? stall.gameObject.AddComponent<PitchPotGame>();
            game.StartRound(stall, pot);
        }

        private void StartRound(MinigameStall stall, Transform pot)
        {
            _stall = stall;
            _pot = pot;
            _player = FindFirstObjectByType<PlayerController>();
            _cam = FindFirstObjectByType<CameraRig>();
            Active = this;
            InputGate.BlockMovement(this);
            InputGate.BlockGameplay(this);
            Thrown = Hits = 0;
            Finished = false;
            foreach (var a in _spent) if (a != null) Destroy(a);
            _spent.Clear();

            Vector3 toPot = _pot.position - ThrowSpot;
            toPot.y = 0f;
            _baseYaw = Quaternion.LookRotation(toPot).eulerAngles.y;
            _player.Teleport(ThrowSpot, _baseYaw);
            if (_cam != null)
            {
                _camYaw = _cam.Yaw; _camPitch = _cam.Pitch; _camDist = _cam.Distance;
                _cam.Configure(_player.transform, _baseYaw, 16f, 4.2f);
            }
            BuildUI();
            NewArrow();
            Call("投壶！", "tóuhú!", "Pitch-pot! Eight arrows: get them in the pot's mouth.");
            ChatAudit.Write("GAMES", "pitch-pot: started a round");
        }

        public void Stop()
        {
            if (Active != this) return;
            Active = null;
            InputGate.UnblockMovement(this);
            InputGate.UnblockGameplay(this);
            if (_arrow != null) Destroy(_arrow);
            foreach (var a in _spent) if (a != null) Destroy(a, 0.1f);
            _spent.Clear();
            if (_aimLine != null) Destroy(_aimLine);
            if (_panel != null) Destroy(_panel.gameObject);
            if (_cam != null && _player != null) _cam.Configure(_player.transform, _camYaw, _camPitch, _camDist);
        }

        private void OnDisable() => Stop();

        private void NewArrow()
        {
            // A breeze: every arrow starts a little off line, so you have to aim.
            AimOffset = Random.Range(-6f, 6f);
            Power = 0f;
            Charging = false;
            _arrowFlying = false;
            _arrow = MakeArrow();
            PlaceArrowInHand();
        }

        // ------------------------------------------------------------------ the throw

        private void Update()
        {
            if (Active != this) return;
            float dt = Time.deltaTime;
            if (Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.Escape))
            {
                Finish(early: true);
                return;
            }
            if (Finished) return;

            if (!_arrowFlying)
            {
                float turn = (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow) ? 1f : 0f) - (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow) ? 1f : 0f);
                AimOffset = Mathf.Clamp(AimOffset + turn * AimSpeed * dt, -20f, 20f);
                _player.FaceTowards(_player.transform.position + Direction, instant: true);
                if (Input.GetMouseButtonDown(0)) { Charging = true; _chargeT = 0f; }
                if (Charging)
                {
                    _chargeT += dt;
                    Power = Mathf.PingPong(_chargeT * PowerRate, 1f);
                    if (Input.GetMouseButtonUp(0) || !Input.GetMouseButton(0)) Throw(Power);
                }
                PlaceArrowInHand();
            }
            else StepArrow(dt);
            UpdateAimLine();
            UpdateUI();
        }

        /// <summary>Throws the arrow in hand at this power (0-1) along the current aim. Also used by the self-test.</summary>
        public void Throw(float power)
        {
            if (_arrowFlying || Finished) return;
            Power = Mathf.Clamp01(power);
            Charging = false;
            _arrowPos = LaunchPoint;
            _arrowVel = Launch(Direction, Power);
            _arrowFlying = true;
            AudioManager.Instance?.PlaySfx("SFX/rpg_cloth2", 0.5f);
        }

        public static Vector3 Launch(Vector3 dir, float power)
        {
            float speed = Mathf.Lerp(SpeedMin, SpeedMax, power), e = Elevation * Mathf.Deg2Rad;
            return dir.normalized * (speed * Mathf.Cos(e)) + Vector3.up * (speed * Mathf.Sin(e));
        }

        public enum Outcome { Flying, In, Rim, Miss }

        /// <summary>
        /// Flies an arrow one step: into the mouth (from above, inside the radius) is a hit; across the rim or into the
        /// pot's side is "so close"; the ground is a miss.
        /// </summary>
        public static Outcome Step(ref Vector3 pos, ref Vector3 vel, float dt, Vector3 mouth)
        {
            Vector3 p0 = pos;
            vel += Vector3.down * Gravity * dt;
            pos += vel * dt;
            if (p0.y >= mouth.y && pos.y < mouth.y)
            {
                float t = (p0.y - mouth.y) / Mathf.Max(0.0001f, p0.y - pos.y);
                Vector3 c = Vector3.Lerp(p0, pos, t);
                float d = new Vector2(c.x - mouth.x, c.z - mouth.z).magnitude;
                if (d < MouthRadius) { pos = c; return Outcome.In; }
                if (d < MouthRadius + RimWidth) { pos = c; return Outcome.Rim; }
            }
            float side = new Vector2(pos.x - mouth.x, pos.z - mouth.z).magnitude;
            if (pos.y < mouth.y && pos.y > mouth.y - MouthHeight && side < BodyRadius) return Outcome.Rim;
            if (pos.y <= WorldShape.TerrainHeight(pos.x, pos.z)) return Outcome.Miss;
            return Outcome.Flying;
        }

        /// <summary>Where a throw at this power and aim would end up (self-test and tuning).</summary>
        public Outcome Predict(float power, float aimOffset)
        {
            Vector3 dir = Quaternion.Euler(0f, _baseYaw + aimOffset, 0f) * Vector3.forward;
            Vector3 pos = _player.transform.position + Vector3.up * 1.3f + dir * 0.3f, vel = Launch(dir, power);
            for (int i = 0; i < 600; i++)
            {
                var o = Step(ref pos, ref vel, 1f / 120f, MouthCenter);
                if (o != Outcome.Flying) return o;
            }
            return Outcome.Miss;
        }

        private void StepArrow(float dt)
        {
            Outcome o = Outcome.Flying;
            for (int i = 0; i < 4 && o == Outcome.Flying; i++) o = Step(ref _arrowPos, ref _arrowVel, dt / 4f, MouthCenter);
            _arrow.transform.SetPositionAndRotation(_arrowPos, Quaternion.LookRotation(_arrowVel.sqrMagnitude > 0.01f ? _arrowVel : Vector3.down));
            if (o == Outcome.Flying) return;

            Thrown++;
            string nth = $"第{Catalog.ChineseNumber(Thrown)}支";
            switch (o)
            {
                case Outcome.In:
                    Hits++;
                    _arrow.transform.position = MouthCenter + Vector3.down * 0.25f + Random.insideUnitSphere * 0.03f;
                    _arrow.transform.rotation = Quaternion.Euler(Random.Range(-8f, 8f), Random.Range(0f, 360f), 0f) * Quaternion.LookRotation(Vector3.down);
                    Call($"{nth}：中了！", $"dì {Language.Pinyin.Of(Catalog.ChineseNumber(Thrown))} zhī: zhòng le!", "In the pot!", true);
                    AudioManager.Instance?.PlaySfx("SFX/rpg_handleCoins", 0.6f);
                    break;
                case Outcome.Rim:
                    _arrow.transform.position = new Vector3(_arrowPos.x, WorldShape.TerrainHeight(_arrowPos.x, _arrowPos.z) + 0.03f, _arrowPos.z) + Random.insideUnitSphere * 0.2f;
                    _arrow.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 90f);
                    Call($"{nth}：差一点！", "chà yìdiǎn!", "So close: it hit the rim.");
                    AudioManager.Instance?.PlaySfx("SFX/ui_click_002", 0.6f);
                    break;
                default:
                    Call($"{nth}：没中。", "méi zhòng.", "Missed.");
                    AudioManager.Instance?.PlaySfx("SFX/footstep_grass_001", 0.5f);
                    break;
            }
            _spent.Add(_arrow);
            _arrow = null;
            _arrowFlying = false;
            ChatAudit.Write("GAMES", $"pitch-pot arrow {Thrown}: {o} (power {Power:0.00}, aim {AimOffset:+0.0;-0.0}°)");
            if (Thrown >= Arrows) Finish(early: false);
            else NewArrow();
        }

        private void Finish(bool early)
        {
            if (Finished) { Stop(); return; }
            Finished = true;
            var d = SaveSystem.Data;
            bool best = Hits > d.pitchPotBest && !early;
            if (!early) d.pitchPotBest = Mathf.Max(d.pitchPotBest, Hits);
            int today = DayNightCycle.Instance != null ? DayNightCycle.Instance.Day : d.day;
            string prize = null;
            if (!early && Hits >= PrizeHits && d.pitchPotPrizeDay != today)
            {
                d.pitchPotPrizeDay = today;
                prize = Surprise.Give("Pitch-pot", Hits >= 7 ? 2 : 1, null, 0.6f);
            }
            string zh = $"{Catalog.ChineseNumber(Thrown)}支中了{Catalog.ChineseNumber(Hits)}支";
            ChatAudit.Write("GAMES", $"pitch-pot round over: {Hits}/{Thrown}{(best ? " (new best)" : "")}{(prize != null ? $", prize {prize}" : "")}");
            if (!early || Thrown > 0)
                GameEvents.Banner(Hits >= PrizeHits ? "投壶: well thrown!" : "投壶 pitch-pot",
                    $"{zh}  ({Language.Pinyin.Of(zh)})\n{Hits} of {Thrown} arrows in the pot" + (best ? " · your best yet!" : $" · best {d.pitchPotBest}") +
                    (prize != null ? $"\nA prize: {prize}" : Hits >= PrizeHits ? "\n(Today's prize is already won: come back tomorrow.)" : $"\n{PrizeHits}+ hits wins a prize once a day."), Hits >= PrizeHits);
            Stop();
        }

        // ------------------------------------------------------------------ visuals

        private GameObject MakeArrow()
        {
            var root = new GameObject("PitchPotArrow");
            var shaft = Part(root.transform, PrimitiveType.Cylinder, new Vector3(0f, 0f, 0f), new Vector3(0.025f, 0.32f, 0.025f), Quaternion.Euler(90f, 0f, 0f), new Color(0.86f, 0.74f, 0.52f));
            Part(root.transform, PrimitiveType.Cube, new Vector3(0f, 0f, -0.28f), new Vector3(0.07f, 0.07f, 0.12f), Quaternion.Euler(0f, 0f, 45f), new Color(0.85f, 0.25f, 0.22f));
            Part(root.transform, PrimitiveType.Sphere, new Vector3(0f, 0f, 0.33f), new Vector3(0.045f, 0.045f, 0.06f), Quaternion.identity, new Color(0.3f, 0.3f, 0.32f));
            return root;
        }

        private static GameObject Part(Transform parent, PrimitiveType type, Vector3 pos, Vector3 scale, Quaternion rot, Color color)
        {
            var go = GameObject.CreatePrimitive(type);
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.transform.localRotation = rot;
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = GameAssets.Instance.bobberWhite;
            var mpb = new MaterialPropertyBlock();
            mpb.SetColor("_BaseColor", color);
            r.SetPropertyBlock(mpb);
            return go;
        }

        private void PlaceArrowInHand()
        {
            if (_arrow == null) return;
            Vector3 dir = Quaternion.AngleAxis(-Elevation * (0.4f + 0.6f * Power), Vector3.Cross(Vector3.up, Direction)) * Direction;
            _arrow.transform.SetPositionAndRotation(LaunchPoint, Quaternion.LookRotation(dir));
        }

        private void UpdateAimLine()
        {
            if (_aimLine == null)
            {
                _aimLine = Part(null, PrimitiveType.Cube, Vector3.zero, Vector3.one, Quaternion.identity, new Color(1f, 1f, 1f, 1f));
                _aimLine.name = "PitchPotAim";
            }
            _aimLine.SetActive(!_arrowFlying && !Finished);
            Vector3 a = _player.transform.position, dir = Direction;
            float len = ThrowDistance - 0.5f;
            Vector3 mid = a + dir * (0.4f + len * 0.5f);
            mid.y = WorldShape.TerrainHeight(mid.x, mid.z) + 0.03f;
            _aimLine.transform.SetPositionAndRotation(mid, Quaternion.LookRotation(dir));
            _aimLine.transform.localScale = new Vector3(0.03f, 0.01f, len);
        }

        private void BuildUI()
        {
            var ui = FindFirstObjectByType<GameUI>();
            if (ui == null || ui.HudRoot == null) return;
            var panel = UIFactory.Panel(ui.HudRoot, "PitchPot", UITheme.Cream.WithAlpha(0.95f));
            _panel = panel.rectTransform.Anchor(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 110), new Vector2(620, 190));
            _call = UIFactory.Text(_panel, "Call", "", 34, UITheme.Ink, TextAlignmentOptions.Top, title: true);
            _call.rectTransform.Anchor(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -12), new Vector2(580, 80));
            _status = UIFactory.Text(_panel, "Status", "", 20, UITheme.InkSoft, TextAlignmentOptions.Left);
            _status.rectTransform.Anchor(new Vector2(0, 0), new Vector2(0, 0), new Vector2(24, 64), new Vector2(280, 30));
            var track = UIFactory.Image(_panel, "PowerTrack", UITheme.Hex("#CDBBA0"), GameAssets.Instance.roundedRectSmall);
            track.rectTransform.Anchor(new Vector2(1, 0), new Vector2(1, 0), new Vector2(-24, 64), new Vector2(270, 24)).pivot = new Vector2(1, 0.5f);
            var fill = UIFactory.Image(track.transform, "Fill", UITheme.Orange, GameAssets.Instance.roundedRectSmall);
            _powerFill = fill.rectTransform;
            _powerFill.anchorMin = Vector2.zero;
            _powerFill.anchorMax = new Vector2(0f, 1f);
            _powerFill.offsetMin = _powerFill.offsetMax = Vector2.zero;
            _help = UIFactory.Text(_panel, "Help", "<b>[A]/[D]</b> aim   ·   hold <b>LMB</b> and let go to throw   ·   <b>[E]</b> stop", 18, UITheme.InkSoft, TextAlignmentOptions.Center);
            _help.rectTransform.Anchor(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 22), new Vector2(580, 28));
        }

        private void Call(string zh, string py, string en, bool good = false)
        {
            LastCall = zh;
            if (_call != null) _call.text = $"<color={(good ? "#2C7F79" : "#3A2A1E")}>{zh}</color>\n<size=18><i><color=#2C7F79>{py}</color></i>  <color=#8A7563>{en}</color></size>";
            _feedbackUntil = Time.time + 2.5f;
        }

        private void UpdateUI()
        {
            if (_panel == null) return;
            _status.text = $"Arrow {Mathf.Min(Thrown + 1, Arrows)}/{Arrows}   ·   中 {Hits}   ·   best {SaveSystem.Data.pitchPotBest}";
            _powerFill.anchorMax = new Vector2(Power, 1f);
        }
    }
}
