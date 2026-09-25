# Crash regression corpus

Keep minimal UTF-8 `.sql` reproducers here when a parser, formatter, safe mode, or
CLI crash is found. Prefer one issue per file and a descriptive filename. The
corpus test exercises every file through parsing, strict formatting, safe
formatting, and CLI stdin. Do not put secrets or production queries here.

The initial entries cover malformed and boundary SQL; they are not claims of
previous crashes. Add an actual reproducer and its expected behavior whenever
a crash is discovered.
