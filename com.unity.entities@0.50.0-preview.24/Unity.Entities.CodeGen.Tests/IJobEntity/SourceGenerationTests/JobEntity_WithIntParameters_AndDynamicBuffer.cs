using NUnit.Framework;

namespace Unity.Entities.CodeGen.Tests.SourceGenerationTests
{
    public class JobEntity_WithIntParameters_AndDynamicBuffer : JobEntitySourceGenerationTests
    {
        private const string Code =
            @"using Unity.Collections;
            using Unity.Entities;
            using Unity.Collections.LowLevel.Unsafe;

            public partial struct VehicleDespawnJob : IJobEntity
            {
                public EntityCommandBuffer.ParallelWriter EntityCommandBuffer;

                [NativeSetThreadIndex]
                int nativeThreadIndex;

                public void Execute(Entity entity, [EntityInQueryIndex] int entityInQueryIndex, in DynamicBuffer<MyBufferInt> myBufferInts, ref Translation translation, in VehiclePathing vehicle)
                {
                    translation.Value += entityInQueryIndex + entity.Version + myBufferInts[2].Value + nativeThreadIndex;
                    if (vehicle.CurvePos >= 1.0f)
                    {
                        EntityCommandBuffer.DestroyEntity(entityInQueryIndex, entity);
                    }
                }
            }

            public struct VehiclePathing : IComponentData
            {
                public float CurvePos;
            }

            public struct Translation : IComponentData
            {
                public float Value;
            }

            public struct MyBufferInt : IBufferElementData
            {
                public int Value;
            }

            public partial class JobEntity_WithIntParamsAndDynamicBuffer : SystemBase
            {
                private EndSimulationEntityCommandBufferSystem _DespawnBarrier;

                protected override void OnCreate()
                {
                    base.OnCreate();
                    _DespawnBarrier = World.GetOrCreateSystem<EndSimulationEntityCommandBufferSystem>();
                }

                protected override void OnUpdate()
                {
                    var job = new VehicleDespawnJob
                    {
                        EntityCommandBuffer = _DespawnBarrier.CreateCommandBuffer().AsParallelWriter()
                    };
                    Dependency = job.ScheduleParallel(Dependency);
                }
            }";

        [Test]
        public void JobEntity_WithEntityAndEntityInQueryIndexTestJobEntity_WithEntity_EntityInQueryIndex_NativeThreadIndex_DynamicBufferTest()
        {
            RunTest(
                Code,
                new GeneratedType
                {
                    Name = "JobEntity_WithIntParamsAndDynamicBuffer"
                });
        }
    }
}
