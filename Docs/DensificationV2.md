# Densification V2

Select `DensificationV2` in the Scene 2 Swarm Control UI, then use **Reset Scene** and **Play Motion**. It shares the existing densification spawn area and supports all four spawn layouts. No scene or prefab setup is required.

This variant follows the local Boids rules in section 3.1 of Hénard et al., *Human Perception of Swarm Fragmentation* (HRI 2024), DOI: [10.1145/3610977.3634961](https://doi.org/10.1145/3610977.3634961).

- Neighbours are agents whose centers are strictly inside the perception radius, with full 360-degree perception (zero blind spot).
- Cohesion attracts an agent toward the mean position of its perceived neighbours. Separation averages the unit vectors away from those neighbours.
- Alignment averages neighbour velocities, and defaults to zero to avoid coordinated flocking. Random movement and friction retain the existing force and Euler integration rules.
- Overlap avoidance sums unit vectors away from perceived neighbours inside the safety distance.
- There is no common fate attraction or fixed goal area. Agents that lose contact do not follow a hidden global destination. Separate local groups can remain fragmented.
- Existing environmental obstacle avoidance, speed limiting, integration steps, and randomness sampling are retained.

The paper specifies stronger cohesion than separation for densification; its Table 1 provides a flocking example, not an exact densification preset. V2 therefore uses project-scale defaults: cohesion 0.5, separation 0.35, alignment 0, friction 1, random movement 0, overlap avoidance 1, safety distance 0.35, perception radius 1.7, and obstacle radius 0.1. Maximum speed and environmental avoidance remain adjustable using the existing controls. Sparse starts outside perception range need greater perception, closer spacing, or random movement to establish local contact.

The lower force intensities and higher friction reduce overshoot. For an isolated pair, cohesion and separation balance at `separation / cohesion = 0.7` world units, outside the safety distance. The 0.35 safety distance also leaves clearance around the default agents (collider diameter approximately 0.312). Overlap avoidance remains a discontinuous local force, but its smaller intensity reduces the impulse when the threshold is crossed. These steering values reduce motion; crowded groups, obstacles, or user-enabled randomness can still produce movement. The recording termination rule below does not alter the steering forces.

The runtime group readout, perception graph outlines, recording cluster counts, and trajectory cluster samples use V2's center-distance neighbourhood definition. Existing modes retain collider-edge perception.

**Record Current Settings** records this variant as `DensificationV2`. Its default recording rule is **Fixed Duration**: captured motion ends only at the recording timeout (`recordingTimePerSim`, currently 30 seconds), or at a positive per-type duration override. It starts immediately and adds no end-condition tail. Dwell, density thresholds, and motion settling do not end the default recording early. The recorded end reason is `timeout`. Closing markers and the finished card follow normally, so the video can be longer than the motion duration.

The rule controls recording runs and their normal motion shutdown; ordinary Play Motion remains manual. The same timeout default applies when the per-motion settings have no V2 entry. A configured V2 entry remains an explicit override; **Motion Settled** is still available in the inspector when desired. Restart Play mode after Unity compiles to pick up the changed defaults on the dynamically created recorder. Add V2 to the relevant batch list to include it in a batch.

The original `Densification` enum value, preset, common fate, goal area, neighbour calculation, and steering defaults are preserved. `DensificationV2` is appended to the enum so existing serialized motion selections keep their numeric values.
