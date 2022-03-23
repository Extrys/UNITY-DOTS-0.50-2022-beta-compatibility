using Unity.Profiling;
using Unity.Transforms;

namespace Unity.Entities.Editor.PerformanceTests
{
    static class DefaultStrategyHelpers
    {
        static readonly ProfilerMarker k_UpdateParentingMarker = new ProfilerMarker($"{nameof(DefaultStrategyHelpers)}.{nameof(UpdateParenting)}");

        public unsafe static void UpdateParenting(World world)
        {
            using (k_UpdateParentingMarker.Auto())
            {
                var parentSystem = world.GetOrCreateSystem<ParentSystem>();
                parentSystem.Update(world.Unmanaged);
                world.Unmanaged.ResolveSystemState(parentSystem)->CompleteDependencyInternal();
            }
        }
    }
}
