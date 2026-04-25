using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;

var options = AnalyzerOptions.Parse(args);
var analyzer = new DesignAnalyzer(options);
var result = await analyzer.RunAsync();

Console.WriteLine($"Analyzed {result.Projects.Count} projects, {result.Types.Count} types, {result.Relationships.Count} relationships, {result.CallGraph.Count} calls.");
Console.WriteLine($"Output: {Path.GetFullPath(options.OutputDirectory)}");

internal sealed record AnalyzerOptions(
    string InputPath,
    string OutputDirectory,
    string[] ProjectFilters,
    bool IncludeTests,
    bool Render,
    string RenderFormat,
    int MaxTypesPerDiagram,
    int MaxSequenceDepth,
    string ClassDetail,
    bool IncludeDtos,
    int MaxClassProperties,
    int MaxClassMethods)
{
    public static AnalyzerOptions Parse(string[] args)
    {
        var repoRoot = FindRepoRoot(Environment.CurrentDirectory);
        var input = Path.Combine(repoRoot, "Backend", "Microservices", "Microservices.sln");
        var output = Path.Combine(repoRoot, "artifacts", "design");
        var projects = new List<string>
        {
            "User.Microservice",
            "Ai.Microservice",
            "Feed.Microservice",
            "Notification.Microservice",
            "ApiGateway",
            "SharedLibrary"
        };
        var includeTests = false;
        var render = false;
        var renderFormat = "svg";
        var maxTypes = 45;
        var maxDepth = 5;
        var classDetail = "balanced";
        var includeDtos = false;
        var maxClassProperties = 0;
        var maxClassMethods = 0;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            var value = i + 1 < args.Length ? args[i + 1] : "";
            switch (arg)
            {
                case "--solution":
                case "--input":
                    input = Path.GetFullPath(value, Environment.CurrentDirectory);
                    i++;
                    break;
                case "--output":
                    output = Path.GetFullPath(value, Environment.CurrentDirectory);
                    i++;
                    break;
                case "--project":
                    projects.Add(value);
                    i++;
                    break;
                case "--only":
                    projects = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
                    i++;
                    break;
                case "--include-tests":
                    includeTests = true;
                    break;
                case "--render":
                    render = true;
                    if (!string.IsNullOrWhiteSpace(value) && !value.StartsWith("--", StringComparison.Ordinal))
                    {
                        renderFormat = value;
                        i++;
                    }
                    break;
                case "--max-types":
                    maxTypes = int.Parse(value);
                    i++;
                    break;
                case "--max-sequence-depth":
                    maxDepth = int.Parse(value);
                    i++;
                    break;
                case "--class-detail":
                    classDetail = value;
                    if (classDetail.Equals("detailed", StringComparison.OrdinalIgnoreCase))
                    {
                        maxClassProperties = 12;
                        maxClassMethods = 10;
                    }
                    i++;
                    break;
                case "--include-dtos":
                    includeDtos = true;
                    break;
                case "--class-properties":
                    maxClassProperties = int.Parse(value);
                    i++;
                    break;
                case "--class-methods":
                    maxClassMethods = int.Parse(value);
                    i++;
                    break;
                case "--help":
                case "-h":
                    PrintHelpAndExit();
                    break;
            }
        }

        return new AnalyzerOptions(
            input,
            output,
            projects.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            includeTests,
            render,
            renderFormat,
            maxTypes,
            maxDepth,
            classDetail,
            includeDtos,
            maxClassProperties,
            maxClassMethods);
    }

    private static string FindRepoRoot(string start)
    {
        var current = new DirectoryInfo(start);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, ".git")) ||
                File.Exists(Path.Combine(current.FullName, "Backend", "Microservices", "Microservices.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return start;
    }

    private static void PrintHelpAndExit()
    {
        Console.WriteLine("""
        DesignAnalyzer

        Usage:
          dotnet run --project Tools/DesignAnalyzer -- [options]

        Options:
          --solution|--input <path>      .sln or .csproj to analyze. Default: Backend/Microservices/Microservices.sln
          --output <dir>                Output directory. Default: artifacts/design
          --only <csv>                  Project/service filter. Default: all MeAI services + SharedLibrary
          --project <name>              Add one project/service filter
          --include-tests               Include test projects
          --max-types <number>          Types per class diagram chunk. Default: 45
          --max-sequence-depth <number> Calls to follow from each entry method. Default: 5
          --class-detail balanced|compact|detailed
                                        balanced is default: feature diagrams from Command/Query/Handler/deps
          --include-dtos                Include request/response/model/validator types in class diagrams
          --class-properties <number>   Properties per type in class diagrams. Default: 0
          --class-methods <number>      Methods per type in class diagrams. Default: 0
          --render [svg|png|pdf]        Render Mermaid files through mmdc
        """);
        Environment.Exit(0);
    }
}

internal sealed class DesignAnalyzer(AnalyzerOptions options)
{
    private static readonly SymbolDisplayFormat FullNameFormat = new(
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static void IndexConsumer(INamedTypeSymbol symbol, TypeInfo typeInfo, AnalysisContext context)
    {
        foreach (var iface in symbol.AllInterfaces)
        {
            if (iface.Name != "IConsumer" || iface.TypeArguments.Length == 0)
            {
                continue;
            }

            var ns = iface.ContainingNamespace?.ToDisplayString() ?? "";
            var isMassTransitConsumer =
                ns == "MassTransit" ||
                iface.ToDisplayString(FullNameFormat).StartsWith("MassTransit.IConsumer<", StringComparison.Ordinal) ||
                iface.ToDisplayString(FullNameFormat).StartsWith("IConsumer<", StringComparison.Ordinal);

            if (!isMassTransitConsumer)
            {
                continue;
            }

            var message = iface.TypeArguments[0].OriginalDefinition.ToDisplayString(FullNameFormat);

            if (!context.ConsumersByMessageFullName.TryGetValue(message, out var consumers))
            {
                consumers = [];
                context.ConsumersByMessageFullName[message] = consumers;
            }

            if (!consumers.Any(c => c.Id == typeInfo.Id))
            {
                consumers.Add(typeInfo);
            }
        }
    }

    private static void TryAddPublishConsumerEdges(
        InvocationExpressionSyntax invocation,
        IMethodSymbol target,
        TypeInfo callerType,
        IMethodSymbol caller,
        SemanticModel semanticModel,
        AnalysisContext context,
        DesignModel model)
    {
        if (target.Name != "Publish")
        {
            return;
        }

        var containingType = target.ContainingType?.ToDisplayString(FullNameFormat) ?? "";
        var containingNamespace = target.ContainingNamespace?.ToDisplayString() ?? "";

        var isMassTransitPublish =
            containingNamespace.StartsWith("MassTransit", StringComparison.Ordinal) ||
            containingType.Contains("MassTransit.IPublishEndpoint", StringComparison.Ordinal) ||
            containingType.Contains("MassTransit.IBus", StringComparison.Ordinal) ||
            containingType.Contains("MassTransit.ISendEndpoint", StringComparison.Ordinal);

        if (!isMassTransitPublish)
        {
            return;
        }

        var messageType = ResolvePublishedMessageType(invocation, target, semanticModel);
        var messageKey = messageType?.OriginalDefinition.ToDisplayString(FullNameFormat);

        if (messageKey is null)
        {
            return;
        }

        if (!context.ConsumersByMessageFullName.TryGetValue(messageKey, out var consumers))
        {
            return;
        }

        foreach (var consumer in consumers)
        {
            var consumeMethod =
                consumer.Methods.FirstOrDefault(m => m.Name == "Consume")?.Signature
                ?? "Consume(context)";

            model.CallGraph.Add(new CallEdge(
                CallerType: callerType.Id,
                CallerMethod: MethodSignature(caller),
                TargetType: consumer.Id,
                TargetMethod: consumeMethod,
                Kind: $"MassTransit.Publish<{messageType.Name}>",
                Location: SourceLocation.From(invocation.GetLocation())));
        }
    }

    private static ITypeSymbol? ResolvePublishedMessageType(
        InvocationExpressionSyntax invocation,
        IMethodSymbol target,
        SemanticModel semanticModel)
    {
        if (target.TypeArguments.Length > 0)
        {
            return target.TypeArguments[0];
        }

        if (invocation.ArgumentList.Arguments.Count == 0)
        {
            return null;
        }

        var expression = invocation.ArgumentList.Arguments[0].Expression;

        var convertedType = semanticModel.GetTypeInfo(expression).ConvertedType;
        if (convertedType is not null && convertedType.SpecialType != SpecialType.System_Object)
        {
            return convertedType;
        }

        var type = semanticModel.GetTypeInfo(expression).Type;
        if (type is not null && type.SpecialType != SpecialType.System_Object)
        {
            return type;
        }

        return null;
    }

    public async Task<DesignModel> RunAsync()
    {
        RegisterMsBuild();
        PrepareOutputDirectory(options.OutputDirectory);

        using var workspace = MSBuildWorkspace.Create(new Dictionary<string, string>
        {
            ["Configuration"] = "Debug",
            ["Platform"] = "AnyCPU"
        });

        workspace.RegisterWorkspaceFailedHandler(e => Console.Error.WriteLine($"MSBuildWorkspace {e.Diagnostic.Kind}: {e.Diagnostic.Message}"));

        var solution = await LoadAsync(workspace);
        var projects = solution.Projects
            .Where(ShouldAnalyzeProject)
            .OrderBy(p => p.FilePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var model = new DesignModel
        {
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            InputPath = Path.GetFullPath(options.InputPath),
            Projects = projects.Select(ProjectInfo.FromProject).ToList()
        };

        var context = new AnalysisContext();

        foreach (var project in projects)
        {
            var compilation = await project.GetCompilationAsync();
            if (compilation is null)
            {
                continue;
            }

            foreach (var document in project.Documents.Where(d => d.SourceCodeKind == SourceCodeKind.Regular))
            {
                var root = await document.GetSyntaxRootAsync();
                var semanticModel = await document.GetSemanticModelAsync();
                if (root is null || semanticModel is null)
                {
                    continue;
                }

                foreach (var declaration in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
                {
                    if (semanticModel.GetDeclaredSymbol(declaration) is not INamedTypeSymbol symbol)
                    {
                        continue;
                    }

                    if (context.TypeBySymbol.ContainsKey(symbol.OriginalDefinition))
                    {
                        continue;
                    }

                    var typeInfo = TypeInfo.FromSymbol(symbol, project, document.FilePath);
                    context.TypeBySymbol[symbol.OriginalDefinition] = typeInfo;
                    context.TypeByFullName.TryAdd(typeInfo.FullName, typeInfo);
                    IndexRequestHandler(symbol, typeInfo, context);
                    IndexConsumer(symbol, typeInfo, context);
                    model.Types.Add(typeInfo);
                }

                foreach (var declaration in root.DescendantNodes().OfType<EnumDeclarationSyntax>())
                {
                    if (semanticModel.GetDeclaredSymbol(declaration) is not INamedTypeSymbol symbol)
                    {
                        continue;
                    }

                    if (context.TypeBySymbol.ContainsKey(symbol.OriginalDefinition))
                    {
                        continue;
                    }

                    var typeInfo = TypeInfo.FromSymbol(symbol, project, document.FilePath);
                    context.TypeBySymbol[symbol.OriginalDefinition] = typeInfo;
                    context.TypeByFullName.TryAdd(typeInfo.FullName, typeInfo);
                    IndexRequestHandler(symbol, typeInfo, context);
                    IndexConsumer(symbol, typeInfo, context);
                    model.Types.Add(typeInfo);
                }
            }
        }

        foreach (var project in projects)
        {
            foreach (var document in project.Documents.Where(d => d.SourceCodeKind == SourceCodeKind.Regular))
            {
                var root = await document.GetSyntaxRootAsync();
                var semanticModel = await document.GetSemanticModelAsync();
                if (root is null || semanticModel is null)
                {
                    continue;
                }

                ExtractRelationships(project, root, semanticModel, context, model);
                ExtractDi(root, semanticModel, context, model);
                ExtractCalls(root, semanticModel, context, model);
            }
        }

        model.Relationships = model.Relationships.DistinctBy(r => r.Key).OrderBy(r => r.Source).ThenBy(r => r.Target).ToList();
        model.DependencyRegistrations = model.DependencyRegistrations.DistinctBy(r => r.Key).OrderBy(r => r.Service).ThenBy(r => r.Implementation).ToList();
        model.CallGraph = model.CallGraph.DistinctBy(c => c.Key).OrderBy(c => c.CallerType).ThenBy(c => c.CallerMethod).ToList();

        var files = new DiagramWriter(options, model).WriteAll();
        model.OutputFiles = files;

        var jsonPath = Path.Combine(options.OutputDirectory, "design.json");
        await File.WriteAllTextAsync(jsonPath, JsonSerializer.Serialize(model, _jsonOptions));

        var manifestPath = Path.Combine(options.OutputDirectory, "manifest.json");
        var manifest = Manifest.From(model, files.Prepend(jsonPath).ToArray());
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest, _jsonOptions));

        if (options.Render)
        {
            RenderMermaidFiles(options.OutputDirectory, options.RenderFormat);
        }

        return model;
    }

    private static void RegisterMsBuild()
    {
        if (!MSBuildLocator.IsRegistered)
        {
            MSBuildLocator.RegisterDefaults();
        }
    }

    private static void PrepareOutputDirectory(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);

        foreach (var file in Directory.EnumerateFiles(outputDirectory, "*", SearchOption.AllDirectories))
        {
            var name = Path.GetFileName(file);
            var extension = Path.GetExtension(file);
            if (extension is ".mmd" or ".svg" or ".png" or ".pdf" or ".json" || name == "README.md")
            {
                File.Delete(file);
            }
        }

        foreach (var directory in Directory.EnumerateDirectories(outputDirectory, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length))
        {
            if (!Directory.EnumerateFileSystemEntries(directory).Any())
            {
                Directory.Delete(directory);
            }
        }
    }

    private async Task<Solution> LoadAsync(MSBuildWorkspace workspace)
    {
        if (options.InputPath.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
        {
            return await workspace.OpenSolutionAsync(options.InputPath);
        }

        var project = await workspace.OpenProjectAsync(options.InputPath);
        return project.Solution;
    }

    private bool ShouldAnalyzeProject(Project project)
    {
        var path = project.FilePath ?? "";
        if (!options.IncludeTests && path.Contains($"{Path.DirectorySeparatorChar}test{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (options.ProjectFilters.Length == 0)
        {
            return true;
        }

        return options.ProjectFilters.Any(filter =>
            project.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
            path.Contains(filter, StringComparison.OrdinalIgnoreCase));
    }

    private static void ExtractRelationships(Project project, SyntaxNode root, SemanticModel semanticModel, AnalysisContext context, DesignModel model)
    {
        foreach (var declaration in root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
        {
            if (semanticModel.GetDeclaredSymbol(declaration) is not INamedTypeSymbol typeSymbol ||
                !TryGetKnownType(typeSymbol, context, out var source))
            {
                continue;
            }

            if (typeSymbol.BaseType is { SpecialType: not SpecialType.System_Object } baseType &&
                TryGetKnownType(baseType, context, out var target))
            {
                model.Relationships.Add(Relationship.Inheritance(source, target, project.Name));
            }

            foreach (var iface in typeSymbol.Interfaces)
            {
                if (TryGetKnownType(iface, context, out target))
                {
                    model.Relationships.Add(Relationship.Implements(source, target, project.Name));
                }
            }

            foreach (var member in typeSymbol.GetMembers().OfType<IFieldSymbol>().Where(f => !f.IsImplicitlyDeclared))
            {
                AddTypeUsageRelationship(model, source, member.Type, context, "Composition", member.Name, project.Name);
            }

            foreach (var member in typeSymbol.GetMembers().OfType<IPropertySymbol>())
            {
                AddTypeUsageRelationship(model, source, member.Type, context, "Aggregation", member.Name, project.Name);
            }

            foreach (var method in typeSymbol.GetMembers().OfType<IMethodSymbol>().Where(m => m.MethodKind is MethodKind.Ordinary or MethodKind.Constructor))
            {
                foreach (var parameter in method.Parameters)
                {
                    AddTypeUsageRelationship(model, source, parameter.Type, context, "Dependency", $"{method.Name}({parameter.Name})", project.Name);
                }

                if (method.ReturnsVoid is false)
                {
                    AddTypeUsageRelationship(model, source, method.ReturnType, context, "Dependency", $"{method.Name} return", project.Name);
                }
            }
        }
    }

    private static void ExtractDi(SyntaxNode root, SemanticModel semanticModel, AnalysisContext context, DesignModel model)
    {
        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            var method = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
            if (method is null || method.Name is not ("AddScoped" or "AddTransient" or "AddSingleton"))
            {
                continue;
            }

            var service = "";
            var implementation = "";

            if (method.TypeArguments.Length > 0)
            {
                service = method.TypeArguments[0].ToDisplayString(FullNameFormat);
                implementation = method.TypeArguments.Length > 1 ? method.TypeArguments[1].ToDisplayString(FullNameFormat) : service;
            }
            else if (invocation.ArgumentList.Arguments.Count > 0)
            {
                service = ExtractTypeOf(invocation.ArgumentList.Arguments[0].Expression, semanticModel);
                implementation = invocation.ArgumentList.Arguments.Count > 1
                    ? ExtractTypeOf(invocation.ArgumentList.Arguments[1].Expression, semanticModel)
                    : service;
            }

            if (string.IsNullOrWhiteSpace(service))
            {
                continue;
            }

            model.DependencyRegistrations.Add(new DiRegistration(
                Lifetime: method.Name.Replace("Add", "", StringComparison.Ordinal),
                Service: service,
                Implementation: implementation,
                ServiceKnown: context.TypeByFullName.ContainsKey(service),
                ImplementationKnown: context.TypeByFullName.ContainsKey(implementation),
                Location: SourceLocation.From(invocation.GetLocation())));
        }
    }

    private static void ExtractCalls(SyntaxNode root, SemanticModel semanticModel, AnalysisContext context, DesignModel model)
    {
        foreach (var methodDecl in root.DescendantNodes().OfType<BaseMethodDeclarationSyntax>())
        {
            if (semanticModel.GetDeclaredSymbol(methodDecl) is not IMethodSymbol caller ||
                !TryGetKnownType(caller.ContainingType, context, out var callerType))
            {
                continue;
            }

            foreach (var invocation in methodDecl.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                var target = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
                if (target is null)
                {
                    continue;
                }

                if (TryGetKnownType(target.ContainingType, context, out var targetType))
                {
                    model.CallGraph.Add(new CallEdge(
                        CallerType: callerType.Id,
                        CallerMethod: MethodSignature(caller),
                        TargetType: targetType.Id,
                        TargetMethod: MethodSignature(target),
                        Kind: "MethodCall",
                        Location: SourceLocation.From(invocation.GetLocation())));
                }

                if (target.Name == "Send" && invocation.ArgumentList.Arguments.Count > 0)
                {
                    var requestType = semanticModel.GetTypeInfo(invocation.ArgumentList.Arguments[0].Expression).Type;
                    var requestKey = requestType?.OriginalDefinition.ToDisplayString(FullNameFormat);
                    if (requestKey is not null && context.HandlerByRequestFullName.TryGetValue(requestKey, out var handlerType))
                    {
                        model.CallGraph.Add(new CallEdge(
                            CallerType: callerType.Id,
                            CallerMethod: MethodSignature(caller),
                            TargetType: handlerType.Id,
                            TargetMethod: "Handle(...)",
                            Kind: "MediatR.Send",
                            Location: SourceLocation.From(invocation.GetLocation())));
                    }
                    else if (requestType is not null && TryGetKnownType(requestType, context, out targetType))
                    {
                        model.CallGraph.Add(new CallEdge(
                            CallerType: callerType.Id,
                            CallerMethod: MethodSignature(caller),
                            TargetType: targetType.Id,
                            TargetMethod: "MediatR request",
                            Kind: "MediatR.Send",
                            Location: SourceLocation.From(invocation.GetLocation())));
                    }
                }

                TryAddPublishConsumerEdges(invocation, target, callerType, caller, semanticModel, context, model);
            }
        }
    }

    private static void IndexRequestHandler(INamedTypeSymbol symbol, TypeInfo typeInfo, AnalysisContext context)
    {
        foreach (var iface in symbol.AllInterfaces)
        {
            if (iface.Name != "IRequestHandler" || iface.ContainingNamespace?.ToDisplayString() != "MediatR" || iface.TypeArguments.Length == 0)
            {
                continue;
            }

            var request = iface.TypeArguments[0].OriginalDefinition.ToDisplayString(FullNameFormat);
            context.HandlerByRequestFullName.TryAdd(request, typeInfo);
        }
    }

    private static void AddTypeUsageRelationship(DesignModel model, TypeInfo source, ITypeSymbol type, AnalysisContext context, string kind, string member, string project)
    {
        foreach (var targetSymbol in FlattenType(type))
        {
            if (SymbolEqualityComparer.Default.Equals(source.SymbolKey, targetSymbol.OriginalDefinition))
            {
                continue;
            }

            if (TryGetKnownType(targetSymbol, context, out var target))
            {
                var normalizedKind = IsCollection(type) && kind is "Composition" or "Aggregation" ? "Aggregation" : kind;
                model.Relationships.Add(new Relationship(source.Id, target.Id, normalizedKind, member, project));
            }
        }
    }

    private static IEnumerable<ITypeSymbol> FlattenType(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol array)
        {
            yield return array.ElementType;
            yield break;
        }

        if (type is INamedTypeSymbol named)
        {
            yield return named;
            foreach (var argument in named.TypeArguments)
            {
                foreach (var nested in FlattenType(argument))
                {
                    yield return nested;
                }
            }
        }
    }

    private static bool IsCollection(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol)
        {
            return true;
        }

        if (type is INamedTypeSymbol named)
        {
            return named.AllInterfaces.Any(i => i.ToDisplayString(FullNameFormat).StartsWith("System.Collections.Generic.IEnumerable<", StringComparison.Ordinal)) ||
                   named.ToDisplayString(FullNameFormat).StartsWith("System.Collections.Generic.IEnumerable<", StringComparison.Ordinal);
        }

        return false;
    }

    private static string ExtractTypeOf(ExpressionSyntax expression, SemanticModel semanticModel)
    {
        if (expression is TypeOfExpressionSyntax typeOf)
        {
            var symbol = semanticModel.GetSymbolInfo(typeOf.Type).Symbol as ITypeSymbol;
            return symbol?.ToDisplayString(FullNameFormat) ?? typeOf.Type.ToString();
        }

        var type = semanticModel.GetTypeInfo(expression).Type;
        return type?.ToDisplayString(FullNameFormat) ?? expression.ToString();
    }

    private static bool TryGetKnownType(ITypeSymbol symbol, AnalysisContext context, out TypeInfo type)
    {
        var original = symbol.OriginalDefinition;
        if (context.TypeBySymbol.TryGetValue(original, out type!))
        {
            return true;
        }

        return context.TypeByFullName.TryGetValue(original.ToDisplayString(FullNameFormat), out type!);
    }

    private static string MethodSignature(IMethodSymbol method)
    {
        return method.Parameters.Length == 0
            ? $"{method.Name}()"
            : $"{method.Name}(...)";
    }

    private static void RenderMermaidFiles(string outputDirectory, string format)
    {
        foreach (var file in Directory.EnumerateFiles(outputDirectory, "*.mmd", SearchOption.AllDirectories))
        {
            var output = Path.ChangeExtension(file, format);
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = "mmdc",
                ArgumentList = { "-i", file, "-o", output, "-e", format, "-b", "white", "-q" },
                RedirectStandardError = true,
                RedirectStandardOutput = true
            });

            process?.WaitForExit();
            if (process?.ExitCode != 0)
            {
                Console.Error.WriteLine($"mmdc failed for {file}: {process?.StandardError.ReadToEnd()}");
            }
        }
    }
}

internal sealed class DiagramWriter(AnalyzerOptions options, DesignModel model)
{
    private static string SequenceCallLabel(CallEdge edge)
    {
        if (edge.Kind == "MediatR.Send")
        {
            return $"MediatR.Send -> {edge.TargetMethod}";
        }

        if (edge.Kind.StartsWith("MassTransit.Publish", StringComparison.Ordinal))
        {
            return $"{edge.Kind} -> {edge.TargetMethod}";
        }

        return edge.TargetMethod;
    }

    private static void AppendClassHeader(StringBuilder sb, TypeInfo type)
    {
        var className = MermaidId(type.Id);
        var label = SanitizeLabel(type.Name);
        sb.AppendLine($"  class {className}[\"{label}\"] {{");
    }

    public string[] WriteAll()
    {
        var files = new List<string>();
        files.Add(WriteSystemOverview());
        files.AddRange(WriteEntryPointDiagrams());
        if (options.ClassDetail.Equals("detailed", StringComparison.OrdinalIgnoreCase))
        {
            files.AddRange(WriteDetailedLayerClassDiagrams());
        }
        files.Add(WriteDiGraph());
        files.Add(WriteMarkdownIndex(files));
        return files.ToArray();
    }

    private string WriteSystemOverview()
    {
        var classTypes = model.Types
            .Where(ShouldIncludeInClassDiagram)
            .OrderBy(t => t.Service)
            .ThenBy(t => t.Layer)
            .ThenBy(t => t.Namespace)
            .ThenBy(t => t.Name)
            .ToArray();

        var overviewPath = Path.Combine(options.OutputDirectory, "class", "system-overview.mmd");
        Directory.CreateDirectory(Path.GetDirectoryName(overviewPath)!);
        File.WriteAllText(overviewPath, BuildOverviewDiagram(classTypes));
        return overviewPath;
    }

    private IEnumerable<string> WriteEntryPointDiagrams()
    {
        foreach (var entryPoint in DiscoverEntryPoints().OrderBy(e => e.Service).ThenBy(e => e.Kind).ThenBy(e => e.Name))
        {
            var directory = Path.Combine(options.OutputDirectory, "entrypoints", SafeName(entryPoint.Service), SafeName(entryPoint.Name));
            Directory.CreateDirectory(directory);

            var classPath = Path.Combine(directory, "class.mmd");
            File.WriteAllText(classPath, BuildEntryPointClassDiagram(entryPoint));
            yield return classPath;

            var sequencePath = Path.Combine(directory, "sequence.mmd");
            File.WriteAllText(sequencePath, BuildSequenceDiagram(entryPoint.RootTypeId, entryPoint.RootMethod, entryPoint));
            yield return sequencePath;
        }
    }

    private IEnumerable<string> WriteDetailedLayerClassDiagrams()
    {
        var classTypes = model.Types
            .Where(ShouldIncludeInClassDiagram)
            .OrderBy(t => t.Service)
            .ThenBy(t => t.Layer)
            .ThenBy(t => t.Namespace)
            .ThenBy(t => t.Name)
            .ToArray();
        foreach (var group in classTypes.GroupBy(t => $"{t.Service}/{t.Layer}").OrderBy(g => g.Key))
        {
            var chunks = group.Chunk(Math.Max(10, options.MaxTypesPerDiagram)).ToArray();
            for (var i = 0; i < chunks.Length; i++)
            {
                var service = SafeName(group.First().Service);
                var layer = SafeName(group.First().Layer);
                var suffix = chunks.Length > 1 ? $"-{i + 1}" : "";
                var path = Path.Combine(options.OutputDirectory, "class", $"{service}-{layer}{suffix}.mmd");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, BuildClassDiagram(chunks[i]));
                yield return path;
            }
        }
    }

    private IEnumerable<EntryPoint> DiscoverEntryPoints()
    {
        var typeById = model.Types.ToDictionary(t => t.Id, StringComparer.Ordinal);

        var publicMethodNames = model.Types
            .ToDictionary(
                t => t.Id,
                t => t.Methods
                    .Where(m => m.Accessibility == "Public")
                    .Select(m => m.Name)
                    .ToHashSet(StringComparer.Ordinal),
                StringComparer.Ordinal);

        var apiRoots = model.CallGraph
            .Where(c =>
                typeById.TryGetValue(c.CallerType, out var type) &&
                type.Name.EndsWith("Controller", StringComparison.Ordinal) &&
                publicMethodNames.TryGetValue(c.CallerType, out var methods) &&
                methods.Contains(MethodName(c.CallerMethod)))
            .GroupBy(c => (c.CallerType, c.CallerMethod));

        foreach (var group in apiRoots)
        {
            var type = typeById[group.Key.CallerType];

            yield return new EntryPoint(
                Kind: "api",
                Service: type.Service,
                Name: $"{ShortName(type.Id)}-{group.Key.CallerMethod}",
                RootTypeId: group.Key.CallerType,
                RootMethod: group.Key.CallerMethod,
                Actor: "Client");
        }
    }

    private IEnumerable<string> WriteFeatureClassDiagrams()
    {
        var requests = model.Types
            .Where(t => t.Layer == "Application" && IsRequestLike(t) && t.Feature != "Common")
            .GroupBy(t => (t.Service, t.Feature))
            .OrderBy(g => g.Key.Service)
            .ThenBy(g => g.Key.Feature);

        foreach (var feature in requests)
        {
            var featureRequests = feature.OrderBy(t => t.Name).ToArray();
            var requestChunks = featureRequests.Chunk(Math.Max(4, options.MaxTypesPerDiagram / 4)).ToArray();
            for (var i = 0; i < requestChunks.Length; i++)
            {
                var selected = new Dictionary<string, TypeInfo>(StringComparer.Ordinal);
                foreach (var request in requestChunks[i])
                {
                    AddSelected(selected, request);
                    foreach (var handler in FindHandlersForRequest(request))
                    {
                        AddSelected(selected, handler);
                        foreach (var dependency in FindPrimaryDependencies(handler).Take(8))
                        {
                            AddSelected(selected, dependency);
                        }

                        foreach (var controller in FindControllersForHandler(handler).Take(3))
                        {
                            AddSelected(selected, controller);
                        }
                    }
                }

                if (selected.Count == 0)
                {
                    continue;
                }

                var suffix = requestChunks.Length > 1 ? $"-{i + 1}" : "";
                var path = Path.Combine(options.OutputDirectory, "class", "features", SafeName(feature.Key.Service), $"{SafeName(feature.Key.Feature)}{suffix}.mmd");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, BuildFeatureClassDiagram(selected.Values));
                yield return path;
            }
        }
    }

    private static void AddSelected(Dictionary<string, TypeInfo> selected, TypeInfo type)
    {
        selected.TryAdd(type.Id, type);
    }

    private IEnumerable<TypeInfo> FindHandlersForRequest(TypeInfo request)
    {
        var requestName = request.FullName;
        return model.Types.Where(t =>
            t.Name.EndsWith("Handler", StringComparison.Ordinal) &&
            t.Interfaces.Any(i => i.Contains($"IRequestHandler<{requestName}", StringComparison.Ordinal) || i.Contains($"ICommandHandler<{requestName}", StringComparison.Ordinal) || i.Contains($"IQueryHandler<{requestName}", StringComparison.Ordinal)));
    }

    private IEnumerable<TypeInfo> FindRequestsForHandler(TypeInfo handler)
    {
        foreach (var request in model.Types.Where(IsRequestLike))
        {
            var requestName = request.FullName;
            if (handler.Interfaces.Any(i => i.Contains($"IRequestHandler<{requestName}", StringComparison.Ordinal) ||
                                            i.Contains($"ICommandHandler<{requestName}", StringComparison.Ordinal) ||
                                            i.Contains($"IQueryHandler<{requestName}", StringComparison.Ordinal)))
            {
                yield return request;
            }
        }
    }

    private IEnumerable<TypeInfo> FindPrimaryDependencies(TypeInfo handler)
    {
        var dependencyIds = model.Relationships
            .Where(r => r.Source == handler.Id && r.Kind is "Composition" or "Aggregation" or "Dependency")
            .Select(r => r.Target)
            .Distinct(StringComparer.Ordinal);

        foreach (var dependencyId in dependencyIds)
        {
            var type = model.Types.FirstOrDefault(t => t.Id == dependencyId);
            if (type is null || type.Name == handler.Name || IsLowSignalDependency(type))
            {
                continue;
            }

            yield return type;
        }
    }

    private IEnumerable<TypeInfo> FindControllersForHandler(TypeInfo handler)
    {
        var controllerIds = model.CallGraph
            .Where(c => c.Kind == "MediatR.Send" && c.TargetType == handler.Id)
            .Select(c => c.CallerType)
            .Distinct(StringComparer.Ordinal);

        foreach (var controllerId in controllerIds)
        {
            var type = model.Types.FirstOrDefault(t => t.Id == controllerId);
            if (type is not null)
            {
                yield return type;
            }
        }
    }

    private static bool IsRequestLike(TypeInfo type)
    {
        return type.Name.EndsWith("Command", StringComparison.Ordinal) || type.Name.EndsWith("Query", StringComparison.Ordinal);
    }

    private static bool IsConsumer(TypeInfo type)
    {
        return type.Interfaces.Any(i => i.StartsWith("MassTransit.IConsumer<", StringComparison.Ordinal) || i.StartsWith("IConsumer<", StringComparison.Ordinal));
    }

    private static bool IsServerGrpcService(TypeInfo type)
    {
        return type.Name.EndsWith("GrpcService", StringComparison.Ordinal) &&
               type.BaseType?.EndsWith("ServiceBase", StringComparison.Ordinal) == true;
    }

    private static bool IsGrpcClientWrapper(TypeInfo type)
    {
        return type.Name.EndsWith("GrpcService", StringComparison.Ordinal) && !IsServerGrpcService(type);
    }

    private bool IsGrpcBackedInterface(TypeInfo type)
    {
        return type.Kind == "Interface" &&
               model.DependencyRegistrations.Any(r =>
                   r.Service == type.FullName &&
                   r.Implementation.EndsWith("GrpcService", StringComparison.Ordinal));
    }

    private static bool IsDomainEntity(TypeInfo type)
    {
        var path = (type.FilePath ?? "").Replace('\\', '/');
        return type.Layer == "Domain" && path.Contains("/Entities/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLowSignalDependency(TypeInfo type)
    {
        return type.FullName.StartsWith("System.", StringComparison.Ordinal) ||
               type.Name is "CancellationToken" or "Result" or "Error" ||
               type.Name.EndsWith("Request", StringComparison.Ordinal) ||
               type.Name.EndsWith("Response", StringComparison.Ordinal);
    }

    private bool ShouldIncludeInClassDiagram(TypeInfo type)
    {
        if (type.Name is "AssemblyReference" or "Program" || type.Name.EndsWith("Migration", StringComparison.Ordinal))
        {
            return false;
        }

        var path = (type.FilePath ?? "").Replace('\\', '/');
        if (path.Contains("/Migrations/", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("/Properties/", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("/Build/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (options.ClassDetail.Equals("detailed", StringComparison.OrdinalIgnoreCase))
        {
            return options.IncludeDtos || !IsDtoLike(type, path);
        }

        if (!options.IncludeDtos && IsDtoLike(type, path))
        {
            return false;
        }

        return IsArchitecturalType(type, path);
    }

    private static bool IsArchitecturalType(TypeInfo type, string path)
    {
        return type.Layer == "Domain" && (path.Contains("/Entities/", StringComparison.OrdinalIgnoreCase) || path.Contains("/Repositories/", StringComparison.OrdinalIgnoreCase)) ||
               type.Name.EndsWith("Controller", StringComparison.Ordinal) ||
               type.Name.EndsWith("GrpcService", StringComparison.Ordinal) ||
               type.Name.EndsWith("Handler", StringComparison.Ordinal) ||
               type.Name.EndsWith("Consumer", StringComparison.Ordinal) ||
               type.Name.EndsWith("StateMachine", StringComparison.Ordinal) ||
               type.Name.EndsWith("Repository", StringComparison.Ordinal) ||
               type.Name.EndsWith("UnitOfWork", StringComparison.Ordinal) ||
               type.Name.EndsWith("Service", StringComparison.Ordinal) ||
               type.Name.EndsWith("Client", StringComparison.Ordinal) ||
               type.Name.EndsWith("Provider", StringComparison.Ordinal);
    }

    private static bool IsDtoLike(TypeInfo type, string path)
    {
        return path.Contains("/Models/", StringComparison.OrdinalIgnoreCase) ||
               path.Contains("/Validators/", StringComparison.OrdinalIgnoreCase) ||
               path.Contains("/Mapping/", StringComparison.OrdinalIgnoreCase) ||
               path.Contains("/Setups/", StringComparison.OrdinalIgnoreCase) ||
               path.Contains("/Configs/", StringComparison.OrdinalIgnoreCase) ||
               type.Name.EndsWith("Request", StringComparison.Ordinal) ||
               type.Name.EndsWith("Response", StringComparison.Ordinal) ||
               type.Name.EndsWith("Model", StringComparison.Ordinal) ||
               type.Name.EndsWith("Dto", StringComparison.Ordinal) ||
               type.Name.EndsWith("Validator", StringComparison.Ordinal) ||
               type.Name.EndsWith("Profile", StringComparison.Ordinal) ||
               type.Name.EndsWith("Options", StringComparison.Ordinal);
    }

    private string BuildOverviewDiagram(IEnumerable<TypeInfo> types)
    {
        var sb = new StringBuilder();
        AppendMermaidConfig(sb);
        sb.AppendLine("flowchart LR");

        foreach (var serviceGroup in types.GroupBy(t => t.Service).OrderBy(g => g.Key))
        {
            var serviceId = MermaidId(serviceGroup.Key);
            sb.AppendLine($"  subgraph {serviceId}[\"{serviceGroup.Key}\"]");
            foreach (var layerGroup in serviceGroup.GroupBy(t => t.Layer).OrderBy(g => LayerOrder(g.Key)))
            {
                var layerId = MermaidId($"{serviceGroup.Key}_{layerGroup.Key}");
                sb.AppendLine($"    {layerId}[\"{layerGroup.Key}: {layerGroup.Count()} types\"]");
            }
            sb.AppendLine("  end");
        }

        var edges = new HashSet<string>(StringComparer.Ordinal);
        foreach (var relationship in model.Relationships)
        {
            var source = model.Types.FirstOrDefault(t => t.Id == relationship.Source);
            var target = model.Types.FirstOrDefault(t => t.Id == relationship.Target);
            if (source is null || target is null || source.Service == target.Service && source.Layer == target.Layer)
            {
                continue;
            }

            var sourceLayer = MermaidId($"{source.Service}_{source.Layer}");
            var targetLayer = MermaidId($"{target.Service}_{target.Layer}");
            if (edges.Add($"{sourceLayer}|{targetLayer}"))
            {
                sb.AppendLine($"  {sourceLayer} -.-> {targetLayer}");
            }
        }

        return sb.ToString();
    }

    private string BuildClassDiagram(IEnumerable<TypeInfo> types)
    {
        var selected = types.ToDictionary(t => t.Id, StringComparer.Ordinal);
        var sb = new StringBuilder();
        AppendMermaidConfig(sb);
        sb.AppendLine("classDiagram");

        foreach (var type in selected.Values)
        {
            var className = MermaidId(type.Id);
            AppendClassHeader(sb, type);
            sb.AppendLine($"    <<{type.Kind}>>");
            foreach (var property in type.Properties.Take(options.MaxClassProperties))
            {
                sb.AppendLine($"    +{SanitizeMember(property.Type)} {SanitizeMember(property.Name)}");
            }

            foreach (var method in type.Methods.Take(options.MaxClassMethods))
            {
                sb.AppendLine($"    +{SanitizeMember(method.DisplaySignature)}");
            }

            sb.AppendLine("  }");
        }

        foreach (var relationship in model.Relationships.Where(r => selected.ContainsKey(r.Source) && selected.ContainsKey(r.Target) && ShouldIncludeClassRelationship(r)))
        {
            AppendClassRelationship(sb, relationship);
        }

        return sb.ToString();
    }

    private string BuildFeatureClassDiagram(IEnumerable<TypeInfo> types)
    {
        var selected = types
            .OrderBy(FeatureDiagramRank)
            .ThenBy(t => t.Name)
            .ToDictionary(t => t.Id, StringComparer.Ordinal);
        var sb = new StringBuilder();
        AppendMermaidConfig(sb);
        sb.AppendLine("classDiagram");

        foreach (var type in selected.Values)
        {
            var className = MermaidId(type.Id);
            AppendClassHeader(sb, type);
            sb.AppendLine($"    <<{FeatureStereotype(type)}>>");

            var propertiesToShow = IsRequestLike(type) ? Math.Min(5, Math.Max(2, options.MaxClassProperties)) : options.MaxClassProperties;
            foreach (var property in type.Properties.Where(p => p.Name != "EqualityContract").Take(propertiesToShow))
            {
                sb.AppendLine($"    +{SanitizeMember(property.Type)} {SanitizeMember(property.Name)}");
            }

            var methodsToShow = type.Name.EndsWith("Handler", StringComparison.Ordinal) ? Math.Max(1, options.MaxClassMethods) : options.MaxClassMethods;
            foreach (var method in type.Methods.Take(methodsToShow))
            {
                sb.AppendLine($"    +{SanitizeMember(method.DisplaySignature)}");
            }

            sb.AppendLine("  }");
        }

        var relationshipLines = new HashSet<string>(StringComparer.Ordinal);
        foreach (var relationship in model.Relationships.Where(r => selected.ContainsKey(r.Source) && selected.ContainsKey(r.Target) && ShouldIncludeFeatureRelationship(r, selected)))
        {
            AppendFeatureRelationship(sb, relationship, selected, relationshipLines);
        }

        foreach (var edge in model.CallGraph.Where(c => c.Kind == "MediatR.Send" && selected.ContainsKey(c.CallerType) && selected.ContainsKey(c.TargetType)))
        {
            var line = $"  {MermaidId(edge.CallerType)} ..> {MermaidId(edge.TargetType)} : MediatR.Send";
            if (relationshipLines.Add(line))
            {
                sb.AppendLine(line);
            }
        }

        return sb.ToString();
    }

    private string BuildEntryPointClassDiagram(EntryPoint entryPoint)
    {
        var selected = SelectEntryPointTypes(entryPoint)
            .OrderBy(t => EntryPointTypeRank(entryPoint, t))
            .ThenBy(t => t.Name)
            .ToDictionary(t => t.Id, StringComparer.Ordinal);
        var sb = new StringBuilder();
        AppendMermaidConfig(sb);
        sb.AppendLine("classDiagram");

        foreach (var type in selected.Values)
        {
            AppendEntryPointClass(sb, entryPoint, type);
        }

        var relationshipLines = new HashSet<string>(StringComparer.Ordinal);
        foreach (var relationship in model.Relationships.Where(r => selected.ContainsKey(r.Source) && selected.ContainsKey(r.Target) && ShouldIncludeEntryPointRelationship(r, selected)))
        {
            AppendEntryPointRelationship(sb, relationship, selected, relationshipLines);
        }

        foreach (var edge in model.CallGraph.Where(c => c.CallerType != c.TargetType && selected.ContainsKey(c.CallerType) && selected.ContainsKey(c.TargetType) && IsRelevantEntryPointCall(entryPoint, c)))
        {
            var callLabel = edge.Kind.StartsWith("MassTransit.Publish", StringComparison.Ordinal)
                ? "publishes"
                : edge.Kind == "MediatR.Send"
                    ? "MediatR.Send"
                    : MethodName(edge.TargetMethod);

            var line = $"  {MermaidId(edge.CallerType)} ..> {MermaidId(edge.TargetType)} : {SanitizeLabel(callLabel)}";
            if (relationshipLines.Add(line))
            {
                sb.AppendLine(line);
            }
        }

        return sb.ToString();
    }

    private bool IsRelevantEntryPointCall(EntryPoint entryPoint, CallEdge edge)
    {
        return edge.CallerType == entryPoint.RootTypeId ||
               WalkCalls(entryPoint.RootTypeId, entryPoint.RootMethod, Math.Min(options.MaxSequenceDepth, 4)).Any(call => call.Key == edge.Key);
    }

    private IEnumerable<TypeInfo> SelectEntryPointTypes(EntryPoint entryPoint)
    {
        var selected = new Dictionary<string, TypeInfo>(StringComparer.Ordinal);
        var typeById = model.Types.ToDictionary(t => t.Id, StringComparer.Ordinal);

        if (typeById.TryGetValue(entryPoint.RootTypeId, out var root))
        {
            AddSelected(selected, root);
        }

        foreach (var edge in WalkCalls(entryPoint.RootTypeId, entryPoint.RootMethod, Math.Min(options.MaxSequenceDepth, 4)))
        {
            if (typeById.TryGetValue(edge.TargetType, out var target))
            {
                if (ShouldSelectCallTarget(edge, target))
                {
                    AddSelected(selected, target);
                }
            }

            if (selected.ContainsKey(edge.CallerType) && typeById.TryGetValue(edge.CallerType, out var caller))
            {
                AddSelected(selected, caller);
            }
        }

        foreach (var type in selected.Values.ToArray())
        {
            if (type.Name.EndsWith("Handler", StringComparison.Ordinal))
            {
                foreach (var request in FindRequestsForHandler(type))
                {
                    AddSelected(selected, request);
                }
            }

            if (type.Name.EndsWith("Handler", StringComparison.Ordinal) || IsConsumer(type) || IsServerGrpcService(type) || type.Id == entryPoint.RootTypeId)
            {
                foreach (var dependency in FindPrimaryDependencies(type).Take(10))
                {
                    AddSelected(selected, dependency);
                }
            }
        }

        return selected.Values;
    }

    private static bool ShouldSelectCallTarget(CallEdge edge, TypeInfo target)
    {
        if (edge.Kind == "MediatR.Send")
        {
            return true;
        }

        if (edge.Kind.StartsWith("MassTransit.Publish", StringComparison.Ordinal))
        {
            return true;
        }

        if (target.FullName.StartsWith("SharedLibrary.Common.ResponseModel.", StringComparison.Ordinal) ||
            target.FullName == "SharedLibrary.Common.ApiController")
        {
            return false;
        }

        return target.Name.EndsWith("Handler", StringComparison.Ordinal) ||
               target.Name.EndsWith("Service", StringComparison.Ordinal) ||
               target.Name.EndsWith("Repository", StringComparison.Ordinal) ||
               target.Kind == "Interface" ||
               IsDomainEntity(target) ||
               IsGrpcClientWrapper(target) ||
               IsServerGrpcService(target) ||
               IsConsumer(target);
    }

    private IEnumerable<CallEdge> WalkCalls(string rootTypeId, string rootMethod, int maxDepth)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<(string TypeId, string Method, int Depth)>();
        queue.Enqueue((rootTypeId, rootMethod, 0));

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (current.Depth >= maxDepth)
            {
                continue;
            }

            foreach (var edge in model.CallGraph.Where(c => c.CallerType == current.TypeId && c.CallerMethod == current.Method).Take(12))
            {
                if (!visited.Add(edge.Key))
                {
                    continue;
                }

                yield return edge;
                queue.Enqueue((edge.TargetType, edge.TargetMethod, current.Depth + 1));
            }
        }
    }

    private void AppendEntryPointClass(StringBuilder sb, EntryPoint entryPoint, TypeInfo type)
    {
        var className = MermaidId(type.Id);
        AppendClassHeader(sb, type);
        sb.AppendLine($"    <<{EntryPointStereotype(type)}>>");

        foreach (var member in MembersForEntryPointClass(entryPoint, type))
        {
            sb.AppendLine($"    +{SanitizeMember(member)}");
        }

        sb.AppendLine("  }");
    }

    private IEnumerable<string> MembersForEntryPointClass(EntryPoint entryPoint, TypeInfo type)
    {
        if (IsDomainEntity(type))
        {
            foreach (var property in type.Properties.Where(p => p.Name != "EqualityContract"))
            {
                yield return $"{property.Type} {property.Name}";
            }

            foreach (var field in type.Fields.Where(f => !f.Name.Contains("BackingField", StringComparison.Ordinal)))
            {
                yield return $"{field.Type} {field.Name}";
            }

            yield break;
        }

        if (type.Kind == "Interface" && IsInjectedIntoEntryPoint(type, entryPoint))
        {
            foreach (var method in type.Methods.Where(m => m.Accessibility == "Public"))
            {
                yield return method.DisplaySignature;
            }

            yield break;
        }

        if (IsRequestLike(type))
        {
            foreach (var property in type.Properties.Where(p => p.Name != "EqualityContract").Take(5))
            {
                yield return $"{property.Type} {property.Name}";
            }

            yield break;
        }

        if (type.Name.EndsWith("Handler", StringComparison.Ordinal) || IsConsumer(type))
        {
            foreach (var method in type.Methods.Where(m => m.Name is "Handle" or "Consume").Take(2))
            {
                yield return method.DisplaySignature;
            }

            yield break;
        }

        if (type.Id == entryPoint.RootTypeId)
        {
            foreach (var method in type.Methods.Where(m => m.Signature == entryPoint.RootMethod || m.Name == MethodName(entryPoint.RootMethod)).Take(1))
            {
                yield return method.DisplaySignature;
            }
        }
    }

    private bool IsInjectedIntoEntryPoint(TypeInfo type, EntryPoint entryPoint)
    {
        return model.Relationships.Any(r =>
            r.Target == type.Id &&
            r.Kind is "Composition" or "Aggregation" or "Dependency" &&
            SelectEntryPointTypes(entryPoint).Any(selected => selected.Id == r.Source && (selected.Name.EndsWith("Handler", StringComparison.Ordinal) || IsConsumer(selected) || IsServerGrpcService(selected) || selected.Id == entryPoint.RootTypeId)));
    }

    private static string EntryPointStereotype(TypeInfo type)
    {
        if (type.Name.EndsWith("Controller", StringComparison.Ordinal))
        {
            return "Controller";
        }

        if (IsServerGrpcService(type))
        {
            return "GrpcService";
        }

        if (IsConsumer(type))
        {
            return "Consumer";
        }

        if (type.Name.EndsWith("Command", StringComparison.Ordinal))
        {
            return "Command";
        }

        if (type.Name.EndsWith("Query", StringComparison.Ordinal))
        {
            return "Query";
        }

        if (type.Name.EndsWith("Handler", StringComparison.Ordinal))
        {
            return "Handler";
        }

        if (IsDomainEntity(type))
        {
            return "DomainEntity";
        }

        return type.Kind;
    }

    private static int EntryPointTypeRank(EntryPoint entryPoint, TypeInfo type)
    {
        if (type.Id == entryPoint.RootTypeId)
        {
            return 0;
        }

        if (IsRequestLike(type))
        {
            return 1;
        }

        if (type.Name.EndsWith("Handler", StringComparison.Ordinal))
        {
            return 2;
        }

        if (type.Kind == "Interface")
        {
            return 3;
        }

        if (IsDomainEntity(type))
        {
            return 4;
        }

        return 5;
    }

    private bool ShouldIncludeEntryPointRelationship(Relationship relationship, IReadOnlyDictionary<string, TypeInfo> selected)
    {
        if (relationship.Label?.Contains("<", StringComparison.Ordinal) == true)
        {
            return false;
        }

        var source = selected[relationship.Source];
        var target = selected[relationship.Target];
        if (relationship.Kind is "Inheritance" or "Implements")
        {
            return source.Name.EndsWith("Handler", StringComparison.Ordinal) || IsRequestLike(source) || IsConsumer(source);
        }

        return (source.Name.EndsWith("Handler", StringComparison.Ordinal) || IsConsumer(source) || IsServerGrpcService(source)) &&
               (IsRequestLike(target) || IsPrimaryFeatureDependency(target)) &&
               relationship.Kind is "Composition" or "Aggregation" or "Dependency";
    }

    private static void AppendEntryPointRelationship(StringBuilder sb, Relationship relationship, IReadOnlyDictionary<string, TypeInfo> selected, HashSet<string> relationshipLines)
    {
        var sourceType = selected[relationship.Source];
        var targetType = selected[relationship.Target];

        if (relationship.Kind is "Inheritance" or "Implements")
        {
            var beforeLength = sb.Length;
            AppendClassRelationship(sb, relationship);
            var inheritanceLine = sb.ToString(beforeLength, sb.Length - beforeLength).TrimEnd();
            if (!relationshipLines.Add(inheritanceLine))
            {
                sb.Length = beforeLength;
            }
            return;
        }

        var label = IsRequestLike(targetType) ? "handles" : "uses";
        if (relationship.Kind is "Composition" or "Aggregation" || relationship.Label?.StartsWith(".ctor", StringComparison.Ordinal) == true)
        {
            label = IsRequestLike(targetType) ? "handles" : "injects";
        }

        var line = $"  {MermaidId(sourceType.Id)} ..> {MermaidId(targetType.Id)} : {label}";
        if (relationshipLines.Add(line))
        {
            sb.AppendLine(line);
        }
    }

    private static void AppendClassRelationship(StringBuilder sb, Relationship relationship)
    {
        var source = MermaidId(relationship.Source);
        var target = MermaidId(relationship.Target);
        var arrow = relationship.Kind switch
        {
            "Inheritance" => "<|--",
            "Implements" => "<|..",
            "Composition" => "*--",
            "Aggregation" => "o--",
            _ => "..>"
        };
        sb.AppendLine($"  {target} {arrow} {source} : {SanitizeLabel(relationship.Label)}");
    }

    private bool ShouldIncludeFeatureRelationship(Relationship relationship, IReadOnlyDictionary<string, TypeInfo> selected)
    {
        if (relationship.Label?.Contains("<", StringComparison.Ordinal) == true)
        {
            return false;
        }

        var source = selected[relationship.Source];
        var target = selected[relationship.Target];
        if (relationship.Kind is "Inheritance" or "Implements")
        {
            return source.Name.EndsWith("Handler", StringComparison.Ordinal) || IsRequestLike(source);
        }

        return source.Name.EndsWith("Handler", StringComparison.Ordinal) &&
               (IsRequestLike(target) || IsPrimaryFeatureDependency(target)) &&
               relationship.Kind is "Composition" or "Aggregation" or "Dependency";
    }

    private static void AppendFeatureRelationship(StringBuilder sb, Relationship relationship, IReadOnlyDictionary<string, TypeInfo> selected, HashSet<string> relationshipLines)
    {
        var sourceType = selected[relationship.Source];
        var targetType = selected[relationship.Target];

        if (relationship.Kind is "Inheritance" or "Implements")
        {
            AppendClassRelationship(sb, relationship);
            return;
        }

        var label = IsRequestLike(targetType) ? "handles" : "uses";
        if (relationship.Kind is "Composition" or "Aggregation" || relationship.Label?.StartsWith(".ctor", StringComparison.Ordinal) == true)
        {
            label = IsRequestLike(targetType) ? "handles" : "injects";
        }

        var line = $"  {MermaidId(sourceType.Id)} ..> {MermaidId(targetType.Id)} : {label}";
        if (relationshipLines.Add(line))
        {
            sb.AppendLine(line);
        }
    }

    private static bool IsPrimaryFeatureDependency(TypeInfo type)
    {
        return type.Kind == "Interface" ||
               type.Layer == "Domain" ||
               type.Name.EndsWith("Repository", StringComparison.Ordinal) ||
               type.Name.EndsWith("Service", StringComparison.Ordinal) ||
               type.Name.EndsWith("Client", StringComparison.Ordinal) ||
               type.Name.EndsWith("Provider", StringComparison.Ordinal);
    }

    private static int FeatureDiagramRank(TypeInfo type)
    {
        if (type.Name.EndsWith("Controller", StringComparison.Ordinal))
        {
            return 0;
        }

        if (IsRequestLike(type))
        {
            return 1;
        }

        if (type.Name.EndsWith("Handler", StringComparison.Ordinal))
        {
            return 2;
        }

        if (type.Kind == "Interface")
        {
            return 3;
        }

        return 4;
    }

    private static string FeatureStereotype(TypeInfo type)
    {
        if (type.Name.EndsWith("Controller", StringComparison.Ordinal))
        {
            return "Controller";
        }

        if (type.Name.EndsWith("Command", StringComparison.Ordinal))
        {
            return "Command";
        }

        if (type.Name.EndsWith("Query", StringComparison.Ordinal))
        {
            return "Query";
        }

        if (type.Name.EndsWith("Handler", StringComparison.Ordinal))
        {
            return "Handler";
        }

        return type.Kind;
    }

    private bool ShouldIncludeClassRelationship(Relationship relationship)
    {
        if (relationship.Label?.Contains("<", StringComparison.Ordinal) == true)
        {
            return false;
        }

        if (options.ClassDetail.Equals("detailed", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return relationship.Kind is "Inheritance" or "Implements" or "Composition" or "Aggregation";
    }

    private string WriteDiGraph()
    {
        var path = Path.Combine(options.OutputDirectory, "di-graph.mmd");
        var sb = new StringBuilder();
        AppendMermaidConfig(sb);
        sb.AppendLine("flowchart LR");
        foreach (var registration in model.DependencyRegistrations)
        {
            var service = MermaidId(registration.Service);
            var implementation = MermaidId(registration.Implementation);
            sb.AppendLine($"  {service}[\"{ShortName(registration.Service)}\"] -->|{registration.Lifetime}| {implementation}[\"{ShortName(registration.Implementation)}\"]");
        }

        File.WriteAllText(path, sb.ToString());
        return path;
    }

    private IEnumerable<string> WriteSequenceDiagrams()
    {
        var controllerIds = model.Types
            .Where(t => t.Name.EndsWith("Controller", StringComparison.Ordinal))
            .Select(t => t.Id)
            .ToHashSet(StringComparer.Ordinal);
        var controllerActions = model.Types
            .Where(t => controllerIds.Contains(t.Id))
            .SelectMany(t => t.Methods.Where(m => m.Accessibility == "Public").Select(m => (t.Id, m.Name)))
            .ToHashSet();

        var entryMethods = model.CallGraph
            .Where(c => controllerIds.Contains(c.CallerType) && controllerActions.Contains((c.CallerType, MethodName(c.CallerMethod))))
            .GroupBy(c => (c.CallerType, c.CallerMethod))
            .OrderBy(g => g.Key.CallerType)
            .ThenBy(g => g.Key.CallerMethod)
            .Take(120);

        foreach (var entry in entryMethods)
        {
            var service = model.Types.FirstOrDefault(t => t.Id == entry.Key.CallerType)?.Service ?? "System";
            var path = Path.Combine(options.OutputDirectory, "sequence", SafeName(service), $"{SafeName(ShortName(entry.Key.CallerType))}-{SafeName(entry.Key.CallerMethod)}.mmd");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, BuildSequenceDiagram(entry.Key.CallerType, entry.Key.CallerMethod));
            yield return path;
        }
    }

    private string BuildSequenceDiagram(string rootType, string rootMethod, EntryPoint? entryPoint = null)
    {
        var sb = new StringBuilder();
        AppendMermaidConfig(sb);
        sb.AppendLine("sequenceDiagram");
        if (entryPoint is not null)
        {
            sb.AppendLine($"  actor {MermaidParticipant(entryPoint.Actor)} as {entryPoint.Actor}");
            AppendParticipant(sb, rootType, new HashSet<string>(StringComparer.Ordinal));
            sb.AppendLine($"  {MermaidParticipant(entryPoint.Actor)}->>+{MermaidParticipant(rootType)}: {SanitizeLabel(rootMethod)}");
        }

        var visited = new HashSet<string>(StringComparer.Ordinal);
        var participants = new HashSet<string>(StringComparer.Ordinal);
        if (entryPoint is not null)
        {
            participants.Add(MermaidParticipant(rootType));
        }

        AppendCalls(sb, rootType, rootMethod, 0, visited, participants);
        if (visited.Count == 0)
        {
            AppendParticipant(sb, rootType, participants);
        }

        if (entryPoint is not null)
        {
            sb.AppendLine($"  {MermaidParticipant(rootType)}-->>-{MermaidParticipant(entryPoint.Actor)}: return");
        }

        return sb.ToString();
    }

    private void AppendCalls(StringBuilder sb, string callerType, string callerMethod, int depth, HashSet<string> visited, HashSet<string> participants)
    {
        if (depth >= options.MaxSequenceDepth)
        {
            return;
        }

        var edges = model.CallGraph
            .Where(c => c.CallerType == callerType && c.CallerMethod == callerMethod)
            .OrderBy(c => c.Location?.Line ?? int.MaxValue)
            .ThenBy(c => c.Location?.Column ?? int.MaxValue)
            .ThenBy(c => c.TargetType, StringComparer.Ordinal)
            .Take(12)
            .ToArray();

        foreach (var edge in edges)
        {
            var key = edge.Key;
            if (!visited.Add(key))
            {
                continue;
            }

            var caller = MermaidParticipant(edge.CallerType);
            var target = MermaidParticipant(edge.TargetType);
            AppendParticipant(sb, edge.CallerType, participants);
            AppendParticipant(sb, edge.TargetType, participants);
            sb.AppendLine($"  {caller}->>+{target}: {SanitizeLabel(SequenceCallLabel(edge))}");
            AppendCalls(sb, edge.TargetType, edge.TargetMethod, depth + 1, visited, participants);
            sb.AppendLine($"  {target}-->>-{caller}: return");
        }
    }

    private static void AppendParticipant(StringBuilder sb, string typeId, HashSet<string> participants)
    {
        var participant = MermaidParticipant(typeId);
        if (participants.Add(participant))
        {
            sb.AppendLine($"  participant {participant} as {ShortName(typeId)}");
        }
    }

    private string WriteMarkdownIndex(IReadOnlyCollection<string> files)
    {
        var path = Path.Combine(options.OutputDirectory, "README.md");
        var sb = new StringBuilder();
        sb.AppendLine("# Detailed Design Export");
        sb.AppendLine();
        sb.AppendLine($"Generated from `{model.InputPath}`.");
        sb.AppendLine();
        sb.AppendLine("## Summary");
        sb.AppendLine();
        sb.AppendLine($"- Projects: {model.Projects.Count}");
        sb.AppendLine($"- Types: {model.Types.Count}");
        sb.AppendLine($"- Relationships: {model.Relationships.Count}");
        sb.AppendLine($"- DI registrations: {model.DependencyRegistrations.Count}");
        sb.AppendLine($"- Call edges: {model.CallGraph.Count}");
        sb.AppendLine();
        sb.AppendLine("## Files");
        sb.AppendLine();
        foreach (var file in files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            sb.AppendLine($"- `{Path.GetRelativePath(options.OutputDirectory, file)}`");
        }

        File.WriteAllText(path, sb.ToString());
        return path;
    }

    private static string MermaidId(string value)
    {
        var chars = value.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray();
        return new string(chars);
    }

    private static void AppendMermaidConfig(StringBuilder sb)
    {
        sb.AppendLine("---");
        sb.AppendLine("config:");
        sb.AppendLine("  flowchart:");
        sb.AppendLine("    defaultRenderer: \"elk\"");
        sb.AppendLine("    curve: step");
        sb.AppendLine("---");
    }

    private static string MermaidParticipant(string value) => $"P_{MermaidId(value)}";

    private static int LayerOrder(string layer) => layer switch
    {
        "WebApi" => 0,
        "Application" => 1,
        "Domain" => 2,
        "Infrastructure" => 3,
        "SharedLibrary" => 4,
        "ApiGateway" => 5,
        _ => 10
    };

    private static string MethodName(string signature)
    {
        var index = signature.IndexOf('(', StringComparison.Ordinal);
        return index < 0 ? signature : signature[..index];
    }

    private static string SafeName(string value)
    {
        var chars = value.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '-').ToArray();
        return new string(chars).Trim('-');
    }

    private static string ShortName(string value) => value.Split('.', '+').LastOrDefault() ?? value;

    private static string SanitizeMember(string value) => value.Replace("<", "~", StringComparison.Ordinal).Replace(">", "~", StringComparison.Ordinal);

    private static string SanitizeLabel(string? value) => string.IsNullOrWhiteSpace(value) ? "" : value.Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal).Replace(":", "-", StringComparison.Ordinal);
}

internal sealed class AnalysisContext
{
    public Dictionary<ISymbol, TypeInfo> TypeBySymbol { get; } = new(SymbolEqualityComparer.Default);
    public Dictionary<string, TypeInfo> TypeByFullName { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, TypeInfo> HandlerByRequestFullName { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, List<TypeInfo>> ConsumersByMessageFullName { get; } = new(StringComparer.Ordinal);
}

internal sealed record DesignModel
{
    public DateTimeOffset GeneratedAtUtc { get; set; }
    public string InputPath { get; set; } = "";
    public List<ProjectInfo> Projects { get; set; } = [];
    public List<TypeInfo> Types { get; set; } = [];
    public List<Relationship> Relationships { get; set; } = [];
    public List<DiRegistration> DependencyRegistrations { get; set; } = [];
    public List<CallEdge> CallGraph { get; set; } = [];
    public string[] OutputFiles { get; set; } = [];
}

internal sealed record ProjectInfo(string Name, string FilePath, string Service, string Layer)
{
    public static ProjectInfo FromProject(Project project)
    {
        var path = project.FilePath ?? "";
        return new ProjectInfo(project.Name, path, Classify.Service(path), Classify.Layer(path));
    }
}

internal sealed record TypeInfo
{
    [JsonIgnore]
    public ISymbol SymbolKey { get; init; } = null!;
    public string Id { get; init; } = "";
    public string FullName { get; init; } = "";
    public string Name { get; init; } = "";
    public string Namespace { get; init; } = "";
    public string Kind { get; init; } = "";
    public string Accessibility { get; init; } = "";
    public bool IsAbstract { get; init; }
    public bool IsSealed { get; init; }
    public bool IsStatic { get; init; }
    public string Service { get; init; } = "";
    public string Layer { get; init; } = "";
    public string Feature { get; init; } = "";
    public string Project { get; init; } = "";
    public string? FilePath { get; init; }
    public string? BaseType { get; init; }
    public string[] Interfaces { get; init; } = [];
    public MemberInfo[] Constructors { get; init; } = [];
    public MemberInfo[] Methods { get; init; } = [];
    public MemberInfo[] Properties { get; init; } = [];
    public MemberInfo[] Fields { get; init; } = [];

    public static TypeInfo FromSymbol(INamedTypeSymbol symbol, Project project, string? filePath)
    {
        var service = Classify.Service(project.FilePath ?? filePath ?? "");
        var layer = Classify.Layer(project.FilePath ?? filePath ?? "");
        var feature = Classify.Feature(filePath ?? project.FilePath ?? "");
        var fullName = symbol.ToDisplayString(DesignAnalyzerFullName.Format);

        return new TypeInfo
        {
            SymbolKey = symbol.OriginalDefinition,
            Id = $"{service}.{layer}:{fullName}",
            FullName = fullName,
            Name = symbol.Name,
            Namespace = symbol.ContainingNamespace?.ToDisplayString() ?? "",
            Kind = symbol.TypeKind.ToString(),
            Accessibility = symbol.DeclaredAccessibility.ToString(),
            IsAbstract = symbol.IsAbstract,
            IsSealed = symbol.IsSealed,
            IsStatic = symbol.IsStatic,
            Service = service,
            Layer = layer,
            Feature = feature,
            Project = project.Name,
            FilePath = filePath,
            BaseType = symbol.BaseType?.SpecialType == SpecialType.System_Object ? null : symbol.BaseType?.ToDisplayString(DesignAnalyzerFullName.Format),
            Interfaces = symbol.Interfaces.Select(i => i.ToDisplayString(DesignAnalyzerFullName.Format)).OrderBy(x => x).ToArray(),
            Constructors = symbol.Constructors.Where(c => !c.IsImplicitlyDeclared).Select(MemberInfo.FromMethod).ToArray(),
            Methods = symbol.GetMembers().OfType<IMethodSymbol>().Where(m => m.MethodKind == MethodKind.Ordinary).Select(MemberInfo.FromMethod).ToArray(),
            Properties = symbol.GetMembers().OfType<IPropertySymbol>().Select(MemberInfo.FromProperty).ToArray(),
            Fields = symbol.GetMembers().OfType<IFieldSymbol>().Where(f => !f.IsImplicitlyDeclared).Select(MemberInfo.FromField).ToArray()
        };
    }
}

internal sealed record MemberInfo(string Name, string Type, string Accessibility, bool IsStatic, string Signature, string DisplaySignature)
{
    public static MemberInfo FromMethod(IMethodSymbol method)
    {
        var compactSignature = method.Parameters.Length == 0
            ? $"{method.Name}()"
            : $"{method.Name}(...)";

        return new MemberInfo(
            method.Name,
            method.ReturnType.ToDisplayString(DesignAnalyzerFullName.Format),
            method.DeclaredAccessibility.ToString(),
            method.IsStatic,
            compactSignature,
            compactSignature);
    }

    public static MemberInfo FromProperty(IPropertySymbol property) => new(property.Name, property.Type.ToDisplayString(DesignAnalyzerFullName.Format), property.DeclaredAccessibility.ToString(), property.IsStatic, property.Name, property.Name);
    public static MemberInfo FromField(IFieldSymbol field) => new(field.Name, field.Type.ToDisplayString(DesignAnalyzerFullName.Format), field.DeclaredAccessibility.ToString(), field.IsStatic, field.Name, field.Name);
}

internal sealed record EntryPoint(string Kind, string Service, string Name, string RootTypeId, string RootMethod, string Actor);

internal sealed record Relationship(string Source, string Target, string Kind, string? Label, string Project)
{
    [JsonIgnore]
    public string Key => $"{Source}|{Target}|{Kind}|{Label}";
    public static Relationship Inheritance(TypeInfo source, TypeInfo target, string project) => new(source.Id, target.Id, "Inheritance", null, project);
    public static Relationship Implements(TypeInfo source, TypeInfo target, string project) => new(source.Id, target.Id, "Implements", null, project);
}

internal sealed record DiRegistration(string Lifetime, string Service, string Implementation, bool ServiceKnown, bool ImplementationKnown, SourceLocation? Location)
{
    [JsonIgnore]
    public string Key => $"{Lifetime}|{Service}|{Implementation}|{Location?.FilePath}|{Location?.Line}";
}

internal sealed record CallEdge(string CallerType, string CallerMethod, string TargetType, string TargetMethod, string Kind, SourceLocation? Location)
{
    [JsonIgnore]
    public string Key => $"{CallerType}|{CallerMethod}|{TargetType}|{TargetMethod}|{Kind}";
}

internal sealed record SourceLocation(string FilePath, int Line, int Column)
{
    public static SourceLocation? From(Location location)
    {
        if (!location.IsInSource)
        {
            return null;
        }

        var line = location.GetLineSpan();
        return new SourceLocation(line.Path, line.StartLinePosition.Line + 1, line.StartLinePosition.Character + 1);
    }
}

internal sealed record Manifest(DateTimeOffset GeneratedAtUtc, string InputPath, int ProjectCount, int TypeCount, int RelationshipCount, int DependencyRegistrationCount, int CallEdgeCount, string[] Files)
{
    public static Manifest From(DesignModel model, string[] files) => new(
        model.GeneratedAtUtc,
        model.InputPath,
        model.Projects.Count,
        model.Types.Count,
        model.Relationships.Count,
        model.DependencyRegistrations.Count,
        model.CallGraph.Count,
        files.Select(Path.GetFullPath).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToArray());
}

internal static class Classify
{
    public static string Service(string path)
    {
        var normalized = path.Replace('\\', '/');
        foreach (var segment in normalized.Split('/'))
        {
            if (segment.EndsWith(".Microservice", StringComparison.OrdinalIgnoreCase))
            {
                return segment.Replace(".Microservice", "", StringComparison.OrdinalIgnoreCase);
            }

            if (segment is "ApiGateway" or "SharedLibrary")
            {
                return segment;
            }
        }

        return "System";
    }

    public static string Layer(string path)
    {
        var normalized = path.Replace('\\', '/');
        foreach (var layer in new[] { "Domain", "Application", "Infrastructure", "WebApi", "SharedLibrary", "ApiGateway" })
        {
            if (normalized.Contains($"/{layer}/", StringComparison.OrdinalIgnoreCase) ||
                normalized.EndsWith($"/{layer}.csproj", StringComparison.OrdinalIgnoreCase))
            {
                return layer;
            }
        }

        return "Other";
    }

    public static string Feature(string path)
    {
        var parts = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < parts.Length - 2; i++)
        {
            if (parts[i].Equals("src", StringComparison.OrdinalIgnoreCase) &&
                parts[i + 1].Equals("Application", StringComparison.OrdinalIgnoreCase))
            {
                return parts[i + 2];
            }
        }

        return "Common";
    }
}

internal static class DesignAnalyzerFullName
{
    public static readonly SymbolDisplayFormat Format = new(
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes);
}
