from __future__ import annotations

import math
from dataclasses import dataclass
from typing import Mapping

from ..contract import PERSONA_CONTRACT
from ..selector import SchedulerConfig


@dataclass(frozen=True, slots=True)
class DistributionTolerance:
    """One injectable absolute tolerance for every scheduler-derived ratio bound."""

    absolute: float = 0.05

    def __post_init__(self) -> None:
        if (
            isinstance(self.absolute, bool)
            or not isinstance(self.absolute, (int, float))
            or not math.isfinite(float(self.absolute))
            or not 0 <= float(self.absolute) < 1
        ):
            raise ValueError("absolute distribution tolerance must be in [0, 1)")


@dataclass(frozen=True, slots=True)
class RatioBounds:
    target: float
    minimum: float
    maximum: float


@dataclass(frozen=True, slots=True)
class DistributionPolicy:
    tolerance: DistributionTolerance
    technical: RatioBounds
    easter_egg: RatioBounds
    self_ambient: RatioBounds
    user_direct: RatioBounds


@dataclass(frozen=True, slots=True)
class DrySharpPolicy:
    inventory_target: float
    inventory_acceptance: tuple[float, float]
    inventory_enforcement_minimum_rows: int
    bootstrap_minimum_rows: int
    playback_target: float
    playback_acceptance: tuple[float, float]
    recent_window: int
    recent_max: int
    forbidden_category_groups: frozenset[str]
    forbidden_triggers: frozenset[str]
    forbidden_context_tokens: frozenset[str]


def _bounds(target: float, tolerance: DistributionTolerance) -> RatioBounds:
    absolute = float(tolerance.absolute)
    return RatioBounds(
        target=float(target),
        minimum=max(0.0, float(target) - absolute),
        maximum=min(1.0, float(target) + absolute),
    )


def derive_distribution_policy(
    config: SchedulerConfig,
    tolerance: DistributionTolerance = DistributionTolerance(),
    *,
    acceptance: Mapping[str, object] | None = None,
) -> DistributionPolicy:
    """Derive acceptance bounds from the current scheduler contract."""

    source = PERSONA_CONTRACT.scheduler["acceptance"] if acceptance is None else acceptance
    technical_acceptance = tuple(source["technical_playback_ratio"])
    easter_acceptance = tuple(source["easter_egg_playback_ratio"])
    if len(technical_acceptance) != 2 or len(easter_acceptance) != 2:
        raise ValueError("distribution acceptance ranges must contain two values")
    self_ambient = _bounds(
        config.output_mode_targets["self_talk"]
        + config.output_mode_targets["ambient"],
        tolerance,
    )
    user_direct = _bounds(config.output_mode_targets["user_direct"], tolerance)
    return DistributionPolicy(
        tolerance=tolerance,
        technical=RatioBounds(
            target=config.category_group_weights["technical"],
            minimum=float(technical_acceptance[0]),
            maximum=float(technical_acceptance[1]),
        ),
        easter_egg=RatioBounds(
            target=config.category_group_weights["easter_egg"],
            minimum=float(easter_acceptance[0]),
            maximum=float(easter_acceptance[1]),
        ),
        self_ambient=RatioBounds(
            target=self_ambient.target,
            minimum=float(source["self_talk_ambient_minimum"]),
            maximum=self_ambient.maximum,
        ),
        user_direct=RatioBounds(
            target=user_direct.target,
            minimum=user_direct.minimum,
            maximum=float(source["user_direct_maximum"]),
        ),
    )


def derive_dry_sharp_policy(
    source: Mapping[str, object] | None = None,
) -> DrySharpPolicy:
    """Load the tone contract used by validation, selection and simulation."""

    value = PERSONA_CONTRACT.dry_sharp if source is None else source
    inventory_acceptance = tuple(value["inventory_acceptance"])
    playback_acceptance = tuple(value["playback_acceptance"])
    if len(inventory_acceptance) != 2 or len(playback_acceptance) != 2:
        raise ValueError("dry_sharp acceptance ranges must contain two values")
    return DrySharpPolicy(
        inventory_target=float(value["inventory_target"]),
        inventory_acceptance=(
            float(inventory_acceptance[0]),
            float(inventory_acceptance[1]),
        ),
        inventory_enforcement_minimum_rows=int(
            value["inventory_enforcement_minimum_rows"]
        ),
        bootstrap_minimum_rows=int(value["bootstrap_minimum_rows"]),
        playback_target=float(value["playback_target"]),
        playback_acceptance=(
            float(playback_acceptance[0]),
            float(playback_acceptance[1]),
        ),
        recent_window=int(value["recent_window"]),
        recent_max=int(value["recent_max"]),
        forbidden_category_groups=frozenset(value["forbidden_category_groups"]),
        forbidden_triggers=frozenset(value["forbidden_triggers"]),
        forbidden_context_tokens=frozenset(value["forbidden_context_tokens"]),
    )
