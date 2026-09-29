using UnityEngine;
using UntitledGame.Core;

namespace UntitledGame.Environment
{
    /// <summary>Gentle bobbing/rolling for things floating on the lake.</summary>
    public class Floater : MonoBehaviour
    {
        [SerializeField] private float bobHeight = 0.04f;
        [SerializeField] private float rollDegrees = 2.5f;
        [SerializeField] private float speed = 1f;

        private Vector3 _basePos;
        private Quaternion _baseRot;
        private float _phase;

        public void Configure(float bob, float roll, float spd)
        {
            bobHeight = bob;
            rollDegrees = roll;
            speed = spd;
        }

        private void Start()
        {
            _basePos = transform.localPosition;
            _baseRot = transform.localRotation;
            _phase = Random.value * 10f;
        }

        private void Update()
        {
            float t = Time.time * speed + _phase;
            transform.localPosition = _basePos + Vector3.up * (Mathf.Sin(t * 1.3f) * bobHeight);
            transform.localRotation = _baseRot * Quaternion.Euler(Mathf.Sin(t * 0.9f) * rollDegrees, 0f, Mathf.Cos(t * 1.1f) * rollDegrees);
        }
    }
}
