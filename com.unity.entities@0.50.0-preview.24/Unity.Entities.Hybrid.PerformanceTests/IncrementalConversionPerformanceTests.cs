using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities.Tests;
using Unity.Entities.Tests.Conversion;
using Unity.PerformanceTesting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Unity.Entities.Hybrid.PerformanceTests
{
    [TestFixture]
    public class IncrementalConversionPerformanceTests
    {
        private TestWithObjects _Objects;
        private World DestinationWorld;
        private World ConversionWorld;

        [SetUp]
        public void SetUp()
        {
            _Objects.SetUp();
            DestinationWorld = new World("Test World");
        }

        [TearDown]
        public void TearDown()
        {
            ConversionWorld?.Dispose();
            DestinationWorld.Dispose();
            _Objects.TearDown();
        }

        [OneTimeSetUp]
        public void SetUpOnce()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
            SceneManager.SetActiveScene(scene);
        }

        [OneTimeTearDown]
        public void TearDownOnce()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        }

        void InitializeIncrementalConversion(GameObjectConversionUtility.ConversionFlags conversionFlags)
        {
            var settings = new GameObjectConversionSettings(DestinationWorld, conversionFlags)
            {
                Systems = TestWorldSetup.GetDefaultInitSystemsFromEntitiesPackage(WorldSystemFilterFlags.GameObjectConversion).ToList()
            };
            ConversionWorld = GameObjectConversionUtility.InitializeIncrementalConversion(SceneManager.GetActiveScene(), settings);
        }

        private const GameObjectConversionUtility.ConversionFlags WithoutAssignName =
            GameObjectConversionUtility.ConversionFlags.GameViewLiveConversion |
            GameObjectConversionUtility.ConversionFlags.AddEntityGUID;

        const GameObjectConversionUtility.ConversionFlags WithAssignName =
            GameObjectConversionUtility.ConversionFlags.GameViewLiveConversion |
            GameObjectConversionUtility.ConversionFlags.AddEntityGUID |
            GameObjectConversionUtility.ConversionFlags.AssignName;

        static void SwapDeleteAndReconvert(ref IncrementalConversionBatch batch)
        {
            var tmp = batch.DeletedInstanceIds;
            batch.DeletedInstanceIds = batch.ReconvertHierarchyInstanceIds;
            batch.ReconvertHierarchyInstanceIds = tmp;
        }

        [Test, Performance]
        public void IncrementalConversionPerformance_CreateGameObjects([Values(1000)]int n, [Values(WithAssignName, WithoutAssignName)] GameObjectConversionUtility.ConversionFlags conversionFlags)
        {
            InitializeIncrementalConversion(conversionFlags);
            var reconvert = new NativeArray<int>(n, Allocator.TempJob);
            var args = new IncrementalConversionBatch
            {
                ReconvertHierarchyInstanceIds = reconvert,
            };
            args.EnsureFullyInitialized();
            var objs = new List<GameObject>();

            Measure.Method(() =>
            {
                GameObjectConversionUtility.ConvertIncremental(ConversionWorld, conversionFlags, ref args);
            }).SetUp(() =>
            {
                foreach (var go in objs)
                    Object.DestroyImmediate(go);
                SwapDeleteAndReconvert(ref args);
                GameObjectConversionUtility.ConvertIncremental(ConversionWorld, conversionFlags, ref args);
                for (int i = 0; i < n; i++)
                {
                    var obj = _Objects.CreateGameObject();
                    objs.Add(obj);
                    reconvert[i] = obj.GetInstanceID();
                }

                SwapDeleteAndReconvert(ref args);
            }).MeasurementCount(30).Run();
            args.Dispose();
        }

        [Test, Performance]
        public void IncrementalConversionPerformance_RemoveGameObjects([Values(1000)] int n, [Values(WithAssignName, WithoutAssignName)] GameObjectConversionUtility.ConversionFlags conversionFlags)
        {
            InitializeIncrementalConversion(conversionFlags);

            var instanceIds = new NativeArray<int>(n, Allocator.TempJob);
            var objs = new List<GameObject>();
            var args = new IncrementalConversionBatch
            {
                DeletedInstanceIds = instanceIds,
            };
            args.EnsureFullyInitialized();

            Measure.Method(() =>
            {
                GameObjectConversionUtility.ConvertIncremental(ConversionWorld, conversionFlags, ref args);
            }).SetUp(() =>
            {
                for (int i = 0; i < n; i++)
                {
                    var obj = _Objects.CreateGameObject();
                    objs.Add(obj);
                    instanceIds[i] = obj.GetInstanceID();
                }

                SwapDeleteAndReconvert(ref args);
                GameObjectConversionUtility.ConvertIncremental(ConversionWorld, conversionFlags, ref args);
                SwapDeleteAndReconvert(ref args);
                foreach (var go in objs)
                    Object.DestroyImmediate(go);
            }).MeasurementCount(30).Run();
            args.Dispose();
        }
    }
}
