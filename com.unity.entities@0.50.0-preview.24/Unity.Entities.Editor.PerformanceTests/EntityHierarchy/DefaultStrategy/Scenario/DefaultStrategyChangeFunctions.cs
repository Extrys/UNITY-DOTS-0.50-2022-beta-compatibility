using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Profiling;
using Unity.Transforms;

namespace Unity.Entities.Editor.PerformanceTests
{
    static class DefaultStrategyChangeFunctions
    {
        static readonly EntityQueryDesc k_RootSingles = new EntityQueryDesc
        {
            None = new ComponentType[]{ typeof(Parent), typeof(Child) }
        };

        static readonly EntityQueryDesc k_RootParents = new EntityQueryDesc
        {
            All = new ComponentType[]{ typeof(Child) },
            None = new ComponentType[]{ typeof(Parent) }
        };

        static readonly ProfilerMarker k_ShuffleParentsMarker = new ProfilerMarker($"{nameof(DefaultStrategyChangeFunctions)}.{nameof(PerformParentsShuffle)}");

        // Swaps entities between parents
        public static unsafe void PerformParentsShuffle(World world, EntityHierarchyScenario scenario)
        {
            using (k_ShuffleParentsMarker.Auto())
            {
                world.EntityManager.GetCheckedEntityDataAccess()->EntityComponentStore->IncrementGlobalSystemVersion();

                if (scenario.PercentageOfEntitiesToChange == 0.0f || !world.IsCreated)
                    return;

                if (scenario.MaximumDepth <= 1)
                    throw new NotImplementedException("No implementation for 1-depth shuffle yet.");

                var entityManager = world.EntityManager;
                using var entityQuery = entityManager.CreateEntityQuery(k_RootParents);
                using var query = entityManager.CreateEntityQuery(k_RootSingles);
                var rootParents = entityQuery.ToEntityArrayAsync(Allocator.TempJob, out var rootParentsHandle);
                var rootSingles = query.ToEntityArrayAsync(Allocator.TempJob, out var rootSinglesHandle);
                JobHandle.CombineDependencies(rootParentsHandle, rootSinglesHandle).Complete();

                var workQueue = new NativeQueue<Entity>(Allocator.TempJob);
                var commandBuffer = new EntityCommandBuffer(Allocator.TempJob);

                var job = new ShuffleJob
                {
                    RootParents = rootParents,
                    RootSingles = rootSingles,
                    ParentAccess = entityManager.GetComponentDataFromEntity<Parent>(),
                    ChildrenAccess = entityManager.GetBufferFromEntity<Child>(true),
                    WorkQueue = workQueue,
                    Commands = commandBuffer,
                    ChangesToPerform = scenario.ChangeCount
                };

                job.Run();

                commandBuffer.Playback(entityManager);

                workQueue.Dispose();
                commandBuffer.Dispose();
                rootParents.Dispose();
                rootSingles.Dispose();

                DefaultStrategyHelpers.UpdateParenting(world);
            }
        }

        [BurstCompile]
        struct ShuffleJob : IJob
        {
            [ReadOnly]
            // Root Parents are entities with Children but no Parent (at the root of the hierarchy).
            // Note: This implementation assumes AT LEAST one Root Parent is present.
            public NativeArray<Entity> RootParents;

            [ReadOnly]
            // Root Singles are entities with no Children and no Parent (at the root of the hierarchy).
            // Note: This implementation doesn't assume that any Root Single is present.
            public NativeArray<Entity> RootSingles;

            [ReadOnly]
            public BufferFromEntity<Child> ChildrenAccess;
            public ComponentDataFromEntity<Parent> ParentAccess;

            public NativeQueue<Entity> WorkQueue;
            public EntityCommandBuffer Commands;

            public int ChangesToPerform;

            public void Execute()
            {
                ProcessRootedParents();
                ProcessRemainingParents();
                ProcessRootSingles();
            }

            void ProcessRootedParents()
            {
                // For each parent at the root:
                // 1. Get all children
                // 2. Move first child to root
                // 3. Move subsequent children under first child (attempts to preserve depth)
                // 4. Store children that have children of their own in the work queue

                // Note: Produces 2 or more changes per iteration, make sure we don't bust the allowed amount of change
                for (int parentIndex = 0, parentsCount = RootParents.Length; parentIndex < parentsCount && ChangesToPerform >= 2; ++parentIndex)
                {
                    var children = ChildrenAccess[RootParents[parentIndex]];

                    // First child -> Root
                    var firstChild = children[0].Value;
                    Commands.RemoveComponent<Parent>(firstChild);
                    // Parent changed
                    ChangesToPerform--;

                    // Child changed
                    ChangesToPerform--;

                    if (ChildrenAccess.HasComponent(firstChild))
                        WorkQueue.Enqueue(firstChild);

                    // Parent siblings to first child
                    for (int childIndex = 1, childrenCount = children.Length; childIndex < childrenCount && ChangesToPerform > 0; ++childIndex)
                    {
                        var child = children[childIndex].Value;
                        Commands.SetComponent(child, new Parent {Value = firstChild});
                        // Child changed
                        ChangesToPerform--;

                        if (ChildrenAccess.HasComponent(child))
                            WorkQueue.Enqueue(child);
                    }
                }
            }

            void ProcessRemainingParents()
            {
                // For each remaining parent:
                // 1. Grab a reference to their parent (aka grand-parent)
                // 2. Move first child to grand-parent
                // 3. Move subsequent children under first child (attempts to preserve depth)
                // 4. Store children that have children of their own in the work queue

                while (ChangesToPerform > 0 && !WorkQueue.IsEmpty())
                {
                    var parent = WorkQueue.Dequeue();
                    var grandParent = ParentAccess[parent].Value;
                    var children = ChildrenAccess[parent];

                    // First child -> grand-parent
                    var firstChild = children[0].Value;
                    Commands.SetComponent(firstChild, new Parent {Value = grandParent});
                    ChangesToPerform--;

                    if (ChildrenAccess.HasComponent(firstChild))
                        WorkQueue.Enqueue(firstChild);

                    // Parent siblings to first child
                    for (int childIndex = 1, childrenCount = children.Length; childIndex < childrenCount && ChangesToPerform > 0; ++childIndex)
                    {
                        var child = children[childIndex].Value;
                        Commands.SetComponent(child, new Parent {Value = firstChild});
                        ChangesToPerform--;

                        if (ChildrenAccess.HasComponent(child))
                            WorkQueue.Enqueue(child);
                    }
                }
            }

            void ProcessRootSingles()
            {
                // For each childless entity at the root:
                // 1. Move under first entity in RootParents

                var newParent = RootParents[0];
                for (int i = 0, n = RootSingles.Length; i < n && ChangesToPerform > 0; ++i)
                {
                    var single = RootSingles[i];
                    Commands.AddComponent(single, new Parent { Value = newParent });
                    ChangesToPerform--;
                }
            }
        }
    }
}
