using NUnit.Framework;
using Unity.Collections;
using Unity.Entities.Editor.Tests;
using Unity.PerformanceTesting;

namespace Unity.Entities.Editor.PerformanceTests
{
    [TestFixture]
    [Category(Categories.Performance)]
    class EntityDifferPerformanceTests : DifferTestFixture
    {
        [Test, Performance]
        public void EntityDiffer_Spawn([Values(1000, 10_000, 100_000, 250_000, 500_000, 750_000, 1_000_000)] int entityCount)
        {
            var entities = CreateEntitiesWithMockSharedComponentData(entityCount, Allocator.TempJob, i => i % 100, typeof(EcsTestData), typeof(EcsTestData2), typeof(EcsTestSharedComp));
            var sharedComponentCount = World.EntityManager.GetSharedComponentCount();
            var query = World.EntityManager.CreateEntityQuery(typeof(EcsTestData));
            var newEntities = new NativeList<Entity>(Allocator.TempJob);
            var missingEntities = new NativeList<Entity>(Allocator.TempJob);
            EntityDiffer differ = null;

            Measure.Method(() => { differ.GetEntityQueryMatchDiffAsync(query, newEntities, missingEntities).Complete(); })
                .SetUp(() => differ = new EntityDiffer(World))
                .CleanUp(() => differ.Dispose())
                .SampleGroup($"Check over {entityCount} entities using {sharedComponentCount} different shared components")
                .WarmupCount(10)
                .MeasurementCount(150)
                .Run();

            query.Dispose();
            entities.Dispose();
            newEntities.Dispose();
            missingEntities.Dispose();
        }

        [Test, Performance]
        public void EntityDiffer_NoChange([Values(1000, 10_000, 100_000, 500_000, 1_000_000)] int entityCount)
        {
            var entities = CreateEntitiesWithMockSharedComponentData(entityCount, Allocator.TempJob, i => i % 100, typeof(EcsTestData), typeof(EcsTestData2), typeof(EcsTestSharedComp));
            var sharedComponentCount = World.EntityManager.GetSharedComponentCount();
            var query = World.EntityManager.CreateEntityQuery(typeof(EcsTestData));
            var newEntities = new NativeList<Entity>(Allocator.TempJob);
            var missingEntities = new NativeList<Entity>(Allocator.TempJob);
            EntityDiffer differ = new EntityDiffer(World);

            Measure.Method(() => { differ.GetEntityQueryMatchDiffAsync(query, newEntities, missingEntities).Complete(); })
                .SampleGroup($"Check over {entityCount} entities using {sharedComponentCount} different shared components")
                .WarmupCount(10)
                .MeasurementCount(150)
                .Run();

            query.Dispose();
            entities.Dispose();
            newEntities.Dispose();
            missingEntities.Dispose();
            differ.Dispose();
        }

        [Test, Performance]
        public unsafe void EntityDiffer_Change(
            [Values(1000, 10_000, 100_000, 500_000, 1_000_000)] int entityCount,
            [Values(5_000, 10_000)] int changeCount)
        {
            var entities = CreateEntitiesWithMockSharedComponentData(entityCount, Allocator.TempJob, i => i % 100, typeof(EcsTestData), typeof(EcsTestData2), typeof(EcsTestSharedComp));
            if (changeCount > entityCount)
                changeCount = entityCount;

            var movingEntities = new NativeArray<Entity>(entities.GetSubArray(0, changeCount), Allocator.TempJob);
            entities.Dispose();

            var sharedComponentCount = World.EntityManager.GetSharedComponentCount();
            var query = World.EntityManager.CreateEntityQuery(typeof(EcsTestData));
            var newEntities = new NativeList<Entity>(Allocator.TempJob);
            var missingEntities = new NativeList<Entity>(Allocator.TempJob);
            var differ = new EntityDiffer(World);

            Measure.Method(() => { differ.GetEntityQueryMatchDiffAsync(query, newEntities, missingEntities).Complete(); })
                .SampleGroup($"Check over {entityCount} entities using {sharedComponentCount} different shared components")
                .SetUp(() =>
                {
                    World.EntityManager.GetCheckedEntityDataAccess()->EntityComponentStore->IncrementGlobalSystemVersion();

                    World.EntityManager.DestroyEntity(movingEntities);
                    CreateEntitiesWithMockSharedComponentData(movingEntities, i => i % 100, typeof(EcsTestData), typeof(EcsTestData2), typeof(EcsTestSharedComp));
                })
                .WarmupCount(10)
                .MeasurementCount(150)
                .Run();

            query.Dispose();
            movingEntities.Dispose();
            newEntities.Dispose();
            missingEntities.Dispose();
            differ.Dispose();
        }
    }
}
