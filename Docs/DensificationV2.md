# Densification V2

Select `DensificationV2` in the Scene 2 Swarm Control UI, then use **Reset Scene** and **Play Motion**. It shares the existing densification spawn area and supports all four spawn layouts. No scene or prefab setup is required.

This variant uses the shared `SwarmAgent` implementation restored from the October 2 checkpoint (`f124a1d`), with overlap avoidance corrected to include perceived neighbours only. Its six local Boids forces are based on section 3.1 of Hénard et al., *Human Perception of Swarm Fragmentation* (HRI 2024), DOI: [10.1145/3610977.3634961](https://doi.org/10.1145/3610977.3634961).

- Neighbours use the restored collider-edge perception calculation, with full 360-degree perception (zero blind spot). This differs from the paper's center-distance perception.
- Cohesion attracts an agent toward the mean position of its perceived neighbours. Separation averages the unit vectors away from those neighbours.
- Alignment averages neighbour velocities, and defaults to zero to avoid coordinated flocking. Random movement and friction retain the existing force and Euler integration rules.
- Overlap avoidance sums unit vectors away from perceived neighbours inside the safety distance, matching the paper's restriction to the neighbour set.
- The V2 preset assigns no common fate target or fixed goal area. The shared agent once again applies common fate whenever a target is assigned.
- Environmental obstacle avoidance and speed limiting are retained. Random movement is sampled once per frame, and each agent receives one Euler update using `Time.deltaTime`.

The paper specifies stronger cohesion than separation for densification; its Table 1 provides a flocking example, not an exact densification preset. V2 therefore uses project-scale defaults: cohesion 6.0, separation 0.35, alignment 0, friction 0.5, random movement 0, overlap avoidance 10, safety distance 0.36, and perception radius 1.5. All motion presets set friction to the requested project value of 0.5, overlap avoidance to the requested project value of 10, and safety distance to the requested project value of 0.36 world units. The safety-distance control displays two decimal places. Maximum speed remains adjustable using the existing controls. Sparse starts outside perception range need greater perception, closer spacing, or random movement to establish local contact.

Friction damps velocity, but a zero Noise value does not require physical rest. Cohesion 6.0 and separation 0.35 would balance an isolated pair at approximately 0.0583 world units without overlap avoidance, inside the 0.36 safety distance. Higher Cohesion values also pull agents into this avoidance range. Local groups can retain internal movement, especially near the avoidance threshold.

All motion types update sequentially once per rendered frame using `Time.deltaTime`. `StepSimulation`, its accumulator, fixed-step settings, and substep cap have been removed; elapsed frame time is no longer discarded by a substep limit. The additional state setter, simultaneous V2 staging, obstacle sweeps, overlap correction, and inward-velocity removal have been reverted. Wall-contact tracking used by recording remains available.

The runtime group readout, perception graph outlines, recording cluster counts, and trajectory cluster samples use collider-edge neighbours for every mode, matching the restored agent.

This rollback restores historical behavior rather than an exact reproduction of the paper. Common fate, environmental obstacle forces, speed limiting, and collider-edge perception are existing project extensions. Random movement is now redrawn for every agent update, matching the paper's rule-call sampling.

**Record Current Settings** records this variant as `DensificationV2`. Its recording termination rule is **DBSCAN Count Stable**:

- Measure the initial DBSCAN cluster count at capture `t = 0`, with no unrecorded warm-up, then sample it every recorded frame.
- Stop once the count has remained unchanged throughout the preceding **3 seconds**, after at least **5 seconds of recorded motion**. Stability before 5 seconds does not latch the result: the latest window must still be stable when recording ends.
- Every observed count change restarts the stability period. A sequence such as `2 → 3 → 2` is a change even though its endpoints match. There is no additional dwell, spacing, contraction, or low-speed requirement.
- DBSCAN termination parameters belong to the V2 entry in **SimRecorder → Recording Rules (per motion type)**. `CreateDensificationV2RecordingSettings()` defaults to **fixed epsilon 1.5** world units and **Min Points = 3**. Enable **Use Automatic Eps** to derive epsilon from the median kth-neighbour distance instead; the defaults are k = 3 and spacing multiple = 4. Parameters are captured at the start of the run and shared by recordings and ordinary Play Motion diagnostics. DBSCAN noise is excluded; a stable count of zero also satisfies the count rule when agents are present.
- The maximum duration remains a timeout (`recordingTimePerSim`, currently 30 seconds, or a positive per-type override). A cap below 5 seconds is raised to 5, so closing markers and the finished card are not needed to satisfy the minimum duration.

For example, an unchanged count from the start ends motion at 5 seconds. A final count change at 4 seconds ends motion at 7 seconds if the count remains unchanged. A change at 6 seconds moves the earliest termination to 9 seconds. Times are evaluated at frame boundaries.

The rule applies to current-settings and batch recordings and their normal motion shutdown; ordinary Play Motion remains manual. Existing V2 entries and missing-entry defaults both use this rule, zero start delay, and no separate dwell. An optional configured end delay still adds a recorded tail after termination. Closing markers and the finished card follow normally. Add V2 to the relevant batch list to include it in a batch.

Ordinary **Play Motion** also checks the same DBSCAN count and timing rule for debugging. The Unity Console logs the initial count at `t = 0`, each count change, and one `Termination condition met` message with elapsed time and stability duration. Motion continues after this message for inspection. Pause or Reset Scene cancels the check; the next Play Motion starts a fresh check. Recording and replay do not start this live diagnostic.

For persistent Inspector overrides, add a **SimRecorder** component to the same object as **UI** (Background in Scene6_Expand), then edit its DensificationV2 recording-rule entry before Play Motion or recording starts. If no recorder exists, Play Motion uses `CreateDensificationV2RecordingSettings()` directly. Existing recorder entries retain their explicitly configured DBSCAN parameters. The spatial cluster panel retains independent visualization settings; set it to DBSCAN fixed with epsilon 1.5 and Min Points 3 to display the same grouping as the default termination check.

Successful recordings use end reason `cluster-count-stable`; unstable runs that reach the duration cap use `timeout`. Recording configurations store the DBSCAN method, parameters, initial/final counts, noise count, stability duration, 3-second window, and 5-second minimum. Existing perception-graph group counts remain separate metadata. Count stability does not require cluster membership or the agents' positions to stop changing.

The original `Densification` enum value, preset, common fate, goal area, neighbour calculation, and steering defaults are preserved. `DensificationV2` is appended to the enum so existing serialized motion selections keep their numeric values.

New trajectory headers and batch configurations retain the legacy `simulationStep` field for compatibility and write it as `0`, indicating frame-based integration. Replay displays this as **Per frame**; older recordings retain their recorded fixed-step value.
