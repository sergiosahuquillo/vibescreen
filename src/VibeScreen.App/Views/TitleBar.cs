using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using VibeScreen.App.Localization;
using VibeScreen.App.Theme;
using Path = Avalonia.Controls.Shapes.Path;

namespace VibeScreen.App.Views;

/// <summary>La barra de título propia: la marca, qué tema hay abierto y los mandos de la ventana.</summary>
/// <remarks>
/// <para>
/// Propia y no la del sistema porque es media identidad de la ventana: la del sistema obliga a una
/// franja gris encima de una interfaz que no tiene ni un gris, y deja el nombre del programa en una
/// tipografía que no es la suya.
/// </para>
/// <para>
/// Lo que cuesta es lo que la del sistema daba gratis: arrastrar para mover, doble clic para
/// maximizar y el botón de maximizar cambiando de dibujo según el estado. Todo eso está aquí.
/// </para>
/// </remarks>
public sealed class TitleBar : Border
{
    private readonly Window _window;
    private readonly TextBlock _context = new()
    {
        FontSize = 13,
        Foreground = Palette.FaintBrush,
        VerticalAlignment = VerticalAlignment.Center,
        TextWrapping = TextWrapping.NoWrap,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };

    private readonly Viewbox _themeIcon;
    private readonly Button _themeButton;

    /// <summary>Lo que ocupan los tres botones que dibuja Avalonia a la derecha.</summary>
    private const double CaptionButtonsWidth = 138;

    /// <param name="toggleTheme">Conmuta entre claro y oscuro. Devuelve si la ventana queda en claro.</param>
    public TitleBar(Window window, Func<bool> toggleTheme)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(toggleTheme);
        _window = window;

        Height = 40;
        Background = Palette.SurfaceBrush;
        BorderBrush = Palette.LineBrush;
        BorderThickness = new Thickness(0, 0, 0, 1);
        TextElement.SetFontFamily(this, AppFonts.Ui);

        var brand = Icons.Make(Icons.Brandmark, Palette.AccentBrush, 22);

        var identity = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(14, 0, 0, 0),
            Children =
            {
                brand,
                new TextBlock
                {
                    Text = "VibeScreen",
                    FontFamily = AppFonts.Display,
                    FontWeight = FontWeight.Bold,
                    FontSize = 14,
                    Foreground = Palette.TextBrush,
                    VerticalAlignment = VerticalAlignment.Center,
                },
                new Border
                {
                    Width = 1,
                    Height = 14,
                    Background = Palette.LineStrongBrush,
                    VerticalAlignment = VerticalAlignment.Center,
                },
                _context,
            },
        };

        _themeIcon = Icons.Make(Palette.IsLight ? Icons.Moon : Icons.Sun, Palette.MutedBrush, 15);
        _themeButton = WindowButton(_themeIcon, ThemeLabel(Palette.IsLight), () =>
        {
            bool light = toggleTheme();
            ShowTheme(light);
        });
        _themeButton.Width = 40;
        _themeButton.Margin = new Thickness(0, 0, 7, 0);
        WindowDecorationProperties.SetElementRole(_themeButton, WindowDecorationsElementRole.User);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            // Hueco para los tres botones que dibuja Avalonia por encima, en su propia capa. Son
            // suyos a propósito: en Windows hablan con el sistema, y eso es lo que hace que posarse
            // en el de maximizar saque los Snap Layouts. Unos propios serían un dibujo que se parece.
            Margin = new Thickness(0, 0, CaptionButtonsWidth, 0),
            Children = { _themeButton },
        };

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        Grid.SetColumn(identity, 0);
        Grid.SetColumn(buttons, 2);
        row.Children.Add(identity);
        row.Children.Add(buttons);
        Child = row;

        // Arrastrar la barra mueve la ventana, y el doble clic la maximiza: es lo que hace cualquier
        // barra de título, y al dibujarla nosotros hay que devolvérselo al usuario a mano.
        //
        // A mano y no con el rol de «barra de título» de Avalonia 12: comprobado en marcha que
        // marcar contenido propio con ese rol no hace nada: ni arrastra ni maximiza. Donde el rol sí
        // vale es en los botones de la capa de decoración, y de ahí salen los Snap Layouts.
        //
        // El conmutador de tema queda fuera porque el evento se mira desde dónde nació: sin eso,
        // empezar a pulsarlo y mover un píxel arrastraría la ventana en vez de cambiar el tema.
        PointerPressed += (_, e) =>
        {
            if (e.Source is Visual source && source.FindAncestorOfType<Button>() is not null) return;
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

            if (e.ClickCount == 2) ToggleMaximised();
            else _window.BeginMoveDrag(e);
        };
    }

    /// <summary>Lo que se lee al lado del nombre: el tema que hay abierto en el editor.</summary>
    public Text Context
    {
        set => Loc.Set(_context, TextBlock.TextProperty, value);
    }

    /// <summary>
    /// Pone el icono del conmutador según la paleta que haya quedado puesta, y lo que le dice al lector
    /// de pantalla.
    /// </summary>
    /// <remarks>
    /// El nombre va al botón guardado y no al de subir dos padres desde el icono, que era como se
    /// buscaba: el padre del icono ya es el botón, así que dos arriba caía en el panel que lo contiene
    /// y el botón se quedaba diciendo el modo con el que nació la ventana.
    /// </remarks>
    public void ShowTheme(bool light)
    {
        if (_themeIcon.Child is Path path) path.Data = light ? Icons.Moon : Icons.Sun;
        Loc.Set(_themeButton, AutomationProperties.NameProperty, ThemeLabel(light));
    }

    /// <summary>Lo que hace el conmutador, que es ir al modo que no está puesto.</summary>
    private static Text ThemeLabel(bool light) => light ? Loc.L("titlebar.to-dark") : Loc.L("titlebar.to-light");

    private void ToggleMaximised() =>
        _window.WindowState = _window.WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    private static Button WindowButton(Viewbox icon, Text label, Action click)
    {
        var button = new Button
        {
            Content = icon,
            Classes = { "winbtn" },
            Width = 46,
            Height = 40,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        button.Click += (_, _) => click();
        Loc.Set(button, AutomationProperties.NameProperty, label);
        return button;
    }

}
