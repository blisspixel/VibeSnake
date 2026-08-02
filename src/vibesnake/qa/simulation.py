"""Fast reference simulation built from the production core entities."""

from __future__ import annotations

from vibesnake.core.enums import Direction
from vibesnake.core.exceptions import GridFullException
from vibesnake.core.food import Food
from vibesnake.core.scoring import ScoreManager
from vibesnake.core.snake import Snake
from vibesnake.qa.models import StepEvent, StepRecord


class CoreSimulation:
    """Exercise real movement, food, and scoring code without rendering or audio.

    This is intentionally a reference adapter, not the final game runtime. It
    preserves the current coordinator's ordering so the future deterministic
    rules engine can be checked against known traces during migration.
    """

    def __init__(self, step_seconds: float = 0.05):
        if step_seconds <= 0:
            raise ValueError("step_seconds must be greater than zero")

        self.step_seconds = step_seconds
        self.snake = Snake()
        self.food = Food(self.snake.positions_set)
        self.score = ScoreManager()
        self.alive = True
        self.won = False
        self.death_cause: str | None = None
        self.step_count = 0
        self.food_eaten = 0
        self.wraps = 0
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
        self.score.update(self.step_seconds)
        previous_direction = self.snake.direction
        next_head = self.snake.peek_next_head()
        ate_food = self.food.position is not None and next_head == self.food.position
        speed_bonus = ate_food and self.score.time_since_last_food < 1.5
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

        return self._record(
            commands,
            wrapped=wrapped,
            ate_food=ate_food,
            events=tuple(events),
        )

    def _record(
        self,
        commands: tuple[Direction, ...],
        *,
        wrapped: bool,
        ate_food: bool,
        events: tuple[StepEvent, ...],
    ) -> StepRecord:
        """Capture observable state after a step."""
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
