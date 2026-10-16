using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Shapes;
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

    private static Border Separator() => new()
    {
        Width = 1,
        Height = 12,
        Background = Palette.LineStrongBrush,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>
    /// La versión del ejecutable.
    /// </summary>
    /// <remarks>
    /// Del <c>InformationalVersion</c> y no del <c>Version</c> a secas: es el que lleva el sufijo de
    /// la compilación cuando lo hay, y el que se corresponde con lo que se publica. Se le quita lo
    /// que el SDK añade detrás de un «+», que es el hash de git y no cabe en la franja.
    /// </remarks>
    private static string Version
    {
        get
        {
            string? raw = Assembly.GetEntryAssembly()?
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (raw is null) return "";

            int plus = raw.IndexOf('+', StringComparison.Ordinal);
            return "v" + (plus < 0 ? raw : raw[..plus]);
        }
    }
}
