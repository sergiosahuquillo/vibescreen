using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using VibeScreen.App.Localization;
using VibeScreen.App.Theme;

namespace VibeScreen.App.Views;

/// <summary>La franja de abajo: en qué anda el servicio, y cuánto está costando.</summary>
/// <remarks>
/// Lo que se mira de reojo sin cambiar de sección. A la izquierda, si el panel está pintando y en
/// qué puerto; a la derecha, el caudal de ahora mismo y la versión. La versión no es decoración: es
/// lo primero que hay que preguntar cuando alguien cuenta que algo no le va.
/// </remarks>
public sealed class StatusBar : Border
{
    private readonly Ellipse _dot = new()
    {
        Width = 8,
        Height = 8,
        Fill = Palette.FaintBrush,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private readonly TextBlock _state = new()
    {
        FontSize = 13,
        FontWeight = FontWeight.SemiBold,
        Foreground = Palette.MutedBrush,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private readonly TextBlock _detail = new()
    {
        FontSize = 13,
        Foreground = Palette.FaintBrush,
        VerticalAlignment = VerticalAlignment.Center,
        TextWrapping = TextWrapping.NoWrap,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };

    private readonly TextBlock _rate = new()
    {
        FontSize = 13,
        Foreground = Palette.FaintBrush,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private readonly ContentControl _update = new() { VerticalAlignment = VerticalAlignment.Center, IsVisible = false };
    private string? _updateShown;

    public StatusBar()
    {
        Height = 32;
        Background = Palette.SurfaceBrush;
        BorderBrush = Palette.LineBrush;
        BorderThickness = new Thickness(0, 1, 0, 0);
        Padding = new Thickness(16, 0);
        TextElement.SetFontFamily(this, AppFonts.Ui);

        // Los datos en monoespaciada y las palabras no: el puerto y el caudal son cifras que cambian
        // solas, y en proporcional bailan de ancho en cada refresco.
        foreach (var data in new[] { _detail, _rate }) AppFonts.Mono(data);

        var left = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { _dot, _state, Separator(), _detail },
        };

        var right = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                _rate,
                Separator(),
                AppFonts.Mono(new TextBlock
                {
                    Text = Version,
                    FontSize = 13,
                    Foreground = Palette.FaintBrush,
                    VerticalAlignment = VerticalAlignment.Center,
                }),
                _update,
            },
        };

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(right, 1);
        row.Children.Add(left);
        row.Children.Add(right);
        Child = row;
    }

    /// <summary>Lo que dice la izquierda: el estado y con qué panel.</summary>
    public void Show(Text state, Text detail, DeviceHealth health)
    {
        Loc.Set(_state, TextBlock.TextProperty, state);
        Loc.Set(_detail, TextBlock.TextProperty, detail);
        _state.Foreground = health switch
        {
            DeviceHealth.Painting => Palette.GoodBrush,
            DeviceHealth.Searching => Palette.WarnBrush,
            _ => Palette.MutedBrush,
        };
        _dot.Fill = health switch
        {
            DeviceHealth.Painting => Palette.GoodBrush,
            DeviceHealth.Searching => Palette.WarnBrush,
            _ => Palette.FaintBrush,
        };
    }

    /// <summary>El caudal de ahora mismo, o nada si no hay panel del que decirlo.</summary>
    public void ShowRate(double? bytesPerSecond) =>
        _rate.Text = bytesPerSecond is { } rate ? Loc.Number(rate) + " B/s" : "";

    /// <summary>Se pulsa la pastilla de la versión nueva.</summary>
    public event Action? UpdateClicked;

    /// <summary>
    /// La versión nueva al lado de la de ahora, o nada si no la hay. Se pulsa y lleva a Ajustes, que
    /// es donde se decide qué hacer con ella.
    /// </summary>
    public void ShowUpdate(string? version)
    {
        if (version == _updateShown) return;
        _updateShown = version;

        if (version is null)
        {
            _update.Content = null;
            _update.IsVisible = false;
            return;
        }

        // Una pastilla que se pulsa y no un botón: dentro de un botón, Fluent le pondría su fondo al
        // pasar el ratón, y esto tiene que verse igual que las pastillas de las tarjetas.
        var flag = Blocks.Flag(Loc.L("status.update", version), Tone.Warn, Icons.Import);
        flag.Cursor = new Cursor(StandardCursorType.Hand);
        Loc.Set(flag, ToolTip.TipProperty, Loc.L("status.update.tip"));
        flag.PointerReleased += (_, _) => UpdateClicked?.Invoke();

        _update.Content = flag;
        _update.IsVisible = true;
    }

    private static Border Separator() => new()
    {
        Width = 1,
        Height = 12,
        Background = Palette.LineStrongBrush,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>La versión del ejecutable, como se escriben los tags.</summary>
    private static string Version => BuildInfo.Version.Length == 0 ? "" : "v" + BuildInfo.Version;
}
