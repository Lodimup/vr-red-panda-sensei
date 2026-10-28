#nullable enable
using UnityEngine;

namespace Sensei.App
{
    public sealed class RayPointer : MonoBehaviour
    {
        private Transform hand = null!; private LineRenderer line = null!;
        public void Initialize(Transform source)
        {
            hand = source; line = gameObject.AddComponent<LineRenderer>(); line.positionCount = 2; line.startWidth = 0.003f; line.endWidth = 0.001f;
            line.material = new Material(Resources.Load<Material>("SenseiLine")); line.startColor = Color.white; line.endColor = Color.white;
        }
        public MenuButton? HitButton() => Physics.Raycast(hand.position, hand.forward, out var hit, 3f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) ? hit.collider.GetComponentInParent<MenuButton>() : null;
        public ObjectCardView? HitCard()
        {
            return Physics.Raycast(hand.position, hand.forward, out var hit, 3f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) ? hit.collider.GetComponentInParent<ObjectCardView>() : null;
        }
        private void Update() { line.SetPosition(0, hand.position); line.SetPosition(1, hand.position + hand.forward * 3f); }
    }
}
