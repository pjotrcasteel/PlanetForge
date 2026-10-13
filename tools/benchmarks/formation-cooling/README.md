# Native formation cooling benchmark

Run `dotnet run --project tools/benchmarks/formation-cooling -c Release`. This starts the actual 384-cell formation experience, warms initial publication, then measures six 5-second cooling/publication steps with the shared physical terrain gateway. It prints elapsed native time, current-thread allocated bytes, a SHA-256 over every final owned layer's enthalpy, exact clock and global energy residual. Compare the same protocol at two commits. It does not measure browser FPS, GPU presentation or Safari.

The initial vertical-slice candidate allocated 1,686,495,280 bytes and took 1,772.6 ms in this session; the material-model reuse candidate allocated 437,070,784 bytes and took 1,423.2 ms. Final layer enthalpies have the identical SHA-256 `F53F02B45450DEC518F8C2F87A33DA0AFA110A649BAB8F825FD48A2877C07A1D`. Timing is environment-dependent; the comparison is an experiment, not a performance threshold or a permanent physics checksum.
