"""Offline tools for auditing and migrating the companion persona corpus.

The public names are loaded on first access so importing an unrelated runtime
submodule does not parse configuration files as a package side effect.
"""

from __future__ import annotations

from importlib import import_module
from typing import TYPE_CHECKING


_PUBLIC_EXPORTS = {
    "AuditPair": (".models", "AuditPair"),
    "AuditResult": (".models", "AuditResult"),
    "CorpusFormatError": (".loader", "CorpusFormatError"),
    "CorpusLine": (".models", "CorpusLine"),
    "LegacyLine": (".models", "LegacyLine"),
    "audit_legacy": (".normalization", "audit_legacy"),
    "character_ngrams": (".normalization", "character_ngrams"),
    "load_legacy": (".loader", "load_legacy"),
    "load_v2": (".loader", "load_v2"),
    "normalize_text": (".normalization", "normalize_text"),
    "sha256_file": (".loader", "sha256_file"),
}

__all__ = tuple(_PUBLIC_EXPORTS)


def __getattr__(name: str) -> object:
    target = _PUBLIC_EXPORTS.get(name)
    if target is None:
        raise AttributeError(f"module {__name__!r} has no attribute {name!r}")
    module_name, attribute_name = target
    value = getattr(import_module(module_name, __name__), attribute_name)
    globals()[name] = value
    return value


def __dir__() -> list[str]:
    return sorted(set(globals()) | set(__all__))


if TYPE_CHECKING:
    from .loader import CorpusFormatError, load_legacy, load_v2, sha256_file
    from .models import AuditPair, AuditResult, CorpusLine, LegacyLine
    from .normalization import audit_legacy, character_ngrams, normalize_text
