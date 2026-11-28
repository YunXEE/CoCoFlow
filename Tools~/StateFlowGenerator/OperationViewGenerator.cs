using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// 项目声明 Operation 接口与 State 收件读法，随包 Roslyn 生成器为模块产生类型化视图和事件元数据。
namespace CoCoFlow.Generators
{
    /// <summary>为项目 Operation 合同与直接收件调用生成 Player 可用的视图及程序集元数据。</summary>
    [Generator]
    public sealed class OperationViewGenerator : ISourceGenerator
    {

        #region Public API

        /// <summary>安装语法接收器，收集本次编译的 Operation 接口、绑定属性及 State 收件调用。</summary>
        public void Initialize(GeneratorInitializationContext context) =>
            context.RegisterForSyntaxNotifications(() => new OperationDeclarations());

        /// <summary>校验操作接口与属性模式，为可生成合同输出类型化视图，并为状态读取的载荷生成程序集标记。</summary>
        public void Execute(GeneratorExecutionContext context)
        {
            if (!(context.SyntaxReceiver is OperationDeclarations sections)) return;
            var attribute = context.Compilation.GetTypeByMetadataName("CoCoFlow.Runtime.StateGraph.OperationAttribute");
            if (attribute == null) return;
            var generated = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
            foreach (var property in sections.Properties)
            {
                var model = context.Compilation.GetSemanticModel(property.SyntaxTree);
                var symbol = model.GetDeclaredSymbol(property, context.CancellationToken) as IPropertySymbol;
                var marker = symbol?.GetAttributes().FirstOrDefault(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, attribute));
                if (marker != null && marker.ConstructorArguments.Length != 0)
                    context.ReportDiagnostic(Diagnostic.Create(InvalidDeclaration, property.Identifier.GetLocation(),
                        "An Operation property must use [Operation] without a mode."));
            }
            foreach (var declaration in sections.Declarations)
            {
                var section = context.Compilation.GetSemanticModel(declaration.SyntaxTree)
                    .GetDeclaredSymbol(declaration, context.CancellationToken) as INamedTypeSymbol;
                if (section == null || !section.GetAttributes().Any(item =>
                        SymbolEqualityComparer.Default.Equals(item.AttributeClass, attribute)) || !generated.Add(section))
                    continue;
                var marker = section.GetAttributes().First(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, attribute));
                if (marker.ConstructorArguments.Length != 1 ||
                    !(marker.ConstructorArguments[0].Value is int mode) || (mode != 1 && mode != 2) ||
                    section.Interfaces.Length != 1 || section.Interfaces[0].ToDisplayString() != "CoCoFlow.Runtime.StateGraph.IOperation")
                {
                    context.ReportDiagnostic(Diagnostic.Create(InvalidDeclaration, declaration.Identifier.GetLocation(),
                        "An Operation interface must directly inherit IOperation and declare Continuous or Discrete mode."));
                    continue;
                }
                if (section.IsGenericType ||
                    !context.Compilation.IsSymbolAccessibleWithin(section, context.Compilation.Assembly)) continue;
                var members = section.GetMembers().Where(member => member.IsAbstract && !member.IsStatic).ToArray();
                if (members.Any(member => !(member is IPropertySymbol) &&
                        (!(member is IMethodSymbol method) || method.AssociatedSymbol == null))) continue;
                var properties = members.OfType<IPropertySymbol>().OrderBy(property => property.Name, StringComparer.Ordinal).ToArray();
                if (properties.Any(property => property.IsIndexer || property.GetMethod == null ||
                        !property.Type.IsUnmanagedType || property.ReturnsByRef || property.ReturnsByRefReadonly)) continue;

                string name = "OperationView_" + generated.Count;
                string type = section.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                context.AddSource(name + ".g.cs", Emit(name, type, properties,
                    context.Compilation.GetTypeByMetadataName("UnityEngine.Scripting.PreserveAttribute") != null));
            }

            EmitInboxDeclarations(context, sections.InboxReads);
        }

        #endregion

        #region Internal Logic

        private const string StateGraph = "global::CoCoFlow.Runtime.StateGraph.";
        private const string GeneratedNamespace = "CoCoFlowGeneratedStateFlow";

        private static readonly DiagnosticDescriptor InvalidDeclaration = new DiagnosticDescriptor(
            "COCOOP001", "Invalid Operation declaration", "{0}", "CoCoFlow", DiagnosticSeverity.Error, true);

        private static void EmitInboxDeclarations(GeneratorExecutionContext context,
            IEnumerable<InvocationExpressionSyntax> reads)
        {
            var entries = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var read in reads)
            {
                var model = context.Compilation.GetSemanticModel(read.SyntaxTree);
                var method = model.GetSymbolInfo(read, context.CancellationToken).Symbol as IMethodSymbol;
                if (method?.ContainingType.ToDisplayString() != "CoCoFlow.Runtime.StateGraph.StateEventReadView" ||
                    method.TypeArguments.Length != 1 || !(method.TypeArguments[0] is INamedTypeSymbol payload)) continue;
                var logic = model.GetEnclosingSymbol(read.SpanStart, context.CancellationToken)?.ContainingType;
                if (!CanReference(logic) || !CanReference(payload)) continue;
                var stateType = logic;
                while (stateType != null && stateType.ToDisplayString() != "CoCoFlow.Runtime.StateGraph.StateLogic")
                    stateType = stateType.BaseType;
                if (stateType == null) continue;
                entries.Add($"[assembly: {StateGraph}StateInboxEventAttribute(" +
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
            code.AppendLine($"[assembly: {StateGraph}OperationViewFactoryAttribute(typeof({type}), typeof({GeneratedNamespace}.{name}))]");
            code.AppendLine($"namespace {GeneratedNamespace} {{");
            code.AppendLine($"internal sealed class {name} : {type} {{");
            const string transfer = "global::System.Buffers.SpanAction<byte, int>";
            const string marshal = "global::System.Runtime.InteropServices.MemoryMarshal";
            code.AppendLine($"private readonly {transfer} _read, _write;");
            if (canPreserve) code.AppendLine("[global::UnityEngine.Scripting.Preserve]");
            code.AppendLine($"public {name}({transfer} read, {transfer} write) {{ _read = read; _write = write; }}");
            for (int index = 0; index < properties.Length; index++)
            {
                var property = properties[index];
                string value = property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                code.AppendLine($"public {value} @{property.Name} {{");
                code.AppendLine($"get {{ {value} result = default; _read({marshal}.AsBytes({marshal}.CreateSpan(ref result, 1)), {index}); return result; }}");
                if (property.SetMethod != null)
                    code.AppendLine($"set {{ _write({marshal}.AsBytes({marshal}.CreateSpan(ref value, 1)), {index}); }}");
                code.AppendLine("}");
            }
            code.AppendLine("} }");
            return code.ToString();
        }

        /// <summary>收集可参与生成的接口、属性与调用语法，供语义检查解析实际符号。</summary>
        private sealed class OperationDeclarations : ISyntaxReceiver
        {
            internal readonly List<PropertyDeclarationSyntax> Properties = new List<PropertyDeclarationSyntax>();
            internal readonly List<InterfaceDeclarationSyntax> Declarations = new List<InterfaceDeclarationSyntax>();
            internal readonly List<InvocationExpressionSyntax> InboxReads = new List<InvocationExpressionSyntax>();

            public void OnVisitSyntaxNode(SyntaxNode syntaxNode)
            {
                if (syntaxNode is PropertyDeclarationSyntax property && property.AttributeLists.Count > 0) Properties.Add(property);
                if (syntaxNode is InterfaceDeclarationSyntax section && section.AttributeLists.Count > 0)
                    Declarations.Add(section);
                if (syntaxNode is InvocationExpressionSyntax call && call.Expression is MemberAccessExpressionSyntax member &&
                    member.Name.Identifier.ValueText == "TryGetBatch") InboxReads.Add(call);
            }
        }

        #endregion
    }
}
