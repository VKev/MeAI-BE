# DesignAnalyzer

Roslyn-based console tool for exporting detailed design data from the MeAI-BE C# solution.

It loads `Backend/Microservices/Microservices.sln` with `MSBuildWorkspace`, indexes symbols, extracts relationships, DI registrations, and method calls, then writes JSON plus Mermaid diagrams.

## Run

From the repo root:

```bash
dotnet run --project Tools/DesignAnalyzer -- --output artifacts/design
```

Render diagrams through the installed Mermaid CLI:

```bash
dotnet run --project Tools/DesignAnalyzer -- --output artifacts/design --render svg
```

Or render after export:

```bash
python3 Tools/DesignAnalyzer/render_mermaid.py artifacts/design --format svg
```

## Defaults

The default input is:

```text
Backend/Microservices/Microservices.sln
```

The analyzer is linked to the current MeAI-BE microservice set:

- User.Microservice
- Ai.Microservice
- Feed.Microservice
- Notification.Microservice
- ApiGateway
- SharedLibrary

`Tools/DesignAnalyzer/design-analyzer.json` lists the concrete `.csproj` paths used by the current repository layout.

## Useful Options

```bash
dotnet run --project Tools/DesignAnalyzer -- --only User.Microservice,Ai.Microservice
dotnet run --project Tools/DesignAnalyzer -- --include-tests
dotnet run --project Tools/DesignAnalyzer -- --max-types 40 --max-sequence-depth 4
dotnet run --project Tools/DesignAnalyzer -- --class-detail detailed --class-properties 12 --class-methods 10
dotnet run --project Tools/DesignAnalyzer -- --input Backend/Microservices/User.Microservice/src/WebApi/WebApi.csproj
```

## Outputs

- `design.json`: full machine-readable model.
- `manifest.json`: counts and generated file list.
- `README.md`: generated summary.
- `class/system-overview.mmd`: compact service/layer overview.
- `entrypoints/<Service>/api/<Controller-Action>/class.mmd` and `sequence.mmd`: one pair per API action.
- `entrypoints/<Service>/consumer/<Consumer>/class.mmd` and `sequence.mmd`: one pair per MassTransit consumer.
- `entrypoints/<Service>/grpc/<GrpcService-Method>/class.mmd` and `sequence.mmd`: one pair per server gRPC method.
- `entrypoints/<Service>/handler-grpc/<Handler>/class.mmd` and `sequence.mmd`: one pair for handlers that use gRPC-backed client abstractions.
- `class/*.mmd`: detailed layer diagrams when `--class-detail detailed` is used.
- `di-graph.mmd`: service registration graph extracted from `AddScoped`, `AddTransient`, and `AddSingleton`.

Class diagrams are entrypoint-scoped by default so they are readable but still useful:

- Every Mermaid file starts with:

```yaml
---
config:
  flowchart:
    defaultRenderer: "elk"
    curve: step
---
```

- Excludes migrations, setup/config/mapping files, generated assembly markers, validators, and DTO-like request/response/model types.
- Uses a shape inspired by `generate_design_docs.py`, but with Roslyn-backed relationships instead of regex assumptions.
- API diagrams include Controller, Command/Query, Handler, and primary handler dependencies.
- Consumer and gRPC diagrams use the same class/sequence pair convention.
- Domain entities are rendered with full domain properties/fields when they appear in an entrypoint diagram.
- Injected interfaces are rendered with their public method signatures.
- Use `--class-detail detailed` for layer-wide class diagrams with broader type coverage.

The full Roslyn export is still available in `design.json`.

Relationship kinds:

- `Inheritance`
- `Implements`
- `Composition`
- `Aggregation`
- `Dependency`

Call graph kinds:

- `MethodCall`
- `MediatR.Send`
