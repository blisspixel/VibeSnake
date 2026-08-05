"""Fast reference simulation built from the production core entities."""

from __future__ import annotations

from vibesnake.core.enums import Direction
from vibesnake.core.exceptions import GridFullException
from vibesnake.core.food import Food
from vibesnake.core.near_miss import NearMissDetector, NearMissEvent
from vibesnake.core.scoring import ScoreManager
from vibesnake.core.snake import Snake
from vibesnake.qa.achievement_candidates import (
    RunAchievementMetrics,
    candidate_event_values,
)
from vibesnake.qa.models import StepEvent, StepRecord


class CoreSimulation:
    """Exercise real movement, food, and scoring code without rendering or audio.

    This is intentionally a reference adapter, not the final game runtime. It
    preserves the current coordinator's ordering so the future deterministic
    rules engine can be checked against known traces during migration.
    """

    def __init__(
        self,
        step_seconds: float = 0.05,
        *,
        enable_near_miss: bool = True,
        enable_achievement_candidates: bool = False,
        already_unlocked_achievements: frozenset[str] | None = None,
    ):
        if step_seconds <= 0:
            raise ValueError("step_seconds must be greater than zero")

        self.step_seconds = step_seconds
        self.enable_near_miss = enable_near_miss
        # Default false keeps dual-runtime fixtures stable until shared traces
        # regenerate with achievement_candidate events.
        self.enable_achievement_candidates = enable_achievement_candidates
        # Optional profile unlock set mirrors SnakeRun.ApplyProfileUnlocks.
        self.already_unlocked_achievements = (
            frozenset() if already_unlocked_achievements is None else frozenset(already_unlocked_achievements)
        )
        self.snake = Snake()
        self.food = Food(self.snake.positions_set)
        self.score = ScoreManager()
        self.near_miss = NearMissDetector()
        self.alive = True
        self.won = False
        self.death_cause: str | None = None
        self.step_count = 0
        self.food_eaten = 0
        self.wraps = 0
        self.session_near_misses = 0
        self.session_max_combo = 0
        self.starvation_seconds = 0.0
        self.starvation_limit_seconds = 30.0

    def step(self, commands: tuple[Direction, ...]) -> StepRecord:
        """Apply queued commands and advance the reference core by one tick."""
        if not self.alive:
            raise RuntimeError("cannot advance a finished simulation")

        self.step_count += 1
        for command in commands:
            self.snake.queue_direction(command)

        next_starvation_seconds = self.starvation_seconds + self.step_seconds
        starvation_expired = next_starvation_seconds >= self.starvation_limit_seconds
        combo_before = self.score.combo_count
        self.score.update(self.step_seconds)
        combo_expired = combo_before > 0 and self.score.combo_count == 0
        self.session_max_combo = max(self.session_max_combo, self.score.combo_count)
        if self.enable_near_miss:
            self.near_miss.update(self.step_seconds)
        previous_direction = self.snake.direction
        next_head = self.snake.peek_next_head()
        ate_food = self.food.position is not None and next_head == self.food.position
        speed_bonus = ate_food and self.score.time_since_last_food < 1.5
        # Capture pre-meal remaining hunger for clutch (native does not starve on eat).
        hunger_remaining_before_eat = max(
            0.0,
            self.starvation_limit_seconds - next_starvation_seconds,
        )
        self.starvation_seconds = next_starvation_seconds
        alive, wrapped = self.snake.move(grow=ate_food)
        events: list[StepEvent] = []

        if self.snake.direction != previous_direction:
            events.append(
                StepEvent(
                    kind="direction_changed",
                    direction=self.snake.direction.name,
                )
            )

        # Match native order: direction change, then combo_expired, then movement.
        if combo_expired:
            events.append(StepEvent(kind="combo_expired", value=0))

        if wrapped:
            self.wraps += 1

        if not alive:
            self.alive = False
            self.death_cause = "collision"
            if wrapped:
                events.append(StepEvent(kind="wrapped", position=next_head))
            events.append(
                StepEvent(
                    kind="died",
                    position=next_head,
                    death_cause="self_collision",
                )
            )
            return self._record(
                commands,
                wrapped=wrapped,
                ate_food=False,
                events=tuple(events),
            )

        events.append(StepEvent(kind="moved", position=self.snake.get_head()))
        if wrapped:
            events.append(StepEvent(kind="wrapped", position=self.snake.get_head()))

        if ate_food:
            self.starvation_seconds = 0.0
            self.food_eaten += 1
            points = self.score.add_food_score(
                speed_bonus=speed_bonus,
                snake_length=len(self.snake.body),
            )
            self.session_max_combo = max(self.session_max_combo, self.score.combo_count)
            events.extend(
                (
                    StepEvent(kind="ate_food", position=self.snake.get_head()),
                    StepEvent(kind="score_changed", value=points),
                    StepEvent(
                        kind="hunger_reset",
                        value=round(self.starvation_limit_seconds / self.step_seconds),
                    ),
                )
            )
            self._apply_food_near_misses(hunger_remaining_before_eat, events)
            try:
                self.food.respawn(self.snake.positions_set)
            except GridFullException:
                self.food.position = None
                self.alive = False
                self.won = True
                events.append(StepEvent(kind="won", position=self.snake.get_head()))
        elif starvation_expired:
            self.alive = False
            self.death_cause = "starvation"
            events.append(
                StepEvent(
                    kind="died",
                    position=self.snake.get_head(),
                    death_cause="starvation",
                )
            )
        else:
            # Native applies body proximity only on non-food steps while running.
            self._apply_body_near_miss(events)

        return self._record(
            commands,
            wrapped=wrapped,
            ate_food=ate_food,
            events=tuple(events),
        )

    def _apply_food_near_misses(
        self,
        hunger_remaining_seconds: float,
        events: list[StepEvent],
    ) -> None:
        if not self.enable_near_miss:
            return

        clutch = self.near_miss.check_clutch_eat(
            self.starvation_limit_seconds - hunger_remaining_seconds,
            self.starvation_limit_seconds,
        )
        if clutch is not None:
            self._apply_near_miss_result(clutch, events)

        # CoreSimulation has no tempo powers; style points stay inactive here.

    def _apply_body_near_miss(self, events: list[StepEvent]) -> None:
        if not self.enable_near_miss:
            return

        result = self.near_miss.check_near_miss(
            self.snake.get_head(),
            self.snake.positions_set,
            len(self.snake.body),
        )
        if result is not None:
            self._apply_near_miss_result(result, events)

    def _apply_near_miss_result(
        self,
        near_miss_event: NearMissEvent,
        events: list[StepEvent],
    ) -> None:
        if near_miss_event.is_warning:
            events.append(
                StepEvent(
                    kind="near_miss",
                    position=near_miss_event.position,
                    value=0,
                )
            )
            return

        multiplier = self.near_miss.get_combo_multiplier()
        bonus = int(near_miss_event.score_bonus * multiplier)
        if bonus > 0:
            self.score.add_bonus_score(bonus)
            events.append(StepEvent(kind="score_changed", value=bonus))

        position = None if near_miss_event.position == (-1, -1) else near_miss_event.position
        events.append(
            StepEvent(
                kind="near_miss",
                position=position,
                value=bonus,
            )
        )
        self.near_miss.add_event(near_miss_event)
        self.session_near_misses += 1

    def _append_achievement_candidates(self, events: list[StepEvent]) -> None:
        """Emit achievement_candidate events when the product flag is enabled."""
        if not self.enable_achievement_candidates:
            return
        if self.alive and not self.won:
            return

        metrics = RunAchievementMetrics(
            score=self.score.base_score,
            max_combo=self.session_max_combo,
            length=len(self.snake.body),
            food_eaten=self.food_eaten,
            wrap_count=self.wraps,
            near_misses=self.session_near_misses,
            powerups_collected=0,
            survival_ticks=self.step_count,
            is_terminal=True,
        )
        for index in candidate_event_values(
            metrics,
            already_unlocked=self.already_unlocked_achievements,
        ):
            events.append(StepEvent(kind="achievement_candidate", value=index))

    def _record(
        self,
        commands: tuple[Direction, ...],
        *,
        wrapped: bool,
        ate_food: bool,
        events: tuple[StepEvent, ...],
    ) -> StepRecord:
        """Capture observable state after a step."""
        mutable_events = list(events)
        self._append_achievement_candidates(mutable_events)
        events = tuple(mutable_events)
        return StepRecord(
            step=self.step_count,
            commands=tuple(command.name for command in commands),
            direction=self.snake.direction.name,
            head=self.snake.get_head(),
            food=self.food.position,
            length=len(self.snake.body),
            score=self.score.base_score,
            combo=self.score.combo_count,
            starvation_seconds=round(self.starvation_seconds, 10),
            food_eaten=self.food_eaten,
            wrapped=wrapped,
            ate_food=ate_food,
            alive=self.alive,
            won=self.won,
            death_cause=self.death_cause,
            events=events,
        )
