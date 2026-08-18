namespace MeetingTranscriber.Presentation;

/// <summary>
/// Everything the application says, Spanish first and English second. A screen names an entry
/// here; it never carries the words itself, which is what makes translating a screen a matter of
/// reading one file rather than of finding every literal in it.
/// </summary>
/// <remarks>
/// A catalogue in C# rather than in `.resw`: a name that does not exist is a compile error
/// instead of a blank on screen, both languages sit on the same line so one cannot quietly be
/// added without the other, and none of it needs a packaged host to be read — which is what lets
/// a plain test walk the whole catalogue. The section below is the temporary scaffold's, and it
/// goes when the scaffold does.
/// </remarks>
public static class UiTexts
{
    // ── The packaging checks scaffold ──────────────────────────────────────────────────────────

    public static UiText PackagingChecks { get; } =
        new("Comprobaciones de empaquetado", "Packaging checks");

    public static UiText PackagingChecksAreScaffolding { get; } = new(
        "Andamio temporal: se borra cuando estas dos comprobaciones dejen de hacer falta.",
        "Temporary scaffold: it goes when these two checks stop being needed.");

    public static UiText Language { get; } = new("Idioma", "Language");

    // The two entries below say the same thing in both languages, and that is the claim rather
    // than a translation nobody got to. Somebody who opened the application in a language they
    // cannot read is looking for the one word on screen they do recognise, so a picker that
    // translated the names would hide the way back out. Here rather than in a `switch` so the
    // walk over the catalogue sees them like every other word a person reads.
    public static UiText SpanishName { get; } = new("Español", "Español");

    public static UiText EnglishName { get; } = new("English", "English");

    public static UiText Yes { get; } = new("sí", "yes");

    public static UiText No { get; } = new("no", "no");

    // No room for the exception: what a runtime throws is written in whatever language Windows
    // is installed in, and putting it inside the sentence would make the sentence half-translated
    // for good. It goes on the line below as the data it is.
    public static UiText Failed { get; } = new("FALLÓ:", "FAILED:");

    public static UiText LanguageNotRemembered { get; } = new(
        "El idioma cambió, pero no se pudo recordar la elección: la próxima vez la aplicación "
        + "abrirá en el idioma de Windows.",
        "The language changed, but the choice could not be remembered: next time the application "
        + "will open in Windows' language.");

    public static UiText Package { get; } = new("Paquete: {0}", "Package: {0}");

    public static UiText InstalledAt { get; } = new("Instalado en: {0}", "Installed at: {0}");

    public static UiText Process { get; } = new("Proceso: {0}", "Process: {0}");

    public static UiText SaveReport { get; } = new("Guardar informe", "Save report");

    public static UiText ReportSavedAt { get; } = new("Informe en {0}", "Report at {0}");

    public static UiText ReportNotSaved { get; } =
        new("No se pudo guardar: {0}", "Could not save: {0}");

    // ── Check 1: what a child process inherits ────────────────────────────────────────────────

    public static UiText ChildEnvironmentCheck { get; } =
        new("1 · Entorno del proceso hijo", "1 · Child process environment");

    public static UiText ChildEnvironmentHeading { get; } =
        new("1 · ENTORNO DEL PROCESO HIJO", "1 · CHILD PROCESS ENVIRONMENT");

    public static UiText VariablesInTheApp { get; } =
        new("Variables en la app: {0}", "Variables in the app: {0}");

    public static UiText VariablesInTheChild { get; } =
        new("Variables en el hijo: {0}", "Variables in the child: {0}");

    public static UiText OnlyInTheChild { get; } =
        new("Sólo en el hijo ({0}):", "Only in the child ({0}):");

    public static UiText OnlyInTheApp { get; } =
        new("Sólo en la app ({0}):", "Only in the app ({0}):");

    public static UiText TheChildEnvironmentInFull { get; } =
        new("Entorno completo del hijo:", "The child's environment in full:");

    public static UiText ChildWriteToLocalAppData { get; } = new(
        "Escritura del hijo en %LOCALAPPDATA%:",
        "The child's write to %LOCALAPPDATA%:");

    public static UiText StampWritten { get; } = new("  sello escrito: {0}", "  stamp written: {0}");

    public static UiText InTheContainer { get; } =
        new("  en el contenedor: {0}  {1}", "  in the container: {0}  {1}");

    public static UiText ChildInheritsTheRedirection { get; } = new(
        "  => el hijo HEREDA la redirección: su %LOCALAPPDATA% no es el del usuario.",
        "  => the child INHERITS the redirection: its %LOCALAPPDATA% is not the user's.");

    public static UiText ChildWritesOutsideTheContainer { get; } = new(
        "  => el hijo escribe FUERA del contenedor. Confirmar a mano en {0}",
        "  => the child writes OUTSIDE the container. Confirm by hand at {0}");

    public static UiText CompareTheEnvironmentByHand { get; } = new(
        "PASO MANUAL: correr `cmd /c set` desde el Explorador y comparar contra el bloque de "
        + "arriba. La diferencia es lo que inyecta o tapa el contenedor.",
        "BY HAND: run `cmd /c set` from Explorer and compare it against the block above. The "
        + "difference is what the container injects or hides.");

    public static UiText ChildEnvironmentCheckDone { get; } =
        new("Comprobación 1 lista.", "Check 1 done.");

    public static UiText ChildEnvironmentCheckFailed { get; } =
        new("La comprobación 1 falló.", "Check 1 failed.");

    // ── Check 2: where a write actually lands ─────────────────────────────────────────────────

    public static UiText WritePathCheck { get; } = new("2 · Ruta de escritura", "2 · Write path");

    public static UiText WritePathHeading { get; } = new("2 · RUTA DE ESCRITURA", "2 · WRITE PATH");

    public static UiText NoFolderWasChosen { get; } =
        new("Cancelado: no se eligió carpeta.", "Cancelled: no folder was chosen.");

    public static UiText ChosenPath { get; } = new("Ruta elegida: {0}", "Chosen path: {0}");

    public static UiText ExistsThere { get; } = new("Existe ahí: {0}", "Exists there: {0}");

    public static UiText ContentsReadBack { get; } =
        new("Contenido leído: {0}", "Contents read back: {0}");

    public static UiText ContentsMatch { get; } = new("Coincide: {0}", "Matches: {0}");

    public static UiText LocalAppDataResolvedTo { get; } =
        new("LOCALAPPDATA resuelto a: {0}", "LOCALAPPDATA resolved to: {0}");

    public static UiText WrittenAt { get; } = new("Escrito en: {0}", "Written at: {0}");

    public static UiText PackageRoot { get; } = new("Raíz del paquete: {0}", "Package root: {0}");

    public static UiText PackageRootExists { get; } =
        new("Existe la raíz: {0}", "The root exists: {0}");

    public static UiText RedirectedCopy { get; } =
        new("  copia redirigida: {0}", "  redirected copy: {0}");

    public static UiText CheckTheWritePathByHand { get; } = new(
        "PASO MANUAL: abrir {0} en el Explorador y confirmar que {1} está ahí de verdad y no "
        + "sólo desde adentro de la app.",
        "BY HAND: open {0} in Explorer and confirm {1} is really there and not only from inside "
        + "the app.");

    public static UiText WritePathCheckDone { get; } =
        new("Comprobación 2 lista.", "Check 2 done.");

    public static UiText WritePathCheckCancelled { get; } =
        new("Comprobación 2 cancelada.", "Check 2 cancelled.");

    public static UiText WritePathCheckFailed { get; } =
        new("La comprobación 2 falló.", "Check 2 failed.");
}
