using NUnit.Framework;

namespace Unity.Entities.CodeGen.Tests.SourceGenerationTests
{
    public class JobEntityWithSharedComponent : JobEntitySourceGenerationTests
    {
        private const string Code =
            @"
            using Unity.Entities;
            using Unity.Mathematics;
            using Unity.Transforms;

            public struct MySharedComponent : ISharedComponentData
            {
                public quaternion MyValue;
            }

            public struct Rotation : IComponentData
            {
                public quaternion Value;
            }

            public struct RotationSpeed_ForEach : IComponentData
            {
                public float RadiansPerSecond;
            }

            public partial struct JobEntity_WithMySharedComponent : IJobEntity
            {
                public float MyDeltaTime;

                public void Execute(ref Rotation rotation, in RotationSpeed_ForEach speed, in MySharedComponent mySharedComponent)
                {
                    var myQuaternion =
                        math.mul(
                            math.normalize(rotation.Value),
                            quaternion.AxisAngle(math.up(), speed.RadiansPerSecond * MyDeltaTime));

                    rotation.Value = math.mul(myQuaternion, mySharedComponent.MyValue);
                }
            }

            public partial class JobEntity_WithMySharedComponentSystem : SystemBase
            {
                protected override void OnUpdate()
                {
                    var myEntityJob = new JobEntity_WithMySharedComponent {MyDeltaTime = Time.DeltaTime};

                    var query = GetEntityQuery(new EntityQueryDesc
                    {
                        All = new ComponentType[] { typeof(Rotation), typeof(RotationSpeed_ForEach), typeof(MySharedComponent)},
                        Options = EntityQueryOptions.IncludeDisabled
                    });

                    myEntityJob.Run(query);
                }
            }";

        [Test]
        public void JobEntity_WithSharedComponentTest()
        {
            RunTest(
                Code,
                new GeneratedType
                {
                    Name = "JobEntity_WithMySharedComponentSystem"
                });
        }
    }
}
