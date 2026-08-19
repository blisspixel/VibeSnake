"""Print the current host and plugin public-contract digests."""

from pathlib import Path

from check_agent_interop import calculate_contract_digests


if __name__ == "__main__":
    root = Path(__file__).resolve().parents[1]
    digests = calculate_contract_digests(root)
    print(f"host={digests['host']}")
    print(f"plugin={digests['plugin']}")
