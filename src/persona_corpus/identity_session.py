"""Non-persisted direct-marker exposure limits for authored identity rows."""

from __future__ import annotations

from collections import Counter, deque

from .models import CorpusLine


class IdentitySessionExposure:
    """Tracks one selector session without adding state to durable history.

    Only the four configured direct authored batches participate in the session
    cap.  Legacy editorial identity rows retain their separate exact-provenance,
    720-hour, and daily-cap constraints until the dedicated runtime migration.
    Every displayed row still enters the short semantic-group window, so ordinary
    bubbles provide the required intervening distance for a future direct row.
    """

    def __init__(self) -> None:
        # Selector import must remain side-effect free.  Loading the contract is
        # deferred until a caller actually creates a session ledger.
        from .contract import PERSONA_CONTRACT

        policy = PERSONA_CONTRACT.authored_identity
        session = policy["session_exposure"]
        self._direct_batches = dict(policy["direct_marker_batches"])
        self._minimum_intervening = int(
            session["minimum_intervening_bubbles_same_semantic_group"]
        )
        self._recent_window = int(session["recent_bubbles_per_semantic_group"])
        self._direct_marker_max = int(session["direct_marker_max_per_identity_class"])
        self._recent_semantic_groups: deque[str] = deque()
        self._direct_marker_uses: Counter[str] = Counter()

    def is_eligible(self, row: CorpusLine) -> bool:
        """Return whether a row can be emitted in this in-memory session."""

        marker_classes = self._direct_marker_classes(row)
        if not marker_classes:
            return True
        return (
            all(
                self._direct_marker_uses[marker] < self._direct_marker_max
                for marker in marker_classes
            )
            and self.meets_minimum_intervening_bubbles(row.semantic_group)
            and row.semantic_group not in self._recent_semantic_groups
        )

    def record(self, row: CorpusLine) -> None:
        """Record one successfully displayed row; rejected candidates never call this."""

        marker_classes = self._direct_marker_classes(row)
        self._direct_marker_uses.update(marker_classes)
        self._recent_semantic_groups.append(row.semantic_group)
        retained = max(self._recent_window, self._minimum_intervening + 1)
        while len(self._recent_semantic_groups) > retained:
            self._recent_semantic_groups.popleft()

    def meets_minimum_intervening_bubbles(self, semantic_group: str) -> bool:
        """Expose the explicit (but weaker) three-intervening boundary for audit tests."""

        if not isinstance(semantic_group, str) or not semantic_group:
            raise ValueError("semantic_group must be a non-empty string")
        for index in range(len(self._recent_semantic_groups) - 1, -1, -1):
            if self._recent_semantic_groups[index] == semantic_group:
                return len(self._recent_semantic_groups) - index - 1 >= self._minimum_intervening
        return True

    def _direct_marker_classes(self, row: CorpusLine) -> tuple[str, ...]:
        if not isinstance(row, CorpusLine):
            raise TypeError("identity session rows must be CorpusLine values")
        if row.source_kind != "curated_standalone" or not row.source_reference.startswith(
            "authored:"
        ):
            return ()
        batch_id = row.source_reference[len("authored:") :].split(";", 1)[0]
        assigned_marker = self._direct_batches.get(batch_id)
        if assigned_marker is None:
            return ()
        # This import owns the zero-width normalization policy and also stays
        # lazy so importing ``selector`` alone never reads configuration files.
        from .authored_identity import marker_hits

        hits = marker_hits(row.text)
        return hits if assigned_marker in hits else ()


__all__ = ("IdentitySessionExposure",)
