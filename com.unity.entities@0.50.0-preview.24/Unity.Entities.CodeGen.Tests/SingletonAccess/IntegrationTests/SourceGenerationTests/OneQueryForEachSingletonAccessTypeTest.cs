using System;
using NUnit.Framework;

namespace Unity.Entities.CodeGen.Tests
{
    public class OneQueryForEachSingletonAccessTypeTest  : SingletonAccessSourceGenerationTests
    {
        const string Code =
            @"using Unity.Entities;

              partial class OneQueryForEachSingletonAccessType : SystemBase
              {
                  protected override void OnUpdate()
                  {
                      GetSet_FirstTime();
                      GetSet_SecondTime();
                  }

                  void GetSet_FirstTime()
                  {
                      float singletonValue = GetSingleton<SingletonData>().Value;
                      singletonValue += 10.0f;
                      SetSingleton(new SingletonData() {Value = singletonValue});
                  }

                  void GetSet_SecondTime()
                  {
                      float singletonValue = GetSingleton<SingletonData>().Value;
                      singletonValue += 10.0f;

                      var singleton = new SingletonData() {Value = singletonValue};

                      SetSingleton(singleton);
                  }
              }

              public struct SingletonData : IComponentData
              {
                  public float Value;
              }";

        [Test]
        public void OnlyOneSingletonQueryForEachSingletonAccessType_Test()
        {
            RunTest(
                Code,
                new GeneratedType
                {
                    Name = "OneQueryForEachSingletonAccessType"
                });
        }
    }
}
