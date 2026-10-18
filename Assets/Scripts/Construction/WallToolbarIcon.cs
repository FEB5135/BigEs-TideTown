using UnityEngine;
using UnityEngine.UI;

namespace IdleTown.Construction
{
    // Vector-style icons built with standard UI images, so they share the cards' canvas and masking behavior.
    [RequireComponent(typeof(RectTransform))]
    public sealed class WallToolbarIcon : MonoBehaviour
    {
        public void Initialize(int kind)
        {
            if (kind == 3)
            {
                var handle = Block(.40f, .12f, .12f, .73f, new Color(.38f, .20f, .10f));
                handle.localRotation = Quaternion.Euler(0, 0, -38);
                Block(.42f, .64f, .5f, .24f, new Color(.32f, .33f, .30f));
                Block(.43f, .76f, .46f, .1f, new Color(.65f, .64f, .55f));
                return;
            }
            var edge = kind == 0 ? new Color(.30f, .15f, .07f) : new Color(.30f, .30f, .26f);
            Block(.05f, .12f, .9f, .68f, edge);
            if (kind == 0)
            {
                for (int i = 0; i < 5; i++) Block(.09f + i * .17f, .17f, .14f, .57f, new Color(.66f + (i % 2) * .08f, .39f, .18f));
                Block(.07f, .24f, .86f, .09f, edge);
                Block(.07f, .59f, .86f, .09f, edge);
            }
            else
            {
                for (int row = 0; row < 3; row++)
                    for (int col = 0; col < 3; col++)
                        Block(.09f + col * .28f, .17f + row * .195f, .25f, .17f,
                            new Color(.65f + (col + row) % 2 * .08f, .64f, .55f));
                if (kind == 2)
                {
                    var metal = new Color(.40f, .30f, .17f);
                    Block(.12f, .11f, .10f, .73f, metal);
                    Block(.78f, .11f, .10f, .73f, metal);
                    var brace = Block(.12f, .43f, .78f, .10f, metal);
                    brace.localRotation = Quaternion.Euler(0, 0, 35);
                }
            }
        }

        RectTransform Block(float x, float y, float width, float height, Color tint)
        {
            var rect = new GameObject("Icon detail", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
            rect.SetParent(transform, false);
            rect.anchorMin = new Vector2(x, y);
            rect.anchorMax = new Vector2(x + width, y + height);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            var image = rect.GetComponent<Image>();
            image.color = tint;
            image.raycastTarget = false;
            return rect;
        }
    }
}
