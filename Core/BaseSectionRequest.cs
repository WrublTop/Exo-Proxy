namespace ExoProxy.Core;

public enum BaseSectionRequest
{
    Stay,
    GoToSection,
    GoToHub,
    Logout,     // end operator session → back to boot/login
    ExitGame    // shut the terminal down entirely
}
