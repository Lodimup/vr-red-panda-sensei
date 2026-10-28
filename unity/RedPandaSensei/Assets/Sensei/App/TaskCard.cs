#nullable enable
using Sensei.App.Core;
using Sensei.Engine.Learner;
using Sensei.Engine.Tasks;
using TMPro;
using UnityEngine;

namespace Sensei.App
{
    public sealed class TaskCard : MonoBehaviour
    {
        private string? key; private string? resetTaskId; private long resetSequence;
        private TextMeshPro text = null!; private TaskView? task; private SupportSettings support = new SupportSettings();
        public void Initialize(Transform area, TMP_FontAsset? font, Transform? head = null) { transform.SetParent(area, false); var slot = PanelLayout.Task; transform.localPosition = slot.Centre; PanelText.Surface(transform, "Task background", Vector3.zero, new Vector3(slot.Size.x, slot.Size.y, .01f)); text = PanelText.Create(transform, "Task", new Vector3(0, 0, -.01f), slot.Size - new Vector2(.03f, .02f), 0.30f, font); if (head != null) gameObject.AddComponent<PanelBillboard>().Initialize(head); gameObject.SetActive(false); }
        public void SetKey(string? value) => key = value;
        public void Reset(TaskView? previous) { previous ??= task; if (previous != null) { resetTaskId = previous.TaskId; resetSequence = previous.StartSequence; } task = null; Refresh(); }
        public void Show(TaskView? value) { if (value != null && ((resetTaskId != null && value.TaskId == resetTaskId) || (resetSequence > 0 && value.StartSequence <= resetSequence))) return; task = value; Refresh(); }
        public void SetSupport(SupportSettings value) { support = value; Refresh(); }
        private void Refresh() { if (task == null || task.Status == "ended") { gameObject.SetActive(false); return; } gameObject.SetActive(true); var ja = support.ShowJapanese ? task.Instruction.Japanese : null; var en = support.ShowTranslation ? task.Instruction.English : null; text.text = LogRedactor.Redact($"{ja}\n<size=70%>{en}\n{(task.Status == "completed" ? "completed ✓" : task.Status)}</size>", key); }
    }
}
