using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using VibeScreen.App.Localization;
using VibeScreen.App.Theme;
using Path = Avalonia.Controls.Shapes.Path;

namespace VibeScreen.App.Views;

/// <summary>Las cuatro secciones de la ventana.</summary>
/// <remarks>
/// Un enumerado y no el índice de una pestaña. El sondeo necesita saber si el explorador está a la
/// vista para refrescar los trescientos sensores, y antes lo preguntaba comparando contra un 2
/// escrito a mano, con un comentario al lado pidiendo que nadie reordenara las pestañas. Reordenar
/// esto ahora no rompe nada.
/// </remarks>
public enum AppView
{
    Panel,
    Editor,
    Sensors,
    Settings,
}

/// <summary>La columna de la izquierda: a dónde ir, y qué panel hay conectado.</summary>
/// <remarks>
/// Sustituye a las pestañas de arriba. Con cuatro secciones que se visitan a todas horas, una
/// columna deja sitio para decir de cada una algo más que su nombre —y, abajo del todo, para lo
/// único que el usuario quiere saber sin entrar en ningún sitio: si el panel está ahí.
/// </remarks>
public sealed class NavigationRail : Border
{
    private readonly List<(AppView View, Border Item, TextBlock Text, Viewbox Icon, Border Mark)> _items = [];
    private readonly Action<AppView> _go;

    private readonly TextBlock _device = new()
    {
        FontSize = 14,
        FontWeight = FontWeight.SemiBold,
        LineHeight = 18,
        TextWrapping = TextWrapping.Wrap,
        Foreground = Palette.TextBrush,
    };

    private readonly TextBlock _state = new()
    {
        FontSize = 13,
        Foreground = Palette.MutedBrush,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private readonly Ellipse _dot = new()
    {
        Width = 8,
        Height = 8,
        Fill = Palette.FaintBrush,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private AppView _current = AppView.Panel;

    public NavigationRail(Action<AppView> go)
    {
        ArgumentNullException.ThrowIfNull(go);
        _go = go;

        Width = 232;
        Background = Palette.SurfaceBrush;
        BorderBrush = Palette.LineBrush;
        BorderThickness = new Thickness(0, 0, 1, 0);
        Padding = new Thickness(12, 16);
        TextElement.SetFontFamily(this, AppFonts.Ui);

        var list = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Top };
        list.Children.Add(Item(AppView.Panel, Loc.L("nav.panel"), Icons.Panel));
        list.Children.Add(Item(AppView.Editor, Loc.L("nav.editor"), Icons.Editor));
        list.Children.Add(Item(AppView.Sensors, Loc.L("nav.sensors"), Icons.Sensors));
        list.Children.Add(Item(AppView.Settings, Loc.L("nav.settings"), Icons.Settings));

        var card = new Border
        {
            Background = Palette.Surface2Brush,
            BorderBrush = Palette.LineBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12),
            VerticalAlignment = VerticalAlignment.Bottom,
            Child = new StackPanel
            {
                Spacing = 4,
                Children =
                {
                    Loc.Set(new TextBlock
                    {
                        FontSize = 11,
                        LetterSpacing = 1,
                        Foreground = Palette.FaintBrush,
                    }, TextBlock.TextProperty, Loc.L("rail.device.header").Upper()),
                    _device,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 7,
                        Children = { _dot, _state },
                    },
                },
            },
        };

        var dock = new DockPanel();
        DockPanel.SetDock(card, Dock.Bottom);
        dock.Children.Add(card);
        dock.Children.Add(list);
        Child = dock;

        Mark();
    }

    /// <summary>Marca la sección que se está viendo, sin llamar a nadie.</summary>
    public void Show(AppView view)
    {
        _current = view;
        Mark();
    }

    /// <summary>Lo que se lee en la tarjeta de abajo. Sin panel, el nombre se apaga.</summary>
    public void ShowDevice(string? model, Text? detail, DeviceHealth health)
    {
        Loc.Set(_device, TextBlock.TextProperty, model is null ? Loc.L("rail.device.none") : Text.Raw(model));
        _device.Foreground = model is null ? Palette.MutedBrush : Palette.TextBrush;
        Loc.Set(_state, TextBlock.TextProperty, detail ?? Loc.L("rail.device.no-service"));
        _dot.Fill = health switch
        {
            DeviceHealth.Painting => Palette.GoodBrush,
            DeviceHealth.Searching => Palette.WarnBrush,
            _ => Palette.FaintBrush,
        };
    }

    private Control Item(AppView view, Text label, StreamGeometry icon)
    {
        var glyph = Icons.Make(icon, Palette.MutedBrush, 18);
        var text = Loc.Set(new TextBlock
        {
            FontSize = 15,
            FontWeight = FontWeight.Medium,
            Foreground = Palette.MutedBrush,
            VerticalAlignment = VerticalAlignment.Center,
        }, TextBlock.TextProperty, label);

        // La marca del realce sale por la izquierda del relleno del rail, que es lo que la pega al
        // canto de la ventana en vez de dejarla flotando dentro de la fila.
        var mark = new Border
        {
            Width = 3,
            Height = 20,
            CornerRadius = new CornerRadius(0, 3, 3, 0),
            Background = Palette.AccentBrush,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(-12, 0, 0, 0),
            IsVisible = false,
        };

        var row = new Grid
        {
            Children =
            {
                mark,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 12,
                    Margin = new Thickness(12, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Children = { glyph, text },
                },
            },
        };

        var item = new Border
        {
            MinHeight = 42,
            CornerRadius = Palette.ControlRadius,
            Background = Brushes.Transparent,
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
            Child = row,
        };

        item.PointerPressed += (_, _) =>
        {
            if (_current == view) return;
            _current = view;
            Mark();
            _go(view);
        };
        item.PointerEntered += (_, _) => { if (_current != view) item.Background = Palette.Surface2Brush; };
        item.PointerExited += (_, _) => { if (_current != view) item.Background = Brushes.Transparent; };

        Loc.Set(item, AutomationProperties.NameProperty, label);
        _items.Add((view, item, text, glyph, mark));
        return item;
    }

    private void Mark()
    {
        foreach ((var view, var item, var text, var icon, var mark) in _items)
        {
            bool on = view == _current;
            item.Background = on ? Palette.AccentSoftBrush : Brushes.Transparent;
            mark.IsVisible = on;
            text.Foreground = on ? Palette.TextBrush : Palette.MutedBrush;
            text.FontWeight = on ? FontWeight.SemiBold : FontWeight.Medium;
            if (icon.Child is Path path) path.Stroke = on ? Palette.AccentBrush : Palette.MutedBrush;
        }
    }
}

/// <summary>Cómo de bien va el panel, para el punto de la tarjeta del rail.</summary>
public enum DeviceHealth
{
    Off,
    Searching,
    Painting,
}
