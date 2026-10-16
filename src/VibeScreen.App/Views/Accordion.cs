using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using VibeScreen.App.Theme;

namespace VibeScreen.App.Views;

/// <summary>Deja un solo desplegable abierto a la vez dentro de un contenedor.</summary>
/// <remarks>
/// <para>
/// La columna de la derecha del editor tiene más filas de las que caben: la pantalla, el alta de
/// widgets y las secciones de propiedades de un arco con el valor dentro no entran juntas ni de lejos.
/// Abiertas a la vez obligan a desplazarse para llegar a lo que se acaba de abrir, que es justo lo
/// que se quería ver.
/// </para>
/// <para>
/// Se escucha el evento del propio desplegable en la raíz, y no uno a uno, porque las secciones de
/// propiedades se rehacen enteras con cada widget seleccionado: apuntarlas en una lista obligaría a
/// acordarse de darlas de baja, y olvidarse una vez deja un desplegable que ya no existe mandando
/// cerrar a los demás. Así la raíz no tiene que saber cuántos hay ni cuándo nacen.
/// </para>
/// <para>
/// Desplegables hay de dos clases: los <see cref="Expander"/> de las secciones de propiedades y las
/// cajas plegables de la columna, Pantalla y Añadir, que son <see cref="InspectorPanel"/> y avisan con
/// su propio evento. Para la regla son lo mismo.
/// </para>
/// </remarks>
public static class Accordion
{
    /// <summary>Le pone la regla a un contenedor y lo devuelve, para poder encadenarlo donde se crea.</summary>
    public static T Only<T>(T root)
        where T : Control
    {
        ArgumentNullException.ThrowIfNull(root);

        // «Expanding» y no «Expanded»: el segundo llega cuando la animación de apertura ha terminado,
        // y los demás se cerrarían después de que el sitio ya se hubiera pedido, dando un salto.
        root.AddHandler(Expander.ExpandingEvent, (_, e) => CloseOthers(root, e.Source), RoutingStrategies.Bubble);
        root.AddHandler(InspectorPanel.OpeningEvent, (_, e) => CloseOthers(root, e.Source), RoutingStrategies.Bubble);

        return root;
    }

    /// <summary>Cierra lo que esté abierto, menos lo que se acaba de abrir y lo que lo contiene.</summary>
    private static void CloseOthers(Control root, object? source)
    {
        if (source is not Control opened) return;

        // La lista antes de tocar nada: cerrar uno le quita el contenido al árbol lógico, que es por
        // donde se está recorriendo.
        foreach (var other in root.GetLogicalDescendants().ToList())
        {
            bool open = other switch
            {
                Expander expander => expander.IsExpanded,
                InspectorPanel { Collapsible: true } panel => panel.IsExpanded,
                _ => false,
            };

            if (!open || ReferenceEquals(other, opened)) continue;

            // El que contiene al que se abre se queda: cerrarlo escondería al recién abierto.
            if (opened.GetLogicalAncestors().Contains(other)) continue;

            if (other is Expander closing) closing.IsExpanded = false;
            else if (other is InspectorPanel folding) folding.IsExpanded = false;
        }
    }
}
