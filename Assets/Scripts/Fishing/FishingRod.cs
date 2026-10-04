using UnityEngine;
using UntitledGame.Core;
using UntitledGame.Environment;

namespace UntitledGame.Fishing
{
    /// <summary>
    /// Visual fishing gear built from primitives: a rod held in the character's hand, a red/white
    /// bobber and a sagging line between them. Gameplay just sets targets; this makes it look nice.
    /// </summary>
    public class FishingRod : MonoBehaviour
    {
        [SerializeField] private Transform hand;
        [SerializeField] private Vector3 handOffset = new Vector3(0f, -0.2f, 0.05f);
        [SerializeField] private float rodLength = 1.9f;

        private Transform _rodPivot;
        private Transform _tip;
        private Transform _bobber;
        private LineRenderer _line;
        private float _pitch = 35f;
        private float _pitchVel;
        private Vector3 _bobberRest;
        private Renderer _shaft;
        private MaterialPropertyBlock _mpb;
        private float _dip;

        /// <summary>Upward angle of the rod in degrees (90 = straight up, 0 = horizontal, >90 = over the shoulder).</summary>
        public float TargetPitch { get; set; } = 35f;
        public float Tension { get; set; }
        public float Shake { get; set; }
        public bool LineOut { get; private set; }
        public Vector3 TipPosition => _tip != null ? _tip.position : transform.position;
        public Vector3 BobberPosition => _bobber != null ? _bobber.position : transform.position;
        public Transform Bobber => _bobber;

        /// <summary>The rod is only in the player's hand while fishing; otherwise it's put away.</summary>
        public bool Stowed
        {
            get => _rodPivot != null && !_rodPivot.gameObject.activeSelf;
            set { if (_rodPivot != null && _rodPivot.gameObject.activeSelf == value) _rodPivot.gameObject.SetActive(!value); }
        }

        public void SetHand(Transform handBone, Vector3 offset)
        {
            hand = handBone;
            handOffset = offset;
        }

        private void Awake() => Build();

        private void Build()
        {
            var ga = GameAssets.Instance;
            _rodPivot = new GameObject("Rod").transform;
            _rodPivot.SetParent(transform, false);

            var grip = Prim(PrimitiveType.Cylinder, _rodPivot, new Vector3(0f, 0.16f, 0f), new Vector3(0.05f, 0.16f, 0.05f), ga.bobberRed);
            grip.name = "Grip";
            var shaft = Prim(PrimitiveType.Cylinder, _rodPivot, new Vector3(0f, rodLength * 0.5f, 0f), new Vector3(0.028f, rodLength * 0.5f, 0.028f), ga.rodMaterial);
            shaft.name = "Shaft";
            _shaft = shaft.GetComponent<Renderer>();
            var reel = Prim(PrimitiveType.Cylinder, _rodPivot, new Vector3(0f, 0.36f, 0.05f), new Vector3(0.09f, 0.02f, 0.09f), ga.bobberWhite);
            reel.localRotation = Quaternion.Euler(0f, 0f, 90f);
            reel.name = "Reel";
            _tip = new GameObject("Tip").transform;
            _tip.SetParent(_rodPivot, false);
            _tip.localPosition = new Vector3(0f, rodLength, 0f);

            _bobber = new GameObject("Bobber").transform;
            var white = Prim(PrimitiveType.Sphere, _bobber, Vector3.zero, Vector3.one * 0.16f, ga.bobberWhite);
            var red = Prim(PrimitiveType.Sphere, _bobber, new Vector3(0f, 0.035f, 0f), new Vector3(0.165f, 0.11f, 0.165f), ga.bobberRed);
            var stick = Prim(PrimitiveType.Cylinder, _bobber, new Vector3(0f, 0.1f, 0f), new Vector3(0.02f, 0.05f, 0.02f), ga.bobberRed);
            white.name = "Float"; red.name = "Cap"; stick.name = "Stick";
            _bobber.gameObject.SetActive(false);

            var lineGo = new GameObject("Line");
            lineGo.transform.SetParent(transform, false);
            _line = lineGo.AddComponent<LineRenderer>();
            _line.sharedMaterial = ga.lineMaterial;
            _line.widthMultiplier = 0.012f;
            _line.positionCount = 20;
            _line.useWorldSpace = true;
            _line.numCapVertices = 0;
            _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _line.receiveShadows = false;
            _line.enabled = false;
        }

        private static Transform Prim(PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            return go.transform;
        }

        /// <summary>Tints the shaft to match the equipped rod.</summary>
        public void ApplyRodStyle(string rodId)
        {
            if (_shaft == null) return;
            Color c = rodId switch
            {
                "rod_bamboo" => new Color(0.86f, 0.74f, 0.42f),
                "rod_carbon" => new Color(0.18f, 0.2f, 0.24f),
                "rod_gold" => new Color(1f, 0.78f, 0.25f),
                _ => new Color(0.55f, 0.36f, 0.2f),
            };
            _mpb ??= new MaterialPropertyBlock();
            _mpb.SetColor("_BaseColor", c);
            _mpb.SetColor("_EmissionColor", rodId == "rod_gold" ? new Color(0.25f, 0.18f, 0.02f) : Color.black);
            _shaft.SetPropertyBlock(_mpb);
        }

        private void OnEnable() => Economy.Inventory.Changed += RefreshStyle;
        private void OnDisable() => Economy.Inventory.Changed -= RefreshStyle;
        private void Start() => RefreshStyle();
        private void RefreshStyle() => ApplyRodStyle(Economy.Inventory.Rod.id);

        public void ShowBobber(Vector3 position)
        {
            _bobber.gameObject.SetActive(true);
            _bobber.position = position;
            _bobberRest = position;
            LineOut = true;
            _line.enabled = true;
        }

        public void MoveBobber(Vector3 position)
        {
            _bobber.position = position;
            _bobberRest = position;
        }

        /// <summary>Resting float point; the bobber bobs around it and dips by <paramref name="dip"/>.</summary>
        public void SetBobberRest(Vector3 rest, float dip)
        {
            _bobberRest = rest;
            _dip = dip;
        }

        public void HideBobber()
        {
            _bobber.gameObject.SetActive(false);
            LineOut = false;
            _line.enabled = false;
        }

        private void LateUpdate()
        {
            // Place the rod in the hand, pointing forward and up.
            Vector3 basePos = hand != null
                ? hand.TransformPoint(handOffset)
                : transform.TransformPoint(new Vector3(0.28f, 0.55f, 0.25f));
            _pitch = Mathf.SmoothDamp(_pitch, TargetPitch, ref _pitchVel, 0.08f);
            float bend = Tension * 22f;
            float shake = Shake * Mathf.Sin(Time.time * 38f) * 3f;
            Quaternion yaw = Quaternion.LookRotation(Flat(transform.forward), Vector3.up);
            Quaternion rot = yaw * Quaternion.Euler(90f - _pitch + bend + shake, 0f, 0f);
            _rodPivot.SetPositionAndRotation(basePos, rot);

            if (_bobber.gameObject.activeSelf && LineOut)
            {
                bool floating = WorldShape.IsWater(_bobberRest.x, _bobberRest.z) && Mathf.Abs(_bobberRest.y - WorldShape.WaterLevel) < 0.2f;
                if (floating)
                {
                    float bob = Mathf.Sin(Time.time * 2.1f) * 0.015f + Mathf.Sin(Time.time * 3.7f) * 0.008f;
                    Vector3 p = _bobberRest + Vector3.up * (bob - _dip);
                    _bobber.position = Vector3.Lerp(_bobber.position, p, 1f - Mathf.Exp(-18f * Time.deltaTime));
                    _bobber.rotation = Quaternion.Euler(Mathf.Sin(Time.time * 1.7f) * 6f, 0f, Mathf.Cos(Time.time * 1.3f) * 6f);
                }
                DrawLine();
            }
        }

        private void DrawLine()
        {
            Vector3 a = _tip.position;
            Vector3 b = _bobber.position + Vector3.up * 0.12f;
            float dist = Vector3.Distance(a, b);
            float sag = Mathf.Lerp(dist * 0.12f, 0.01f, Mathf.Clamp01(Tension * 1.5f));
            Vector3 mid = (a + b) * 0.5f + Vector3.down * sag;
            int n = _line.positionCount;
            for (int i = 0; i < n; i++)
            {
                float t = i / (n - 1f);
                Vector3 p = (1 - t) * (1 - t) * a + 2 * (1 - t) * t * mid + t * t * b;
                _line.SetPosition(i, p);
            }
        }

        private static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude < 1e-4f ? Vector3.forward : v.normalized;
        }
    }
}
