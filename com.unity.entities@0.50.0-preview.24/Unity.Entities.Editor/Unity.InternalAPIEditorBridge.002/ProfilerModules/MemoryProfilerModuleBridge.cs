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
      [ProfilerModuleMetadata("MemoryProfilerModuleBridge")]
      class MemoryProfilerModuleBridge : ProfilerModuleBase
    {
        class MemoryProfilerViewController : ProfilerModuleViewController
        {
            readonly MemoryProfilerModuleBridge m_Bridge;

            public MemoryProfilerViewController(MemoryProfilerModuleBridge bridge) : base(bridge.ProfilerWindow)
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

        const string k_Name = "Entities Memory";
        const string k_IconName = "Profiler.Memory";
        public const int k_DefaultOrderIndex = 101;

#if UNITY_2021_1_OR_NEWER
        static string s_LocalizedName = L10n.Tr(k_Name);
#endif

        [NonSerialized]
        static readonly Lazy<MemoryProfilerModuleBase> s_Module = new Lazy<MemoryProfilerModuleBase>(InstantiateMemoryProfilerModule);

        static MemoryProfilerModuleBase InstantiateMemoryProfilerModule()
        {
            var type = TypeCache.GetTypesDerivedFrom<MemoryProfilerModuleBase>()
                .FirstOrDefault(t => !t.IsAbstract && !t.IsGenericType);
            return (MemoryProfilerModuleBase)Activator.CreateInstance(type);
        }

        public MemoryProfilerModuleBridge(Unity.Profiling.Editor.ProfilerModuleChartType window) :
//#if UNITY_2021_1_OR_NEWER
//            base(window, k_Name, s_LocalizedName, k_IconName, Chart.ChartType.Line)
//#else
            base(window)
//#endif
        {
        }

        public MemoryProfilerModuleBase Module => s_Module.Value;

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

        public ProfilerModuleViewController DetailsViewController => new MemoryProfilerViewController(this);

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
