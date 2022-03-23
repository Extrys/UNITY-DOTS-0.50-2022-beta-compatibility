using System;
using System.Linq;
using Mono.Cecil;
using NUnit.Framework;
using Unity.CompilationPipeline.Common.Diagnostics;
using Unity.Entities.CodeGen;
using Unity.Entities.CodeGen.Tests;
using UnityEngine;
using Unity.Entities.CodeGen.Tests.SourceGenerationTests;

namespace Unity.Entities.Hybrid.CodeGen.Tests
{
    [TestFixture]
    class GenerateAuthoringComponentCompileTimeTests : PostProcessorTestBase
    {
        [Test]
        public void GenerateAuthoringComponentAttributeWithNoValidInterfaceThrowsError()
        {
            const string code = @"
                using Unity.Entities;

                [GenerateAuthoringComponent]
                public struct GenerateAuthoringComponentWithNoValidInterface
                {
                    public float Value;
                }";

            var (isSuccess, compilerMessages) =
                TestCompiler.Compile(code, new []
                {
                    typeof(GenerateAuthoringComponentAttribute),
                    typeof(ConvertToEntity),
                    typeof(GameObject),
                    typeof(MonoBehaviour)
                });

            Assert.IsFalse(isSuccess);
            Assert.IsTrue(compilerMessages.Any(msg =>
                msg.message.Contains("GenerateAuthoringComponentWithNoValidInterface has a GenerateAuthoringComponentAttribute, and must therefore implement either the IComponentData interface or the IBufferElementData interface.")));
        }

        protected override void AssertProducesInternal(Type systemType, DiagnosticType type, string[] shouldContains, bool useFailResolver = false) { }
    }
}
