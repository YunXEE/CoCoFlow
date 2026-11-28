using System.Globalization;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

if (args.Length != 6)
{
    Console.Error.WriteLine("Usage: ApiSurface host assembly original.dll replay.dll response.rsp output.json");
    return 2;
}

var host = Path.GetFullPath(args[0]);
var assemblyName = args[1];
var compiler = CSharpCommandLineParser.Default.Parse(
    new[] { "/nostdlib", "@" + Path.GetFullPath(args[4]), "@" + Path.GetFullPath(args[4]) + "2" },
    host, sdkDirectory: null);
var references = compiler.MetadataReferences.Select(reference =>
    MetadataReference.CreateFromFile(Path.GetFullPath(reference.Reference, host), reference.Properties)).ToArray();
var options = compiler.CompilationOptions.WithMetadataImportOptions(MetadataImportOptions.All);
var compiled = CSharpCompilation.Create("ApiSurfaceReader", references: references, options: options);
var source = CSharpCompilation.Create(assemblyName,
    compiler.SourceFiles.Select(file => CSharpSyntaxTree.ParseText(
        File.ReadAllText(file.Path), compiler.ParseOptions, file.Path)),
    references, options);
var declarations = new Dictionary<string, string>(StringComparer.Ordinal);
foreach (var tree in source.SyntaxTrees)
{
    var model = source.GetSemanticModel(tree);
    foreach (var node in tree.GetRoot().DescendantNodes()
                 .Where(node => node is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax or BaseMethodDeclarationSyntax))
    {
        if (model.GetDeclaredSymbol(node)?.GetDocumentationCommentId() is string id)
            declarations[id] = tree.FilePath;
    }
}

var original = ReadAssembly(Path.GetFullPath(args[2]));
var replay = ReadAssembly(Path.GetFullPath(args[3]));
if (!original.SequenceEqual(replay))
{
    Console.Error.WriteLine("Compiler XML replay changed public metadata for " + assemblyName);
    return 1;
}

File.WriteAllText(args[5], JsonSerializer.Serialize(original));
return 0;

Entry[] ReadAssembly(string path)
{
    var reference = MetadataReference.CreateFromFile(path);
    var assembly = (IAssemblySymbol)compiled.AddReferences(reference).GetAssemblyOrModuleSymbol(reference)!;
    if (assembly.Name != assemblyName)
        throw new InvalidDataException($"Expected {assemblyName}, got {assembly.Name} in {path}");
    return NamespaceTypes(assembly.GlobalNamespace).SelectMany(TypeEntries)
        .OrderBy(entry => entry.Id, StringComparer.Ordinal).ToArray();
}

static IEnumerable<INamedTypeSymbol> NamespaceTypes(INamespaceSymbol scope) =>
    scope.GetTypeMembers().Concat(scope.GetNamespaceMembers().SelectMany(NamespaceTypes));

IEnumerable<Entry> TypeEntries(INamedTypeSymbol type)
{
    if (!Visible(type))
        yield break;
    var typeId = type.GetDocumentationCommentId()!;
    var path = declarations.GetValueOrDefault(typeId);
    yield return Describe(type, typeId, path);
    foreach (var member in type.GetMembers().Where(Visible))
    {
        if (member is INamedTypeSymbol nested)
        {
            foreach (var nestedEntry in TypeEntries(nested))
                yield return nestedEntry;
        }
        else if (member.GetDocumentationCommentId() is string id)
        {
            yield return Describe(member, typeId, declarations.GetValueOrDefault(id) ?? path);
        }
    }
}

static bool Accessible(ISymbol symbol) =>
    symbol.DeclaredAccessibility == Accessibility.Public ||
    (symbol.DeclaredAccessibility is Accessibility.Protected or Accessibility.ProtectedOrInternal &&
     symbol.ContainingType is { IsSealed: false });

static bool Visible(ISymbol symbol) =>
    symbol is not IMethodSymbol { AssociatedSymbol: not null } && Accessible(symbol);

Entry Describe(ISymbol symbol, string owner, string? path)
{
    string id = symbol.GetDocumentationCommentId()!;
    var display = new SymbolDisplayFormat(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters |
                         SymbolDisplayGenericsOptions.IncludeTypeConstraints |
                         SymbolDisplayGenericsOptions.IncludeVariance,
        memberOptions: SymbolDisplayMemberOptions.IncludeAccessibility | SymbolDisplayMemberOptions.IncludeModifiers |
                       SymbolDisplayMemberOptions.IncludeType | SymbolDisplayMemberOptions.IncludeParameters |
                       SymbolDisplayMemberOptions.IncludeRef | SymbolDisplayMemberOptions.IncludeConstantValue,
        parameterOptions: SymbolDisplayParameterOptions.IncludeType | SymbolDisplayParameterOptions.IncludeName |
                          SymbolDisplayParameterOptions.IncludeDefaultValue | SymbolDisplayParameterOptions.IncludeParamsRefOut |
                          SymbolDisplayParameterOptions.IncludeExtensionThis,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier |
                              SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers);
    string TypeName(ITypeSymbol? type) => type?.ToDisplayString(display) ?? "";
    string Access(ISymbol? accessor) => accessor is not null && Accessible(accessor)
        ? accessor.DeclaredAccessibility.ToString() : "";

    string signature = symbol.ToDisplayString(display);
    signature += symbol switch
    {
        INamedTypeSymbol type => $" [{type.DeclaredAccessibility} {type.TypeKind}; " +
            $"static={type.IsStatic}, abstract={type.IsAbstract}, sealed={type.IsSealed}, readonly={type.IsReadOnly}, ref={type.IsRefLikeType}; " +
            $"base={TypeName(type.BaseType)}, enum={TypeName(type.EnumUnderlyingType)}, " +
            $"interfaces={string.Join(",", type.Interfaces.Select(TypeName).Order(StringComparer.Ordinal))}]" +
            (type.DelegateInvokeMethod is { } invoke ? " " + invoke.ToDisplayString(display) : ""),
        IPropertySymbol property => $" [get={Access(property.GetMethod)}, set={Access(property.SetMethod)}, init={property.SetMethod?.IsInitOnly}]",
        IEventSymbol evt => $" [add={Access(evt.AddMethod)}, remove={Access(evt.RemoveMethod)}]",
        IFieldSymbol { HasConstantValue: true } field => $" [value={Convert.ToString(field.ConstantValue, CultureInfo.InvariantCulture)}]",
        _ => ""
    };
    if (symbol is IMethodSymbol method)
    {
        var attributes = method.GetAttributes().Concat(method.Parameters.SelectMany(parameter => parameter.GetAttributes()))
            .Where(attribute => attribute.AttributeClass?.ToDisplayString() is
                "System.Diagnostics.ConditionalAttribute" or "System.Runtime.CompilerServices.CallerFilePathAttribute" or
                "System.Runtime.CompilerServices.CallerLineNumberAttribute" or "System.Runtime.CompilerServices.CallerMemberNameAttribute" or
                "System.Runtime.CompilerServices.CallerArgumentExpressionAttribute");
        signature += string.Concat(attributes.Select(attribute => " [" + attribute + "]"));
    }

    bool needsDocs = symbol is IMethodSymbol
    {
        MethodKind: MethodKind.Ordinary or MethodKind.Constructor or MethodKind.UserDefinedOperator or MethodKind.Conversion
    } && declarations.ContainsKey(id);
    var inherited = new SortedSet<string>(StringComparer.Ordinal);
    if (needsDocs && symbol is IMethodSymbol own)
    {
        if (own.OverriddenMethod?.GetDocumentationCommentId() is string baseId)
            inherited.Add(baseId);
        foreach (var contract in own.ContainingType.AllInterfaces)
        foreach (var declaration in contract.GetMembers(own.Name))
            if (SymbolEqualityComparer.Default.Equals(own.ContainingType.FindImplementationForInterfaceMember(declaration), own) &&
                declaration.GetDocumentationCommentId() is string interfaceId)
                inherited.Add(interfaceId);
    }
    return new Entry(id, owner, signature, path, needsDocs, string.Join("\n", inherited));
}

record Entry(string Id, string Owner, string Signature, string? Source, bool NeedsDocs, string Inherits);
