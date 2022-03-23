using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Profiling.Editor;
using UnityEditor;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEditorInternal.Profiling;
using UnityEngine.UIElements;

namespace Unity.Editor.Bridge
{
      [ProfilerModuleMetadata("StructuralChangesProfilerModuleBridge")]
    class StructuralChangesProfilerModuleBridge : ProfilerModuleBase
    {
        class StructuralChangesProfilerViewController : ProfilerModuleViewController
        {
            readonly StructuralChangesProfilerModuleBridge m_Bridge;

            public StructuralChangesProfilerViewController(StructuralChangesProfilerModuleBridge bridge) : base(bridge.ProfilerWindow)
            {
                m_Bridge = bridge;
                ProfilerWindow.SelectedFrameIndexChanged += OnSelectedFrameIndexChanged;
            }

            protected override VisualElement CreateView()
            {
                var view = m_Bridge.Module.CreateView(ProfilerWindow.position);
                OnSelectedFrameIndexChanged(ProfilerWindow.selectedFrameIndex);
                return view;
            }

            protected override void Dispose(bool disposing)
            {
                if (!disposing)
                    return;

                ProfilerWindow.SelectedFrameIndexChanged -= OnSelectedFrameIndexChanged;
                m_Bridge.Module.Dispose(disposing);
                base.Dispose(disposing);
            }

            void OnSelectedFrameIndexChanged(long index)
            {
                m_Bridge.Module.IsRecording = m_Bridge.IsRecording;
                m_Bridge.Module.SelectedFrameIndexChanged(index);
            }
        }

        const string k_Name = "Entities Structural Changes";
        const string k_IconName = "Profiler.CPU";
        public const int k_DefaultOrderIndex = 100;

#if UNITY_2021_1_OR_NEWER
        static string s_LocalizedName = L10n.Tr(k_Name);
#endif

        [NonSerialized]
        static readonly Lazy<StructuralChangesProfilerModuleBase> s_Module = new Lazy<StructuralChangesProfilerModuleBase>(InstantiateStructuralChangesProfilerModule);

        static StructuralChangesProfilerModuleBase InstantiateStructuralChangesProfilerModule()
        {
            var type = TypeCache.GetTypesDerivedFrom<StructuralChangesProfilerModuleBase>()
                .FirstOrDefault(t => !t.IsAbstract && !t.IsGenericType);
            return (StructuralChangesProfilerModuleBase)Activator.CreateInstance(type);
        }

        public StructuralChangesProfilerModuleBridge(Unity.Profiling.Editor.ProfilerModuleChartType window) :
//#if UNITY_2021_1_OR_NEWER
//            base(window, k_Name, s_LocalizedName, k_IconName, Chart.ChartType.StackedFill)
//#else
            base(window)
//#endif
        {
        }

        public StructuralChangesProfilerModuleBase Module => s_Module.Value;

        public bool IsRecording
        {
            get
            {
                if (!ProfilerWindow.IsSetToRecord())
                    return false;

                if (ProfilerDriver.IsConnectionEditor())
                    return (EditorApplication.isPlaying && !EditorApplication.isPaused) || ProfilerDriver.profileEditor;

                return true;
            }
        }

        private protected override int defaultOrderIndex => k_DefaultOrderIndex;

        public ProfilerModuleViewController DetailsViewController => new StructuralChangesProfilerViewController(this);

        protected override List<ProfilerCounterData> CollectDefaultChartCounters()
        {
            var chartCounters = new List<ProfilerCounterData>(Module.ProfilerCounterNames.Length);
            foreach (var counterName in Module.ProfilerCounterNames)
            {
                chartCounters.Add(new ProfilerCounterData()
                {
                    m_Category = Module.ProfilerCategoryName,
                    m_Name = counterName
                });
            }
            return chartCounters;
        }

        internal override void Update()
        {
            base.Update();
            Module.IsRecording = IsRecording;
            Module.Update();
        }

        internal override void Clear()
        {
            base.Clear();
            Module.IsRecording = IsRecording;
            Module.Clear();
        }
    }
}
