from __future__ import annotations

import math
from dataclasses import dataclass

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
) -> DistributionPolicy:
    """Derive acceptance bounds from the current scheduler contract."""

    return DistributionPolicy(
        tolerance=tolerance,
        technical=_bounds(config.category_group_weights["technical"], tolerance),
        easter_egg=_bounds(config.category_group_weights["easter_egg"], tolerance),
        self_ambient=_bounds(
            config.output_mode_targets["self_talk"]
            + config.output_mode_targets["ambient"],
            tolerance,
        ),
        user_direct=_bounds(config.output_mode_targets["user_direct"], tolerance),
    )

