#!/usr/bin/env python3
"""
Generate a draw.io ERD XML from EF Core models in User and AI microservices.

It reads, per service:
- Infrastructure/Context/MyDbContext.cs
- Domain/Entities/*.cs
- Infrastructure/Context/Configuration/*.cs
"""

from __future__ import annotations

import argparse
import re
import sys
import xml.etree.ElementTree as ET
from collections import OrderedDict
from dataclasses import dataclass, field
from pathlib import Path
from typing import Dict, Iterable, List, Optional, Tuple


@dataclass(frozen=True)
class ServiceSpec:
    key: str
    name: str
    relative_path: str


SERVICE_SPECS: Tuple[ServiceSpec, ...] = (
    ServiceSpec("user", "User", "Backend/Microservices/User.Microservice"),
    ServiceSpec("ai", "Ai", "Backend/Microservices/Ai.Microservice"),
)


@dataclass
class EntityProperty:
    name: str
    csharp_type: str
    base_type: str
    nullable: bool
    column_type_hint: Optional[str] = None
    is_key: bool = False


@dataclass
class EntityDefinition:
    class_name: str
    properties: "OrderedDict[str, EntityProperty]" = field(default_factory=OrderedDict)
    key_properties: List[str] = field(default_factory=list)


@dataclass
class PropertyConfig:
    property_name: str
    column_name: Optional[str] = None
    column_type: Optional[str] = None
    is_required: bool = False
    default_value: Optional[str] = None


@dataclass
class RelationshipConfig:
    fk_property: str
    target_entity: str
    cardinality: str  # "one-to-one" or "many-to-one"
    principal_property: Optional[str] = None


@dataclass
class EntityConfig:
    entity_name: str
    table_name: Optional[str] = None
    pk_properties: List[str] = field(default_factory=list)
    unique_indexes: List[List[str]] = field(default_factory=list)
    properties: Dict[str, PropertyConfig] = field(default_factory=dict)
    relationships: List[RelationshipConfig] = field(default_factory=list)


@dataclass
class Column:
    property_name: str
    column_name: str
    db_type: str
    nullable: bool
    is_pk: bool
    is_fk: bool
    is_unique: bool
    default_value: Optional[str] = None


@dataclass
class Table:
    service_key: str
    service_name: str
    entity_name: str
    table_name: str
    table_id: str
    columns: List[Column]
    pk_properties: List[str]
    relationship_configs: List[RelationshipConfig]
    width: int = 320
    x: int = 0
    y: int = 0
    row_ids_by_property: Dict[str, str] = field(default_factory=dict)

    @property
    def height(self) -> int:
        return 30 + (len(self.columns) * 30)

    @property
    def display_title(self) -> str:
        return self.table_name

    def first_pk_property(self) -> Optional[str]:
        if not self.pk_properties:
            return None
        for prop in self.pk_properties:
            if prop in self.row_ids_by_property:
                return prop
        return self.pk_properties[0]

    def find_column_by_property(self, property_name: str) -> Optional["Column"]:
        for column in self.columns:
            if column.property_name == property_name:
                return column
        return None

    def find_column_by_name(self, column_name: str) -> Optional["Column"]:
        for column in self.columns:
            if column.column_name == column_name:
                return column
        return None


@dataclass
class Relation:
    source_table_name: str
    source_column_name: str
    target_table_name: str
    target_column_name: str
    label: str
    cardinality: str


TABLE_STYLE_TEMPLATE = (
    "shape=table;startSize=30;container=1;collapsible=1;childLayout=tableLayout;"
    "fixedRows=1;rowLines=0;fontStyle=1;align=center;resizeLast=1;html=1;"
    "fillColor={fill};strokeColor={stroke};fontFamily=Times New Roman;"
)

ROW_STYLE_TEMPLATE = (
    "shape=tableRow;horizontal=0;startSize=0;fillColor=none;collapsible=0;dropTarget=0;"
    "points=[[0,0.5],[1,0.5]];portConstraint=eastwest;top=0;left=0;right=0;"
    "fontFamily=Times New Roman;bottom={bottom};"
)

KEY_STYLE_BASE = (
    "shape=partialRectangle;connectable=0;fillColor=none;top=0;left=0;bottom=0;right=0;"
    "overflow=hidden;whiteSpace=wrap;html=1;fontFamily=Times New Roman;"
)

VALUE_STYLE_BASE = (
    "shape=partialRectangle;connectable=0;fillColor=none;top=0;left=0;bottom=0;right=0;"
    "align=left;spacingLeft=6;overflow=hidden;whiteSpace=wrap;html=1;fontFamily=Times New Roman;"
)

EDGE_STYLE_BASE = (
    "edgeStyle=orthogonalEdgeStyle;fontSize=12;html=1;rounded=0;fontFamily=Times New Roman;"
)

SERVICE_COLORS = {
    "user": ("#dae8fc", "#6c8ebf"),
    "ai": ("#d5e8d4", "#82b366"),
    "merged": ("#fff2cc", "#d6b656"),
}


CLASS_PATTERN = re.compile(r"public\s+(?:sealed\s+)?class\s+(?P<name>\w+)")
PROPERTY_PATTERN = re.compile(
    r"public\s+(?:virtual\s+)?(?P<type>[\w<>\.,\?\[\]]+)\s+(?P<name>\w+)\s*\{\s*get;\s*set;\s*\}"
)
COLUMN_ATTR_PATTERN = re.compile(r'Column\s*\(\s*TypeName\s*=\s*"([^"]+)"\s*\)')
KEY_ATTR_PATTERN = re.compile(r"\[Key\]")

CONFIG_CLASS_PATTERN = re.compile(
    r"class\s+\w+\s*:\s*IEntityTypeConfiguration<(?P<entity>\w+)>"
)
TO_TABLE_PATTERN = re.compile(r'entity\.ToTable\("(?P<table>[^"]+)"\)')
HAS_KEY_PATTERN = re.compile(r"entity\.HasKey\((?P<expr>.*?)\)\s*(?P<chain>.*?);", re.S)
HAS_INDEX_PATTERN = re.compile(r"entity\.HasIndex\((?P<expr>.*?)\)\s*(?P<chain>.*?);", re.S)
PROPERTY_CONFIG_PATTERN = re.compile(
    r"entity\.Property\(\s*e\s*=>\s*e\.(?P<prop>\w+)\s*\)(?P<chain>.*?);",
    re.S,
)
HAS_COLUMN_NAME_PATTERN = re.compile(r'\.HasColumnName\("([^"]+)"\)')
HAS_COLUMN_TYPE_PATTERN = re.compile(r'\.HasColumnType\("([^"]+)"\)')
HAS_DEFAULT_VALUE_PATTERN = re.compile(r"\.HasDefaultValue\((.*?)\)", re.S)
HAS_ONE_REL_PATTERN = re.compile(
    r"entity\.HasOne(?:<(?P<target>\w+)>)?\([^)]*\)\s*"
    r"(?P<chain>.*?\.HasForeignKey(?:<(?P<fk_entity>\w+)>)?\(\s*d\s*=>\s*d\.(?P<fk>\w+)\s*\).*?);",
    re.S,
)
HAS_PRINCIPAL_KEY_PATTERN = re.compile(
    r"\.HasPrincipalKey(?:<[^>]+>)?\(\s*\w+\s*=>\s*\w+\.(\w+)\s*\)"
)

DBSET_PATTERN = re.compile(r"DbSet<(?P<entity>\w+)>")


def load_service_specs(selected: Iterable[str]) -> List[ServiceSpec]:
    selected_set = set(selected)
    specs = [spec for spec in SERVICE_SPECS if spec.key in selected_set]
    if not specs:
        raise ValueError("No valid services selected.")
    return specs


def sanitize_id(value: str) -> str:
    return re.sub(r"[^a-zA-Z0-9_]", "_", value)


def to_snake_case(value: str) -> str:
    first_pass = re.sub("(.)([A-Z][a-z]+)", r"\1_\2", value)
    second_pass = re.sub("([a-z0-9])([A-Z])", r"\1_\2", first_pass)
    return second_pass.lower()


def normalize_whitespace(value: str) -> str:
    return re.sub(r"\s+", " ", value).strip()


def extract_lambda_properties(expr: str, variable: str = "e") -> List[str]:
    pattern = re.compile(rf"\b{re.escape(variable)}\.(\w+)")
    return pattern.findall(expr)


def parse_dbset_entities(db_context_path: Path) -> List[str]:
    text = db_context_path.read_text(encoding="utf-8")
    return DBSET_PATTERN.findall(text)


def parse_entity_definition(entity_file: Path) -> Optional[EntityDefinition]:
    text = entity_file.read_text(encoding="utf-8")
    class_match = CLASS_PATTERN.search(text)
    if not class_match:
        return None

    definition = EntityDefinition(class_name=class_match.group("name"))
    pending_attributes: List[str] = []

    for raw_line in text.splitlines():
        line = raw_line.strip()
        if not line:
            continue

        if line.startswith("["):
            pending_attributes.append(line)
            continue

        prop_match = PROPERTY_PATTERN.match(line)
        if not prop_match:
            pending_attributes = []
            continue

        csharp_type = prop_match.group("type")
        prop_name = prop_match.group("name")
        nullable = csharp_type.endswith("?")
        base_type = csharp_type[:-1] if nullable else csharp_type

        column_type_hint = None
        is_key = False
        for attribute_line in pending_attributes:
            column_attr = COLUMN_ATTR_PATTERN.search(attribute_line)
            if column_attr:
                column_type_hint = column_attr.group(1)
            if KEY_ATTR_PATTERN.search(attribute_line):
                is_key = True

        prop = EntityProperty(
            name=prop_name,
            csharp_type=csharp_type,
            base_type=base_type,
            nullable=nullable,
            column_type_hint=column_type_hint,
            is_key=is_key,
        )
        definition.properties[prop_name] = prop
        if is_key:
            definition.key_properties.append(prop_name)

        pending_attributes = []

    return definition


def parse_entity_definitions(entities_dir: Path) -> Dict[str, EntityDefinition]:
    entity_map: Dict[str, EntityDefinition] = {}
    for entity_file in sorted(entities_dir.glob("*.cs")):
        definition = parse_entity_definition(entity_file)
        if definition is None:
            continue
        entity_map[definition.class_name] = definition
    return entity_map


def parse_entity_config(config_file: Path) -> Optional[EntityConfig]:
    text = config_file.read_text(encoding="utf-8")
    class_match = CONFIG_CLASS_PATTERN.search(text)
    if not class_match:
        return None

    entity_name = class_match.group("entity")
    config = EntityConfig(entity_name=entity_name)

    to_table_match = TO_TABLE_PATTERN.search(text)
    if to_table_match:
        config.table_name = to_table_match.group("table")

    for key_match in HAS_KEY_PATTERN.finditer(text):
        props = extract_lambda_properties(key_match.group("expr"), variable="e")
        for prop in props:
            if prop not in config.pk_properties:
                config.pk_properties.append(prop)

    for index_match in HAS_INDEX_PATTERN.finditer(text):
        chain = index_match.group("chain")
        if ".IsUnique()" not in chain:
            continue
        props = extract_lambda_properties(index_match.group("expr"), variable="e")
        if props:
            config.unique_indexes.append(props)

    for prop_match in PROPERTY_CONFIG_PATTERN.finditer(text):
        prop_name = prop_match.group("prop")
        chain = prop_match.group("chain")
        column_name_match = HAS_COLUMN_NAME_PATTERN.search(chain)
        column_type_match = HAS_COLUMN_TYPE_PATTERN.search(chain)
        default_match = HAS_DEFAULT_VALUE_PATTERN.search(chain)

        prop_cfg = PropertyConfig(property_name=prop_name)
        if column_name_match:
            prop_cfg.column_name = column_name_match.group(1)
        if column_type_match:
            prop_cfg.column_type = column_type_match.group(1)
        prop_cfg.is_required = ".IsRequired()" in chain
        if default_match:
            prop_cfg.default_value = normalize_whitespace(default_match.group(1))
        config.properties[prop_name] = prop_cfg

    for rel_match in HAS_ONE_REL_PATTERN.finditer(text):
        target_entity = rel_match.group("target")
        fk_property = rel_match.group("fk")
        chain = rel_match.group("chain")
        if not target_entity or not fk_property:
            continue
        cardinality = "one-to-one" if ".WithOne(" in chain else "many-to-one"
        principal_key_match = HAS_PRINCIPAL_KEY_PATTERN.search(chain)
        principal_property = principal_key_match.group(1) if principal_key_match else None
        config.relationships.append(
            RelationshipConfig(
                fk_property=fk_property,
                target_entity=target_entity,
                cardinality=cardinality,
                principal_property=principal_property,
            )
        )

    return config


def parse_entity_configs(config_dir: Path) -> Dict[str, EntityConfig]:
    configs: Dict[str, EntityConfig] = {}
    for config_file in sorted(config_dir.glob("*.cs")):
        cfg = parse_entity_config(config_file)
        if cfg is None:
            continue
        configs[cfg.entity_name] = cfg
    return configs


def is_known_value_type(base_type: str) -> bool:
    normalized = base_type.split(".")[-1]
    return normalized in {
        "bool",
        "byte",
        "sbyte",
        "short",
        "ushort",
        "int",
        "uint",
        "long",
        "ulong",
        "float",
        "double",
        "decimal",
        "Guid",
        "DateTime",
        "DateOnly",
        "TimeOnly",
        "DateTimeOffset",
    }


def map_csharp_type_to_db(csharp_type: Optional[str]) -> str:
    if not csharp_type:
        return "text"

    raw = csharp_type[:-1] if csharp_type.endswith("?") else csharp_type
    raw = raw.split(".")[-1]

    if raw.startswith("List<") or raw.startswith("Dictionary<"):
        return "jsonb"

    mapping = {
        "Guid": "uuid",
        "string": "varchar",
        "bool": "boolean",
        "byte": "smallint",
        "sbyte": "smallint",
        "short": "smallint",
        "ushort": "integer",
        "int": "integer",
        "uint": "bigint",
        "long": "bigint",
        "ulong": "numeric",
        "float": "real",
        "double": "double precision",
        "decimal": "numeric",
        "DateTime": "timestamp with time zone",
        "DateOnly": "date",
        "TimeOnly": "time",
        "DateTimeOffset": "timestamp with time zone",
        "JsonDocument": "jsonb",
    }
    return mapping.get(raw, "text")


def infer_nullability(
    entity_prop: Optional[EntityProperty],
    prop_cfg: Optional[PropertyConfig],
    is_pk: bool,
) -> bool:
    if is_pk:
        return False
    if prop_cfg and prop_cfg.is_required:
        return False
    if entity_prop is None:
        return True
    if entity_prop.nullable:
        return True
    if is_known_value_type(entity_prop.base_type):
        return False
    # Non-nullable reference type under NRT means required by intent.
    return False


def build_columns(
    entity_name: str,
    entity_definition: Optional[EntityDefinition],
    config: Optional[EntityConfig],
) -> Tuple[List[Column], List[str], List[RelationshipConfig]]:
    config = config or EntityConfig(entity_name=entity_name)
    entity_definition = entity_definition or EntityDefinition(class_name=entity_name)

    property_order: List[str] = list(entity_definition.properties.keys())
    for prop_name in config.properties.keys():
        if prop_name not in property_order:
            property_order.append(prop_name)

    pk_props: List[str] = (
        list(config.pk_properties)
        if config.pk_properties
        else list(entity_definition.key_properties)
    )
    if not pk_props and "Id" in property_order:
        pk_props = ["Id"]

    unique_single_props = {
        index_props[0] for index_props in config.unique_indexes if len(index_props) == 1
    }
    fk_props = {relationship.fk_property for relationship in config.relationships}

    columns: List[Column] = []
    for prop_name in property_order:
        entity_prop = entity_definition.properties.get(prop_name)
        prop_cfg = config.properties.get(prop_name)

        column_name = (
            prop_cfg.column_name
            if prop_cfg and prop_cfg.column_name
            else to_snake_case(prop_name)
        )
        db_type = (
            (prop_cfg.column_type if prop_cfg else None)
            or (entity_prop.column_type_hint if entity_prop else None)
            or map_csharp_type_to_db(entity_prop.csharp_type if entity_prop else None)
        )
        is_pk = prop_name in pk_props
        is_fk = prop_name in fk_props
        nullable = infer_nullability(entity_prop, prop_cfg, is_pk)
        is_unique = prop_name in unique_single_props
        default_value = prop_cfg.default_value if prop_cfg else None

        columns.append(
            Column(
                property_name=prop_name,
                column_name=column_name,
                db_type=db_type,
                nullable=nullable,
                is_pk=is_pk,
                is_fk=is_fk,
                is_unique=is_unique,
                default_value=default_value,
            )
        )

    return columns, pk_props, list(config.relationships)


def parse_service_tables(base_dir: Path, service_spec: ServiceSpec) -> List[Table]:
    service_root = base_dir / service_spec.relative_path
    db_context_path = service_root / "src/Infrastructure/Context/MyDbContext.cs"
    entities_dir = service_root / "src/Domain/Entities"
    config_dir = service_root / "src/Infrastructure/Context/Configuration"

    required_paths = [db_context_path, entities_dir, config_dir]
    for path in required_paths:
        if not path.exists():
            raise FileNotFoundError(f"Required path not found: {path}")

    dbset_entities = parse_dbset_entities(db_context_path)
    entity_definitions = parse_entity_definitions(entities_dir)
    entity_configs = parse_entity_configs(config_dir)

    tables: List[Table] = []
    for entity_name in dbset_entities:
        entity_definition = entity_definitions.get(entity_name)
        entity_config = entity_configs.get(entity_name)
        table_name = (
            entity_config.table_name
            if entity_config and entity_config.table_name
            else to_snake_case(entity_name)
        )
        table_id = f"tbl_{service_spec.key}_{sanitize_id(table_name)}"
        columns, pk_props, relationships = build_columns(
            entity_name, entity_definition, entity_config
        )

        table = Table(
            service_key=service_spec.key,
            service_name=service_spec.name,
            entity_name=entity_name,
            table_name=table_name,
            table_id=table_id,
            columns=columns,
            pk_properties=pk_props,
            relationship_configs=relationships,
        )
        tables.append(table)

    return tables


def layout_tables(tables: List[Table]) -> None:
    ordered_tables = sorted(tables, key=lambda t: t.table_name)
    column_width = 320
    column_gap = 80
    row_gap = 80
    top_padding = 40
    left_padding = 40
    col_heights = [top_padding, top_padding, top_padding, top_padding]

    for table in ordered_tables:
        col = min(range(len(col_heights)), key=lambda idx: col_heights[idx])
        table.x = left_padding + col * (column_width + column_gap)
        table.y = col_heights[col]
        col_heights[col] += table.height + row_gap


def build_service_relations(service_tables: List[Table]) -> List[Relation]:
    table_by_service_entity: Dict[Tuple[str, str], Table] = {
        (table.service_key, table.entity_name): table for table in service_tables
    }
    relations: List[Relation] = []

    # Relationship configs are declared on the dependent table side.
    for child_table in service_tables:
        for rel_cfg in child_table.relationship_configs:
            parent_table = table_by_service_entity.get(
                (child_table.service_key, rel_cfg.target_entity)
            )
            if parent_table is None:
                continue

            child_fk_prop = rel_cfg.fk_property
            parent_key_prop = rel_cfg.principal_property or parent_table.first_pk_property()
            if parent_key_prop is None:
                continue

            child_fk_column = child_table.find_column_by_property(child_fk_prop)
            if child_fk_column is None:
                continue
            parent_key_column = parent_table.find_column_by_property(parent_key_prop)
            if parent_key_column is None:
                continue

            relations.append(
                Relation(
                    # Principal side -> dependent side.
                    source_table_name=parent_table.table_name,
                    source_column_name=parent_key_column.column_name,
                    target_table_name=child_table.table_name,
                    target_column_name=child_fk_column.column_name,
                    label="",
                    cardinality=rel_cfg.cardinality,
                )
            )

    return relations


def merge_tables_by_name(service_tables: List[Table]) -> List[Table]:
    merged_by_name: "OrderedDict[str, Table]" = OrderedDict()

    def merge_column(existing: Column, incoming: Column) -> None:
        if existing.db_type != incoming.db_type and not existing.db_type:
            existing.db_type = incoming.db_type
        existing.nullable = existing.nullable and incoming.nullable
        existing.is_pk = existing.is_pk or incoming.is_pk
        existing.is_fk = existing.is_fk or incoming.is_fk
        existing.is_unique = existing.is_unique or incoming.is_unique
        if existing.default_value is None:
            existing.default_value = incoming.default_value

    # Keep deterministic merge order.
    ordered_source_tables = sorted(
        service_tables, key=lambda t: (t.table_name, t.service_key, t.entity_name)
    )

    for source_table in ordered_source_tables:
        merged = merged_by_name.get(source_table.table_name)
        if merged is None:
            merged = Table(
                service_key="merged",
                service_name="Merged",
                entity_name=source_table.table_name,
                table_name=source_table.table_name,
                table_id=f"tbl_{sanitize_id(source_table.table_name)}",
                columns=[],
                pk_properties=[],
                relationship_configs=[],
            )
            merged_by_name[source_table.table_name] = merged

        merged_cols_by_name = {col.column_name: col for col in merged.columns}
        for source_col in source_table.columns:
            existing = merged_cols_by_name.get(source_col.column_name)
            if existing is None:
                merged.columns.append(
                    Column(
                        property_name=source_col.column_name,
                        column_name=source_col.column_name,
                        db_type=source_col.db_type,
                        nullable=source_col.nullable,
                        is_pk=source_col.is_pk,
                        is_fk=source_col.is_fk,
                        is_unique=source_col.is_unique,
                        default_value=source_col.default_value,
                    )
                )
            else:
                merge_column(existing, source_col)

        merged.pk_properties = [
            column.column_name for column in merged.columns if column.is_pk
        ]

    return list(merged_by_name.values())


def build_relations(tables: List[Table], relation_specs: List[Relation]) -> List[Relation]:
    table_by_name = {table.table_name: table for table in tables}
    seen: set[Tuple[str, str, str, str, str]] = set()
    resolved: List[Relation] = []

    for relation in relation_specs:
        source_table = table_by_name.get(relation.source_table_name)
        target_table = table_by_name.get(relation.target_table_name)
        if source_table is None or target_table is None:
            continue

        source_col = source_table.find_column_by_name(relation.source_column_name)
        target_col = target_table.find_column_by_name(relation.target_column_name)
        if source_col is None or target_col is None:
            continue

        if relation.source_column_name not in source_table.row_ids_by_property:
            continue
        if relation.target_column_name not in target_table.row_ids_by_property:
            continue

        key = (
            relation.source_table_name,
            relation.source_column_name,
            relation.target_table_name,
            relation.target_column_name,
            relation.cardinality,
        )
        if key in seen:
            continue
        seen.add(key)
        resolved.append(relation)

    return resolved


def table_style(service_key: str) -> str:
    fill, stroke = SERVICE_COLORS.get(service_key, ("#fff2cc", "#d6b656"))
    return TABLE_STYLE_TEMPLATE.format(fill=fill, stroke=stroke)


def build_key_marker(column: Column) -> str:
    if column.is_pk and column.is_fk:
        return "PK,FK"
    if column.is_pk:
        return "PK"
    if column.is_fk:
        return "FK"
    return ""


def build_column_label(column: Column) -> str:
    parts = [f"{column.column_name}: {column.db_type}"]
    if column.is_unique:
        parts.append("unique")
    if not column.nullable:
        parts.append("not null")
    if column.default_value:
        parts.append(f"default {column.default_value}")
    return " ".join(parts)


def edge_style(cardinality: str) -> str:
    # Draw.io crow-foot markers.
    if cardinality == "one-to-one":
        return EDGE_STYLE_BASE + "endArrow=ERoneToOne;"
    return EDGE_STYLE_BASE + "endArrow=ERoneToMany;"


def generate_drawio_tree(tables: List[Table], relation_specs: List[Relation]) -> ET.ElementTree:
    if not tables:
        raise ValueError("No tables were parsed.")

    max_x = max(table.x + table.width for table in tables) + 200
    max_y = max(table.y + table.height for table in tables) + 200

    model = ET.Element(
        "mxGraphModel",
        {
            "dx": str(max_x),
            "dy": str(max_y),
            "grid": "1",
            "gridSize": "10",
            "guides": "1",
            "tooltips": "1",
            "connect": "1",
            "arrows": "1",
            "fold": "1",
            "page": "1",
            "pageScale": "1",
            "pageWidth": "1700",
            "pageHeight": "2200",
            "math": "0",
            "shadow": "0",
        },
    )
    root = ET.SubElement(model, "root")
    ET.SubElement(root, "mxCell", {"id": "0"})
    ET.SubElement(root, "mxCell", {"id": "1", "parent": "0"})

    for table in tables:
        table_cell = ET.SubElement(
            root,
            "mxCell",
            {
                "id": table.table_id,
                "parent": "1",
                "style": table_style(table.service_key),
                "value": f"<i>{table.display_title}</i>",
                "vertex": "1",
            },
        )
        ET.SubElement(
            table_cell,
            "mxGeometry",
            {
                "height": str(table.height),
                "width": str(table.width),
                "x": str(table.x),
                "y": str(table.y),
                "as": "geometry",
            },
        )

        for idx, column in enumerate(table.columns, start=1):
            row_id = f"{table.table_id}_r{idx}"
            table.row_ids_by_property[column.property_name] = row_id
            is_last = idx == len(table.columns)

            row_cell = ET.SubElement(
                root,
                "mxCell",
                {
                    "id": row_id,
                    "parent": table.table_id,
                    "style": ROW_STYLE_TEMPLATE.format(bottom=1 if is_last else 0),
                    "value": "",
                    "vertex": "1",
                },
            )
            ET.SubElement(
                row_cell,
                "mxGeometry",
                {
                    "height": "30",
                    "width": str(table.width),
                    "y": str(30 * idx),
                    "as": "geometry",
                },
            )

            key_marker = build_key_marker(column)
            key_style = KEY_STYLE_BASE + ("fontStyle=1;" if key_marker else "")
            key_cell = ET.SubElement(
                root,
                "mxCell",
                {
                    "id": f"{row_id}_k",
                    "parent": row_id,
                    "style": key_style,
                    "value": key_marker,
                    "vertex": "1",
                },
            )
            key_geo = ET.SubElement(
                key_cell,
                "mxGeometry",
                {"height": "30", "width": "30", "as": "geometry"},
            )
            ET.SubElement(
                key_geo,
                "mxRectangle",
                {"height": "30", "width": "30", "as": "alternateBounds"},
            )

            value_style = VALUE_STYLE_BASE + ("fontStyle=5;" if column.is_pk else "")
            value_cell = ET.SubElement(
                root,
                "mxCell",
                {
                    "id": f"{row_id}_v",
                    "parent": row_id,
                    "style": value_style,
                    "value": build_column_label(column),
                    "vertex": "1",
                },
            )
            value_geo = ET.SubElement(
                value_cell,
                "mxGeometry",
                {"height": "30", "width": str(table.width - 30), "x": "30", "as": "geometry"},
            )
            ET.SubElement(
                value_geo,
                "mxRectangle",
                {
                    "height": "30",
                    "width": str(table.width - 30),
                    "as": "alternateBounds",
                },
            )

    table_by_name = {table.table_name: table for table in tables}
    relations = build_relations(tables, relation_specs)
    for idx, relation in enumerate(relations, start=1):
        source_table = table_by_name[relation.source_table_name]
        target_table = table_by_name[relation.target_table_name]
        source_row_id = source_table.row_ids_by_property[relation.source_column_name]
        target_row_id = target_table.row_ids_by_property[relation.target_column_name]

        edge_cell = ET.SubElement(
            root,
            "mxCell",
            {
                "id": f"rel_{idx}",
                "value": relation.label,
                "style": edge_style(relation.cardinality),
                "edge": "1",
                "parent": "1",
                "source": source_row_id,
                "target": target_row_id,
            },
        )
        ET.SubElement(edge_cell, "mxGeometry", {"relative": "1", "as": "geometry"})

    tree = ET.ElementTree(model)
    try:
        ET.indent(tree, space="  ")
    except AttributeError:
        # Python < 3.9 fallback (safe to skip pretty formatting).
        pass
    return tree


def parse_args(argv: List[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Generate draw.io ERD XML from User + Ai microservice EF Core models."
    )
    parser.add_argument(
        "--base-dir",
        default=".",
        help="Repository root directory (default: current directory).",
    )
    parser.add_argument(
        "--output",
        default="erd.drawio.xml",
        help="Output XML file path. Relative paths are resolved from --base-dir.",
    )
    parser.add_argument(
        "--services",
        nargs="+",
        default=[spec.key for spec in SERVICE_SPECS],
        choices=[spec.key for spec in SERVICE_SPECS],
        help="Services to include (default: user ai).",
    )
    return parser.parse_args(argv)


def main(argv: List[str]) -> int:
    args = parse_args(argv)
    base_dir = Path(args.base_dir).resolve()
    output_path = Path(args.output)
    if not output_path.is_absolute():
        output_path = base_dir / output_path

    service_specs = load_service_specs(args.services)
    service_tables: List[Table] = []
    for service_spec in service_specs:
        parsed = parse_service_tables(base_dir, service_spec)
        service_tables.extend(parsed)

    if not service_tables:
        raise RuntimeError("No tables found in selected services.")

    relation_specs = build_service_relations(service_tables)
    tables = merge_tables_by_name(service_tables)
    layout_tables(tables)
    tree = generate_drawio_tree(tables, relation_specs)

    output_path.parent.mkdir(parents=True, exist_ok=True)
    tree.write(output_path, encoding="utf-8", xml_declaration=False)

    relation_count = len(build_relations(tables, relation_specs))
    print(
        f"Generated draw.io XML: {output_path} "
        f"(tables={len(tables)}, relations={relation_count}, services={','.join(args.services)})"
    )
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main(sys.argv[1:]))
    except Exception as exc:
        print(f"Error: {exc}", file=sys.stderr)
        raise SystemExit(1)
