using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using VibeScreen.App.Localization;
using VibeScreen.App.Theme;
using VibeScreen.App.Updates;
using VibeScreen.Core.Updates;

namespace VibeScreen.App.Views;

/// <summary>
/// La tarjeta «Actualizaciones» de Ajustes: qué versión hay, si hay otra, y qué hacer con ella.
/// </summary>
/// <remarks>
/// Lo que cambia con el estado —la pastilla, la última comprobación, los botones, el error— se rehace
/// entero en cada cambio; el selector del modo no, para que no se le vaya el ratón de encima al
/// elegir. Instalar siempre pasa por un botón y una pregunta: ningún modo instala solo.
/// </remarks>
public sealed class UpdatesCard : UserControl
{
    private readonly AppSettings _settings;
    private readonly Updater _updater;
    private readonly Action _save;

    private readonly ContentControl _flag = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _checked;
    private readonly TextBlock _modeHelp = Blocks.Help(Text.Empty);
    private readonly StackPanel _live = new() { Spacing = 12 };

    /// <param name="save">Guarda los ajustes después de cambiar el modo.</param>
    public UpdatesCard(AppSettings settings, Updater updater, Action save)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(updater);
        ArgumentNullException.ThrowIfNull(save);
        _settings = settings;
        _updater = updater;
        _save = save;

        var mode = new Segmented<UpdateMode>(
            [
                new(UpdateMode.Notify, Loc.L("update.mode.notify")),
                new(UpdateMode.Manual, Loc.L("update.mode.manual")),
                new(UpdateMode.Off, Loc.L("update.mode.off")),
            ],
            settings.Updates,
            SetMode);

        var installed = Blocks.Stat(Loc.L("update.installed"),
            Text.Raw(BuildInfo.Version.Length == 0 ? "—" : "v" + BuildInfo.Version));
        var (checkedStat, checkedValue, _) = Blocks.LiveStat(Loc.L("update.checked"), data: false);
        _checked = checkedValue;

        var stats = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 16 };
        Grid.SetColumn(checkedStat, 1);
        stats.Children.Add(installed);
        stats.Children.Add(checkedStat);

        Content = Blocks.Card(Loc.L("update.title"), _flag,
            stats,
            Blocks.Field(Loc.L("update.mode"), mode, null),
            _modeHelp,
            _live);

        Refresh();
    }

    // Ajustes rehace su formulario entero al cambiar de tema o volver a fábrica, y con él esta
    // tarjeta: la de antes deja de escuchar en cuanto sale del árbol, o se quedaría colgada del
    // actualizador para siempre.
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _updater.Changed += Refresh;
        Refresh();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _updater.Changed -= Refresh;
        base.OnDetachedFromVisualTree(e);
    }

    private void SetMode(UpdateMode mode)
    {
        _settings.Updates = mode;
        _save();
        _updater.ModeChanged();

        // Pasar a avisar es pedir que mire: no tiene sentido esperar a mañana para saber si ya hay una.
        if (mode == UpdateMode.Notify) _ = _updater.CheckAsync();
        Refresh();
    }

    private void Refresh()
    {
        var state = _updater.State;
        bool off = _settings.Updates == UpdateMode.Off;

        Loc.Set(_modeHelp, TextBlock.TextProperty,
            !Updater.Supported ? Loc.L("update.mode.dev")
            : _settings.Updates switch
            {
                UpdateMode.Manual => Loc.L("update.mode.manual.help"),
                UpdateMode.Off => Loc.L("update.mode.off.help"),
                _ => Loc.L("update.mode.notify.help"),
            });

        Loc.Set(_checked, TextBlock.TextProperty, Checked(state.CheckedAt));
        _flag.Content = off || !Updater.Supported ? null : Flag(state);

        // Vacía y a la vista, la tarjeta le sumaría su separación por debajo y quedaría un hueco.
        _live.Children.Clear();
        _live.IsVisible = !off && Updater.Supported;
        if (!_live.IsVisible) return;

        if (Failure(state) is { } failure)
        {
            var said = Blocks.Help(failure);
            said.Foreground = Palette.BadBrush;
            _live.Children.Add(said);
        }

        var buttons = new List<Control>();
        if (state.Release is { } release && Install(state, release) is { } install) buttons.Add(install);
        if (state.Release is { Page: { } page })
            buttons.Add(Blocks.Action(Loc.L("update.notes"), Icons.Link, () => _ = OpenAsync(page)));

        var check = Blocks.Action(Loc.L("update.check"), Icons.Refresh, () => _ = _updater.CheckAsync());
        check.IsEnabled = state.Phase is not (UpdatePhase.Checking or UpdatePhase.Downloading or UpdatePhase.Installing);
        buttons.Add(check);

        _live.Children.Add(Blocks.Cluster([.. buttons]));
    }

    /// <summary>El botón de instalar, o nada si no hay nada que instalar.</summary>
    private Button? Install(UpdateState state, ReleaseInfo release)
    {
        string version = release.Version.ToString();
        var (text, enabled) = state.Phase switch
        {
            UpdatePhase.Available => (Loc.L("update.install.download", version), true),
            UpdatePhase.Ready => (Loc.L("update.install.ready", version), true),
            UpdatePhase.Downloading => (Loc.L("update.install.downloading", state.Percent), false),
            UpdatePhase.Installing => (Loc.L("update.install.installing"), false),
            _ => (Text.Empty, false),
        };
        if (text == Text.Empty) return null;

        var button = Blocks.Action(text, Icons.Import, () => _ = ConfirmAndInstallAsync(release), "primary");
        button.IsEnabled = enabled;
        return button;
    }

    private async Task ConfirmAndInstallAsync(ReleaseInfo release)
    {
        string version = release.Version.ToString();
        if (!await Forms.ConfirmAsync(this, Loc.L("update.confirm.title", version),
                Loc.L("update.confirm.question"), Loc.L("update.confirm.accept")))
            return;

        await _updater.InstallAsync();
    }

    /// <summary>
    /// Abre la página de la release. No lanza: la llaman sin esperarla, y no poder abrir el
    /// navegador no merece más que no abrirlo.
    /// </summary>
    private async Task OpenAsync(Uri page)
    {
        if (TopLevel.GetTopLevel(this)?.Launcher is not { } launcher) return;

        try
        {
            await launcher.LaunchUriAsync(page);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            if (TopLevel.GetTopLevel(this) is MainWindow window)
                window.Toast(Loc.L("update.notes.failed", e.Message), Icons.Alert);
        }
    }

    private static Border? Flag(UpdateState state) => state.Phase switch
    {
        UpdatePhase.Checking => Blocks.Flag(Loc.L("update.flag.checking")),
        UpdatePhase.UpToDate when state.Failure is null => Blocks.Flag(Loc.L("update.flag.up-to-date"), Tone.Good),
        UpdatePhase.Available or UpdatePhase.Ready when state.Release is { } release =>
            Blocks.Flag(Loc.L("update.flag.available", release.Version.ToString()), Tone.Warn, Icons.Import),
        UpdatePhase.Downloading => Blocks.Flag(Loc.L("update.flag.downloading", state.Percent), Tone.Warn),
        UpdatePhase.Installing => Blocks.Flag(Loc.L("update.flag.installing"), Tone.Warn),
        _ when state.Failure is not null => Blocks.Flag(Loc.L("update.flag.failed"), Tone.Bad, Icons.Alert),
        _ => null,
    };

    /// <summary>Lo que salió mal la última vez, en palabras.</summary>
    private static Text? Failure(UpdateState state) => state.Failure switch
    {
        UpdateFailure.Network => Loc.L("update.error.network", state.Detail ?? ""),
        UpdateFailure.Timeout => Loc.L("update.error.timeout"),
        UpdateFailure.Release => Loc.L("update.error.release"),
        UpdateFailure.Hash => Loc.L("update.error.hash"),
        UpdateFailure.Disk => Loc.L("update.error.disk", state.Detail ?? ""),
        UpdateFailure.Cancelled => Loc.L("update.error.cancelled"),
        UpdateFailure.Installer => Loc.L("update.error.installer", state.Detail ?? "", Updater.InstallLog),
        _ => (Text?)null,
    };

    /// <summary>
    /// Cuándo contestó GitHub por última vez: la hora si fue hoy, y la fecha si no. En el formato del
    /// idioma de la ventana, que es el de todas sus cifras.
    /// </summary>
    private static Text Checked(DateTimeOffset? at)
    {
        if (at is not { } when) return Loc.L("update.checked.never");

        var local = when.ToLocalTime();
        return local.Date == DateTime.Today
            ? Text.Of(() => Loc.S("update.checked.today", local.ToString("t", Loc.Culture)))
            : Text.Of(() => local.ToString("g", Loc.Culture));
    }
}
