using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using VibeScreen.App.Localization;

namespace VibeScreen.App.Theme;

/// <summary>
/// Los avisos que aparecen un momento en una esquina y se van solos.
/// </summary>
/// <remarks>
/// <para>
/// Para lo que ha salido bien: guardado, aplicado al panel, identificador copiado, brillo cambiado.
/// Eso antes no se decía en ninguna parte —el brillo se veía en el panel, y guardar solo quitaba el
/// punto del nombre del fichero—, o se decía en el mismo sitio donde se cuentan los errores, que es
/// peor: un mensaje que a veces es una avería y a veces una felicitación acaba sin leerse.
/// </para>
/// <para>
/// No llevan botón de cerrar ni se apilan sin fin: son tres como mucho y se van solos. Lo que hace
/// falta recordar no cabe en un aviso que desaparece, y para eso está la tarjeta de avisos.
/// </para>
/// </remarks>
public sealed class Toasts : Panel
{
    private const int MostAtOnce = 3;
    private static readonly TimeSpan OnScreen = TimeSpan.FromMilliseconds(2600);

    private readonly StackPanel _stack = new()
    {
        Spacing = 8,
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Bottom,
        // 48 y no 16: por debajo se come la barra de estado, que es justo donde está el caudal.
        Margin = new Thickness(0, 0, 20, 48),
    };

    public Toasts()
    {
        // La capa no recoge clics: está por encima de todo y, si los recogiera, se comería los de la
        // esquina de abajo a la derecha aunque no hubiera ningún aviso a la vista.
        IsHitTestVisible = false;
        Children.Add(_stack);
    }

    /// <summary>Saca un aviso.</summary>
    public void Show(Text text, StreamGeometry? icon = null)
    {
        while (_stack.Children.Count >= MostAtOnce) _stack.Children.RemoveAt(0);

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Children =
            {
                Icons.Make(icon ?? Icons.Check, Palette.GoodBrush, 18),
                Loc.Set(new TextBlock
                {
                    FontSize = 14,
                    Foreground = Palette.TextBrush,
                    VerticalAlignment = VerticalAlignment.Center,
                }, TextBlock.TextProperty, text),
            },
        };

        var toast = new Border
        {
            Background = Palette.Surface3Brush,
            BorderBrush = Palette.LineStrongBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            BoxShadow = Palette.PopShadow,
            Padding = new Thickness(14, 10),
            HorizontalAlignment = HorizontalAlignment.Right,
            Opacity = 0,
            RenderTransform = new TranslateTransform(0, 8),
            Transitions =
            [
                new DoubleTransition { Property = OpacityProperty, Duration = Palette.Slow, Easing = Palette.Ease },
            ],
            Child = row,
        };
        TextElement.SetFontFamily(toast, AppFonts.Ui);

        _stack.Children.Add(toast);

        // Entra en el siguiente fotograma: puesto ya visible, la transición no tiene de dónde salir
        // y el aviso aparece de golpe en vez de subir.
        Dispatcher.UIThread.Post(() =>
        {
            toast.Opacity = 1;
            toast.RenderTransform = new TranslateTransform(0, 0);
        }, DispatcherPriority.Background);

        DispatcherTimer.RunOnce(
            () =>
            {
                toast.Opacity = 0;
                DispatcherTimer.RunOnce(() => _stack.Children.Remove(toast), Palette.Slow);
            },
            OnScreen);
    }
}
