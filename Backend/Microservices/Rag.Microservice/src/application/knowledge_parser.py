"""Knowledge-base markdown parser — pure, no I/O.

The actual file reading happens in the infrastructure layer (`KnowledgeLoaderPort`).
This module just turns a `.md` file's TEXT into a list of sections, each
representing one ingestable doc.

Naming convention preserved from the original `service/knowledge_bootstrap.py`:
    <filename>.md                 → namespace `<filename-slug>`
    `## <Heading Text>`           → docId `knowledge:<namespace>:<heading-slug>`
"""
from __future__ import annotations

import hashlib
import re
from dataclasses import dataclass


_SLUG_RE = re.compile(r"[^a-z0-9]+")


def slugify(text: str) -> str:
    """`"4 Reasons Why"` → `"4-reasons-why"`. Strips leading numbering like `1. `."""
    s = text.strip().lower()
    s = re.sub(r"^\d+\.\s*", "", s)
    s = _SLUG_RE.sub("-", s).strip("-")
    return s or "untitled"


@dataclass(slots=True)
class KnowledgeDoc:
    document_id: str
    content: str
    fingerprint: str


def parse_knowledge_md(text: str, namespace: str) -> list[KnowledgeDoc]:
    """Splits markdown TEXT (already read from disk) by top-level `## ` headings.

    The file's `# ` (h1) and any preamble before the first `## ` are dropped —
    they're meant as file-level intro, not retrievable content.
    """
    docs: list[KnowledgeDoc] = []
    current_heading: str | None = None
    current_lines: list[str] = []

    def flush() -> None:
        if current_heading is None or not current_lines:
            return
        body = "\n".join(current_lines).strip()
        if not body:
            return
        slug = slugify(current_heading)
        doc_id = f"knowledge:{namespace}:{slug}"
        full = f"## {current_heading}\n\n{body}".strip()
        fp = hashlib.sha256(full.encode("utf-8")).hexdigest()
        docs.append(KnowledgeDoc(document_id=doc_id, content=full, fingerprint=fp))

    for line in text.splitlines():
        if line.startswith("## "):
            flush()
            current_heading = line[3:].strip()
            current_lines = []
        elif current_heading is not None:
            current_lines.append(line)
    flush()
    return docs
