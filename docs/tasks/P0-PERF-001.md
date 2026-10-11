# P0-PERF-001

TASK ID: P0-PERF-001
OWNER: Performance / Reliability Engineer
REVIEWER: Project Director + QA Engineer
PRD REFERENCES: §24, §29.1, F43
RELEASE: P0 feasibility
DEPENDENCIES: P0-COLLECTOR-001, P0-WINDOWS-001
OBJECTIVE: Record real hardware baseline profiling for CPU, memory, sampling latency, thread count, and handle stability
IN SCOPE:
- Native provider ReadSnapshot latency benchmark
- Headless Collector SampleOnce latency benchmark
- Memory footprint (working set, heap delta)
- Thread and kernel handle count stability
- Reproducible benchmark evidence
OUT OF SCOPE:
- Premature optimization or sacrificing measurement correctness for microsecond gains
EXPECTED FILES:
- tests/SpeedMeter.Checks/PerfFixtures.cs
- docs/handoffs/P0-PERF-001.md
- docs/reviews/P0-PERF-001.md
ACCEPTANCE CONDITIONS:
- Provider latency < 20 ms
- Collector latency < 25 ms
- Working set < 50 MB budget for core engine
- Zero thread or handle growth across sampling bursts
TEST REQUIREMENTS:
- Benchmark fixtures in check runner
STATUS: In progress
