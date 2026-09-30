using System.Collections.Generic;
using UnityEngine;

namespace UntitledGame.Economy
{
    /// <summary>Marks a displayed item on a market stall; the UI shows its price when you're close.</summary>
    public class PriceTag : MonoBehaviour
    {
        public string itemId;

        public static readonly List<PriceTag> All = new List<PriceTag>();

        private void OnEnable() => All.Add(this);
        private void OnDisable() => All.Remove(this);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() => All.Clear();
    }
}
