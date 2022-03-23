using System.IO;
using System.Linq;
using NUnit.Framework;
using Unity.Entities.CodeGen.Tests;
using Unity.Entities.CodeGen.Tests.SourceGenerationTests;
using UnityEngine;

namespace Unity.Entities.Hybrid.CodeGen.Tests.SourceGenerationTests
{
    public abstract class AuthoringComponentSourceGenerationIntegrationTest : IntegrationTest
    {
        protected override string ExpectedPath =>
            "Packages/com.unity.entities/Unity.Entities.Hybrid.CodeGen.Tests/AuthoringComponent/IntegrationTests/SourceGenerationTests";

        protected void RunAuthoringComponentSourceGenerationTest(string cSharpCode, params GeneratedType[] generatedTypes)
        {
            var (isSuccess, compilerMessages) = TestCompiler.Compile(
                    cSharpCode,
                    referencedTypes: new []
                    {
                        typeof(GenerateAuthoringComponentAttribute),
                        typeof(ConvertToEntity),
                        typeof(GameObject),
                        typeof(MonoBehaviour)
                    });

            if (!isSuccess)
                Assert.Fail($"Compilation failed with errors {string.Join(", ", compilerMessages.Select(msg => msg.message))}");

            RunSourceGenerationTest(generatedTypes, Path.Combine(TestCompiler.DirectoryForTestDll, TestCompiler.OutputDllName));
            TestCompiler.CleanUp();
        }
    }
}
