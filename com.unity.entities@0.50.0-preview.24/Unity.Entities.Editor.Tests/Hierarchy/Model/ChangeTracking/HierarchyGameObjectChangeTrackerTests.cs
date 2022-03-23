using NUnit.Framework;
using Unity.Collections;
using Unity.Jobs;

namespace Unity.Entities.Editor.Tests
{
    class HierarchyGameObjectChangeTrackerTests
    {
        [Test]
        public void HierarchyGameObjectChangeTracker_EventQueueFiltering()
        {
            using var l = new NativeList<GameObjectChangeTrackerEvent>(Allocator.TempJob);

            new HierarchyGameObjectChangeTracker.RecordEventJob { Events = l, Event = new GameObjectChangeTrackerEvent(GameObjectChangeTrackerEvent.EventType.CreatedOrChanged, 1) }.Run();
            new HierarchyGameObjectChangeTracker.RecordEventJob { Events = l, Event = new GameObjectChangeTrackerEvent(GameObjectChangeTrackerEvent.EventType.CreatedOrChanged, 2) }.Run();
            new HierarchyGameObjectChangeTracker.RecordEventJob { Events = l, Event = new GameObjectChangeTrackerEvent(GameObjectChangeTrackerEvent.EventType.Moved, 1, 2) }.Run();
            new HierarchyGameObjectChangeTracker.RecordEventJob { Events = l, Event = new GameObjectChangeTrackerEvent(GameObjectChangeTrackerEvent.EventType.Destroyed, 1) }.Run();

            Assert.That(l.ToArray(), Is.EquivalentTo(new[]
            {
                new GameObjectChangeTrackerEvent(GameObjectChangeTrackerEvent.EventType.CreatedOrChanged | GameObjectChangeTrackerEvent.EventType.Moved | GameObjectChangeTrackerEvent.EventType.Destroyed, 1, 0),
                new GameObjectChangeTrackerEvent(GameObjectChangeTrackerEvent.EventType.CreatedOrChanged, 2)
            }));
        }

    }
}
