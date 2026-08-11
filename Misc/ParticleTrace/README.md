# ParticleTrace

Runs one particle effect as a simulation with no graphics device and writes down what it did, so two
runs can be compared as state rather than as pixels.

An effect that looks wrong is a difference in state long before it is a difference in pixels, and the
state is the only place the difference can be attributed to the function that produced it. A
screenshot can say that an effect looks wrong; it cannot say which operator made it so.

```
dotnet run --project Misc/ParticleTrace -c Release -- \
    --vpk "$STEAM/Counter-Strike Global Offensive/game/csgo/pak01_dir.vpk" \
    --file particles/water_fx/waterfall_anubis.vpcf_c \
    --out waterfall.jsonl --receipt waterfall.receipt.json \
    --seed 0 --steps 30
```

Run with no arguments for the full option list.

## What makes two runs comparable

The renderer draws a fresh 12-bit seed per system instance, which is exactly what a comparison
cannot have. `--seed` fixes the root's seed and derives every child's from it, so an effect replays
identically. Nothing else in the particle renderer is a source of randomness — `ParticleRandom` is
the only reader of the shared random table.

The step schedule is fixed too (`--dt`, `--steps`), rather than following real frame times, and the
control points are seeded from the effect's authored `game` configuration — the same one the viewer
applies outside the editor, since several constants an effect depends on live there.

Renderers are not constructed at all in this mode. They allocate GL objects in their constructors, so
building them would need a context; skipping them is what lets the simulation run on a build agent.
The manifest still records which renderer classes the effect has, so a trace says which drawing it
did not do.

## The trace

Newline-delimited JSON, one record per line, discriminated by `k`:

| `k` | one per | carries |
|---|---|---|
| `manifest` | trace | asset, seed, timestep, and every system's authored function list in run order |
| `step` | system, step | age, timestep, live count, seed |
| `p` | live particle, step | every attribute in `ParticleFields` |
| `fn` | function, step | which fields it changed, on how many particles, and which particle ids |
| `init` | initializer, spawning particle | which fields it wrote |
| `cp` | control point, step | position, previous position, orientation |

`fn` and `init` are the attribution layer. A field that first disagrees at some step was last written
by the last record naming it at or before that step — which is a fact the run recorded, not a guess
from reading the operator list.

Floats are written round-trippable, so a reader recovers the exact bits. Non-finite values are
written as bare `NaN`/`Infinity`: they are a divergence signal and mapping them to `null` would hide
one. Records emitted before the first step (the initial burst `Start` seeds) carry step `-1`.

## What it does not do

It says nothing about whether the effect is *correct*. That needs a second trace from the game to
compare against. Everything here exists to make such a comparison mean something when it arrives.
