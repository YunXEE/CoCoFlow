using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CoCoFlow.Generators
{
    [Generator]
    public sealed class CoCoOperationViewGenerator : ISourceGenerator
    {
        private const string Core = "global::CoCoFlow.Runtime.Core.";
        private const string GeneratedNamespace = "CoCoFlowGeneratedStateFlow";

        public void Initialize(GeneratorInitializationContext context) =>
            context.RegisterForSyntaxNotifications(() => new Sections());

        public void Execute(GeneratorExecutionContext context)
        {
            if (!(context.SyntaxReceiver is Sections sections)) return;
            var attribute = context.Compilation.GetTypeByMetadataName("CoCoFlow.Runtime.Core.CoCoOperationSectionAttribute");
            if (attribute == null) return;
            var generated = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
            foreach (var declaration in sections.Declarations)
            {
                var section = context.Compilation.GetSemanticModel(declaration.SyntaxTree)
                    .GetDeclaredSymbol(declaration, context.CancellationToken) as INamedTypeSymbol;
                if (section == null || !section.GetAttributes().Any(item =>
                        SymbolEqualityComparer.Default.Equals(item.AttributeClass, attribute)) || !generated.Add(section))
                    continue;
                if (section.IsGenericType ||
                    !context.Compilation.IsSymbolAccessibleWithin(section, context.Compilation.Assembly)) continue;
                var members = section.GetMembers().Where(member => member.IsAbstract && !member.IsStatic).ToArray();
                if (members.Any(member => !(member is IPropertySymbol) &&
                        (!(member is IMethodSymbol method) || method.AssociatedSymbol == null))) continue;
                var properties = members.OfType<IPropertySymbol>().OrderBy(property => property.Name, StringComparer.Ordinal).ToArray();
                if (properties.Any(property => property.IsIndexer || property.GetMethod == null ||
                        !property.Type.IsUnmanagedType || property.ReturnsByRef || property.ReturnsByRefReadonly)) continue;

                string name = "SectionView_" + generated.Count;
                string type = section.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                context.AddSource(name + ".g.cs", Emit(name, type, properties,
                    context.Compilation.GetTypeByMetadataName("UnityEngine.Scripting.PreserveAttribute") != null));
            }

            EmitInboxDeclarations(context, sections.InboxReads);
        }

        private static void EmitInboxDeclarations(GeneratorExecutionContext context,
            IEnumerable<InvocationExpressionSyntax> reads)
        {
            var entries = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var read in reads)
            {
                var model = context.Compilation.GetSemanticModel(read.SyntaxTree);
                var method = model.GetSymbolInfo(read, context.CancellationToken).Symbol as IMethodSymbol;
                if (method?.ContainingType.ToDisplayString() != "CoCoFlow.Runtime.Core.CoCoStateEventReadView" ||
                    method.TypeArguments.Length != 1 || !(method.TypeArguments[0] is INamedTypeSymbol payload)) continue;
                var logic = model.GetEnclosingSymbol(read.SpanStart, context.CancellationToken)?.ContainingType;
                if (!CanReference(logic) || !CanReference(payload)) continue;
                var stateType = logic;
                while (stateType != null && stateType.ToDisplayString() != "CoCoFlow.Runtime.Core.CoCoStateLogic")
                    stateType = stateType.BaseType;
                if (stateType == null) continue;
                entries.Add($"[assembly: {Core}CoCoStateInboxEventAttribute(" +
                    $"typeof({logic.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}), " +
                    $"typeof({payload.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}))]");
            }

            if (entries.Count != 0) context.AddSource("StateInboxEvents.g.cs", string.Join("\n", entries));
        }

        private static bool CanReference(INamedTypeSymbol type)
        {
            if (type == null || type.IsGenericType) return false;
            for (var parent = type; parent != null; parent = parent.ContainingType)
                if (parent.DeclaredAccessibility != Accessibility.Public &&
                    parent.DeclaredAccessibility != Accessibility.Internal) return false;
            return true;
        }

        private static string Emit(string name, string type, IPropertySymbol[] properties, bool canPreserve)
        {
            var code = new StringBuilder();
            code.AppendLine($"[assembly: {Core}CoCoOperationViewFactoryAttribute(typeof({type}), typeof({GeneratedNamespace}.{name}))]");
            code.AppendLine($"namespace {GeneratedNamespace} {{");
            code.AppendLine($"internal sealed class {name} : {Core}ICoCoOperationSectionViewFactory<{type}> {{");
            if (canPreserve) code.AppendLine("[global::UnityEngine.Scripting.Preserve]");
            code.AppendLine($"public {name}() {{ }}");
            code.AppendLine($"public {type} Create(in {Core}CoCoOperationSectionViewContext<{type}> context) => new View(in context);");
            code.AppendLine($"private sealed class View : {type} {{");
            code.AppendLine($"private readonly {Core}CoCoOperationSectionReader _reader;");
            for (int index = 0; index < properties.Length; index++)
            {
                var property = properties[index];
                string value = property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                code.AppendLine($"private readonly {Core}CoCoOperationSectionField<{value}> _field{index};");
                code.AppendLine($"public {value} @{property.Name} {{ get => _reader.Read(_field{index});" +
                    (property.SetMethod != null ? $" set => _reader.Write(_field{index}, value); }}" : " }"));
            }
            code.AppendLine($"public View(in {Core}CoCoOperationSectionViewContext<{type}> context) {{");
            code.AppendLine("_reader = context.Reader;");
            for (int index = 0; index < properties.Length; index++)
                code.AppendLine($"if (!context.TryGetField({index}, out _field{index})) throw new global::System.InvalidOperationException(\"Cannot resolve {properties[index].Name} in the Operation Section.\");");
            code.AppendLine("} } } }");
            return code.ToString();
        }

        private sealed class Sections : ISyntaxReceiver
        {
            internal readonly List<InterfaceDeclarationSyntax> Declarations = new List<InterfaceDeclarationSyntax>();
            internal readonly List<InvocationExpressionSyntax> InboxReads = new List<InvocationExpressionSyntax>();

            public void OnVisitSyntaxNode(SyntaxNode syntaxNode)
            {
                if (syntaxNode is InterfaceDeclarationSyntax section && section.AttributeLists.Count > 0)
                    Declarations.Add(section);
                if (syntaxNode is InvocationExpressionSyntax call && call.Expression is MemberAccessExpressionSyntax member &&
                    member.Name.Identifier.ValueText == "TryGetBatch") InboxReads.Add(call);
            }
        }
    }
}
