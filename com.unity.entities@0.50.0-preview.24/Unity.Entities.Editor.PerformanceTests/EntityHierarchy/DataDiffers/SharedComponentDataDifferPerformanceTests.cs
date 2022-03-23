using NUnit.Framework;
using Unity.Collections;
using Unity.Entities.Editor.Tests;
using Unity.PerformanceTesting;

namespace Unity.Entities.Editor.PerformanceTests
{
    [TestFixture]
    [Category(Categories.Performance)]
    class SharedComponentDataDifferPerformanceTests : DifferTestFixture
    {
        [Test, Performance]
        public void SharedComponentDataDiffer_Spawn([Values(1000, 10_000, 100_000, 250_000, 500_000, 750_000, 1_000_000)] int entityCount)
        {
            var entities = CreateEntitiesWithMockSharedComponentData(entityCount, Allocator.TempJob, i => i % 100, typeof(EcsTestData), typeof(EcsTestSharedComp));
            var sharedComponentCount = World.EntityManager.GetSharedComponentCount();
            var query = World.EntityManager.CreateEntityQuery(typeof(EcsTestData));
            SharedComponentDataDiffer sharedComponentDataDiffer = null;

            Measure.Method(() =>
                {
                    var result = sharedComponentDataDiffer.GatherComponentChanges(World.EntityManager, query, Allocator.TempJob);
                    result.Dispose();
                })
                .SetUp(() =>
                {
                    sharedComponentDataDiffer = new SharedComponentDataDiffer(typeof(EcsTestSharedComp));
                })
                .CleanUp(() =>
                {
                    sharedComponentDataDiffer.Dispose();
                })
                .SampleGroup($"First check over {entityCount} entities using {sharedComponentCount} different shared components")
                .WarmupCount(10)
                .MeasurementCount(100)
                .Run();

            entities.Dispose();
            query.Dispose();
        }

        [Test, Performance]
        public void SharedComponentDataDiffer_NoChanges([Values(0, 1000, 10_000, 100_000, 500_000, 1_000_000)] int entityCount)
        {
            var entities = CreateEntitiesWithMockSharedComponentData(entityCount, Allocator.TempJob, i => i % 100, typeof(EcsTestData), typeof(EcsTestSharedComp));
            var sharedComponentCount = World.EntityManager.GetSharedComponentCount();
            var query = World.EntityManager.CreateEntityQuery(typeof(EcsTestData));
            var sharedComponentDataDiffer = new SharedComponentDataDiffer(typeof(EcsTestSharedComp));

            Measure.Method(() =>
                {
                    var result = sharedComponentDataDiffer.GatherComponentChanges(World.EntityManager, query, Allocator.TempJob);
                    result.Dispose();
                })
                .SampleGroup($"Check over {entityCount} entities using {sharedComponentCount} different shared components")
                .WarmupCount(10)
                .MeasurementCount(100)
                .Run();

            sharedComponentDataDiffer.Dispose();
            entities.Dispose();
            query.Dispose();
        }

        [Test, Performance]
        public unsafe void SharedComponentDataDiffer_Change(
            [Values(1000, 10_000, 100_000, 500_000, 1_000_000)] int entityCount,
            [Values(1000, 5000, 10_000)] int changeCount)
        {
            var entities = CreateEntitiesWithMockSharedComponentData(entityCount, Allocator.TempJob, i => i % 100, typeof(EcsTestData), typeof(EcsTestSharedComp));
            var sharedComponentCount = World.EntityManager.GetSharedComponentCount();
            var query = World.EntityManager.CreateEntityQuery(typeof(EcsTestData));
            var sharedComponentDataDiffer = new SharedComponentDataDiffer(typeof(EcsTestSharedComp));
            var counter = entities.Length;
            if (changeCount > entityCount)
                changeCount = entityCount;

            Measure.Method(() =>
                {
                    var result = sharedComponentDataDiffer.GatherComponentChanges(World.EntityManager, query, Allocator.TempJob);
                    result.Dispose();
                })
                .SetUp(() =>
                {
                    World.EntityManager.GetCheckedEntityDataAccess()->EntityComponentStore->IncrementGlobalSystemVersion();
                    for (var i = 0; i < changeCount; i++)
                    {
                        World.EntityManager.SetSharedComponentData(entities[i], new EcsTestSharedComp { value = counter++ % 100 });
                    }
                })
                .SampleGroup($"{changeCount} changes over {entityCount} entities using {sharedComponentCount} different shared components")
                .WarmupCount(10)
                .MeasurementCount(100)
                .Run();

            entities.Dispose();
            query.Dispose();
            sharedComponentDataDiffer.Dispose();
        }
    }
}
