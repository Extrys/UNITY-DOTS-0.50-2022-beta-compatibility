using NUnit.Framework;

namespace Unity.Entities.Hybrid.CodeGen.Tests.SourceGenerationTests
{

#if !UNITY_2020_1
    [Ignore("We only run these tests on current default version of Unity in Release (different compiler versions can cause valid failures).  Bump this #if version and regenerate when Unity version changes.")]
#endif

    public class WithDifferentAttributes : AuthoringComponentSourceGenerationIntegrationTest
    {
        [Test]
        public void WithDifferentAttributesTest()
        {
            const string withAttributeTargetingClassOnly = "WithAttributeTargetingClassOnly";
            string codeString1 =
                $@"using System;
                  using Unity.Entities;

                  [GenerateAuthoringComponent]
                  [TestAttributeTargetingClassOnly]
                  public class {withAttributeTargetingClassOnly} : IComponentData
                  {{
                      public int MyInt;
                      public bool MyBool;
                  }}

                  [AttributeUsage(AttributeTargets.Class)]
                  public class TestAttributeTargetingClassOnlyAttribute : Attribute
                  {{
                  }}";

            RunAuthoringComponentSourceGenerationTest(codeString1, new GeneratedType{Name = $"{withAttributeTargetingClassOnly}Authoring"});

            const string withAttributeTargetingClassAndOthers = "WithAttributeTargetingMultipleThingsIncludingClass";
            string codeString2 =
                $@"using System;
                    using Unity.Entities;

                    [GenerateAuthoringComponent]
                    [TestAttributeTargetingClassAndOthers]
                    public class {withAttributeTargetingClassAndOthers} : IComponentData
                    {{
                            public int MyInt;
                            public bool MyBool;
                    }}

                    [AttributeUsage(AttributeTargets.Class|AttributeTargets.Struct|AttributeTargets.Method)]
                    public class TestAttributeTargetingClassAndOthersAttribute : Attribute
                    {{
                    }}";

            RunAuthoringComponentSourceGenerationTest(codeString2, new GeneratedType{Name = $"{withAttributeTargetingClassAndOthers}Authoring"});

            const string withAttributeThatDoesNotSpecifyTarget = "WithAttributeThatDoesNotSpecifyTarget";
            string codeString3 =
                $@"using System;
                  using Unity.Entities;

                  [GenerateAuthoringComponent]
                  [TestAttributeWithNoSpecificTarget]
                  public struct {withAttributeThatDoesNotSpecifyTarget} : IComponentData
                  {{
                      public int MyInt;
                  }}

                  public class TestAttributeWithNoSpecificTargetAttribute : Attribute
                  {{
                  }}";

            RunAuthoringComponentSourceGenerationTest(codeString3, new GeneratedType{Name = $"{withAttributeThatDoesNotSpecifyTarget}Authoring"});

            const string withAttributeThatExplicitlyExcludesClassTarget = "WithAttributeThatDoesNotTargetClass";
            string codeString4 =
                $@"using System;
                  using Unity.Entities;

                  [GenerateAuthoringComponent]
                  [TestAttributeExplicitlyExcludingClassTarget]
                  public struct {withAttributeThatExplicitlyExcludesClassTarget} : IComponentData
                  {{
                      public int MyInt;
                  }}

                  [AttributeUsage(AttributeTargets.GenericParameter|AttributeTargets.Struct)]
                  public class TestAttributeExplicitlyExcludingClassTargetAttribute : Attribute
                  {{
                  }}";

            RunAuthoringComponentSourceGenerationTest(codeString4, new GeneratedType{Name = $"{withAttributeThatExplicitlyExcludesClassTarget}Authoring"});
        }
    }
}
