# Densification V2

Select `DensificationV2` in the Scene 2 Swarm Control UI, then use **Reset Scene** and **Play Motion**. It shares the existing densification spawn area and supports all four spawn layouts. No scene or prefab setup is required.

This variant uses the shared `SwarmAgent` implementation restored from the October 2 checkpoint (`f124a1d`), with overlap avoidance corrected to include perceived neighbours only. Its six local Boids forces are based on section 3.1 of Hénard et al., *Human Perception of Swarm Fragmentation* (HRI 2024), DOI: [10.1145/3610977.3634961](https://doi.org/10.1145/3610977.3634961).

- Neighbours use the restored collider-edge perception calculation, with full 360-degree perception (zero blind spot). This differs from the paper's center-distance perception.
- Cohesion attracts an agent toward the mean position of its perceived neighbours. Separation averages the unit vectors away from those neighbours.
- Alignment averages neighbour velocities, and defaults to zero to avoid coordinated flocking. Random movement and friction retain the existing force and Euler integration rules.
- Overlap avoidance sums unit vectors away from perceived neighbours inside the safety distance, matching the paper's restriction to the neighbour set.
- The V2 preset assigns no common fate target or fixed goal area. The shared agent once again applies common fate whenever a target is assigned.
- Environmental obstacle avoidance and speed limiting are retained. Random movement is sampled once per frame, and each agent receives one Euler update using `Time.deltaTime`.

The paper specifies stronger cohesion than separation for densification; its Table 1 provides a flocking example, not an exact densification preset. V2 therefore uses project-scale defaults: cohesion 2.0, separation 0.35, alignment 0, friction 1, random movement 0, overlap avoidance 1, safety distance 0.35, perception radius 1.5, and obstacle radius 0.1. Maximum speed and environmental avoidance remain adjustable using the existing controls. Sparse starts outside perception range need greater perception, closer spacing, or random movement to establish local contact.

Friction damps velocity, but a zero Noise value does not require physical rest. Cohesion 2.0 and separation 0.35 would balance an isolated pair at 0.175 world units without overlap avoidance, inside the 0.35 safety distance. Higher Cohesion values also pull agents into this avoidance range. Local groups can retain internal movement, especially near the avoidance threshold.

All motion types update sequentially once per rendered frame using `Time.deltaTime`. `StepSimulation`, its accumulator, fixed-step settings, and substep cap have been removed; elapsed frame time is no longer discarded by a substep limit. The additional state setter, simultaneous V2 staging, obstacle sweeps, overlap correction, and inward-velocity removal have been reverted. Wall-contact tracking used by recording remains available.

The runtime group readout, perception graph outlines, recording cluster counts, and trajectory cluster samples use collider-edge neighbours for every mode, matching the restored agent.

This rollback restores historical behavior rather than an exact reproduction of the paper. Common fate, environmental obstacle forces, speed limiting, and collider-edge perception are existing project extensions. Random movement is now redrawn for every agent update, matching the paper's rule-call sampling.

**Record Current Settings** records this variant as `DensificationV2`. Its default recording rule is **Fixed Duration**: captured motion ends only at the recording timeout (`recordingTimePerSim`, currently 30 seconds), or at a positive per-type duration override. It starts immediately and adds no end-condition tail. Dwell, density thresholds, and motion settling do not end the default recording early. The recorded end reason is `timeout`. Closing markers and the finished card follow normally, so the video can be longer than the motion duration.

The rule controls recording runs and their normal motion shutdown; ordinary Play Motion remains manual. The same timeout default applies when the per-motion settings have no V2 entry. A configured V2 entry remains an explicit override; **Motion Settled** is still available in the inspector when desired. Restart Play mode after Unity compiles to pick up the changed defaults on the dynamically created recorder. Add V2 to the relevant batch list to include it in a batch.

The original `Densification` enum value, preset, common fate, goal area, neighbour calculation, and steering defaults are preserved. `DensificationV2` is appended to the enum so existing serialized motion selections keep their numeric values.

New trajectory headers and batch configurations retain the legacy `simulationStep` field for compatibility and write it as `0`, indicating frame-based integration. Replay displays this as **Per frame**; older recordings retain their recorded fixed-step value.
