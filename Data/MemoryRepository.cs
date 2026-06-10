using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace ExoProxy.Data;

public class MemoryRepository
{
    public const int RoverCapacity = 64;
    public const int LocalCapacity = 64;

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

    public void Load(string operatorLogin)
    {
        _statePath = Path.Combine(AppContext.BaseDirectory, "SaveData",
            $"memory_state_{operatorLogin.ToUpper()}.yaml");

        if (File.Exists(_contentPath))
        {
            try { _allFiles = _deserializer.Deserialize<List<MemoryFile>>(File.ReadAllText(_contentPath)) ?? []; }
            catch { _allFiles = []; }
        }

        if (File.Exists(_statePath))
        {
            try { _state = _deserializer.Deserialize<MemoryStorageState>(File.ReadAllText(_statePath)) ?? new(); }
            catch { _state = new(); }
        }
        else
        {
            InitDefaultState();
            Save();
        }

        _state.RoverBlocks = PadLayout(_state.RoverBlocks, RoverCapacity);
        _state.LocalBlocks = PadLayout(_state.LocalBlocks, LocalCapacity);
    }

    private void InitDefaultState()
    {
        _state = new MemoryStorageState
        {
            // em_burst_042 (4 blocks): 0,1,2 then 8 — FRAGMENTED
            // field_log_003 (2 blocks): 3,4
            // free: 5
            // chem_probe_atm (2 blocks): 6,7
            // em_burst_042 cont. (1 of 4): 8
            // thermal_031 (4 blocks): 9,10,11,12
            // free: 13,14,15
            RoverBlocks =
            [
                "em_burst_042", "em_burst_042", "em_burst_042",
                "field_log_003", "field_log_003",
                "",
                "chem_probe_atm", "chem_probe_atm",
                "em_burst_042",
                "thermal_031", "thermal_031", "thermal_031", "thermal_031",
                "", "", ""
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

    // ── queries ───────────────────────────────────────────────────────────────

    public MemoryFile? GetFile(string id) =>
        _allFiles.FirstOrDefault(f => f.Id == id);

    public List<string?> GetLayout(string location)
    {
        var raw = location == "rover" ? _state.RoverBlocks : _state.LocalBlocks;
        return raw.Select(x => string.IsNullOrEmpty(x) ? (string?)null : x).ToList();
    }

    public List<MemoryFile> GetFilesAt(string location)
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

    public int GetUsed(string location)  => GetLayout(location).Count(x => x != null);
    public int GetCapacity(string location) => location == "rover" ? RoverCapacity : LocalCapacity;

    public int GetFragmentPercent(string location)
    {
        var layout  = GetLayout(location);
        var ids     = layout.Where(x => x != null).Distinct().ToList();
        if (ids.Count == 0) return 0;
        int frag    = ids.Count(id => IsFragmented(id!, layout));
        return frag * 100 / ids.Count;
    }

    // ── mutations ─────────────────────────────────────────────────────────────

    // Move file from rover to local station (removes from rover).
    public bool MoveToLocal(string fileId)
    {
        var file = GetFile(fileId);
        if (file == null) return false;
        if (!_state.RoverBlocks.Any(x => x == fileId)) return false;
        if (_state.LocalBlocks.Any(x => x == fileId))  return false;

        int free = _state.LocalBlocks.Count(x => string.IsNullOrEmpty(x));
        if (free < file.Blocks) return false;

        // Remove from rover
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
        return true;
    }

    // Move file from local to SUIRDC uplink (removes from local).
    public bool SyncToSuirdc(string fileId)
    {
        if (!_state.LocalBlocks.Any(x => x == fileId)) return false;
        if (_state.SuirdcFiles.Contains(fileId))       return false;

        for (int i = 0; i < _state.LocalBlocks.Count; i++)
            if (_state.LocalBlocks[i] == fileId) _state.LocalBlocks[i] = "";

        _state.SuirdcFiles.Add(fileId);
        Save();
        return true;
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

    public List<string?> GetDefragTarget(string location)
    {
        var layout   = GetLayout(location);
        var occupied = layout.Where(x => x != null).ToList();
        var free     = Enumerable.Repeat<string?>(null, layout.Count - occupied.Count).ToList();
        return [.. occupied, .. free];
    }

    public void ApplyLayout(string location, List<string?> layout)
    {
        var raw = layout.Select(x => x ?? "").ToList();
        if (location == "rover") _state.RoverBlocks = raw;
        else                     _state.LocalBlocks  = raw;
        Save();
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_statePath)!);
        File.WriteAllText(_statePath, _serializer.Serialize(_state));
    }
}
