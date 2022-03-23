using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Unity.Collections;
using Unity.Jobs;
using Unity.Transforms;

namespace Unity.Entities.Editor.PerformanceTests
{
    [TestFixture]
    [Category(Categories.Performance)]
    class DefaultStrategyScenarioValidation
    {
        [Test]
        public void GettingAWorldFromAGenerator_ReturnsAFreshInstance()
        {
            var scenario = new EntityHierarchyScenario(
                AmountOfEntities.Low,
                AmountOfChange.None,
                AmountOfFragmentation.Low,
                DepthOfStructure.Shallow,
                ItemsVisibility.AllCollapsed,
                "World Generation Validation");

            using (var generator = new DefaultStrategyWorldGenerator(scenario))
            {
                var original = generator.Original;
                var instance1 = generator.Get();
                var instance2 = generator.Get();

                // Original world doesn't get overwritten
                Assert.That(original, Is.SameAs(generator.Original));

                // Get() doesn't return the original instance
                Assert.That(generator.Original, Is.Not.SameAs(instance1).And.Not.SameAs(instance2));

                // Get() returns a fresh instance each time
                Assert.That(instance1, Is.Not.SameAs(instance2));
            }
        }

        [Test]
        [Category(Categories.Performance)]
        public void CreatingWorld_MatchesExpectations(
            // TODO: Generate a *few* scenarios for validation; this combinatorial thingy is too much work
            [Values(AmountOfEntities.Low, AmountOfEntities.Medium, AmountOfEntities.High)]
            AmountOfEntities amountOfEntities,
            [Values(AmountOfFragmentation.Low, AmountOfFragmentation.Medium, AmountOfFragmentation.High)]
            AmountOfFragmentation amountOfFragmentation,
            [Values(DepthOfStructure.Shallow, DepthOfStructure.Deep)]
            DepthOfStructure depthOfStructure
            )
        {
            var scenario = new EntityHierarchyScenario(
                amountOfEntities,
                AmountOfChange.None,
                amountOfFragmentation,
                depthOfStructure,
                ItemsVisibility.AllCollapsed,
                "World Generation Validation");

            using (var generator = new DefaultStrategyWorldGenerator(scenario))
            {
                var world = generator.Get();

                // Validate entity count
                var entityCount = world.EntityManager.UniversalQuery.CalculateEntityCount();
                Assert.That(entityCount, Is.EqualTo(scenario.TotalEntities), "Unexpected entity count.");

                // Validate that each generated EntityGuid is unique
                var guidQuery = world.EntityManager.CreateEntityQuery(typeof(EntityGuid));
                var guids = guidQuery.ToComponentDataArray<EntityGuid>(Allocator.TempJob);
                Assert.That(guids.Length, Is.EqualTo(scenario.TotalEntities), "Unexpected amount of Entity GUIDs.");
                Assert.That(guids.Distinct().Count(), Is.EqualTo(guids.Length), "Repeat Entity GUIDs found.");

                guidQuery.Dispose();
                guids.Dispose();

                // Validate depth
                var depth = GetStructureDepth(world);
                Assert.That(depth, Is.EqualTo(scenario.MaximumDepth), "Unexpected Depth.");

                // Validate segmentation (how many segments are actually created, and do we have at least one chunk per segment)
                var chunkCount = world.EntityManager.UniversalQuery.CalculateChunkCount();
                Assert.That(chunkCount, Is.GreaterThanOrEqualTo(scenario.SegmentsCount), "Unexpected Chunk count");

                var segmentCount = GetUniqueSharedComponentCount<SegmentId>(world);
                Assert.That(segmentCount, Is.EqualTo(scenario.SegmentsCount), "Unexpected Segment count");

                // Validate fragmentation (average chunk usage)
                var averageChunkUtilization = GetAverageChunkUtilization(world);
                var (low, high) = GetExpectedAverageChunkUtilization(amountOfEntities, amountOfFragmentation, depthOfStructure);
                Assert.That(averageChunkUtilization, Is.GreaterThanOrEqualTo(low).And.LessThanOrEqualTo(high), "Unexpected Chunk utilization.");
            }
        }

        static IEnumerable ChangeFunction_CaseProvider()
        {
            // Setting tolerances for how much side-effect is acceptable when performing changes. Note that there can NEVER be less change than expected, only more.
            const float lowChangeTolerance = 0.20f; // Low amounts of change is *especially* sensitive to side-effects
            const float mediumChangeTolerance = 0.075f;
            const float highChangeTolerance = 0.03f;

            // TODO: Add more change functions here, as needed.
            var shuffleFunction = (Action<World, EntityHierarchyScenario>) DefaultStrategyChangeFunctions.PerformParentsShuffle;

            // Extremes
            // NONE
            yield return BuildChangeFunctionCase(shuffleFunction, AmountOfChange.None, AmountOfEntities.Low, AmountOfFragmentation.Low, 0f);
            yield return BuildChangeFunctionCase(shuffleFunction, AmountOfChange.None, AmountOfEntities.Low, AmountOfFragmentation.Medium, 0f);
            yield return BuildChangeFunctionCase(shuffleFunction, AmountOfChange.None, AmountOfEntities.Low, AmountOfFragmentation.High, 0f);
            yield return BuildChangeFunctionCase(shuffleFunction, AmountOfChange.None, AmountOfEntities.Medium, AmountOfFragmentation.Low, 0f);
            yield return BuildChangeFunctionCase(shuffleFunction, AmountOfChange.None, AmountOfEntities.Medium, AmountOfFragmentation.Medium, 0f);
            yield return BuildChangeFunctionCase(shuffleFunction, AmountOfChange.None, AmountOfEntities.Medium, AmountOfFragmentation.High, 0f);
            yield return BuildChangeFunctionCase(shuffleFunction, AmountOfChange.None, AmountOfEntities.High, AmountOfFragmentation.Low, 0f);
            yield return BuildChangeFunctionCase(shuffleFunction, AmountOfChange.None, AmountOfEntities.High, AmountOfFragmentation.Medium, 0f);
            yield return BuildChangeFunctionCase(shuffleFunction, AmountOfChange.None, AmountOfEntities.High, AmountOfFragmentation.High, 0f);

            // ALL
            yield return BuildChangeFunctionCase(shuffleFunction, AmountOfChange.All, AmountOfEntities.Low, AmountOfFragmentation.Low, 0f);
            yield return BuildChangeFunctionCase(shuffleFunction, AmountOfChange.All, AmountOfEntities.Low, AmountOfFragmentation.Medium, 0f);
            yield return BuildChangeFunctionCase(shuffleFunction, AmountOfChange.All, AmountOfEntities.Low, AmountOfFragmentation.High, 0f);
            yield return BuildChangeFunctionCase(shuffleFunction, AmountOfChange.All, AmountOfEntities.Medium, AmountOfFragmentation.Low, 0f);
            yield return BuildChangeFunctionCase(shuffleFunction, AmountOfChange.All, AmountOfEntities.Medium, AmountOfFragmentation.Medium, 0f);
            yield return BuildChangeFunctionCase(shuffleFunction, AmountOfChange.All, AmountOfEntities.Medium, AmountOfFragmentation.High, 0f);
            yield return BuildChangeFunctionCase(shuffleFunction, AmountOfChange.All, AmountOfEntities.High, AmountOfFragmentation.Low, 0f);
            yield return BuildChangeFunctionCase(shuffleFunction, AmountOfChange.All, AmountOfEntities.High, AmountOfFragmentation.Medium, 0f);
            yield return BuildChangeFunctionCase(shuffleFunction, AmountOfChange.All, AmountOfEntities.High, AmountOfFragmentation.High, 0f);

            // The rest
            yield return BuildChangeFunctionCase(shuffleFunction, AmountOfChange.Low, AmountOfEntities.Low, AmountOfFragmentation.Low, lowChangeTolerance);
            yield return BuildChangeFunctionCase(shuffleFunction, AmountOfChange.Low, AmountOfEntities.Low, AmountOfFragmentation.Medium, lowChangeTolerance);
            yield return BuildChangeFunctionCase(shuffleFunction, AmountOfChange.Low, AmountOfEntities.Low, AmountOfFragmentation.High, lowChangeTolerance);
            yield return BuildChangeFunctionCase(shuffleFunction, AmountOfChange.Low, AmountOfEntities.Medium, AmountOfFragmentation.Low, lowChangeTolerance);
            yield return BuildChangeFunctionCase(shuffleFunction, AmountOfChange.Low, AmountOfEntities.Medium, AmountOfFragmentation.Medium, lowChangeTolerance);
            yield return BuildChangeFunctionCase(shuffleFunction, AmountOfChange.Low, AmountOfEntities.Medium, AmountOfFragmentation.High, lowChangeTolerance);
            yield return BuildChangeFunctionCase(shuffleFunction, AmountOfChange.Low, AmountOfEntities.High, AmountOfFragmentation.Low, lowChangeTolerance);
            yield return BuildChangeFunctionCase(shuffleFunction, AmountOfChange.Low, AmountOfEntities.High, AmountOfFragmentation.Medium, lowChangeTolerance);
            yield return BuildChangeFunctionCase(shuffleFunction, AmountOfChange.Low, AmountOfEntities.High, AmountOfFragmentation.High, lowChangeTolerance);

            yield return BuildChangeFunctionCase(shuffleFunction, AmountOfChange.Medium, AmountOfEntities.Low, AmountOfFragmentation.Low, mediumChangeTolerance);
            yield return BuildChangeFunctionCase(shuffleFunction, AmountOfChange.Medium, AmountOfEntities.Low, AmountOfFragmentation.Medium, mediumChangeTolerance);
            yield return BuildChangeFunctionCase(shuffleFunction, AmountOfChange.Medium, AmountOfEntities.Low, AmountOfFragmentation.High, mediumChangeTolerance);
            yield return BuildChangeFunctionCase(shuffleFunction, AmountOfChange.Medium, AmountOfEntities.Medium, AmountOfFragmentation.Low, mediumChangeTolerance);
            yield return BuildChangeFunctionCase(shuffleFunction, AmountOfChange.Medium, AmountOfEntities.Medium, AmountOfFragmentation.Medium, mediumChangeTolerance);
            yield return BuildChangeFunctionCase(shuffleFunction, AmountOfChange.Medium, AmountOfEntities.Medium, AmountOfFragmentation.High, mediumChangeTolerance);
            yield return BuildChangeFunctionCase(shuffleFunction, AmountOfChange.Medium, AmountOfEntities.High, AmountOfFragmentation.Low, mediumChangeTolerance);
            yield return BuildChangeFunctionCase(shuffleFunction, AmountOfChange.Medium, AmountOfEntities.High, AmountOfFragmentation.Medium, mediumChangeTolerance);
            yield return BuildChangeFunctionCase(shuffleFunction, AmountOfChange.Medium, AmountOfEntities.High, AmountOfFragmentation.High, mediumChangeTolerance);

            yield return BuildChangeFunctionCase(shuffleFunction, AmountOfChange.High, AmountOfEntities.Low, AmountOfFragmentation.Low, highChangeTolerance);
            yield return BuildChangeFunctionCase(shuffleFunction, AmountOfChange.High, AmountOfEntities.Low, AmountOfFragmentation.Medium, highChangeTolerance);
            yield return BuildChangeFunctionCase(shuffleFunction, AmountOfChange.High, AmountOfEntities.Low, AmountOfFragmentation.High, highChangeTolerance);
            yield return BuildChangeFunctionCase(shuffleFunction, AmountOfChange.High, AmountOfEntities.Medium, AmountOfFragmentation.Low, highChangeTolerance);
            yield return BuildChangeFunctionCase(shuffleFunction, AmountOfChange.High, AmountOfEntities.Medium, AmountOfFragmentation.Medium, highChangeTolerance);
            yield return BuildChangeFunctionCase(shuffleFunction, AmountOfChange.High, AmountOfEntities.Medium, AmountOfFragmentation.High, highChangeTolerance);
            yield return BuildChangeFunctionCase(shuffleFunction, AmountOfChange.High, AmountOfEntities.High, AmountOfFragmentation.Low, highChangeTolerance);
            yield return BuildChangeFunctionCase(shuffleFunction, AmountOfChange.High, AmountOfEntities.High, AmountOfFragmentation.Medium, highChangeTolerance);
            yield return BuildChangeFunctionCase(shuffleFunction, AmountOfChange.High, AmountOfEntities.High, AmountOfFragmentation.High, highChangeTolerance);
        }

        static TestCaseData BuildChangeFunctionCase(Action<World, EntityHierarchyScenario> changeFunction,
            AmountOfChange amountOfChange, AmountOfEntities amountOfEntities, AmountOfFragmentation amountOfFragmentation, float tolerance)
        {
            var nameBuilder = new StringBuilder(150);
            nameBuilder
               .Append($"{changeFunction.Method.Name}:".PadRight(22))
               .Append("\t")
               .Append($"Change = {amountOfChange}".PadRight(22))
               .Append("\t")
               .Append($"Entities = {amountOfEntities}".PadRight(24))
               .Append("\t")
               .Append($"Fragmentation = {amountOfFragmentation}".PadRight(28))
               .Append("\t")
               .Append($"Tolerance = +{tolerance:P1}");

            return
                new TestCaseData(changeFunction, amountOfEntities, amountOfChange, amountOfFragmentation, tolerance)
               .SetName(nameBuilder.ToString());
        }

        [Test, TestCaseSource(nameof(ChangeFunction_CaseProvider))]
        [Category(Categories.Performance)]
        public void ChangeFunction_ProducesResultsWithinAcceptableBoundaries(
            Action<World, EntityHierarchyScenario> changeFunction,
            AmountOfEntities amountOfEntities,
            AmountOfChange amountOfChange,
            AmountOfFragmentation amountOfFragmentation,
            float tolerance)
        {
            // A change function must:
            //    - Produce at least the number of changes expected by the scenario
            //    - Produce an amount of change that is within a specified margin of error to compensate for side-effects (different based on the amount of change expected)
            //    - Never produce a structure that is deeper than the max depth specified by the scenario
            //    - Produce a structure that is no more than 1 level shallower than the max depth specified by the scenario (exact depth may not always be possible due to amount of change)
            //    - Produce a structure with the same number of Entities as there were in the World prior to its execution

            var scenario = new EntityHierarchyScenario(
                amountOfEntities,
                amountOfChange,
                amountOfFragmentation,
                DepthOfStructure.Deep,
                ItemsVisibility.AllCollapsed,
                "World Generation Validation Scenario");

            using (var generator = new DefaultStrategyWorldGenerator(scenario))
            {
                var world = generator.Get();

                using (var entityGuidWatcher = new ComponentDataDiffer(typeof(EntityGuid)))
                using (var parentWatcher = new ComponentDataDiffer(typeof(Parent)))
                {
                    // snapshot
                    using (entityGuidWatcher.GatherComponentChangesAsync(world.EntityManager.UniversalQuery, Allocator.TempJob, out var entityHandle))
                    using (parentWatcher.GatherComponentChangesAsync(world.EntityManager.UniversalQuery, Allocator.TempJob, out var parentHandle))
                    {
                        JobHandle.CombineDependencies(entityHandle, parentHandle).Complete();
                    }

                    changeFunction(world, scenario);

                    // re-snapshot and compare
                    using (var entityGuidChanges = entityGuidWatcher.GatherComponentChangesAsync(world.EntityManager.UniversalQuery, Allocator.TempJob, out var entityHandle))
                    using (var parentChanges = parentWatcher.GatherComponentChangesAsync(world.EntityManager.UniversalQuery, Allocator.TempJob, out var parentHandle))
                    {
                        JobHandle.CombineDependencies(entityHandle, parentHandle).Complete();

                        // A change function can never result in a different entity count than the World originally had
                        Assert.That(world.EntityManager.UniversalQuery.CalculateEntityCount(), Is.EqualTo(scenario.TotalEntities), "Unexpected entity count.");

                        var changedEntities = new HashSet<EntityGuid>();

                        ExtractChangedEntities(entityGuidChanges, changedEntities);
                        ExtractParentingChangedEntities(parentChanges, world, changedEntities);

                        var expectedCount = scenario.ChangeCount;
                        var marginOfError = (int)Math.Ceiling(expectedCount * tolerance);

                        // We can never have LESS than the expected amount of changed entities.
                        // Due to the mechanism of removing entities from chunks, we can detect false positives and unchanged entities can be counted as changed.
                        Assert.That(changedEntities.Count, Is.GreaterThanOrEqualTo(expectedCount).And.LessThanOrEqualTo(expectedCount + marginOfError), "Unexpected number of changed entities.");

                        var depth = GetStructureDepth(world);

                        // We can never have a level DEEPER than the specified max depth of the scenario
                        // Due to the interaction between AmountOfEntities, AmountOfChange, and DepthOfStructure, we can end up with a structure 1 less than expected
                        Assert.That(depth, Is.LessThanOrEqualTo(scenario.MaximumDepth).And.GreaterThanOrEqualTo(scenario.MaximumDepth - 1), "Unexpected depth of structure.");
                    }
                }
            }
        }

        static void ExtractChangedEntities(in ComponentDataDiffer.ComponentChanges entityGuidChanges, HashSet<EntityGuid> changedEntities)
        {
            var (addedEntities, addedGuids) = entityGuidChanges.GetAddedComponents<EntityGuid>(Allocator.TempJob);
            var (removedEntities, removedGuids) = entityGuidChanges.GetRemovedComponents<EntityGuid>(Allocator.TempJob);
            foreach (var g in addedGuids)
            {
                changedEntities.Add(g);
            }

            foreach (var g in removedGuids)
            {
                changedEntities.Add(g);
            }
            addedEntities.Dispose();
            removedEntities.Dispose();
            addedGuids.Dispose();
            removedGuids.Dispose();
        }

        static void ExtractParentingChangedEntities(in ComponentDataDiffer.ComponentChanges parentChanges, World world, HashSet<EntityGuid> changedEntities)
        {
            var (addedParentEntities, addedParentComponents) = parentChanges.GetAddedComponents<Parent>(Allocator.TempJob);
            var (removedParentEntities, removedParentComponents) = parentChanges.GetRemovedComponents<Parent>(Allocator.TempJob);

            foreach (var entity in addedParentEntities)
            {
                changedEntities.Add(world.EntityManager.GetComponentData<EntityGuid>(entity));
            }

            foreach (var entity in removedParentEntities)
            {
                changedEntities.Add(world.EntityManager.GetComponentData<EntityGuid>(entity));
            }

            foreach (var newParent in addedParentComponents)
            {
                if (world.EntityManager.Exists(newParent.Value))
                    changedEntities.Add(world.EntityManager.GetComponentData<EntityGuid>(newParent.Value));
            }

            foreach (var previousParent in removedParentComponents)
            {
                if (world.EntityManager.Exists(previousParent.Value))
                    changedEntities.Add(world.EntityManager.GetComponentData<EntityGuid>(previousParent.Value));
            }

            addedParentEntities.Dispose();
            removedParentEntities.Dispose();
            addedParentComponents.Dispose();
            removedParentComponents.Dispose();
        }

        static int GetStructureDepth(World world)
        {
            var entityManager = world.EntityManager;
            var maxDepth = 0;
            using (var query = entityManager.CreateEntityQuery(new EntityQueryDesc { All = new ComponentType[] { typeof(Parent) }, None = new ComponentType[] { typeof(Child) } }))
            using (var parents = query.ToComponentDataArray<Parent>(Allocator.TempJob))
            {
                for (var i = 0; i < parents.Length; i++)
                {
                    var current = parents[i].Value;
                    var depth = 1;
                    while (true)
                    {
                        depth++;

                        if (!entityManager.HasComponent<Parent>(current))
                            break;

                        current = entityManager.GetComponentData<Parent>(current).Value;
                    }

                    if (depth > maxDepth)
                        maxDepth = depth;
                }
            }

            return maxDepth;
        }

        static int GetUniqueSharedComponentCount<T>(World world) where T : struct, ISharedComponentData
        {
            var allSharedComponentValues = new List<T>();
            world.EntityManager.GetAllUniqueSharedComponentData(allSharedComponentValues);
            return allSharedComponentValues.Count;
        }

        static float GetAverageChunkUtilization(World world)
        {
            var totalUtilization = 0f;
            var chunks = world.EntityManager.GetAllChunks();
            for (var i = 0; i < chunks.Length; i++)
            {
                var c = chunks[i];
                totalUtilization += (float)c.ChunkEntityCount / c.Capacity;
            }

            var averageUtilization = totalUtilization / chunks.Length;

            chunks.Dispose();

            return averageUtilization;
        }

        static (float low, float high) GetExpectedAverageChunkUtilization(AmountOfEntities amountOfEntities, AmountOfFragmentation fragmentation, DepthOfStructure depthOfStructure)
        {
            switch (fragmentation)
            {
                case AmountOfFragmentation.Low
                    // Both conditions can impact fragmentation in uncontrollable ways
                    when amountOfEntities == AmountOfEntities.Low
                      || depthOfStructure == DepthOfStructure.Deep: // We expect chunk to be [66%, 100%] used
                    return (0.66f, 1.00f);
                case AmountOfFragmentation.Low: // We expect chunk to be [80%, 100%] used
                    return (0.80f, 1.00f);
                case AmountOfFragmentation.Medium: // We expect chunks to be [25%, 50%] used
                    return (0.25f, 0.50f);
                case AmountOfFragmentation.High: // We expect chunk to be [0%, 10%] used
                    return (0.00f, 0.10f);
            }

            throw new ArgumentOutOfRangeException(nameof(fragmentation), fragmentation, null);
        }
    }
}
