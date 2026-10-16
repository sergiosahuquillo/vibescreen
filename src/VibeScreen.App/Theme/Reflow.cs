using Avalonia;
using Avalonia.Controls;

namespace VibeScreen.App.Theme;

/// <summary>
/// Avisa cuando un control cruza un ancho, para recolocar lo que lleve dentro.
/// </summary>
/// <remarks>
/// <para>
/// Es lo que en el diseño es una consulta de medios. Avalonia no las tiene, así que hay que mirar el
/// ancho de verdad y reordenar a mano. Se mide el <b>control</b> y no la ventana: lo que decide si
/// dos cosas caben una al lado de otra es el hueco que les queda, no el tamaño de la pantalla, y en
/// esta ventana el rail se lleva 232 px fijos.
/// </para>
/// <para>
/// Solo avisa al cruzar, no en cada píxel de un arrastre: recolocar es rehacer controles, y hacerlo
/// sesenta veces por segundo mientras se estira la ventana se ve como un tirón.
/// </para>
/// </remarks>
public static class Reflow
{
    /// <param name="control">El que se mide.</param>
    /// <param name="threshold">Por debajo de este ancho se considera estrecho.</param>
    /// <param name="changed">Se llama al cruzar, y una vez al principio con el estado que haya.</param>
    public static void OnWidth(Control control, double threshold, Action<bool> changed)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(changed);

        bool? last = null;

        void Check(double width)
        {
            // Sin ancho todavía no hay nada que decidir: al construirse, el control mide cero y
            // avisar ahí dejaría la vista en estrecho hasta la primera pasada de layout.
            if (width <= 0) return;

            bool narrow = width < threshold;
            if (last == narrow) return;

            last = narrow;
            changed(narrow);
        }

        control.PropertyChanged += (_, e) =>
        {
            if (e.Property == Visual.BoundsProperty && e.NewValue is Rect bounds) Check(bounds.Width);
        };

        Check(control.Bounds.Width);
    }
}
