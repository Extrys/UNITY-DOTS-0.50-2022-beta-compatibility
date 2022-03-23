using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.Editor.Bridge
{
    abstract class StructuralChangesProfilerModuleBase
    {
        public bool IsRecording { get; set; }
        public abstract string ProfilerCategoryName { get; }
        public abstract string[] ProfilerCounterNames { get; }
        public abstract VisualElement CreateView(Rect area);
        public virtual void Dispose(bool disposing) { }
        public abstract void SelectedFrameIndexChanged(long index);
        public abstract void Update();
        public abstract void Clear();
    }
}
