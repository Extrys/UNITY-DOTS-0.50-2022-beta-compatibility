using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Unity.Entities.SourceGen.Common;

namespace Unity.Entities.SourceGen.SystemGeneratorCommon
{
    public partial class JobEntityDescription
    {
        public MemberDeclarationSyntax Generate()
        {
            string partialStructImplementation =
            $@"{GetPartialStructNameAndInterfaces()}
               {{
                    {UserExecuteMethodParams.Select(p => p.FieldText).SeparateByNewLine()}{Environment.NewLine}
                    {"public Unity.Entities.EntityManager __EntityManager;\n".EmitIfTrue(UserExecuteMethodParams.Any(param => param.RequiresEntityManagerAccess))}
                    [System.Runtime.CompilerServices.CompilerGenerated]
                    {GetExecuteMethodSignature()}
                    {{
                        {UserExecuteMethodParams.Select(p => p.VariableDeclarationText).SeparateByNewLine()}{Environment.NewLine}
                        int count = batch.Count;
                        for(int i = 0; i < count; ++i){Environment.NewLine}
                        {{
                            {UserExecuteMethodParams.Where(p => p.RequiresLocalCode).Select(p => p.LocalCodeText).SeparateByNewLine()}{Environment.NewLine}
                            Execute({UserExecuteMethodParams.Select(param => param.ExecuteArgumentText).SeparateByComma()});
                        }}
                    }}

                    {GetScheduleAndRunMethods()}
               }}";

            string GetPartialStructNameAndInterfaces()
            {
                return HasEntityInQueryIndex()
                    ? $"partial struct {m_TypeName} : IJobEntityBatchWithIndex"
                    : $"partial struct {m_TypeName} : IJobEntityBatch";
            }

            string GetExecuteMethodSignature()
            {
                return HasEntityInQueryIndex()
                    ? "public void Execute(ArchetypeChunk batch, int batchIndex, int indexOfFirstEntityInQuery)"
                    : "public void Execute(ArchetypeChunk batch, int batchIndex)";
            }

            return GenerateFromString(partialStructImplementation);
        }

        MemberDeclarationSyntax GenerateFromString(string syntaxString)
        {
            var newStructNode = SyntaxFactory.ParseMemberDeclaration(syntaxString);

            // Return just the new struct, we only want to return top-level members
            if (m_Parents.Length == 0)
                return newStructNode;

            // Generate each namespace and return the top level
            MemberDeclarationSyntax lastChildNode = newStructNode;
            foreach (var parentSymbol in m_Parents)
            {
                var newNode = default(MemberDeclarationSyntax);
                if (parentSymbol.IsNamespace)
                {
                    newNode = SyntaxFactory.NamespaceDeclaration(SyntaxFactory.ParseName((parentSymbol as INamespaceSymbol).ToFullName()),
                        new SyntaxList<ExternAliasDirectiveSyntax>(), new SyntaxList<UsingDirectiveSyntax>(),
                        new SyntaxList<MemberDeclarationSyntax>(lastChildNode));
                }
                else if (parentSymbol.IsType)
                {
                    newNode = SyntaxFactory.ClassDeclaration(default,
                        new SyntaxTokenList(SyntaxFactory.Token(SyntaxKind.PartialKeyword)),
                        SyntaxFactory.Token(SyntaxKind.ClassKeyword), SyntaxFactory.Identifier(parentSymbol.Name), default, default, default,
                        SyntaxFactory.Token(SyntaxKind.OpenBraceToken),
                        new SyntaxList<MemberDeclarationSyntax>(lastChildNode),
                        SyntaxFactory.Token(SyntaxKind.CloseBraceToken), default);
                }
                lastChildNode = newNode;
            }

            return lastChildNode;
        }

        static string GetScheduleAndRunMethods()
        {
            var source =
            $@"
                public Unity.Jobs.JobHandle Schedule(Unity.Entities.EntityQuery query, Unity.Jobs.JobHandle dependsOn = default(Unity.Jobs.JobHandle)) => __ThrowCodeGenException();
                public Unity.Jobs.JobHandle Schedule(Unity.Jobs.JobHandle dependsOn = default(Unity.Jobs.JobHandle)) => __ThrowCodeGenException();

                public Unity.Jobs.JobHandle ScheduleByRef(Unity.Entities.EntityQuery query, Unity.Jobs.JobHandle dependsOn = default(Unity.Jobs.JobHandle)) => __ThrowCodeGenException();
                public Unity.Jobs.JobHandle ScheduleByRef(Unity.Jobs.JobHandle dependsOn = default(Unity.Jobs.JobHandle)) => __ThrowCodeGenException();

                public Unity.Jobs.JobHandle ScheduleParallel(Unity.Entities.EntityQuery query, Unity.Jobs.JobHandle dependsOn = default(Unity.Jobs.JobHandle)) => __ThrowCodeGenException();
                public Unity.Jobs.JobHandle ScheduleParallel(Unity.Jobs.JobHandle dependsOn = default(Unity.Jobs.JobHandle)) => __ThrowCodeGenException();

                public Unity.Jobs.JobHandle ScheduleParallelByRef(Unity.Entities.EntityQuery query, Unity.Jobs.JobHandle dependsOn = default(Unity.Jobs.JobHandle)) => __ThrowCodeGenException();
                public Unity.Jobs.JobHandle ScheduleParallelByRef(Unity.Jobs.JobHandle dependsOn = default(Unity.Jobs.JobHandle)) => __ThrowCodeGenException();

                public void Run(Unity.Entities.EntityQuery query) => __ThrowCodeGenException();
                public void Run() => __ThrowCodeGenException();

                public void RunByRef(Unity.Entities.EntityQuery query) => __ThrowCodeGenException();
                public void RunByRef() => __ThrowCodeGenException();

                Unity.Jobs.JobHandle __ThrowCodeGenException() => throw new System.Exception(""This method should have been replaced by source gen."");
            ";

            return source;
        }
    }
}
