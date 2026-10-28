using UnityEngine;

namespace Sensei.App.Core
{
    public readonly struct PanelRect
    {
        public readonly Vector3 Centre;
        public readonly Vector2 Size;
        public PanelRect(float x, float y, float z, float width, float height) { Centre = new Vector3(x, y, z); Size = new Vector2(width, height); }
        public Rect Bounds => new Rect(Centre.x - Size.x / 2, Centre.y - Size.y / 2, Size.x, Size.y);
    }
    public static class PanelLayout
    {
        public static PanelRect Sensei => new PanelRect(-.85f, 1.125f, .25f, .75f, .75f);
        public static PanelRect Caption => new PanelRect(-.85f, .55f, .10f, .60f, .20f);
        public static PanelRect YouSaid => new PanelRect(-.85f, .40f, .10f, .60f, .08f);
        public static PanelRect Task => new PanelRect(0, 1.25f, .05f, .70f, .22f);
        // RULING(orchestrator 2026-10-28): hint center x = +0.64 m.
        public static PanelRect Hint => new PanelRect(.64f, 1.25f, .05f, .46f, .30f);
        public static PanelRect Status => new PanelRect(0, 1.55f, .10f, .80f, .12f);
    }
}
