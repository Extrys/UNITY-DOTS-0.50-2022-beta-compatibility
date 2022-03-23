using NUnit.Framework;

namespace Unity.Entities.CodeGen.Tests.SourceGenerationTests
{
    public class JobEntityAllNecessaryArgumentsPassedCorrectly : JobEntitySourceGenerationTests
    {
        const string Code =
            @"using Unity.Burst;
            using Unity.Collections;
            using Unity.Entities;
            using Unity.Mathematics;
            using Unity.Transforms;

            public partial struct StreamSubScenesIn : IJobEntity
            {
                public NativeList<Entity> AddRequestList;
                public float3 CameraPosition;
                public float MaxDistanceSquared;
                public float MinDistanceSquared;

                public void Execute(Entity entity, in Rotation rotation)
                {
                    if (rotation.Value < MaxDistanceSquared)
                    {
                        AddRequestList.Add(entity);
                    }
                }
            }

            public partial class JobEntity_CorrectArgumentsPassedSystem : SystemBase
            {
                NativeList<Entity> _addRequestList;

                protected override void OnCreate()
                {
                    _addRequestList = new NativeList<Entity>(Allocator.Persistent);
                }

                protected override void OnUpdate()
                {
                    var random = new Random();
                    var config = new StreamingLogicConfig
                    {
                        DistanceForStreamingIn = random.NextFloat(),
                        DistanceForStreamingOut = random.NextFloat()
                    };

                    var job = new StreamSubScenesIn
                    {
                        AddRequestList = _addRequestList,
                        CameraPosition = float3.zero,
                        MaxDistanceSquared =
                            TestMath.Multiply(
                                TestMath.AddTwo(
                                    1 / (config.DistanceForStreamingIn * (10f + config.DistanceForStreamingOut)) - 5f),
                                42f),
                        MinDistanceSquared = config.DistanceForStreamingIn / config.DistanceForStreamingOut
                    };
                    Dependency = job.Schedule(Dependency);
                }
            }

            public struct StreamingLogicConfig
            {
                public float DistanceForStreamingIn;
                public float DistanceForStreamingOut;
            }

            public struct Rotation : IComponentData
            {
                public float Value;
            }

            public static class TestMath
            {
                public static float Multiply(float input1, float input2) { return input1 * input2; }
                public static float AddTwo(float input) { return input + 2; }
            }
            ";

        [Test]
        public void JobEntity_AllNecessaryArgumentsPassedCorrectlyToGeneratedExecuteMethodTest()
        {
            RunTest(
                Code,
                new GeneratedType
                {
                    Name = "JobEntity_CorrectArgumentsPassedSystem"
                });
        }
    }
}
