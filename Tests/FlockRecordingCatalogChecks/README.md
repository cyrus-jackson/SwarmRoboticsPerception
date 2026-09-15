# Flocking grid checks

From the repository root, with .NET 10 installed:

```sh
dotnet run --project Tests/FlockRecordingCatalogChecks/FlockRecordingCatalogChecks.csproj --artifacts-path /tmp/flock-grid-test-artifacts
```

Checks the actual selection code against all 18 recorded conditions (40 unique videos and matching JSON trajectories each), numeric layout order, locale independence, condition filtering, invalid inputs, duplicate layouts, mixed recording batches, and missing layouts. No external packages are required. Synthetic files are created in a temporary folder and removed after the checks.

The Unity-specific grid is additionally checked in Play mode: all 40 cells render, speed and radius changes select new clips, and the group can restart. Video playback starts together but uses independent decoders with frame dropping enabled; use the trajectory notebook for precise frame/time measurements.
