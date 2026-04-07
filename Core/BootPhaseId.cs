namespace ExoProxy.Core;

public enum BootPhaseId
{
    // ── Boot sequence (non-interactive presentation layer) ──────────────────
    CrtWarmup = 0,   // CRT phosphor warm-up: scanline sweep, amber bloom
    BiosHeader = 1,   // BIOS vendor banner + version block; DEL key intercept here
    MemoryCount = 2,   // RAM count-up (64 KB steps)
    HardwareDetection = 3,   // CPU, co-processor, bus, clock speed lines
    StorageDetection = 4,   // Storage units A/B, read/write test results
    SessionLog = 5,   // "LAST SESSION" block — SOL number, 184-day gap warning
    InstrumentInit = 6,   // 9-instrument POST lines (radar → sample collection)
    PostComplete = 7,   // POST summary line: warnings / faults / advisories
    FaultSummary = 8,   // Expanded fault detail block (manipulator arm, comms)
    OsLoader = 9,   // "Booting EXO-OS from STORAGE UNIT A..." + progress bar
    DosShell = 10,  // DOS-style shell: C:\SR74> DIR listing (main menu)
    CreditsView = 11,  // Full-screen credits (pushed, not replaced)
    ShutdownSequence = 12,  // "SHUTDOWN /H" — power-off animation, then exit
}