using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Microsoft.UI.Xaml;

using Windows.ApplicationModel;
using Windows.Storage.Pickers;

namespace MeetingTranscriber.App;

/// <summary>
/// Andamio temporal para las dos comprobaciones de empaquetado del paso 4.
/// Se borra cuando exista MeetingTranscriber.Cli.
/// </summary>
public sealed partial class MainWindow : Window
{
    private const string ProbeFileName = "meeting-transcriber-probe.txt";

    private readonly StringBuilder _report = new();

    public MainWindow()
    {
        InitializeComponent();
        Write($"Paquete: {Package.Current.Id.FamilyName}");
        Write($"Instalado en: {Package.Current.InstalledLocation.Path}");
        Write($"Proceso: {Environment.ProcessPath}");
    }

    /// <summary>
    /// Comprobacion 1: que entorno hereda un proceso hijo lanzado desde el paquete.
    /// La fase 5 necesita lanzar Claude Code con el entorno saneado, asi que hay que
    /// saber que le llega y que no.
    /// </summary>
    private async void OnRunEnvironmentProbe(object sender, RoutedEventArgs e)
    {
        EnvironmentButton.IsEnabled = false;
        try
        {
            Section("1 · ENTORNO DEL PROCESO HIJO");

            var child = await CaptureChildEnvironmentAsync();
            var parent = Environment.GetEnvironmentVariables()
                .Cast<System.Collections.DictionaryEntry>()
                .ToDictionary(entry => (string)entry.Key, entry => (string?)entry.Value ?? string.Empty,
                              StringComparer.OrdinalIgnoreCase);

            Write($"Variables en la app: {parent.Count}");
            Write($"Variables en el hijo: {child.Count}");

            var onlyInChild = child.Keys.Except(parent.Keys, StringComparer.OrdinalIgnoreCase).Order().ToArray();
            var onlyInParent = parent.Keys.Except(child.Keys, StringComparer.OrdinalIgnoreCase).Order().ToArray();

            Write(string.Empty);
            Write($"Solo en el hijo ({onlyInChild.Length}):");
            foreach (var key in onlyInChild)
            {
                Write($"  {key}={child[key]}");
            }

            Write(string.Empty);
            Write($"Solo en la app ({onlyInParent.Length}):");
            foreach (var key in onlyInParent)
            {
                Write($"  {key}={parent[key]}");
            }

            Write(string.Empty);
            Write("Entorno completo del hijo:");
            foreach (var (key, value) in child.OrderBy(pair => pair.Key))
            {
                Write($"  {key}={value}");
            }

            await ReportChildRedirectionAsync();

            Write(string.Empty);
            Write("PASO MANUAL: correr `cmd /c set` desde el Explorador y comparar contra");
            Write("el bloque de arriba. La diferencia es lo que inyecta o tapa el contenedor.");

            Status("Comprobacion 1 lista.");
        }
        catch (Exception ex)
        {
            Write($"FALLO: {ex}");
            Status("Comprobacion 1 fallo.");
        }
        finally
        {
            EnvironmentButton.IsEnabled = true;
        }
    }

    /// <summary>
    /// Comprobacion 2: si una ruta de escritura elegida por el usuario queda redirigida
    /// por la virtualizacion de filesystem del contenedor.
    /// </summary>
    private async void OnRunFileSystemProbe(object sender, RoutedEventArgs e)
    {
        FileSystemButton.IsEnabled = false;
        try
        {
            Section("2 · RUTA DE ESCRITURA");

            var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
            picker.FileTypeFilter.Add("*");
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var folder = await picker.PickSingleFolderAsync();
            if (folder is null)
            {
                Write("Cancelado: no se eligio carpeta.");
                Status("Comprobacion 2 cancelada.");
                return;
            }

            var chosen = Path.Combine(folder.Path, ProbeFileName);
            var stamp = Guid.NewGuid().ToString();
            await File.WriteAllTextAsync(chosen, stamp);

            Write($"Ruta elegida:   {chosen}");
            Write($"Existe ahi:     {File.Exists(chosen)}");
            Write($"Contenido leido: {(File.Exists(chosen) ? await File.ReadAllTextAsync(chosen) : "-")}");
            Write($"Coincide:       {File.Exists(chosen) && await File.ReadAllTextAsync(chosen) == stamp}");

            // El caso clasico de redireccion: una escritura directa a LOCALAPPDATA.
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var appDataProbe = Path.Combine(localAppData, ProbeFileName);
            await File.WriteAllTextAsync(appDataProbe, stamp);

            Write(string.Empty);
            Write($"LOCALAPPDATA resuelto a: {localAppData}");
            Write($"Escrito en:              {appDataProbe}");

            var packageRoot = Path.Combine(
                Path.GetDirectoryName(localAppData.TrimEnd(Path.DirectorySeparatorChar))!,
                "Local", "Packages", Package.Current.Id.FamilyName);
            Write($"Raiz del paquete:        {packageRoot}");
            Write($"Existe la raiz:          {Directory.Exists(packageRoot)}");

            foreach (var shadow in FindShadows(packageRoot))
            {
                Write($"  copia redirigida: {shadow}");
            }

            Write(string.Empty);
            Write($"PASO MANUAL: abrir {folder.Path} en el Explorador y confirmar que");
            Write($"{ProbeFileName} esta ahi de verdad y no solo desde adentro de la app.");

            Status("Comprobacion 2 lista.");
        }
        catch (Exception ex)
        {
            Write($"FALLO: {ex}");
            Status("Comprobacion 2 fallo.");
        }
        finally
        {
            FileSystemButton.IsEnabled = true;
        }
    }

    private async void OnSaveReport(object sender, RoutedEventArgs e)
    {
        try
        {
            var target = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                "comprobaciones-empaquetado.txt");
            await File.WriteAllTextAsync(target, _report.ToString());
            Status($"Informe en {target}");
        }
        catch (Exception ex)
        {
            Status($"No se pudo guardar: {ex.Message}");
        }
    }

    /// <summary>
    /// Heredar el entorno no es lo mismo que heredar el contenedor. El hijo ve
    /// LOCALAPPDATA apuntando a la ruta real, pero si sus escrituras se redirigen igual
    /// que las de la app, cualquier cosa que Claude Code deje ahi muere al desinstalar.
    /// </summary>
    private async Task ReportChildRedirectionAsync()
    {
        const string ChildProbeFileName = "meeting-transcriber-child-probe.txt";

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var stamp = Guid.NewGuid().ToString();

        var startInfo = new ProcessStartInfo("cmd.exe", $"/c echo {stamp}> \"%LOCALAPPDATA%\\{ChildProbeFileName}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using (var child = Process.Start(startInfo) ?? throw new InvalidOperationException("cmd.exe no arranco."))
        {
            await child.WaitForExitAsync();
        }

        var containerPath = Path.Combine(
            Path.GetDirectoryName(localAppData.TrimEnd(Path.DirectorySeparatorChar))!,
            "Local", "Packages", Package.Current.Id.FamilyName, "LocalCache", "Local", ChildProbeFileName);

        Write(string.Empty);
        Write("Escritura del hijo en %LOCALAPPDATA%:");
        Write($"  sello escrito:        {stamp}");
        Write($"  en el contenedor:     {File.Exists(containerPath)}  {containerPath}");
        Write(File.Exists(containerPath)
            ? "  => el hijo HEREDA la redireccion: su %LOCALAPPDATA% no es el del usuario."
            : $"  => el hijo escribe FUERA del contenedor. Confirmar a mano en {Path.Combine(localAppData, ChildProbeFileName)}");
    }

    private static async Task<Dictionary<string, string>> CaptureChildEnvironmentAsync()
    {
        var startInfo = new ProcessStartInfo("cmd.exe", "/c set")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var child = Process.Start(startInfo)
            ?? throw new InvalidOperationException("cmd.exe no arranco.");

        var output = await child.StandardOutput.ReadToEndAsync();
        await child.WaitForExitAsync();

        return output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.TrimEnd('\r'))
            .Select(line => line.Split('=', 2))
            .Where(parts => parts.Length == 2)
            .ToDictionary(parts => parts[0], parts => parts[1], StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> FindShadows(string packageRoot)
    {
        if (!Directory.Exists(packageRoot))
        {
            yield break;
        }

        IEnumerable<string> matches;
        try
        {
            matches = Directory.EnumerateFiles(packageRoot, ProbeFileName, SearchOption.AllDirectories);
        }
        catch (UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (var match in matches)
        {
            yield return match;
        }
    }

    private void Section(string title)
    {
        Write(string.Empty);
        Write(new string('=', title.Length));
        Write(title);
        Write(new string('=', title.Length));
    }

    private void Write(string line)
    {
        _report.AppendLine(line);
        OutputText.Text = _report.ToString();
    }

    private void Status(string message) => StatusText.Text = message;
}
