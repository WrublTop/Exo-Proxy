namespace ExoProxy.Core;

public enum BootPhaseId
{
    // ── Implemented ─────────────────────────────────────────────────────────
    CrtWarmup        = 0,  // CRT phosphor warm-up: scanline sweep, amber bloom
    BiosHeader       = 1,  // BIOS vendor banner + version block; F4 key skip
    Login            = 2,  // Operator login / account selection / registration
    QlinkHandshake   = 3,  // QLINK v2.1 uplink handshake + frequency EQ visualizer

    // ── Planned ─────────────────────────────────────────────────────────────
    HardwareDetection = 4,  // CPU, co-processor, bus, clock speed lines
    StorageDetection  = 5,  // Storage units A/B, read/write test results
    SessionLog        = 6,  // "LAST SESSION" block — SOL number, 184-day gap warning
    InstrumentInit    = 7,  // 9-instrument POST lines (radar → sample collection)
    PostComplete      = 8,  // POST summary line: warnings / faults / advisories
    FaultSummary      = 9,  // Expanded fault detail block (manipulator arm, comms)
    OsLoader          = 10, // "Booting EXO-OS from STORAGE UNIT A..." + progress bar
    DosShell          = 11, // DOS-style shell: C:\SR74> DIR listing (main menu)
    CreditsView       = 12, // Full-screen credits (pushed, not replaced)
    ShutdownSequence  = 13, // "SHUTDOWN /H" — power-off animation, then exit
}
