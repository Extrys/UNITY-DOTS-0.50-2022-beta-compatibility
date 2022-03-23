using System;
using NUnit.Framework;

namespace Unity.Entities.CodeGen.Tests
{
    public class GetSetSingletonOutsideForEach_Test  : SingletonAccessSourceGenerationTests
    {
        const string Code =
            @"using Unity.Entities;

              partial class GetSetSingletonOutsideForEach : SystemBase
              {
                  protected override void OnUpdate()
                  {
                      float singletonValue = GetSingleton<SingletonData>().Value;
                      singletonValue += 10.0f;
                      SetSingleton(new SingletonData() {Value = singletonValue});
                      GetSingletonEntity<SingletonData>();
                  }
              }

              public struct SingletonData : IComponentData
              {
                  public float Value;
              }";

        [Test]
        public void GetSetSingleton_OutsideEntitiesForEach_Test()
        {
            RunTest(
                Code,
                new GeneratedType
                {
                    Name = "GetSetSingletonOutsideForEach"
                });
        }
    }
}
