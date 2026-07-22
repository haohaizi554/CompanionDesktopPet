"""Offline tools for auditing and migrating the companion persona corpus."""

from .loader import CorpusFormatError, load_legacy, load_v2, sha256_file
from .models import AuditPair, AuditResult, CorpusLine, LegacyLine
from .normalization import audit_legacy, character_ngrams, normalize_text

__all__ = [
    "AuditPair",
    "AuditResult",
    "CorpusFormatError",
    "CorpusLine",
    "LegacyLine",
    "audit_legacy",
    "character_ngrams",
    "load_legacy",
    "load_v2",
    "normalize_text",
    "sha256_file",
]
