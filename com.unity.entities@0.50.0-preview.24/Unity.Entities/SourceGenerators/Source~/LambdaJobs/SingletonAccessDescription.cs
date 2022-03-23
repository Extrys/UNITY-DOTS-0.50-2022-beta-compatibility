﻿using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Unity.Entities.SourceGen.Common;
using Unity.Entities.SourceGen.SystemGeneratorCommon;

namespace Unity.Entities.SourceGen.LambdaJobs
{
    class SingletonAccessDescription
    {
        public SingletonAccessType AccessType { get; }
        public INamedTypeSymbol SingletonType { get; }
        public string EntityQueryFieldName { get; set; }

        public bool Success { get; } = true;
        public MethodDeclarationSyntax ContainingMethod { get; }
        public SyntaxNode OriginalNode { get; }

        readonly ArgumentSyntax _argumentSyntax;

        public SingletonAccessDescription(
            SingletonAccessCandidate candidate,
            MethodDeclarationSyntax containingMethod,
            SemanticModel semanticModel)
        {
            ContainingMethod = containingMethod;
            AccessType = candidate.SingletonAccessType;
            OriginalNode = candidate.SyntaxNode;

            var genericTypesUsedInMethod =
                containingMethod
                    .ChildNodes()
                    .OfType<TypeParameterListSyntax>()
                    .SelectMany(t => t.Parameters)
                    .Select(t => t.Identifier.ValueText)
                    .ToArray();
            var genericParameterNames = GetGenericParameterNames(genericTypesUsedInMethod).ToArray();

            switch (candidate.SingletonAccessType)
            {
                case SingletonAccessType.GetSingleton:
                case SingletonAccessType.GetSingletonEntity:
                {
                    var genericNameSyntax = candidate.SyntaxNode.ChildNodes().OfType<GenericNameSyntax>().FirstOrDefault();
                    var typeArgumentListSyntax = genericNameSyntax?.ChildNodes().OfType<TypeArgumentListSyntax>().FirstOrDefault();

                    // I.e. GetSingleton<GenericComponentData<T>>()
                    if (typeArgumentListSyntax != null && typeArgumentListSyntax.ChildNodes().OfType<GenericNameSyntax>().Any())
                    {
                        Success = false;
                        return;
                    }

                    var typeArgumentIdentifierNameSyntax = typeArgumentListSyntax?.Arguments.First();
                    var symbol = ModelExtensions.GetSymbolInfo(semanticModel, typeArgumentIdentifierNameSyntax).Symbol;

                    // I.e. GetSingleton<T>(), where T is generic
                    if (symbol is ITypeParameterSymbol)
                    {
                        Success = false;
                        return;
                    }

                    SingletonType = (INamedTypeSymbol)symbol.GetSymbolType();
                    break;
                }
                case SingletonAccessType.Set:
                {
                    var argumentSyntax = candidate.SyntaxNode.ChildNodes().OfType<ArgumentListSyntax>().FirstOrDefault()?.Arguments.SingleOrDefault();

                    switch (argumentSyntax?.Expression)
                    {
                        case IdentifierNameSyntax identifierNameSyntax:
                            if (genericParameterNames.Contains(identifierNameSyntax.Identifier.ValueText))
                            {
                                Success = false;
                                return;
                            }
                            break;
                        case ObjectCreationExpressionSyntax objectCreationExpressionSyntax:
                            if (objectCreationExpressionSyntax.Type is GenericNameSyntax)
                            {
                                Success = false;
                                return;
                            }
                            break;
                    }

                    var symbol =
                        argumentSyntax.Expression is DefaultExpressionSyntax
                            ? ModelExtensions.GetTypeInfo(semanticModel, argumentSyntax.Expression).Type
                            : ModelExtensions.GetSymbolInfo(semanticModel, argumentSyntax.Expression).Symbol;

                    SingletonType = (INamedTypeSymbol)symbol.GetSymbolType();
                    _argumentSyntax = argumentSyntax.WithoutPreprocessorTrivia();
                    break;
                }
            }
        }

        public SyntaxNode GenerateReplacementNode()
        {
            switch (AccessType)
            {
                case SingletonAccessType.GetSingleton:
                case SingletonAccessType.GetSingletonEntity:
                    var statement =
                        AccessType == SingletonAccessType.GetSingleton
                            ? $"{EntityQueryFieldName}.GetSingleton<{SingletonType}>();"
                            : $"{EntityQueryFieldName}.GetSingletonEntity();";

                    return
                        SyntaxFactory
                            .ParseStatement(statement)
                            .DescendantNodes()
                            .OfType<InvocationExpressionSyntax>()
                            .FirstOrDefault();
                case SingletonAccessType.Set:
                    return SyntaxFactory
                        .ParseStatement($"{EntityQueryFieldName}.SetSingleton({_argumentSyntax});")
                        .DescendantNodes()
                        .OfType<InvocationExpressionSyntax>()
                        .FirstOrDefault();
                case SingletonAccessType.None:
                    return null;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        IEnumerable<string> GetGenericParameterNames(IEnumerable<string> genericTypes)
        {
            if (!genericTypes.Any())
            {
                return Enumerable.Empty<string>();
            }

            return
                ContainingMethod
                    .ChildNodes()
                    .OfType<ParameterListSyntax>()
                    .SelectMany(p => p.Parameters)
                    .Where(IsGenericParameter)
                    .Select(p => p.Identifier.ValueText);

            bool IsGenericParameter(ParameterSyntax parameter)
            {
                if (parameter.Type is IdentifierNameSyntax identifierNameSyntax)
                {
                    return genericTypes.Contains(identifierNameSyntax.Identifier.ValueText);
                }
                return false;
            }
        }
    }
}
