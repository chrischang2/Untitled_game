using System.Collections.Generic;
using UnityEngine;

namespace UntitledGame.Economy
{
    /// <summary>Marks a displayed item on a market stall; the UI shows its price when you're close.</summary>
    public class PriceTag : MonoBehaviour
    {
        public string itemId;

        public static readonly List<PriceTag> All = new List<PriceTag>();
        /// <summary>Every tag in the scene, shown or hidden (ShopStock hides goods that aren't on sale yet).</summary>
        public static readonly List<PriceTag> Known = new List<PriceTag>();

        private void Awake() => Known.Add(this);
        private void OnDestroy() => Known.Remove(this);
        private void OnEnable() => All.Add(this);
        private void OnDisable() => All.Remove(this);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            All.Clear();
            Known.Clear();
        }
    }
}
