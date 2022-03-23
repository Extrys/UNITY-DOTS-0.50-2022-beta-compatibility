using NUnit.Framework;
using Unity.Collections;
using Unity.Jobs;
using Unity.PerformanceTesting;

namespace Unity.Entities.Editor.PerformanceTests
{
    [TestFixture]
    [Category(Categories.Performance)]
    class HierarchyGameObjectChangeTrackerPerformanceTests
    {
        [Test, Performance]
        public void AddEventToAccumulator([Values(100, 1000, 10_000, 100_000, 500_000)] int initialQueueSize)
        {
            using var events = new NativeList<GameObjectChangeTrackerEvent>(2048, Allocator.Persistent);
            for (var i = 0; i < initialQueueSize; i++)
            {
                events.Add(new GameObjectChangeTrackerEvent(GameObjectChangeTrackerEvent.EventType.CreatedOrChanged, i));
            }

            Measure.Method(() =>
            {
                new HierarchyGameObjectChangeTracker.RecordEventJob { Events = events, Event = new GameObjectChangeTrackerEvent(GameObjectChangeTrackerEvent.EventType.Destroyed, initialQueueSize - 1) }.Run();
            })
            .WarmupCount(5)
            .MeasurementCount(100)
            .Run();
        }
    }
}
