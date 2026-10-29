#!/usr/bin/env python3
"""
mcp-authorize Phase-2 gate (task b8): runnable isolation/selection/pin/Origin/stdio matrix plus
cross-language golden-vector parity, invoked as a single command locally or in CI.

Proves McpPlugin 7.0 end-to-end before the host (c1) and engines (Phase 4) consume it:
  * live multi-tenant isolation matrix (2 accounts x 2 instances x 2 sessions) with leak-detection:
    the harness FAILS if cross-account routing/notification leakage is introduced (Phase2Gate*
    tests in McpPlugin.Server.Tests).
  * cross-language golden-vector parity: the C# ProjectIdentity reference vectors the TS consumers
    must reproduce at Phase 4 (ProjectIdentityGoldenVector* in McpPlugin.Tests).

Hermetic: no external authorization server, no multi-process orchestration, a stable CI gate. Run
from any directory (the repo root is resolved from this script's location).

Usage:
    python scripts/phase2_gate_matrix.py              # build + run the whole gate
    python scripts/phase2_gate_matrix.py --no-build   # reuse an existing Release build
Exit 0 = PASS; otherwise the exit code of the first failing dotnet command (2 = bad argument).
"""
from __future__ import annotations

import argparse
import subprocess
from pathlib import Path
from typing import List, Optional, Sequence

ROOT = Path(__file__).resolve().parents[1]
CONFIGURATION = "Release"

# (title, test project, test filter, what failed)
SUITES = (
    ("Phase-2 isolation/selection/pin/Origin/stdio matrix (McpPlugin.Server.Tests)",
     "McpPlugin.Server.Tests/McpPlugin.Server.Tests.csproj", "FullyQualifiedName~IsolationMatrix",
     "isolation matrix failed"),
    ("Golden-vector parity: C# ProjectIdentity reference (McpPlugin.Tests)",
     "McpPlugin.Tests/McpPlugin.Tests.csproj", "FullyQualifiedName~ProjectIdentityGoldenVector",
     "golden-vector parity failed"),
)


class GateFailure(Exception):
    def __init__(self, message: str, code: int):
        super().__init__(message)
        self.code = code


def dotnet(args: Sequence[str], failure: str) -> None:
    code = subprocess.run(["dotnet", *args], cwd=ROOT).returncode
    if code != 0:
        raise GateFailure(failure, code)


def run(build: bool) -> None:
    if build:
        print(f"== restore + build ({CONFIGURATION}) ==", flush=True)
        dotnet(["restore"], "restore failed")
        dotnet(["build", "--no-restore", "--configuration", CONFIGURATION], "build failed")

    for title, project, test_filter, failure in SUITES:
        print(f"\n== {title} ==", flush=True)
        dotnet(["test", project, "--no-build", "--configuration", CONFIGURATION, "--verbosity", "normal",
                "--filter", test_filter, "--", "RunConfiguration.TreatNoTestsAsError=true"], failure)

    print("\n== Phase-2 gate: PASS ==")


def main(argv: Optional[List[str]] = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.strip().splitlines()[0])
    parser.add_argument("--no-build", action="store_true", help="reuse an existing Release build")
    args = parser.parse_args(argv)
    try:
        run(build=not args.no_build)
    except GateFailure as exc:
        print(f"\n== Phase-2 gate: FAIL ({exc}) ==")
        return exc.code
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
