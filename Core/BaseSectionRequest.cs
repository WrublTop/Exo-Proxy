namespace ExoProxy.Core;

public enum BaseSectionRequest
{
    Stay,
    GoToSection,
    GoToHub,
    Dock,       // rover docked at base → advance SOL and return to the hub
    Perish,     // rover lost in the field → operator permadeath, back to login
    Logout,     // end operator session → back to boot/login
    ExitGame    // shut the terminal down entirely
}
