using NUnit.Framework;

namespace Unity.Entities.CodeGen.Tests.SourceGenerationTests
{
    public class JobEntityMultipleInvocations : JobEntitySourceGenerationTests
    {
        private const string Code =
            @"using Unity.Burst;
            using Unity.Entities;
            using Unity.Mathematics;
            using Unity.Transforms;

            public partial struct MyEntityJob : IJobEntity
            {
                public float MyDeltaTime;

                public void Execute(ref Rotation rotation, in RotationSpeed_ForEach speed)
                {
                    rotation.Value =
                        math.mul(
                            math.normalize(rotation.Value),
                            quaternion.AxisAngle(math.up(), speed.RadiansPerSecond * MyDeltaTime));
                }
            }

            [BurstCompile(FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.High, CompileSynchronously = true)]
            public partial struct YourEntityJob : IJobEntity
            {
                public float YourMultiplier;

                public void Execute(ref Translation translation)
                {
                    translation.Value *= YourMultiplier;
                }
            }

            public struct MustHaveComponentData : IComponentData
            {
                public float Value;
            }

            public struct Rotation : IComponentData
            {
                public quaternion Value;
            }

            public struct RotationSpeed_ForEach : IComponentData
            {
                public float RadiansPerSecond;
            }

            public struct Translation : IComponentData
            {
                public float Value;
            }

            public partial class JobEntity_MultipleInvocationsSystem : SystemBase
            {
                protected override void OnUpdate()
                {
                    var myJob = new MyEntityJob{MyDeltaTime = Time.DeltaTime};
                    Dependency = myJob.ScheduleParallel(Dependency);

                    var query = GetEntityQuery(new EntityQueryDesc
                    {
                        All = new ComponentType[] { typeof(MustHaveComponentData), typeof(Translation)},
                        Options = EntityQueryOptions.IncludeDisabled
                    });
                    query.SetChangedVersionFilter(typeof(Translation));

                    var yourEntityJob = new YourEntityJob{YourMultiplier = 1.2345f};
                    Dependency = yourEntityJob.ScheduleParallel(query, Dependency);
                }
            }";

        [Test]
        public void JobEntity_MultipleInvocationsTest()
        {
            RunTest(
                Code,
                new GeneratedType
                {
                    Name = "JobEntity_MultipleInvocationsSystem"
                });
        }
    }
}
