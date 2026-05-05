"""Filesystem-backed knowledge loader — implements `KnowledgeLoaderPort`.

Reads `*.md` files from a directory, parses each via the pure
`knowledge_parser` module, and yields ingestable docs.
"""
from __future__ import annotations

import logging
import os
from typing import Iterable

from ..application.knowledge_parser import KnowledgeDoc, parse_knowledge_md, slugify

logger = logging.getLogger("rag-service.knowledge-loader")


class FilesystemKnowledgeLoader:
    """Implements `KnowledgeLoaderPort`. Scans `dir_path` for `*.md` and
    yields `(namespace, file_path)` pairs in deterministic alphabetical order.

    `parse_namespace(...)` reads the file and delegates to the pure parser.
    """

    def __init__(self, dir_path: str) -> None:
        self._dir = dir_path

    def list_namespaces(self) -> Iterable[tuple[str, str]]:
        if not os.path.isdir(self._dir):
            logger.info("Knowledge dir %s does not exist; skipping bootstrap", self._dir)
            return
        for filename in sorted(os.listdir(self._dir)):
            if not filename.endswith(".md"):
                continue
            namespace = slugify(filename[:-3])
            yield namespace, os.path.join(self._dir, filename)

    def parse_namespace(self, namespace: str, file_path: str) -> list[KnowledgeDoc]:
        with open(file_path, "r", encoding="utf-8") as f:
            text = f.read()
        return parse_knowledge_md(text, namespace)
