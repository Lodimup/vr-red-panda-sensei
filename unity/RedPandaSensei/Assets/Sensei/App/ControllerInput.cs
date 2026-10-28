#nullable enable
using Sensei.App.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

namespace Sensei.App
{
    public sealed class ControllerInput : MonoBehaviour
    {
        private EngineHost host = null!; private PracticeAreaView area = null!; private Transform hand = null!; private RayPointer pointer = null!;
        private InputAction trigger = null!, grip = null!, repeat = null!, hint = null!, replace = null!, pace = null!;
        private ObjectCardView? grabbed; private float yDown = -1; private bool replaced;
        public void Initialize(EngineHost engineHost, PracticeAreaView areaView, Transform handTransform)
        {
            host = engineHost; area = areaView; hand = handTransform; pointer = gameObject.AddComponent<RayPointer>(); pointer.Initialize(hand);
            trigger = Action("<XRController>{RightHand}/triggerPressed"); grip = Action("<XRController>{RightHand}/gripPressed");
            repeat = Action("<XRController>{RightHand}/secondaryButton"); hint = Action("<XRController>{LeftHand}/primaryButton"); replace = Action("<XRController>{LeftHand}/secondaryButton"); pace = Action("<XRController>{LeftHand}/thumbstickClicked");
            trigger.performed += _ => { var card = pointer.HitCard(); if (card != null) host.Select(card.InstanceId); };
            grip.performed += _ => { grabbed = pointer.HitCard(); if (grabbed != null) grabbed.Dragging = true; };
            grip.canceled += _ => Release();
            repeat.performed += context => { _ = host.EventTurn("help_repeat"); };
            hint.performed += context => { _ = host.EventTurn("help_hint"); };
            replace.performed += _ => { yDown = Time.time; replaced = false; };
            replace.canceled += _ => { yDown = -1; };
            pace.performed += _ => host.TogglePace();
            foreach (var a in new[] { trigger, grip, repeat, hint, replace, pace }) a.Enable();
        }
        private static InputAction Action(string path) => new InputAction(type: InputActionType.Button, binding: path);
        private void Update()
        {
            if (grabbed != null) grabbed.transform.position = hand.position;
            if (yDown >= 0 && !replaced && Time.time - yDown >= 1) { replaced = true; host.ReplaceArea(); }
        }
        private void Release()
        {
            if (grabbed == null) return;
            var card = grabbed; grabbed = null; card.Dragging = false;
            ObjectCardView? destination = null;
            foreach (var hit in Physics.OverlapSphere(hand.position, 0.08f))
            {
                var candidate = hit.GetComponentInParent<ObjectCardView>();
                if (candidate != null && candidate != card && (candidate.CatalogId == "bag" || candidate.CatalogId == "box")) { destination = candidate; break; }
            }
            var region = destination == null ? PracticeLayout.RegionAt(area.transform.InverseTransformPoint(hand.position)) : null;
            if (destination == null && region == null) return;
            host.Move(card.InstanceId, region, destination != null ? destination.InstanceId : null, ok =>
            {
                if (!ok) { var device = InputDevices.GetDeviceAtXRNode(XRNode.RightHand); device.SendHapticImpulse(0, 0.5f, 0.1f); }
            });
        }
        private void OnDestroy() { foreach (var a in new[] { trigger, grip, repeat, hint, replace, pace }) a?.Dispose(); }
    }
}
