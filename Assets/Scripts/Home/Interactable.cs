using System.Collections.Generic;
using UnityEngine;

namespace UntitledGame.Home
{
    /// <summary>Something the player can use with F when standing next to it.</summary>
    public abstract class Interactable : MonoBehaviour
    {
        private static readonly List<Interactable> All = new List<Interactable>();

        [SerializeField] protected float radius = 1.9f;

        public abstract string Prompt { get; }
        public virtual bool Available => true;
        public abstract void Interact();

        protected virtual void OnEnable() => All.Add(this);
        protected virtual void OnDisable() => All.Remove(this);

        public static Interactable Nearest(Vector3 pos)
        {
            Interactable best = null;
            float bestD = float.MaxValue;
            foreach (var i in All)
            {
                if (!i.Available) continue;
                float d = Vector3.Distance(pos, i.transform.position);
                if (d < i.radius && d < bestD)
                {
                    bestD = d;
                    best = i;
                }
            }
            return best;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() => All.Clear();
    }
}
