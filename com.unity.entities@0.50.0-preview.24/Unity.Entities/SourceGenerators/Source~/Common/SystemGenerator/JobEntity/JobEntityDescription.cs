using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Unity.Entities.SourceGen.Common;
using Unity.Entities.SourceGen.SystemGenerator;

namespace Unity.Entities.SourceGen.SystemGeneratorCommon
{
    public partial class JobEntityDescription
    {
        public readonly string FullTypeName;
        public bool Valid { get; private set; }
        public readonly JobEntityParam[] UserExecuteMethodParams;

        public List<(INamedTypeSymbol, bool)> QueryAllTypes = new List<(INamedTypeSymbol, bool)>();
        public List<(INamedTypeSymbol, bool)> QueryAnyTypes = new List<(INamedTypeSymbol, bool)>();
        public List<(INamedTypeSymbol, bool)> QueryNoneTypes = new List<(INamedTypeSymbol, bool)>();
        public List<(INamedTypeSymbol, bool)> QueryChangeFilterTypes = new List<(INamedTypeSymbol, bool)>();
        public EntityQueryOptions EntityQueryOptions = EntityQueryOptions.Default;

        readonly string m_UserExecuteMethodSignature;
        readonly IFieldSymbol[] m_RefFieldSymbols;

        string m_TypeName;
        INamespaceOrTypeSymbol[] m_Parents;

        public bool HasEntityInQueryIndex() => UserExecuteMethodParams.Any(e => e is JobEntityParam_EntityInQueryIndex);

        public JobEntityDescription(BaseTypeDeclarationSyntax candidate, SemanticModel semanticModel, ISourceGeneratorDiagnosable diagnosable) :
            this(semanticModel.GetDeclaredSymbol(candidate), diagnosable) {}

        public JobEntityDescription(INamedTypeSymbol jobEntityType, ISourceGeneratorDiagnosable diagnosable)
        {
            Valid = true;
            FullTypeName = jobEntityType.ToFullName();

            // Extract Execute method arguments
            var executeMethods = jobEntityType.GetMembers().OfType<IMethodSymbol>().Where(m => m.Name == "Execute").ToArray();
            var userExecuteMethods = executeMethods.Where(m => !m.HasAttribute("System.Runtime.CompilerServices.CompilerGeneratedAttribute")).ToArray();
            if (userExecuteMethods.Length != 1)
            {
                JobEntityGeneratorErrors.SGJE0008(diagnosable, jobEntityType.Locations.First(), FullTypeName, userExecuteMethods);
                Valid = false;
                return;
            }

            var userExecuteMethod = userExecuteMethods[0];
            m_UserExecuteMethodSignature = $"{userExecuteMethod.Name}({userExecuteMethod.Parameters.Select(p => $"{p.Type.Name} {p.Name}").SeparateByComma().TrimEnd('\n')})";
            UserExecuteMethodParams =
                userExecuteMethod
                    .Parameters
                    .Select(p =>
                    {
                        var param = JobEntityParam.Create(p, diagnosable, out var valid);
                        Valid &= valid;
                        return param;
                    })
                    .ToArray();

            m_TypeName = jobEntityType.Name;
            m_Parents = jobEntityType.GetParentsFromMostToLeastNested().ToArray();
            m_RefFieldSymbols = jobEntityType.GetMembers().OfType<IFieldSymbol>().Where(m => m.Type.IsReferenceType).ToArray();

            FillQueryInfoFromAttributes(jobEntityType);

            Valid = OutputErrors(diagnosable);
        }

        void FillQueryInfoFromAttributes(INamedTypeSymbol jobEntityType)
        {
            var attributes = jobEntityType.GetAttributes();

            foreach (var attribute in attributes)
            {
                switch (attribute.AttributeClass.ToFullName())
                {
                    case "Unity.Entities.WithAllAttribute":
                        foreach (var argument in attribute.ConstructorArguments)
                            QueryAllTypes.AddRange(argument.Values.Select(val => (val.Value as INamedTypeSymbol, true)));
                        break;

                    case "Unity.Entities.WithNoneAttribute":
                        foreach (var argument in attribute.ConstructorArguments)
                            QueryNoneTypes.AddRange(argument.Values.Select(val => (val.Value as INamedTypeSymbol, true)));
                        break;

                    case "Unity.Entities.WithAnyAttribute":
                        foreach (var argument in attribute.ConstructorArguments)
                            QueryAnyTypes.AddRange(argument.Values.Select(val => (val.Value as INamedTypeSymbol, true)));
                        break;

                    case "Unity.Entities.WithChangeFilterAttribute":
                        foreach (var argument in attribute.ConstructorArguments)
                            QueryChangeFilterTypes.AddRange(argument.Values.Select(val => (val.Value as INamedTypeSymbol, true)));
                        break;

                    case "Unity.Entities.WithEntityQueryOptionsAttribute":
                        var firstArgument = attribute.ConstructorArguments[0];
                        if (firstArgument.Kind == TypedConstantKind.Array)
                            foreach (var entityQueryOptions in firstArgument.Values)
                                EntityQueryOptions |= (EntityQueryOptions) (int) entityQueryOptions.Value;
                        else
                            EntityQueryOptions |= (EntityQueryOptions) (int) firstArgument.Value;
                        break;
                }
            }

            QueryChangeFilterTypes.AddRange(UserExecuteMethodParams.OfType<IHasChangeFilter>()
                .Where(param => param.HasChangeFilter)
                .Select(param => (((JobEntityParam) param).TypeSymbol as INamedTypeSymbol, true)));
        }

        bool OutputErrors(ISourceGeneratorDiagnosable systemTypeGeneratorContext)
        {
            var valid = Valid;
            int validEntityInQueryIndexCount = 0;

            foreach (var param in UserExecuteMethodParams)
            {
                switch (param)
                {
                    case JobEntityParam_EntityInQueryIndex entityInQueryIndex:
                    {
                        if (entityInQueryIndex.IsInt)
                        {
                            if (validEntityInQueryIndexCount == 0)
                            {
                                validEntityInQueryIndexCount = 1;
                                continue;
                            }
                            JobEntityGeneratorErrors.SGJE0007(systemTypeGeneratorContext,
                                entityInQueryIndex.ParameterSymbol.Locations.Single(),
                                FullTypeName, m_UserExecuteMethodSignature);
                            valid = false;
                            continue;
                        }

                        JobEntityGeneratorErrors.SGJE0006(
                            systemTypeGeneratorContext,
                            entityInQueryIndex.ParameterSymbol.Locations.Single(),
                            FullTypeName, m_UserExecuteMethodSignature,
                            entityInQueryIndex.ParameterSymbol.Name);
                        valid = false;
                        continue;
                    }
                    default:
                        continue;
                }
            }

            if (m_RefFieldSymbols.Length > 0)
            {
                JobEntityGeneratorErrors.SGJE0009(systemTypeGeneratorContext, m_RefFieldSymbols[0].Locations.FirstOrDefault(), FullTypeName);
                return false;
            }
            return valid;
        }
    }

    public class JobEntityParam_SharedComponent : JobEntityParam, IHasChangeFilter
    {
        internal JobEntityParam_SharedComponent(IParameterSymbol parameterSymbol, bool hasChangeFilter) : base(parameterSymbol)
        {
            HasChangeFilter = hasChangeFilter;
            VariableName = $"{char.ToLowerInvariant(TypeName[0])}{TypeName.Substring(1)}Data";
            RequiresEntityManagerAccess = true;
            RequiresTypeHandleFieldInSystemBase = false;

            VariableDeclarationText = $"var {VariableName} = batch.GetSharedComponentData({FieldName}, __EntityManager);";
            FieldText = $"{(IsReadOnly ? "[Unity.Collections.ReadOnly]" : "")}public Unity.Entities.SharedComponentTypeHandle<{FullyQualifiedTypeName}> {FieldName};";
            JobEntityFieldAssignment = $"GetSharedComponentTypeHandle<{FullyQualifiedTypeName}>()";

            ExecuteArgumentText = parameterSymbol.RefKind switch
            {
                RefKind.Ref => $"ref {VariableName}",
                RefKind.In => $"in {VariableName}",
                _ => VariableName
            };
        }

        public bool HasChangeFilter { get; }
    }

    public class JobEntityParam_Entity : JobEntityParam
    {
        internal JobEntityParam_Entity(IParameterSymbol parameterSymbol) : base(parameterSymbol)
        {
            RequiresTypeHandleFieldInSystemBase = false;
            RequiresLocalCode = true;

            FieldName = "__EntityTypeHandle";
            VariableName = "entityPointer";

            const string localName = "entity";
            LocalCodeText = $"var {localName} = InternalCompilerInterface.UnsafeGetCopyOfNativeArrayPtrElement<Entity>({VariableName}, i);";

            VariableDeclarationText = $@"var {VariableName} = InternalCompilerInterface.UnsafeGetChunkEntityArrayIntPtr(batch, {FieldName});";
            FieldText = "[Unity.Collections.ReadOnly] public Unity.Entities.EntityTypeHandle __EntityTypeHandle;";
            ExecuteArgumentText = localName;

            JobEntityFieldAssignment = "GetEntityTypeHandle()";
        }
    }

    public class JobEntityParam_DynamicBuffer : JobEntityParam
    {
        public readonly ITypeSymbol BufferArgumentType;
        internal JobEntityParam_DynamicBuffer(IParameterSymbol parameterSymbol)
            : base(parameterSymbol)
        {
            BufferArgumentType = ((INamedTypeSymbol)TypeSymbol).TypeArguments.First();
            TypeSymbol = BufferArgumentType;

            VariableName = $"{parameterSymbol.Name}BufferAccessor";

            var localName = $"retrievedByIndexIn{VariableName}";
            LocalCodeText = $"var {localName} = {VariableName}[i];";
            FieldName = $"__{BufferArgumentType.ToFullName().Replace('.', '_')}TypeHandle";

            RequiresTypeHandleFieldInSystemBase = false;
            RequiresLocalCode = true;

            VariableDeclarationText = $"var {VariableName} = batch.GetBufferAccessor({FieldName});";
            FieldText = $"public Unity.Entities.BufferTypeHandle<{BufferArgumentType.ToFullName()}> {FieldName};";

            JobEntityFieldAssignment = $"GetBufferTypeHandle<{BufferArgumentType}>({(IsReadOnly ? "true" : "false")})";

            ExecuteArgumentText = parameterSymbol.RefKind switch
            {
                RefKind.Ref => $"ref {localName}",
                RefKind.In => $"in {localName}",
                _ => localName
            };
        }
    }

    public class JobEntityParam_ManagedComponent : JobEntityParam, IHasChangeFilter
    {
        internal JobEntityParam_ManagedComponent(IParameterSymbol parameterSymbol, bool hasChangeFilter) : base(parameterSymbol)
        {
            HasChangeFilter = hasChangeFilter;
            RequiresTypeHandleFieldInSystemBase = false;
            RequiresEntityManagerAccess = true;
            RequiresLocalCode = true;
            VariableName = $"{parameterSymbol.Name}ManagedComponentAccessor";
            var localName = $"retrievedByIndexIn{VariableName}";
            LocalCodeText = $"var {localName} = {VariableName}[i];";

            VariableDeclarationText = $"var {VariableName} = batch.GetManagedComponentAccessor({FieldName}, __EntityManager);";
            FieldText = $"{(IsReadOnly ? "[Unity.Collections.ReadOnly]" : "")}public Unity.Entities.ComponentTypeHandle<{FullyQualifiedTypeName}> {FieldName};";

            JobEntityFieldAssignment = $"EntityManager.GetComponentTypeHandle<{FullyQualifiedTypeName}>({(IsReadOnly ? "true" : "false")})";

            ExecuteArgumentText = localName;

            // We do not allow managed components to be used by ref. See SourceGenerationErrors.DC0024.
            if(parameterSymbol.RefKind == RefKind.In)
                ExecuteArgumentText = $"in {localName}";
        }

        public bool HasChangeFilter { get;}
    }

    public class JobEntityParam_ComponentData : JobEntityParam, IHasChangeFilter
    {
        internal JobEntityParam_ComponentData(IParameterSymbol parameterSymbol, bool hasChangeFilter) : base(parameterSymbol)
        {
            HasChangeFilter = hasChangeFilter;

            VariableName = $"{char.ToLowerInvariant(TypeName[0])}{TypeName.Substring(1)}Data";
            RequiresTypeHandleFieldInSystemBase = true;
            RequiresLocalCode = true;
            FieldText = $"{"[Unity.Collections.ReadOnly] ".EmitIfTrue(IsReadOnly)}public Unity.Entities.ComponentTypeHandle<{FullyQualifiedTypeName}> {FieldName};";

            VariableDeclarationText = IsReadOnly
                ? $"var {VariableName} = InternalCompilerInterface.UnsafeGetChunkNativeArrayReadOnlyIntPtr<{FullyQualifiedTypeName}>(batch, {FieldName});"
                : $"var {VariableName} = InternalCompilerInterface.UnsafeGetChunkNativeArrayIntPtr<{FullyQualifiedTypeName}>(batch, {FieldName});";

            var localName = $"{VariableName}__ref";
            LocalCodeText = $"ref var {localName} = ref InternalCompilerInterface.UnsafeGetRefToNativeArrayPtrElement<{FullyQualifiedTypeName}>({VariableName}, i);";

            ExecuteArgumentText = parameterSymbol.RefKind switch
            {
                RefKind.Ref => $"ref {localName}",
                RefKind.In => $"in {localName}",
                _ => VariableName
            };
        }

        public bool HasChangeFilter { get; }
    }

    class JobEntityParam_TagComponent : JobEntityParam, IHasChangeFilter
    {
        internal JobEntityParam_TagComponent(IParameterSymbol parameterSymbol, bool hasChangeFilter) : base(parameterSymbol)
        {
            HasChangeFilter = hasChangeFilter;
            RequiresTypeHandleFieldInSystemBase = false;
            ExecuteArgumentText = "default";
        }

        public bool HasChangeFilter { get; }
    }

    class JobEntityParam_EntityInQueryIndex : JobEntityParam
    {
        public bool IsInt => TypeSymbol.IsInt();

        internal JobEntityParam_EntityInQueryIndex(IParameterSymbol parameterSymbol)
            : base(parameterSymbol)
        {
            RequiresTypeHandleFieldInSystemBase = false;
            RequiresLocalCode = true;
            LocalCodeText = "var entityInQueryIndex = indexOfFirstEntityInQuery + i;";
            ExecuteArgumentText = "entityInQueryIndex";
            IsQueryableType = false;
        }
    }

    public interface IHasChangeFilter
    {
        public bool HasChangeFilter { get; }
    }

    public abstract class JobEntityParam
    {
        public bool RequiresEntityManagerAccess { get; protected set; }
        public bool RequiresLocalCode { get; protected set; }
        public bool RequiresTypeHandleFieldInSystemBase { get; protected set; } = true;
        public string FullyQualifiedTypeName { get; }
        public string TypeName { get; }

        public IParameterSymbol ParameterSymbol { get; }
        public ITypeSymbol TypeSymbol { get; protected set; }

        public string FieldName { get; protected set; }
        public string VariableName { get; protected set; }

        public bool IsReadOnly { get; }
        public string LocalCodeText { get; protected set; }
        public string FieldText { get; protected set; }
        public string VariableDeclarationText { get; protected set; }
        public string ExecuteArgumentText { get; protected set; }
        public string JobEntityFieldAssignment { get; protected set; }

        public bool IsQueryableType { get; protected set; } = true;

        internal JobEntityParam(IParameterSymbol parameterSymbol)
        {
            ParameterSymbol = parameterSymbol;
            TypeSymbol = parameterSymbol.Type;
            FullyQualifiedTypeName = TypeSymbol.GetSymbolTypeName();
            TypeName = TypeSymbol.Name;
            FieldName = $"__{TypeName}TypeHandle";
            IsReadOnly = parameterSymbol.IsReadOnly();
        }

        public static JobEntityParam Create(IParameterSymbol parameterSymbol, ISourceGeneratorDiagnosable diagnosable, out bool valid)
        {
            var typeSymbol = parameterSymbol.Type;
            valid = true;

            var hasChangeFilter = false;
            foreach (var attribute in parameterSymbol.GetAttributes())
            {
                switch (attribute.AttributeClass.ToFullName())
                {
                    case "Unity.Entities.EntityInQueryIndex":
                        return new JobEntityParam_EntityInQueryIndex(parameterSymbol);
                    case "Unity.Entities.ChangeFilterAttribute":
                        hasChangeFilter = true;
                        break;
                }
            }

            if (typeSymbol.InheritsFromInterface("Unity.Entities.ISharedComponentData"))
                return new JobEntityParam_SharedComponent(parameterSymbol, hasChangeFilter);

            if (typeSymbol.Is("Unity.Entities.Entity"))
                return new JobEntityParam_Entity(parameterSymbol);

            if (typeSymbol.IsDynamicBuffer())
                return new JobEntityParam_DynamicBuffer(parameterSymbol);

            if (!typeSymbol.IsValueType
                && typeSymbol.InheritsFromInterface("Unity.Entities.IComponentData")
                | typeSymbol.InheritsFromType("UnityEngine.Behaviour"))
            {
                return new JobEntityParam_ManagedComponent(parameterSymbol, hasChangeFilter);
            }

            if (typeSymbol.IsValueType)
            {
                if (typeSymbol.InheritsFromInterface("Unity.Entities.IComponentData"))
                {
                    if (typeSymbol is INamedTypeSymbol namedTypeSymbol && namedTypeSymbol.TypeArguments.Any()) // Generic parameters are unsupported
                    {
                        JobEntityGeneratorErrors.SGJE0011(diagnosable, parameterSymbol.Locations.Single(), parameterSymbol.Name);
                        valid = false;
                        return null;
                    }

                    if (typeSymbol.GetMembers().OfType<IFieldSymbol>().Any())
                        return new JobEntityParam_ComponentData(parameterSymbol, hasChangeFilter);
                    return new JobEntityParam_TagComponent(parameterSymbol, hasChangeFilter);
                }

                if (typeSymbol.InheritsFromInterface("Unity.Entities.IBufferElementData"))
                {
                    JobEntityGeneratorErrors.SGJE0012(diagnosable, parameterSymbol.Locations.Single(), typeSymbol.Name);
                    valid = false;
                }
                else
                {
                    JobEntityGeneratorErrors.SGJE0003(diagnosable, typeSymbol.Locations.Single(),
                        parameterSymbol.Name, typeSymbol.GetSymbolTypeName());
                    valid = false;
                    return new JobEntityParamValueTypesPassedWithDefaultArguments(parameterSymbol);
                }
                return null;
            }

            JobEntityGeneratorErrors.SGJE0010(diagnosable, parameterSymbol.Locations.Single(), parameterSymbol.Name, typeSymbol.ToFullName());
            valid = false;
            return null;
        }

        class JobEntityParamValueTypesPassedWithDefaultArguments : JobEntityParam
        {
            internal JobEntityParamValueTypesPassedWithDefaultArguments(IParameterSymbol parameterSymbol) : base(parameterSymbol)
            {
                RequiresTypeHandleFieldInSystemBase = false;
                ExecuteArgumentText = "default";
            }
        }
    }
}
