using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Unity.Entities.SourceGen.Common;

namespace Unity.Entities.SourceGen.SystemGeneratorCommon
{
    class ComponentTypeHandleFieldDescription
    {
        public ITypeSymbol TypeSymbol;
        public bool IsReadOnly;
        public string FieldName;
        public string FieldAssignment;
        public FieldDeclarationSyntax FieldDeclaration;

        public ComponentTypeHandleFieldDescription(ITypeSymbol typeSymbol, bool isReadOnly, bool isInISystem)
        {
            TypeSymbol = typeSymbol;
            IsReadOnly = isReadOnly;
            FieldName = $"__{TypeSymbol.ToFullName().Replace(".", "_")}_{(IsReadOnly ? "RO" : "RW")}_ComponentTypeHandle";
            FieldAssignment = $@"{FieldName} = {"systemState.".EmitIfTrue(isInISystem)}GetComponentTypeHandle<{TypeSymbol.ToFullName()}>({(IsReadOnly ? "true" : "false")});";
            FieldDeclaration = (FieldDeclarationSyntax)SyntaxFactory.ParseMemberDeclaration($"Unity.Entities.ComponentTypeHandle<{TypeSymbol.ToFullName()}> {FieldName};");
        }
    }
}
