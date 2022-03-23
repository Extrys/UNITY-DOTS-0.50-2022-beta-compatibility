using NUnit.Framework;

namespace Unity.Entities.CodeGen.Tests.SourceGenerationTests
{
    public class JobEntityWithManagedComponent : JobEntitySourceGenerationTests
    {
        private const string Code =
            @"using Unity.Burst;
            using Unity.Entities;
            using Unity.Mathematics;
            using Unity.Transforms;
            using Unity.Jobs;

            public class MyManagedComponent : IComponentData
            {
                public quaternion MyValue;
            }

            public struct UndesiredComponent : IComponentData
            {
            }

            public struct MustHaveComponent : IComponentData
            {
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
            }

            public partial struct Job : IJobEntity
            {
                public float MyDeltaTime;

                public void Execute(ref Rotation rotation, in RotationSpeed_ForEach speed, MyManagedComponent myManagedComponent)
                {
                    var myQuaternion =
                        math.mul(
                            math.normalize(rotation.Value),
                            quaternion.AxisAngle(math.up(), speed.RadiansPerSecond * MyDeltaTime));

                    rotation.Value = math.mul(myQuaternion, myManagedComponent.MyValue);
                }
            }

            public partial class JobEntity_WithMyManagedComponentSystem : SystemBase
            {
                protected override void OnUpdate()
                {
                    var myEntityJob = new Job {MyDeltaTime = Time.DeltaTime};

                    var query = GetEntityQuery(new EntityQueryDesc
                    {
                        All = new ComponentType[] { typeof(Rotation), typeof(RotationSpeed_ForEach), typeof(MyManagedComponent), typeof(MustHaveComponent)},
                        None = new ComponentType[] { typeof(UndesiredComponent) }
                    });
                    query.SetChangedVersionFilter(typeof(Translation));

                    myEntityJob.Run(query);
                }
            }";

        [Test]
        public void JobEntity_WithManagedComponentTest()
        {
            RunTest(
                Code,
                new GeneratedType
                {
                    Name = "JobEntity_WithMyManagedComponentSystem"
                });
        }
    }
}
