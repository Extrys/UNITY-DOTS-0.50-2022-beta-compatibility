using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Runtime.CompilerServices;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using NUnit.Framework;
using Unity.CompilationPipeline.Common.Diagnostics;
using UnityEditor.Compilation;

namespace Unity.Entities.CodeGen.Tests
{
    public abstract class PostProcessorTestBase
    {
        class FailResolver : IAssemblyResolver
        {
            public void Dispose() {}
            public AssemblyDefinition Resolve(AssemblyNameReference name) { return null; }
            public AssemblyDefinition Resolve(AssemblyNameReference name, ReaderParameters parameters) { return null; }
        }

        class OnDemandResolver : IAssemblyResolver
        {
            public void Dispose()
            {
            }

            public AssemblyDefinition Resolve(AssemblyNameReference name)
            {
                return Resolve(name, new ReaderParameters(ReadingMode.Deferred));
            }

            public AssemblyDefinition Resolve(AssemblyNameReference name, ReaderParameters parameters)
            {
                var assembly = AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == name.Name);
                var fileName = assembly.Location;
                parameters.AssemblyResolver = this;
                parameters.SymbolStream = PdbStreamFor(fileName);
                var bytes = File.ReadAllBytes(fileName);
                return AssemblyDefinition.ReadAssembly(new MemoryStream(bytes), parameters);
            }
        }

        protected AssemblyDefinition AssemblyDefinitionFor(Type type, bool useFailResolver = false)
        {
            var assemblyLocation = type.Assembly.Location;

            IAssemblyResolver resolver;
            if (useFailResolver)
                resolver = new FailResolver();
            else
                resolver = new OnDemandResolver();

            var ad = AssemblyDefinition.ReadAssembly(new MemoryStream(File.ReadAllBytes(assemblyLocation)),
                new ReaderParameters(ReadingMode.Immediate)
                {
                    ReadSymbols = true,
                    ThrowIfSymbolsAreNotMatching = true,
                    SymbolReaderProvider = new PortablePdbReaderProvider(),
                    AssemblyResolver = resolver,
                    SymbolStream = PdbStreamFor(assemblyLocation)
                }
            );

            if (!ad.MainModule.HasSymbols)
                throw new Exception("NoSymbols");
            return ad;
        }

        protected TypeDefinition TypeDefinitionFor(Type type, bool useFailResolver = false)
        {
            var ad = AssemblyDefinitionFor(type, useFailResolver);
            var fullName = type.FullName.Replace("+", "/");
            return ad.MainModule.GetType(fullName).Resolve();
        }

        protected TypeDefinition TypeDefinitionFor(string typeName, Type nextToType, bool useFailResolver = false)
        {
            var ad = AssemblyDefinitionFor(nextToType, useFailResolver);
            var fullName = nextToType.FullName.Replace("+", "/");
            fullName = fullName.Replace(nextToType.Name, typeName);
            return ad.MainModule.GetType(fullName).Resolve();
        }

        protected MethodDefinition MethodDefinitionForOnlyMethodOf(Type type, bool useFailResolver = false)
        {
            return MethodDefinitionForOnlyMethodOfDefinition(TypeDefinitionFor(type, useFailResolver));
        }

        protected MethodDefinition MethodDefinitionForOnlyMethodOfDefinition(TypeDefinition typeDefinition)
        {
            var a = typeDefinition.GetMethods().Where(m => !m.IsConstructor && !m.IsStatic && !m.IsCompilerControlled &&
                !m.CustomAttributes.Any(c => c.AttributeType.Name == nameof(CompilerGeneratedAttribute))).ToList();
            return a.Count == 1 ? a.Single() : a.Single(m => m.Name == "Test");
        }

        static MemoryStream PdbStreamFor(string assemblyLocation)
        {
            var file = Path.ChangeExtension(assemblyLocation, ".pdb");
            if (!File.Exists(file))
                return null;
            return new MemoryStream(File.ReadAllBytes(file));
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        protected static T EnsureNotOptimizedAway<T>(T x) { return x; }

        [MethodImpl(MethodImplOptions.NoInlining)]
        protected static ref T EnsureNotOptimizedAway<T>(ref T x) { return ref x; }

        protected static void AssertSourceGenerationFailure(CompilerMessage[] compilerMessages, string expectedErrorMessage)
        {
            Assert.IsTrue(compilerMessages.Length == 1);

            CompilerMessage compilerMessage = compilerMessages.Single();

            Assert.AreEqual(compilerMessage.type, CompilerMessageType.Error);
            Assert.IsTrue(compilerMessage.message.Contains(expectedErrorMessage));
        }

        protected void AssertProducesWarning(Type systemType, params string[] shouldContainWarnings)
        {
            AssertProducesInternal(systemType, DiagnosticType.Warning, shouldContainWarnings);
        }

        protected void AssertProducesError(Type systemType, params string[] shouldContainErrors)
        {
            AssertProducesInternal(systemType, DiagnosticType.Error, shouldContainErrors);
        }

        protected static void AssertDiagnosticHasSufficientFileAndLineInfo(List<DiagnosticMessage> errors)
        {
            string diagnostic = errors.Select(dm => dm.MessageData).SeparateByComma();
            if (!diagnostic.Contains(".cs"))
                Assert.Fail("Diagnostic message had no file info: " + diagnostic);

            var match = Regex.Match(diagnostic, "\\.cs:?\\((?<line>.*?),(?<column>.*?)\\)");
            if (!match.Success)
                Assert.Fail("Diagnostic message had no line info: " + diagnostic);

            var line = int.Parse(match.Groups["line"].Value);
            if (line > 2000)
                Assert.Fail("Unreasonable line number in errormessage: " + diagnostic);
        }

        protected abstract void AssertProducesInternal(Type systemType, DiagnosticType type, string[] shouldContains, bool useFailResolver = false);
    }
}
