#nullable enable
using System;
using Sensei.App.Core;
using TMPro;
using UnityEngine;
using UnityEngine.XR;

namespace Sensei.App
{
    public sealed class MenuButton : MonoBehaviour
    {
        public Action? Press;
    }
    public sealed class MenuPanel : MonoBehaviour
    {
        private readonly MenuState state = new MenuState(); private Transform hand = null!, head = null!; private EngineHost host = null!; private TextMeshPro status = null!, summary = null!, reset = null!;
        public void Initialize(Transform handTransform, Transform headTransform, EngineHost owner, TMP_FontAsset? font)
        {
            host = owner; hand = handTransform; head = headTransform; transform.SetParent(hand, false); transform.localPosition = new Vector3(0, .18f, .12f); transform.localRotation = Quaternion.Euler(-25, 0, 0);
            PanelText.Surface(transform, "Menu background", Vector3.zero, new Vector3(.56f, .74f, .01f)).GetComponent<Renderer>().sharedMaterial.renderQueue = 3100;
            status = Foreground(PanelText.Create(transform, "Service status", new Vector3(0, .30f, -.012f), new Vector2(.52f, .1f), 0.20f, font));
            for (var i = 0; i < MenuState.Labels.Length; i++) { var index = i; Button(MenuState.Labels[i], .21f - i * .055f, font, async () => { summary.text = "Saving…"; summary.text = await host.SetBand(MenuState.BandFor(index)) ? "Starting point: " + MenuState.Labels[index] : "Could not save starting point."; }); }
            Button("Explain in English", -.08f, font, async () => { summary.text = "Asking Sensei…"; summary.text = await host.EventTurn("help_english") ? "" : "Could not ask Sensei."; });
            Button("Session summary", -.135f, font, async () => { summary.text = "Loading…"; try { summary.text = await host.Summary(); } catch (Exception) { summary.text = "Could not load the summary."; } });
            reset = Button("Reset progress", -.19f, font, async () => { if (state.PressReset()) { reset.text = "Reset progress"; summary.text = "Resetting…"; summary.text = await host.ResetProgress() ? "Progress reset." : "Could not reset progress."; } else reset.text = "Confirm reset progress"; });
            summary = Foreground(PanelText.Create(transform, "Summary", new Vector3(0, -.28f, -.012f), new Vector2(.5f, .12f), 0.18f, font));
            gameObject.SetActive(false);
        }
        private TextMeshPro Button(string label, float y, TMP_FontAsset? font, Action action)
        {
            var go = new GameObject(label); go.transform.SetParent(transform, false); go.transform.localPosition = new Vector3(0, y, -.015f);
            var surface = PanelText.Surface(go.transform, "Button background", new Vector3(0, 0, .001f), new Vector3(.5f, .046f, .004f)).GetComponent<Renderer>(); surface.sharedMaterial.color = new Color(.27f, .24f, .21f); surface.sharedMaterial.renderQueue = 3101;
            var box = go.AddComponent<BoxCollider>(); box.size = new Vector3(.5f, .048f, .02f); go.AddComponent<MenuButton>().Press = () => { if (label != "Reset progress") { state.CancelReset(); reset.text = "Reset progress"; } action(); };
            var text = Foreground(PanelText.Create(go.transform, "Label", new Vector3(0, 0, -.005f), new Vector2(.49f, .048f), 0.20f, font)); text.text = label; return text;
        }
        private static TextMeshPro Foreground(TextMeshPro text) { var material = new Material(text.fontSharedMaterial); material.renderQueue = 3102; text.fontSharedMaterial = material; return text; }
        public void Toggle()
        {
            state.Toggle();
            if (state.IsOpen)
            {
                var device = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
                var tracked = device.isValid && device.TryGetFeatureValue(CommonUsages.isTracked, out var value) && value;
                // RULING(orchestrator 2026-10-28): untracked left controller uses a head-relative menu.
                if (MenuState.PlacementFor(tracked) == MenuPlacement.InFrontOfHead) { transform.SetParent(head, false); transform.localPosition = new Vector3(0, -.15f, .6f); transform.localRotation = Quaternion.identity; }
                else { transform.SetParent(hand, false); transform.localPosition = new Vector3(0, .18f, .12f); transform.localRotation = Quaternion.Euler(-25, 0, 0); }
            }
            gameObject.SetActive(state.IsOpen); reset.text = "Reset progress"; RefreshStatus();
        }
        public void RefreshStatus() { if (status != null) status.text = $"LLM: {host.ModelStatus}\nVoice: {host.VoiceStatus}   Recognizer: {host.RecognizerStatus}"; }
    }
}
