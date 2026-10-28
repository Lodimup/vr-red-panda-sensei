using UnityEngine;

namespace Sensei.App
{
    public sealed class PanelBillboard : MonoBehaviour
    {
        private Transform head;
        public void Initialize(Transform target) { head = target; FaceHead(); }
        public void FaceHead() { if (head == null) return; var direction = head.position - transform.position; direction.y = 0; if (direction.sqrMagnitude > .001f) transform.rotation = Quaternion.LookRotation(-direction); }
        private void LateUpdate() => FaceHead();
    }
}
