#nullable enable
using UnityEngine;

namespace Sensei.App
{
    public sealed class ObjectCardView : MonoBehaviour
    {
        public string InstanceId { get; private set; } = "";
        public string CatalogId { get; private set; } = "";
        private Transform head = null!; private Transform image = null!; private LineRenderer outline = null!; private HighlightView glow = null!;
        private Vector3 from; private Vector3 target; private float targetScale = 1; private float changedAt; private float bornAt; private float removeAt = -1;
        public bool Dragging { get; set; }
        public void Initialize(string id, string catalog, Transform headTransform)
        {
            InstanceId = id; CatalogId = catalog; head = headTransform; bornAt = Time.time;
            image = Quad("Image", transform); var tex = Resources.Load<Texture2D>("Objects/" + catalog + "_card");
            var renderer = image.GetComponent<Renderer>(); renderer.material = new Material(Resources.Load<Material>("SenseiCard")); renderer.material.mainTexture = tex;
            float aspect = tex != null ? (float)tex.width / tex.height : 1; image.localScale = new Vector3(0.2f * aspect, 0.2f, 1); image.localPosition = new Vector3(0, 0.1f, 0);
            var collider = gameObject.AddComponent<BoxCollider>(); collider.center = new Vector3(0, 0.1f, 0); collider.size = new Vector3(0.2f * aspect, 0.2f, 0.025f);
            outline = Ring("Selection", image, false); outline.gameObject.SetActive(false);
            var ring = Ring("Highlight", image, true); glow = ring.gameObject.AddComponent<HighlightView>(); glow.Initialize(ring);
        }
        private static Transform Quad(string name, Transform parent)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad); go.name = name; Destroy(go.GetComponent<Collider>()); go.transform.SetParent(parent, false);
            go.GetComponent<Renderer>().material = new Material(Resources.Load<Material>("SenseiCard")); return go.transform;
        }
        private static LineRenderer Ring(string name, Transform parent, bool round)
        {
            var obj = new GameObject(name); obj.transform.SetParent(parent, false); obj.transform.localPosition = new Vector3(0, 0, 0.02f);
            var line = obj.AddComponent<LineRenderer>(); line.useWorldSpace = false; line.loop = true;
            line.material = new Material(Resources.Load<Material>("SenseiCard")); line.widthMultiplier = round ? 0.06f : 0.015f;
            if (round)
            {
                line.positionCount = 64;
                for (var i = 0; i < 64; i++) { var a = i * Mathf.PI * 2 / 64; line.SetPosition(i, new Vector3(Mathf.Cos(a) * 0.575f, Mathf.Sin(a) * 0.575f, 0)); }
            }
            else
            {
                line.positionCount = 4;
                line.SetPositions(new[] { new Vector3(-0.52f, -0.52f, 0), new Vector3(0.52f, -0.52f, 0), new Vector3(0.52f, 0.52f, 0), new Vector3(-0.52f, 0.52f, 0) });
                line.startColor = line.endColor = Color.white;
            }
            return line;
        }
        public void SetTarget(Transform parent, Vector3 position, float scale, bool selected)
        {
            if (transform.parent != parent) transform.SetParent(parent, true);
            from = transform.localPosition; target = position; targetScale = scale; changedAt = Time.time; outline.gameObject.SetActive(selected);
        }
        public void Highlight(int durationMs) => glow.Show(durationMs);
        public void Remove() { removeAt = Time.time; GetComponent<Collider>().enabled = false; }
        private void Update()
        {
            if (!Dragging) transform.localPosition = Vector3.Lerp(from, target, Mathf.Clamp01((Time.time - changedAt) / 0.25f));
            var direction = head.position - transform.position; direction.y = 0; if (direction.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(-direction);
            var size = Mathf.Clamp01((Time.time - bornAt) / 0.2f); if (removeAt >= 0) { size = 1 - Mathf.Clamp01((Time.time - removeAt) / 0.15f); if (size <= 0) Destroy(gameObject); }
            transform.localScale = Vector3.one * targetScale * size;

        }
    }
}
