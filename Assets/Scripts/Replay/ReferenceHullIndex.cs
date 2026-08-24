using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Look-up of "how far did this swarm spread with no randomness?", keyed by the layout it started
/// from.
///
/// The point of a matched-start batch is that a randomised run and a zero-randomness run begin
/// from the identical arrangement of agents. This index turns the recorded zero-randomness runs
/// into stop targets: when the randomised run reaches the hull area its own reference settled at,
/// the two clips end at the same degree of dispersion, so randomness changes the manner of the
/// motion rather than how far the swarm got.
///
/// Targets are matched on layout AND perception radius AND max speed. All three matter — the
/// reference plateau spans 25 to 566 u2 across the radius sweep, and rises about 26% between max
/// speed 1.5 and 4 at the same radius, so a target borrowed from the wrong condition would fire
/// far too early or never.
///
/// Reading is deliberately not JsonUtility.FromJson&lt;SwarmTrajectory&gt;. A recording is 3-12 MB
/// and most of that is the per-agent pose array, which this index never looks at. Instead the
/// header is parsed first and the file is abandoned immediately unless it is a reference run;
/// reference files are then scanned for their hull area series alone.
/// </summary>
public class ReferenceHullIndex
{
    /// <summary>
    /// How far a recorded parameter may sit from the one being matched and still count.
    ///
    /// Not zero, and not a rounded key, because these values come off UI sliders and are stored as
    /// whatever float that produced. One batch recorded "max speed 4" as 3.9654903, another as
    /// 3.9889901 and a third as 3.9918423. Exact or 2-decimal matching silently found no reference
    /// for a whole condition. The tolerances are far tighter than the real spacing between levels —
    /// radii step by 0.25 and the speeds tested are 1.5 and 4 — so a loose match cannot reach the
    /// wrong level.
    /// </summary>
    public const float RadiusTolerance = 0.02f;
    public const float SpeedTolerance = 0.1f;

    /// <summary>An inexact match wider than this is logged, so silent drift cannot creep in.</summary>
    private const float QuietMatch = 0.0005f;

    /// <summary>
    /// A plateau must differ from the spawn area by at least this factor, in whichever direction
    /// the motion type moves, before it counts as a target. Dispersion grows past 1.2x,
    /// densification shrinks past 1/1.2. Between the two the reference did not really move and its
    /// "plateau" is just where it started.
    /// </summary>
    public const float MinSeparation = 1.2f;

    /// <summary>
    /// Version tag on the cache file. Bump whenever the plateau maths changes or Entry gains a
    /// field: a cache written before `startArea` existed would deserialise it as 0, which makes
    /// every entry read as a contraction and inverts the direction of every test.
    /// </summary>
    private const int CacheVersion = 3;

    // Entries grouped by layout, then searched by nearest parameter within tolerance. A dictionary
    // keyed on rounded floats cannot express "close enough", which is what this needs.
    private readonly Dictionary<string, List<Entry>> byLayout = new Dictionary<string, List<Entry>>();
    private int count;

    public int Count => count;
    public string SourceFolder { get; private set; }
    public int FilesScanned { get; private set; }
    public int FilesSkipped { get; private set; }

    public class Entry
    {
        public string layoutId;
        public float perceptionRadius;
        public float maxSpeed;

        /// <summary>Plateau hull area: the median area from the point the hull stopped changing.</summary>
        public float plateauArea;

        /// <summary>
        /// Hull area on the reference's own first frame.
        ///
        /// Carried so the direction of the test is a property of the reference rather than of
        /// whatever run is being compared against it. Inferring direction from the other run's
        /// frame 0 breaks whenever the target lands inside the spread of starting areas: the test
        /// silently flips to "has it shrunk to this?" and a swarm that expanded tenfold is reported
        /// as never having arrived.
        /// </summary>
        public float startArea;

        /// <summary>True when the reference expanded to its plateau, false when it contracted.</summary>
        public bool Growing => plateauArea >= startArea;

        /// <summary>
        /// How far the plateau sits from the spawn area. Near 1 in either direction means the
        /// reference barely moved, so its area is not a usable target — it is inside the noise of
        /// where the swarm started.
        /// </summary>
        public float Growth => startArea > 0.0001f ? plateauArea / startArea : 0f;

        /// <summary>False when the reference did not disperse or contract enough to define a target.</summary>
        public bool HasDirection => Growth >= MinSeparation || (Growth > 0f && Growth <= 1f / MinSeparation);

        /// <summary>Seconds into the reference clip at which the plateau began.</summary>
        public float plateauTime;

        /// <summary>Frame index of the plateau, so the reference's own hull outline can be read back.</summary>
        public int plateauFrame;

        /// <summary>Full path to the recording, for loading that outline on demand.</summary>
        public string sourcePath;

        /// <summary>False when the reference never settled, so plateauArea is a lower bound.</summary>
        public bool settled;

        public string sourceFile;
    }

    // ----------------------------------------------------------------- keys

    /// <summary>
    /// The reference plateau for a run, or false when nothing was recorded close enough to match.
    ///
    /// Matching is nearest-within-tolerance on both radius and speed, and the radius is the tighter
    /// of the two because it is the swept axis: two adjacent radii differ by 0.25 and their plateau
    /// areas by 20-40 u2, so picking a neighbour would be badly wrong. Ties are broken on radius
    /// first for the same reason.
    /// </summary>
    public bool TryGet(string layoutId, float perceptionRadius, float maxSpeed, out Entry entry)
    {
        entry = null;
        if (string.IsNullOrEmpty(layoutId) || !byLayout.TryGetValue(layoutId, out List<Entry> candidates))
        {
            return false;
        }

        float best = float.MaxValue;

        foreach (Entry candidate in candidates)
        {
            float dr = Mathf.Abs(candidate.perceptionRadius - perceptionRadius);
            float ds = Mathf.Abs(candidate.maxSpeed - maxSpeed);
            if (dr > RadiusTolerance || ds > SpeedTolerance) continue;

            // Radius weighted far above speed: a radius mismatch is a different experimental level,
            // a speed mismatch of this size is slider noise on the same one.
            float cost = dr * 1000f + ds;
            if (cost < best)
            {
                best = cost;
                entry = candidate;
            }
        }

        if (entry == null) return false;

        float radiusGap = Mathf.Abs(entry.perceptionRadius - perceptionRadius);
        float speedGap = Mathf.Abs(entry.maxSpeed - maxSpeed);
        if (radiusGap > QuietMatch || speedGap > QuietMatch)
        {
            Debug.Log($"[ReferenceHullIndex] Matched R {perceptionRadius:F4}/maxSpeed {maxSpeed:F4} to a " +
                      $"reference recorded at R {entry.perceptionRadius:F4}/maxSpeed {entry.maxSpeed:F4} " +
                      $"(gap {radiusGap:F4}/{speedGap:F4}). These come off sliders, so small gaps are " +
                      $"expected; a large one means the levels do not line up.");
        }

        return true;
    }

    // ----------------------------------------------------------------- building

    /// <summary>
    /// Scans a folder tree of recordings and indexes every zero-randomness run in it.
    ///
    /// <paramref name="useCache"/> keeps a small json beside the folder so a rescan of an unchanged
    /// set of recordings costs nothing. Entries are re-derived whenever a file's timestamp or size
    /// no longer matches what the cache recorded.
    /// </summary>
    public static ReferenceHullIndex Build(string folder, float randomTolerance = 0.001f,
                                           bool useCache = true, bool verbose = true)
    {
        ReferenceHullIndex index = new ReferenceHullIndex { SourceFolder = folder };

        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            Debug.LogWarning($"[ReferenceHullIndex] No folder at '{folder}'. No targets will be resolved.");
            return index;
        }

        Dictionary<string, CacheRecord> cache = useCache ? LoadCache(folder) : new Dictionary<string, CacheRecord>();
        Dictionary<string, CacheRecord> fresh = new Dictionary<string, CacheRecord>();

        string[] files = Directory.GetFiles(folder, "*.json", SearchOption.AllDirectories);
        System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();

        foreach (string path in files)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            if (name.EndsWith("_config", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("batch_config", StringComparison.OrdinalIgnoreCase) ||
                name.Equals(CacheFileName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            FileInfo info = new FileInfo(path);
            string signature = $"{info.Length}:{info.LastWriteTimeUtc.Ticks}";

            if (cache.TryGetValue(path, out CacheRecord cached) && cached.signature == signature)
            {
                fresh[path] = cached;
                if (cached.isReference) index.Add(cached.ToEntry(path));
                index.FilesSkipped++;
                continue;
            }

            Entry entry = ScanFile(path, randomTolerance, out bool isReference);
            fresh[path] = CacheRecord.From(signature, isReference, entry);

            if (isReference && entry != null) index.Add(entry);
            index.FilesScanned++;
        }

        clock.Stop();

        if (useCache) SaveCache(folder, fresh);

        if (verbose)
        {
            Debug.Log($"[ReferenceHullIndex] {index.Count} reference targets from {files.Length} files " +
                      $"under '{folder}' ({index.FilesScanned} read, {index.FilesSkipped} from cache, " +
                      $"{clock.ElapsedMilliseconds} ms).");
        }

        return index;
    }

    private void Add(Entry entry)
    {
        if (entry == null || string.IsNullOrEmpty(entry.layoutId)) return;

        if (!byLayout.TryGetValue(entry.layoutId, out List<Entry> list))
        {
            list = new List<Entry>();
            byLayout[entry.layoutId] = list;
        }

        // Replace an existing entry for the same level rather than stacking duplicates, so
        // re-recording a reference supersedes the old one instead of racing it.
        for (int i = 0; i < list.Count; i++)
        {
            if (Mathf.Abs(list[i].perceptionRadius - entry.perceptionRadius) <= RadiusTolerance &&
                Mathf.Abs(list[i].maxSpeed - entry.maxSpeed) <= SpeedTolerance)
            {
                list[i] = entry;
                return;
            }
        }

        list.Add(entry);
        count++;
    }

    /// <summary>
    /// Reads one recording. Returns null and isReference=false for anything that is not a usable
    /// zero-randomness run, without having read the frame data.
    /// </summary>
    private static Entry ScanFile(string path, float randomTolerance, out bool isReference)
    {
        isReference = false;

        try
        {
            using (FileStream stream = File.OpenRead(path))
            using (StreamReader reader = new StreamReader(stream))
            {
                TrajectoryHeader header = ReadHeader(reader);
                if (header == null) return null;

                // The whole point of the early exit: a randomised run is abandoned here, before any
                // of its several megabytes of pose data has been touched.
                if (Mathf.Abs(header.randomMovement) > randomTolerance) return null;
                if (!header.hullAreaRecorded) return null;
                if (string.IsNullOrEmpty(header.spawnLayoutId)) return null;

                isReference = true;

                List<float> times = new List<float>();
                List<float> areas = new List<float>();
                ReadSeries(reader, times, areas);

                if (areas.Count == 0) return null;

                Plateau plateau = FindPlateau(times.ToArray(), areas.ToArray());

                return new Entry
                {
                    layoutId = header.spawnLayoutId,
                    perceptionRadius = header.perceptionRadius,
                    maxSpeed = header.maxSpeed,
                    plateauArea = plateau.area,
                    plateauTime = plateau.time,
                    plateauFrame = plateau.index,
                    startArea = areas[0],
                    settled = plateau.reached,
                    sourceFile = Path.GetFileName(path),
                    sourcePath = path,
                };
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[ReferenceHullIndex] Could not read {Path.GetFileName(path)}: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// Pulls the header object out of the front of the file by brace matching, then hands it to
    /// JsonUtility. Brace matching rather than a fixed read size because the header carries the
    /// agent name array and the geometry list, so its length varies with the scene.
    /// </summary>
    private static TrajectoryHeader ReadHeader(StreamReader reader)
    {
        const string marker = "\"header\":";
        StringBuilder buffer = new StringBuilder();

        int c;
        while ((c = reader.Read()) >= 0)
        {
            buffer.Append((char)c);
            if (buffer.Length > marker.Length + 4) break;
            if (buffer.ToString().Contains(marker)) break;
        }

        if (!buffer.ToString().Contains(marker)) return null;

        // Advance to the opening brace of the header object.
        while ((c = reader.Read()) >= 0 && c != '{') { }
        if (c != '{') return null;

        StringBuilder json = new StringBuilder("{");
        int depth = 1;
        bool inString = false;
        bool escaped = false;

        while (depth > 0 && (c = reader.Read()) >= 0)
        {
            char ch = (char)c;
            json.Append(ch);

            if (escaped) { escaped = false; continue; }
            if (ch == '\\' && inString) { escaped = true; continue; }
            if (ch == '"') { inString = !inString; continue; }
            if (inString) continue;

            if (ch == '{') depth++;
            else if (ch == '}') depth--;
        }

        return depth == 0 ? JsonUtility.FromJson<TrajectoryHeader>(json.ToString()) : null;
    }

    /// <summary>
    /// Scans the remainder of the file for the per-frame timestamp and hull area.
    ///
    /// A frame object has exactly four keys — t, a, v, c — and the v and c arrays hold bare numbers
    /// with no keys of their own, so looking for the literal "t": and "a": tokens cannot collide
    /// with anything nested. The pose arrays are stepped over rather than parsed.
    /// </summary>
    private static void ReadSeries(StreamReader reader, List<float> times, List<float> areas)
    {
        StringBuilder number = new StringBuilder();

        // Four characters, not three: the token is quote, key, quote, colon.
        char[] window = new char[4];
        int filled = 0;
        int c;

        while ((c = reader.Read()) >= 0)
        {
            char ch = (char)c;

            if (filled < 4) { window[filled++] = ch; }
            else { window[0] = window[1]; window[1] = window[2]; window[2] = window[3]; window[3] = ch; }

            if (filled < 4 || window[0] != '"' || window[2] != '"' || window[3] != ':') continue;

            char key = window[1];
            if (key != 't' && key != 'a') continue;

            number.Clear();
            while ((c = reader.Read()) >= 0)
            {
                char d = (char)c;
                if (d == '-' || d == '+' || d == '.' || d == 'e' || d == 'E' || (d >= '0' && d <= '9'))
                {
                    number.Append(d);
                    continue;
                }
                break;
            }

            if (number.Length == 0) continue;

            if (float.TryParse(number.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture,
                               out float value))
            {
                if (key == 't') times.Add(value);
                else areas.Add(value);
            }

            filled = 0;
        }

        // A trailing partial frame would leave the two series different lengths.
        int n = Mathf.Min(times.Count, areas.Count);
        if (times.Count > n) times.RemoveRange(n, times.Count - n);
        if (areas.Count > n) areas.RemoveRange(n, areas.Count - n);
    }

    // ----------------------------------------------------------------- plateau

    public struct Plateau
    {
        public bool reached;
        public float area;
        public float time;
        public int index;
    }

    /// <summary>
    /// First moment the hull stops changing meaningfully and stays that way.
    ///
    /// Deliberately a line-for-line port of Analysis/steady_state.py find_plateau, including the
    /// default tolerances, so the target Unity stops on is the same number the analysis calls
    /// A*(R). If the two drift apart, every recorded clip ends somewhere the analysis did not
    /// predict, and nothing downstream would say so.
    ///
    /// The slope is measured over a rolling window and compared against a tolerance expressed as a
    /// fraction of the run's own area scale per second, so one setting works whether a swarm
    /// settles at 25 u2 or 900 u2.
    /// </summary>
    public static Plateau FindPlateau(float[] times, float[] areas,
                                      float slopeTolerance = 0.02f,
                                      float holdSeconds = 3f,
                                      float windowSeconds = 1f)
    {
        int n = Mathf.Min(times.Length, areas.Length);
        if (n < 3)
        {
            return new Plateau
            {
                reached = false,
                area = n > 0 ? areas[n - 1] : 0f,
                time = n > 0 ? times[n - 1] : 0f,
                index = n - 1,
            };
        }

        float dt = MedianDiff(times, n);
        if (dt <= 0f) dt = 1e-3f;

        int window = Mathf.Max(2, Mathf.RoundToInt(windowSeconds / dt));
        int holdFrames = Mathf.Max(1, Mathf.RoundToInt(holdSeconds / dt));

        float scale = 1e-6f;
        for (int i = 0; i < n; i++) scale = Mathf.Max(scale, areas[i]);
        float limit = slopeTolerance * scale;

        int runLength = 0;
        int start = -1;

        for (int i = window; i < n; i++)
        {
            // A non-positive span would divide by zero. numpy yields inf there, which fails the
            // tolerance test and breaks the run, so reset rather than skip — skipping would let a
            // settled streak survive a gap the Python version treats as a break.
            float span = times[i] - times[i - window];
            bool settled = span > 0f && Mathf.Abs((areas[i] - areas[i - window]) / span) <= limit;

            if (settled)
            {
                runLength++;
                if (runLength == 1) start = i;

                if (runLength >= holdFrames)
                {
                    return new Plateau
                    {
                        reached = true,
                        index = start,
                        time = times[start],
                        area = MedianFrom(areas, start, n),
                    };
                }
            }
            else
            {
                runLength = 0;
                start = -1;
            }
        }

        return new Plateau
        {
            reached = false,
            index = n - 1,
            time = times[n - 1],
            area = areas[n - 1],
        };
    }

    private static float MedianDiff(float[] times, int n)
    {
        if (n < 2) return 0f;

        float[] diffs = new float[n - 1];
        for (int i = 1; i < n; i++) diffs[i - 1] = times[i] - times[i - 1];
        Array.Sort(diffs);

        int mid = diffs.Length / 2;
        return diffs.Length % 2 == 1 ? diffs[mid] : 0.5f * (diffs[mid - 1] + diffs[mid]);
    }

    private static float MedianFrom(float[] values, int from, int to)
    {
        int count = to - from;
        if (count <= 0) return 0f;

        float[] slice = new float[count];
        Array.Copy(values, from, slice, 0, count);
        Array.Sort(slice);

        int mid = count / 2;
        return count % 2 == 1 ? slice[mid] : 0.5f * (slice[mid - 1] + slice[mid]);
    }

    // ----------------------------------------------------------------- cache

    private const string CacheFileName = "reference_hull_index";

    [Serializable]
    private class CacheRecord
    {
        public string path;
        public string signature;
        public bool isReference;
        public string layoutId;
        public float perceptionRadius;
        public float maxSpeed;
        public float plateauArea;
        public float plateauTime;
        public int plateauFrame;
        public float startArea;
        public bool settled;
        public string sourceFile;

        public static CacheRecord From(string signature, bool isReference, Entry entry)
        {
            CacheRecord record = new CacheRecord { signature = signature, isReference = isReference };
            if (entry == null) return record;

            record.layoutId = entry.layoutId;
            record.perceptionRadius = entry.perceptionRadius;
            record.maxSpeed = entry.maxSpeed;
            record.plateauArea = entry.plateauArea;
            record.plateauTime = entry.plateauTime;
            record.plateauFrame = entry.plateauFrame;
            record.startArea = entry.startArea;
            record.settled = entry.settled;
            record.sourceFile = entry.sourceFile;
            return record;
        }

        public Entry ToEntry(string path)
        {
            if (string.IsNullOrEmpty(layoutId)) return null;
            return new Entry
            {
                layoutId = layoutId,
                perceptionRadius = perceptionRadius,
                maxSpeed = maxSpeed,
                plateauArea = plateauArea,
                plateauTime = plateauTime,
                plateauFrame = plateauFrame,
                startArea = startArea,
                settled = settled,
                sourceFile = sourceFile ?? Path.GetFileName(path),
                sourcePath = path,
            };
        }
    }

    [Serializable]
    private class CacheFile
    {
        public int version = CacheVersion;
        public List<CacheRecord> records = new List<CacheRecord>();
    }

    private static string CachePath(string folder) => Path.Combine(folder, CacheFileName + ".json");

    private static Dictionary<string, CacheRecord> LoadCache(string folder)
    {
        Dictionary<string, CacheRecord> map = new Dictionary<string, CacheRecord>();
        string path = CachePath(folder);
        if (!File.Exists(path)) return map;

        try
        {
            CacheFile file = JsonUtility.FromJson<CacheFile>(File.ReadAllText(path));
            if (file == null || file.version != CacheVersion || file.records == null) return map;

            foreach (CacheRecord record in file.records)
            {
                if (record != null && !string.IsNullOrEmpty(record.path)) map[record.path] = record;
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[ReferenceHullIndex] Ignoring unreadable cache: {e.Message}");
        }

        return map;
    }

    private static void SaveCache(string folder, Dictionary<string, CacheRecord> records)
    {
        try
        {
            CacheFile file = new CacheFile();
            foreach (KeyValuePair<string, CacheRecord> pair in records)
            {
                pair.Value.path = pair.Key;
                file.records.Add(pair.Value);
            }
            File.WriteAllText(CachePath(folder), JsonUtility.ToJson(file));
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[ReferenceHullIndex] Could not write cache: {e.Message}");
        }
    }

    // ----------------------------------------------------------------- reporting

    /// <summary>Every indexed target, sorted, for a one-line-per-entry log at batch start.</summary>
    public string Describe(int maxLines = 12)
    {
        if (count == 0) return "no reference targets indexed";

        List<Entry> all = new List<Entry>();
        foreach (List<Entry> list in byLayout.Values) all.AddRange(list);
        all.Sort((a, b) =>
        {
            int c = string.CompareOrdinal(a.layoutId, b.layoutId);
            if (c != 0) return c;
            c = a.maxSpeed.CompareTo(b.maxSpeed);
            return c != 0 ? c : a.perceptionRadius.CompareTo(b.perceptionRadius);
        });

        StringBuilder sb = new StringBuilder();
        int unsettled = 0;

        for (int i = 0; i < all.Count; i++)
        {
            if (!all[i].settled) unsettled++;
            if (i >= maxLines) continue;

            sb.AppendLine($"   {all[i].layoutId}  R {all[i].perceptionRadius:F2}  " +
                          $"ms {all[i].maxSpeed:F1}  ->  {all[i].plateauArea:F1} u2" +
                          (all[i].settled ? "" : "  (NEVER SETTLED, lower bound)"));
        }

        if (all.Count > maxLines) sb.AppendLine($"   ... and {all.Count - maxLines} more");
        if (unsettled > 0) sb.AppendLine($"   {unsettled} reference runs never reached a plateau.");

        return sb.ToString().TrimEnd();
    }
}
