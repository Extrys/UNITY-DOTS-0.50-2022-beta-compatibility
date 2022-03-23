using NUnit.Framework;

namespace Unity.Entities.CodeGen.Tests.SourceGenerationTests
{
    public class JobEntityAndLambdaJobsInSameOnUpdateMethod : JobEntitySourceGenerationTests
    {
        const string Code =
            @"using Unity.Entities;
            using Unity.Mathematics;
            using Unity.Transforms;
            using Unity.Jobs;

            namespace OuterNamespace
            {
                namespace InnerNamespace
                {
                    public partial class MyFirstClass
                    {
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

                        public struct Rotation : IComponentData
                        {
	                        public quaternion Value;
                        }

                        public struct Translation : IComponentData
                        {
                            public float Value;
                        }

                        public struct RotationSpeed_ForEach : IComponentData
                        {
	                        public float RadiansPerSecond;
                        }
                    }

                    public partial class TwoForEachTypes
                    {
                        public partial class Child : SystemBase
                        {
                            protected override void OnUpdate()
                            {
                                var myEntityJob = new MyFirstClass.MyEntityJob { MyDeltaTime = Time.DeltaTime };
                                JobHandle myJobHandle = myEntityJob.ScheduleParallel(Dependency);

                                Dependency = Entities.ForEach((ref MyFirstClass.Translation translation) => { translation.Value *= 1.2345f; }).Schedule(myJobHandle);
                            }
                        }
                    }
                }
            }";

        [Test]
        public void JobEntity_AndLambdaJobs_InSameOnUpdateMethodTest()
        {
            RunTest(
                Code,
                new GeneratedType
                {
                    Name = "OuterNamespace.InnerNamespace.JobEntityAndForEach"
                });
        }
    }
}
