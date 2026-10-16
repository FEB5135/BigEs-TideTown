using System;
using UnityEngine;

namespace IdleTown.Construction
{
    /// <summary>
    /// Resource-team integration point. TrySpend must check and deduct both resources as one operation.
    /// </summary>
    public interface IWallResourceStore
    {
        int Wood { get; }
        int Stone { get; }
        bool CanAfford(int wood, int stone);
        bool TrySpend(int wood, int stone);
    }

    public sealed class SimpleWallResourceStore : MonoBehaviour, IWallResourceStore
    {
        [SerializeField, Min(0)] int startingWood = 60;
        [SerializeField, Min(0)] int startingStone = 30;

        public int Wood { get; private set; }
        public int Stone { get; private set; }
        public event Action Changed;

        void Awake()
        {
            Wood = Mathf.Max(0, startingWood);
            Stone = Mathf.Max(0, startingStone);
        }

        public bool CanAfford(int wood, int stone)
        {
            return wood >= 0 && stone >= 0 && Wood >= wood && Stone >= stone;
        }

        public bool TrySpend(int wood, int stone)
        {
            if (!CanAfford(wood, stone))
                return false;

            Wood -= wood;
            Stone -= stone;
            Changed?.Invoke();
            return true;
        }

        public void Add(int wood, int stone)
        {
            Wood = (int)Math.Min(int.MaxValue, Math.Max(0L, (long)Wood + wood));
            Stone = (int)Math.Min(int.MaxValue, Math.Max(0L, (long)Stone + stone));
            Changed?.Invoke();
        }
    }
}
