using UnityEngine;
using UntitledGame.Core;

namespace UntitledGame.Environment
{
    /// <summary>Code-built particle bursts (splashes, ripple rings, sparkles) shared by gameplay.</summary>
    public static class Effects
    {
        private static ParticleSystem _splash, _ring, _sparkle;

        private static ParticleSystem GetSplash()
        {
            if (_splash != null) return _splash;
            _splash = Create("FX Splash", GameAssets.Instance.splashMaterial);
            var main = _splash.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 3.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.16f);
            main.gravityModifier = 1.1f;
            var shape = _splash.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 28f;
            shape.radius = 0.08f;
            shape.rotation = new Vector3(-90f, 0f, 0f);
            var col = _splash.colorOverLifetime;
            col.enabled = true;
            col.color = FadeOut();
            return _splash;
        }

        private static ParticleSystem GetRing()
        {
            if (_ring != null) return _ring;
            _ring = Create("FX Ring", GameAssets.Instance.rippleMaterial);
            var main = _ring.main;
            main.startLifetime = 1.4f;
            main.startSpeed = 0f;
            main.startSize = 0.3f;
            var size = _ring.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.3f, 1f, 3.2f));
            var col = _ring.colorOverLifetime;
            col.enabled = true;
            col.color = FadeOut();
            var r = _ring.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            return _ring;
        }

        private static ParticleSystem GetSparkle()
        {
            if (_sparkle != null) return _sparkle;
            _sparkle = Create("FX Sparkle", GameAssets.Instance.sparkleMaterial);
            var main = _sparkle.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.1f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 2.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.3f);
            main.gravityModifier = -0.1f;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI);
            var shape = _sparkle.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.25f;
            var col = _sparkle.colorOverLifetime;
            col.enabled = true;
            col.color = FadeOut();
            return _sparkle;
        }

        private static ParticleSystem Create(string name, Material mat)
        {
            var go = new GameObject(name);
            Object.DontDestroyOnLoad(go);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.maxParticles = 400;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission;
            em.rateOverTime = 0f;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return ps;
        }

        private static ParticleSystem.MinMaxGradient FadeOut()
        {
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.5f), new GradientAlphaKey(0f, 1f) });
            return new ParticleSystem.MinMaxGradient(g);
        }

        private static void Emit(ParticleSystem ps, Vector3 pos, int count)
        {
            var p = new ParticleSystem.EmitParams { position = pos, applyShapeToPosition = true };
            ps.Emit(p, count);
        }

        public static void Splash(Vector3 pos, float strength = 1f)
        {
            if (GameAssets.Instance == null) return;
            Emit(GetSplash(), pos + Vector3.up * 0.03f, Mathf.RoundToInt(Mathf.Lerp(5, 22, strength)));
            Ring(pos, strength);
        }

        public static void Ring(Vector3 pos, float strength = 1f)
        {
            if (GameAssets.Instance == null) return;
            var ps = GetRing();
            var p = new ParticleSystem.EmitParams
            {
                position = new Vector3(pos.x, WorldShape.WaterLevel + 0.03f, pos.z),
                startSize = Mathf.Lerp(0.2f, 0.5f, strength),
            };
            ps.Emit(p, 1);
        }

        public static void Sparkle(Vector3 pos, int count = 18)
        {
            if (GameAssets.Instance == null) return;
            Emit(GetSparkle(), pos, count);
        }
    }
}
