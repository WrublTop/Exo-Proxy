using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace ExoProxy.Data;

public class MemoryRepository
{
    public const int RoverCapacity  = 32;
    public const int LocalCapacity  = 32;
    public const int SuirdcCapacity = 16;

    private static readonly string _contentPath =
        Path.Combine(AppContext.BaseDirectory, "Content", "memory_files.yaml");

    private string _statePath = "";

    private static readonly IDeserializer _deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private static readonly ISerializer _serializer = new SerializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .Build();

    private List<MemoryFile>   _allFiles = [];
    private MemoryStorageState _state    = new();

    // Set when a file existed but could not be parsed.
    public string? LoadWarning { get; private set; }

    public void Load(string operatorLogin)
    {
        _statePath = Path.Combine(AppContext.BaseDirectory, "SaveData",
            $"memory_state_{operatorLogin.ToUpper()}.yaml");

        if (File.Exists(_contentPath))
        {
            try { _allFiles = _deserializer.Deserialize<List<MemoryFile>>(File.ReadAllText(_contentPath)) ?? []; }
            catch
            {
                // Content file is shipped data, not a save — never quarantined.
                _allFiles = [];
                LoadWarning = "TAPE INDEX UNREADABLE — CONTACT SUIRDC MAINTENANCE";
            }
        }

        if (File.Exists(_statePath))
        {
            try { _state = _deserializer.Deserialize<MemoryStorageState>(File.ReadAllText(_statePath)) ?? new(); }
            catch
            {
                SaveGuard.Quarantine(_statePath);
                InitDefaultState();
                Save();
                LoadWarning = "MEMORY CHECKSUM FAILURE — STATE RESTORED FROM DEFAULTS";
            }
        }
        else
        {
            InitDefaultState();
            Save();
        }

        _state.RoverBlocks = PadLayout(_state.RoverBlocks, RoverCapacity);
        _state.LocalBlocks = PadLayout(_state.LocalBlocks, LocalCapacity);
        SanitizeState();
    }

    private void InitDefaultState()
    {
        _state = new MemoryStorageState
        {
            // Pre-loaded before the first mission. hng_seeker_hull_diag is left
            // FRAGMENTED (split off past the power log) so DEFRAG has work fresh.
            // field_log_003 (2): 0,1   hull_diag (4): 2,3,4 ... 8   power_drain (3): 5,6,7
            RoverBlocks =
            [
                "field_log_003", "field_log_003",
                "hng_seeker_hull_diag", "hng_seeker_hull_diag", "hng_seeker_hull_diag",
                "hng_power_drain_scan", "hng_power_drain_scan", "hng_power_drain_scan",
                "hng_seeker_hull_diag",
            ],
            LocalBlocks = [],
            SuirdcFiles = []
        };
    }

    private static List<string> PadLayout(List<string> layout, int capacity)
    {
        while (layout.Count < capacity) layout.Add("");
        if (layout.Count > capacity) layout = layout[..capacity];
        return layout;
    }

    // Drops unknown file ids and files whose block count no longer matches the
    // catalogue — a partial file lies about its size.
    private void SanitizeState()
    {
        SanitizeLayout(_state.RoverBlocks);
        SanitizeLayout(_state.LocalBlocks);
        _state.SuirdcFiles.RemoveAll(id => GetFile(id) == null);
    }

    private void SanitizeLayout(List<string> layout)
    {
        foreach (var id in layout.Where(x => !string.IsNullOrEmpty(x)).Distinct().ToList())
        {
            var file = GetFile(id);
            bool valid = file != null && layout.Count(x => x == id) == file.Blocks;
            if (valid) continue;
            for (int i = 0; i < layout.Count; i++)
                if (layout[i] == id) layout[i] = "";
        }
    }

    // ── queries ───────────────────────────────────────────────────────────────

    public MemoryFile? GetFile(string id) =>
        _allFiles.FirstOrDefault(f => f.Id == id);

    // Evidence checks for COMMS gating: held on any tape / synced to SUIRDC.
    public bool HasFileAnywhere(string fileId) =>
        _state.RoverBlocks.Contains(fileId) ||
        _state.LocalBlocks.Contains(fileId) ||
        _state.SuirdcFiles.Contains(fileId);

    public bool IsSynced(string fileId) =>
        _state.SuirdcFiles.Contains(fileId);

    public List<string?> GetLayout(StorageLocation location)
    {
        var raw = location switch
        {
            StorageLocation.Rover => _state.RoverBlocks,
            StorageLocation.Local => _state.LocalBlocks,
            _                     => [],   // SUIRDC is not block-addressed
        };
        return raw.Select(x => string.IsNullOrEmpty(x) ? (string?)null : x).ToList();
    }

    public List<MemoryFile> GetFilesAt(StorageLocation location)
    {
        var layout = GetLayout(location);
        var seen   = new HashSet<string>();
        var result = new List<MemoryFile>();
        foreach (var id in layout)
        {
            if (id != null && seen.Add(id))
            {
                var f = GetFile(id);
                if (f != null) result.Add(f);
            }
        }
        return result;
    }

    public List<MemoryFile> GetSuirdcFiles() =>
        _state.SuirdcFiles
              .Select(id => GetFile(id))
              .Where(f => f != null)
              .Select(f => f!)
              .ToList();

    public int GetSuirdcUsed() => GetSuirdcFiles().Sum(f => f.Blocks);

    public bool IsFragmented(string fileId, List<string?> layout)
    {
        var positions = layout.Select((id, i) => (id, i))
                              .Where(x => x.id == fileId)
                              .Select(x => x.i)
                              .ToList();
        for (int i = 1; i < positions.Count; i++)
            if (positions[i] != positions[i - 1] + 1) return true;
        return false;
    }

    public int GetUsed(StorageLocation location) => GetLayout(location).Count(x => x != null);

    public int GetCapacity(StorageLocation location) => location switch
    {
        StorageLocation.Rover => RoverCapacity,
        StorageLocation.Local => LocalCapacity,
        _                     => SuirdcCapacity,
    };

    public int GetFragmentPercent(StorageLocation location)
    {
        var layout  = GetLayout(location);
        var ids     = layout.Where(x => x != null).Distinct().ToList();
        if (ids.Count == 0) return 0;
        int frag    = ids.Count(id => IsFragmented(id!, layout));
        return frag * 100 / ids.Count;
    }

    // ── mutations ─────────────────────────────────────────────────────────────

    // Move file from rover to local station (removes from rover).
    public TransferResult MoveToLocal(string fileId)
    {
        var file = GetFile(fileId);
        if (file == null)                              return TransferResult.SourceMissing;
        // AlreadyStored before SourceMissing — a re-moved file is absent from
        // the source precisely because it already sits at the destination.
        if (_state.LocalBlocks.Any(x => x == fileId))  return TransferResult.AlreadyStored;
        if (!_state.RoverBlocks.Any(x => x == fileId)) return TransferResult.SourceMissing;

        int free = _state.LocalBlocks.Count(x => string.IsNullOrEmpty(x));
        if (free < file.Blocks) return TransferResult.InsufficientSpace;

        for (int i = 0; i < _state.RoverBlocks.Count; i++)
            if (_state.RoverBlocks[i] == fileId) _state.RoverBlocks[i] = "";

        // Place in local — contiguous first, scatter fallback
        bool placed = false;
        for (int i = 0; i <= _state.LocalBlocks.Count - file.Blocks && !placed; i++)
        {
            bool fits = true;
            for (int j = 0; j < file.Blocks; j++)
                if (!string.IsNullOrEmpty(_state.LocalBlocks[i + j])) { fits = false; break; }
            if (fits)
            {
                for (int j = 0; j < file.Blocks; j++)
                    _state.LocalBlocks[i + j] = fileId;
                placed = true;
            }
        }
        if (!placed)
        {
            int rem = file.Blocks;
            for (int i = 0; i < _state.LocalBlocks.Count && rem > 0; i++)
                if (string.IsNullOrEmpty(_state.LocalBlocks[i])) { _state.LocalBlocks[i] = fileId; rem--; }
        }

        Save();
        return TransferResult.Ok;
    }

    // Move file from local to SUIRDC custody (removes from local, irreversible).
    public TransferResult SyncToSuirdc(string fileId)
    {
        var file = GetFile(fileId);
        if (file == null)                              return TransferResult.SourceMissing;
        if (_state.SuirdcFiles.Contains(fileId))       return TransferResult.AlreadyStored;
        if (!_state.LocalBlocks.Any(x => x == fileId)) return TransferResult.SourceMissing;
        if (GetSuirdcUsed() + file.Blocks > SuirdcCapacity)
            return TransferResult.InsufficientSpace;

        for (int i = 0; i < _state.LocalBlocks.Count; i++)
            if (_state.LocalBlocks[i] == fileId) _state.LocalBlocks[i] = "";

        _state.SuirdcFiles.Add(fileId);
        Save();
        return TransferResult.Ok;
    }

    public bool DeleteFromRover(string fileId)
    {
        bool changed = false;
        for (int i = 0; i < _state.RoverBlocks.Count; i++)
            if (_state.RoverBlocks[i] == fileId) { _state.RoverBlocks[i] = ""; changed = true; }
        if (changed) Save();
        return changed;
    }

    public bool DeleteFromLocal(string fileId)
    {
        bool changed = false;
        for (int i = 0; i < _state.LocalBlocks.Count; i++)
            if (_state.LocalBlocks[i] == fileId) { _state.LocalBlocks[i] = ""; changed = true; }
        if (changed) Save();
        return changed;
    }

    // Defrag target: blocks grouped per file (first-appearance order), then free
    // space. Plain left-compaction isn't enough — it preserves interleavings.
    public List<string?> GetDefragTarget(StorageLocation location)
    {
        var layout = GetLayout(location);
        var result = new List<string?>(layout.Count);
        foreach (var id in layout.Where(x => x != null).Distinct())
            result.AddRange(Enumerable.Repeat(id, layout.Count(x => x == id)));
        while (result.Count < layout.Count) result.Add(null);
        return result;
    }

    public void ApplyLayout(StorageLocation location, List<string?> layout)
    {
        var raw = layout.Select(x => x ?? "").ToList();
        if (location == StorageLocation.Rover) _state.RoverBlocks = raw;
        else                                   _state.LocalBlocks = raw;
        Save();
    }

    // Registers a runtime-generated file (e.g. a proks assay) into the in-memory
    // catalogue only — never written back to Content/memory_files.yaml.
    public void RegisterDynamicFile(MemoryFile file)
    {
        if (GetFile(file.Id) is null) _allFiles.Add(file);
    }

    // Adds a collected file onto the rover tape. True if it ends up there (incl.
    // already present); false only when there's no room, so the caller can report it.
    public bool AddFileToRover(string fileId)
    {
        var file = GetFile(fileId);
        if (file == null) return false;
        if (_state.RoverBlocks.Contains(fileId)) return true;

        int free = _state.RoverBlocks.Count(x => string.IsNullOrEmpty(x));
        if (free < file.Blocks) return false;

        int remaining = file.Blocks;
        for (int i = 0; i < _state.RoverBlocks.Count && remaining > 0; i++)
            if (string.IsNullOrEmpty(_state.RoverBlocks[i])) { _state.RoverBlocks[i] = fileId; remaining--; }

        Save();
        return true;
    }

    // Flush the rover tape (Local/SUIRDC are safe) — the volatile field buffer,
    // lost on the next undock if not offloaded. Returns whether anything was there.
    public bool WipeRover()
    {
        bool any = _state.RoverBlocks.Any(b => !string.IsNullOrEmpty(b));
        for (int i = 0; i < _state.RoverBlocks.Count; i++) _state.RoverBlocks[i] = "";
        if (any) Save();
        return any;
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_statePath)!);
        File.WriteAllText(_statePath, _serializer.Serialize(_state));
    }
}
