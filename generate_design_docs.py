import os
import glob
import re
from pathlib import Path

repo_root = Path(r"c:\Users\funny\Documents\GitHub\MeAI-BE")
backend_dir = repo_root / "Backend" / "Microservices"
out_dir = repo_root / "DetailedDesign"

def parse_csharp_dependencies(handler_code):
    # look for private fields
    deps = re.findall(r'private\s+readonly\s+([A-Za-z0-9_<>]+)\s+_[A-Za-z0-9_]+', handler_code)
    # also try to find unit of work accesses
    uow_deps = re.findall(r'\.Repository<([A-Za-z0-9_]+)>', handler_code)
    
    unique_deps = set()
    for dtype in deps:
        if "IUnitOfWork" in dtype:
            unique_deps.add("IUnitOfWork")
        else:
            unique_deps.add(dtype)
            
    for u in uow_deps:
        unique_deps.add(f"IRepository<{u}>")
        
    return list(unique_deps)

def generate_docs():
    out_dir.mkdir(exist_ok=True)
    
    services = [d for d in backend_dir.iterdir() if d.is_dir() and (d.name.endswith('.Microservice') or d.name == 'ApiGateway')]
    
    total_functions = 0
    
    for service in services:
        service_name = service.name
        
        # We group by Feature. A feature is essentially the folder name under Application/
        app_dir = service / "src" / "Application"
        if not app_dir.exists():
            continue
            
        features = {}
        
        # Find commands and queries
        for cq_file in app_dir.rglob("*.cs"):
            if cq_file.name.endswith("Command.cs") or cq_file.name.endswith("Query.cs"):
                code = cq_file.read_text(encoding='utf-8')
                
                # identify class/record name
                cq_match = re.search(r'(?:class|record|struct)\s+([A-Za-z0-9_]+(?:Command|Query))', code)
                if not cq_match:
                    continue
                cq_name = cq_match.group(1)
                
                # identify handler
                handler_match = re.search(r'(?:class|record)\s+([A-Za-z0-9_]+Handler)', code)
                handler_name = handler_match.group(1) if handler_match else f"{cq_name}Handler"
                
                # identify feature from path
                # e.g. User.Microservice/src/Application/Users/Commands/EditProfileCommand.cs -> Users
                parts = cq_file.relative_to(app_dir).parts
                if len(parts) >= 2:
                    feature = parts[0]
                else:
                    feature = "Common"
                
                deps = parse_csharp_dependencies(code)
                
                # if there is no handler in this file, check if there's a separate handler file
                if not handler_match:
                    handler_file = cq_file.with_name(f"{cq_name}Handler.cs")
                    if handler_file.exists():
                        handler_code = handler_file.read_text(encoding='utf-8')
                        deps = parse_csharp_dependencies(handler_code)

                if feature not in features:
                    features[feature] = []
                
                # identify potential controller name (basic assumption based on feature name)
                controller_name = f"{feature}Controller"
                    
                features[feature].append({
                    'name': cq_name,
                    'handler': handler_name,
                    'deps': deps,
                    'controller': controller_name
                })
        
        if features:
            service_out = out_dir / service_name
            service_out.mkdir(exist_ok=True)
            
            for feature, endpoints in features.items():
                md_path = service_out / f"{feature}Feature.md"
                with open(md_path, 'w', encoding='utf-8') as f:
                    f.write(f"# 3. Detailed Design: {feature} Feature ({service_name})\n\n")
                    f.write(f"This document contains the detailed design for every Command and Query in the `{feature}` module.\n\n")
                    f.write("---\n\n")
                    
                    for ep in endpoints:
                        total_functions += 1
                        name = ep['name']
                        handler = ep['handler']
                        deps = ep['deps']
                        ctrl = ep['controller']
                        
                        f.write(f"## 3.1 {name}\n\n")
                        f.write(f"### 3.1.1 Class Diagram\n\n")
                        f.write("```mermaid\n")
                        f.write("classDiagram\n")
                        f.write(f"    {ctrl} ..> MediatR : Uses\n")
                        f.write(f"    {ctrl} ..> {name} : Creates\n")
                        f.write(f"    {handler} ..> {name} : Handles\n")
                        for d in deps:
                            # clean type name for mermaid (replace < > with ~)
                            clean_d = d.replace('<', '~').replace('>', '~')
                            f.write(f"    {handler} ..> {clean_d} : Injects\n")
                        f.write("```\n\n")
                        
                        f.write(f"### 3.1.2 Sequence Diagram\n\n")
                        f.write("```mermaid\n")
                        f.write("sequenceDiagram\n")
                        f.write("    actor Client\n")
                        f.write(f"    Client ->> {ctrl}: Execute Request\n")
                        f.write(f"    {ctrl} ->> MediatR: Send({name})\n")
                        f.write(f"    MediatR ->> {handler}: Handle()\n")
                        
                        # Add a tiny bit of internal processing
                        if not deps:
                            f.write(f"    {handler} -->> {handler}: Process Business Logic\n")
                        
                        for d in deps:
                            clean_d = d.replace('<', '~').replace('>', '~')
                            f.write(f"    {handler} ->> {clean_d}: Invoke operation\n")
                            f.write(f"    {clean_d} -->> {handler}: Return data\n")
                            
                        f.write(f"    {handler} -->> MediatR: Return Result\n")
                        f.write(f"    MediatR -->> {ctrl}: Return Result\n")
                        f.write(f"    {ctrl} -->> Client: HTTP Response\n")
                        f.write("```\n\n")

    print(f"Documentation generated successfully. Processed {total_functions} functions.")

if __name__ == '__main__':
    generate_docs()
