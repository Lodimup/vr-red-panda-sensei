#nullable enable
using Sensei.Engine.Learner;
using Sensei.Engine.Tasks;
using TMPro;
using UnityEngine;

namespace Sensei.App
{
    public sealed class TaskCard : MonoBehaviour
    {
        private TextMeshPro text = null!; private TaskView? task; private SupportSettings support = new SupportSettings();
        public void Initialize(Transform area, TMP_FontAsset? font) { transform.SetParent(area, false); transform.localPosition = new Vector3(0, 1.35f, 0); PanelText.Surface(transform, "Task background", Vector3.zero, new Vector3(.65f, .25f, .01f)); text = PanelText.Create(transform, "Task", new Vector3(0, 0, -.01f), new Vector2(.62f, .23f), 2.8f, font); gameObject.SetActive(false); }
        public void Show(TaskView? value) { task = value; Refresh(); }
        public void SetSupport(SupportSettings value) { support = value; Refresh(); }
        private void Refresh() { if (task == null || task.Status == "ended") { gameObject.SetActive(false); return; } gameObject.SetActive(true); var ja = support.ShowJapanese ? task.Instruction.Japanese : null; var en = support.ShowTranslation ? task.Instruction.English : null; text.text = $"{ja}\n<size=70%>{en}\n{(task.Status == "completed" ? "completed ✓" : task.Status)}</size>"; }
    }
}
