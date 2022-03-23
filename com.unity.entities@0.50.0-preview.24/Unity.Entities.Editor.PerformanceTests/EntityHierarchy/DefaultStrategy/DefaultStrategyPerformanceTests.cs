using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Runtime;
using JetBrains.Annotations;
using NUnit.Framework;
using Unity.Entities.Editor.Tests;
using Unity.PerformanceTesting;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.TestTools;

namespace Unity.Entities.Editor.PerformanceTests
{
    [TestFixture]
    [Category(Categories.Performance)]
    class DefaultStrategyPerformanceTests : EntityWindowIntegrationTestFixture
    {
        IDisposable m_ProfilerState;

        WorldGenerator m_CurrentGenerator;
        World m_CurrentWorld;
        EntityHierarchyScenario m_CurrentScenario;

        [OneTimeSetUp]
        public void CaptureProfilerState() => m_ProfilerState = ProfilerState.Capture();

        [OneTimeTearDown]
        public void ResetProfilerState() => m_ProfilerState.Dispose();

        [UnitySetUp, UsedImplicitly]
        public IEnumerator SetUpGC()
        {
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);

            yield return SkipAnEditorFrame();
        }

        [Ignore("Deprecated"), UnityTest, Performance]
        public IEnumerator BasicScenarioRunner(
            [Values(ScenarioId.ScenarioA, ScenarioId.ScenarioB, ScenarioId.ScenarioC, ScenarioId.ScenarioD)]
            ScenarioId scenarioId,
            [Values(ItemsVisibility.AllExpanded, ItemsVisibility.AllCollapsed)]
            ItemsVisibility itemsVisibility)
        {
            const int warmupCount = 2;
            const int measurementsCount = 10;

            m_CurrentScenario = EntityHierarchyScenario.GetScenario(scenarioId, itemsVisibility);
            m_CurrentGenerator = new DefaultStrategyWorldGenerator(m_CurrentScenario);

            // Run Warmup
            for (var i = 0; i < warmupCount; ++i)
            {
                yield return SetUpBeforeMeasurement();
                DefaultStrategyChangeFunctions.PerformParentsShuffle(m_CurrentWorld, m_CurrentScenario);
                MeasureMethod();
                yield return CleanUpAfterMeasurement();
            }

            SetupProfilerReport($"scenario-{scenarioId.ToString()}-{itemsVisibility.ToString()}");

            // Run Measurements
            var groupName = m_CurrentScenario.ToString();
            for (var i = 0; i < measurementsCount; ++i)
            {
                yield return SetUpBeforeMeasurement();

                DefaultStrategyChangeFunctions.PerformParentsShuffle(m_CurrentWorld, m_CurrentScenario);

                StartProfiling();
                using (Measure.Scope(groupName))
                {
                    MeasureMethod();
                }
                StopProfiling();

                yield return CleanUpAfterMeasurement();
            }

            TeardownProfilerReport();
            m_CurrentGenerator.Dispose();
        }

        [Ignore("Deprecated"), UnityTest, Performance]
        public IEnumerator NameSearchScenarioRunner(
            [Values(ScenarioId.ScenarioA, ScenarioId.ScenarioB, ScenarioId.ScenarioC)]
            ScenarioId scenarioId,
            // "entity" matches everything, "not_found" matches nothing, "100" matches some of the things
            [Values("entity", "not_found", "100")]
            string pattern)
        {
            const int warmupCount = 2;
            const int measurementsCount = 10;

            // Note: Visibility doesn't matter, but marking it as collapsed takes less time per iteration setup.
            m_CurrentScenario = EntityHierarchyScenario.GetScenario(scenarioId, ItemsVisibility.AllCollapsed);
            m_CurrentGenerator = new DefaultStrategyWorldGenerator(m_CurrentScenario);

            // Run Warmup
            for (var i = 0; i < warmupCount; ++i)
            {
                yield return SetUpBeforeMeasurement();
                SearchElement.Search(pattern);
                MeasureMethod();
                yield return CleanUpAfterMeasurement();
            }

            SetupProfilerReport($"scenario-{scenarioId.ToString()}-search-{pattern}");

            // Run Measurements
            var groupName = m_CurrentScenario.ToString();
            for (var i = 0; i < measurementsCount; ++i)
            {
                yield return SetUpBeforeMeasurement();

                SearchElement.Search(pattern);

                StartProfiling();
                using (Measure.Scope(groupName))
                {
                    MeasureMethod();
                }
                StopProfiling();

                yield return CleanUpAfterMeasurement();
            }

            TeardownProfilerReport();
            m_CurrentGenerator.Dispose();
        }

        void MeasureMethod()
        {
            Window.Update();
            Window.Repaint();
        }

        IEnumerator SetUpBeforeMeasurement()
        {
            yield return SkipAnEditorFrame();

            m_CurrentWorld = m_CurrentGenerator.Get();

            yield return PrepareWindow();
        }

        IEnumerator CleanUpAfterMeasurement()
        {
            yield return SkipAnEditorFrame();

            m_CurrentWorld.Dispose();
            m_CurrentWorld = null;
            Window.ChangeCurrentWorld(null);

            yield return SkipAnEditorFrameAndDiffingUtility();

            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true);

            yield return SkipAnEditorFrame();
        }

        IEnumerator PrepareWindow()
        {
            Window.ChangeCurrentWorld(m_CurrentWorld);

            // Currently, only fully expanded and fully collapsed are supported.
            // More granular visibility may be implemented, if needed.
            if (m_CurrentScenario.PercentageOfVisibleItems == 0.0f)
                FullView.FastCollapseAll();
            else
                FullView.FastExpandAll();

            yield return SkipAnEditorFrameAndDiffingUtility();
            yield return SkipAnEditorFrame();
        }

        [Conditional("SAVE_PERFORMANCE_TESTS_PROFILING_LOGS")]
        static void SetupProfilerReport(string logName)
        {
            var reportDirectory = Path.Combine(Application.dataPath, "../Library/PerformanceReports/");
            Directory.CreateDirectory(reportDirectory);

            Profiler.logFile = Path.GetFullPath(Path.Combine(reportDirectory, $"{logName}-{DateTime.Now:MM-dd HH-mm-ss}"));
            Profiler.enableBinaryLog = true;

            // Allow 1GB instead of default 512MB
            Profiler.maxUsedMemory = 1024 * 1024 * 1024;

            Debug.Log($"Prepared profiler log file: {Profiler.logFile}");
        }

        [Conditional("SAVE_PERFORMANCE_TESTS_PROFILING_LOGS")]
        static void TeardownProfilerReport()
        {
            Profiler.enabled = false;
            Profiler.enableBinaryLog = false;
            Profiler.logFile = "";
        }

        [Conditional("SAVE_PERFORMANCE_TESTS_PROFILING_LOGS")]
        static void StartProfiling() => Profiler.enabled = true;

        [Conditional("SAVE_PERFORMANCE_TESTS_PROFILING_LOGS")]
        static void StopProfiling() => Profiler.enabled = false;

        class ProfilerState : IDisposable
        {
            readonly bool m_IsEnabled;
            readonly string m_LogFile;
            readonly bool m_IsBinaryLoggingEnabled;
            readonly int m_MaxBufferSize;

            ProfilerState()
            {
                m_IsEnabled = Profiler.enabled;
                m_LogFile = Profiler.logFile;
                m_IsBinaryLoggingEnabled = Profiler.enableBinaryLog;
                m_MaxBufferSize = Profiler.maxUsedMemory;
            }

            public static IDisposable Capture() => new ProfilerState();

            public void Dispose()
            {
                Profiler.enabled = m_IsEnabled;
                Profiler.logFile = m_LogFile;
                Profiler.enableBinaryLog = m_IsBinaryLoggingEnabled;
                Profiler.maxUsedMemory = m_MaxBufferSize;
            }
        }
    }
}
