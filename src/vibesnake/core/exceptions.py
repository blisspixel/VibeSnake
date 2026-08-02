"""Domain exceptions raised by the Python rules reference."""


class GridFullException(Exception):
    """Report that food cannot spawn because every grid cell is occupied."""

    def __init__(self, occupied_count: int, grid_size: int):
        """
        Initialize grid-full exception with diagnostic context.

        Args:
            occupied_count: Number of occupied grid cells (snake + powerups)
            grid_size: Total grid capacity (GRID_WIDTH × GRID_HEIGHT)
        """
        self.occupied_count = occupied_count
        self.grid_size = grid_size
        self.occupancy_percent = (occupied_count / grid_size) * 100 if grid_size > 0 else 100.0

        message = (
            f"Grid full: {occupied_count}/{grid_size} cells occupied "
            f"({self.occupancy_percent:.1f}% full). Cannot spawn food."
        )
        super().__init__(message)
