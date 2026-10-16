using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VibeScreen.App;

namespace VibeScreen.ThemeCheck;

/// <summary>
/// La ventana de verdad, sin pantalla, para poder preguntarle a los controles qué pintan.
/// </summary>
/// <remarks>
/// <para>
/// <b>Se mide el control, no el código que lo viste.</b> Leer los estilos y dar por hecho que se
/// aplican es justo lo que falla: un selector que no casa compila igual de bien que uno que sí, y la
/// sección plegable llevaba meses con dos estilos escritos que no pintaban nada —uno porque le
/// faltaba un salto de plantilla y otro porque Fluent lo ganaba con una pseudoclase—. Aquí se monta
/// el control, se le aplica la plantilla y se le pregunta el valor que ha quedado.
/// </para>
/// <para>
/// Sin pantalla porque no hace falta ninguna: la plantilla, los estilos y la medida son cosa del
/// árbol, no del dibujo. Así esto corre igual en una consola sin escritorio.
/// </para>
/// </remarks>
internal static class Bench
{
    private static Window _root = null!;

    /// <summary>La aplicación en marcha, que es de donde salen las claves del tema.</summary>
    public static Application App { get; private set; } = null!;

    /// <summary>Levanta la aplicación con la misma pila de estilos que la ventana de verdad.</summary>
    public static void Start()
    {
        AppBuilder.Configure<ThemeApp>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = true })
            .SetupWithoutStarting();

        App = Application.Current!;
        Palette.Apply(AppTheme.Dark, Palette.DefaultAccent);

        _root = new Window { Width = 1456, Height = 908 };
        _root.Show();
    }

    /// <summary>
    /// Cuelga algo de la ventana, le pasa la maquetación y lo devuelve ya medido.
    /// </summary>
    /// <remarks>
    /// Arriba a la izquierda y no de contenido de la ventana a secas: ahí el control se estira a los
    /// 908 px de alto de la ventana y medirlo deja de decir nada. Así sale del alto que pide.
    /// </remarks>
    public static T Mount<T>(T control)
        where T : Control
    {
        _root.Content = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Children = { control },
        };

        Settle();
        return control;
    }

    /// <summary>
    /// Pone algo de contenido de la ventana, estirado a su tamaño entero, y lo devuelve ya medido.
    /// </summary>
    /// <remarks>
    /// Lo contrario de <see cref="Mount"/>, y para lo contrario: aquí no se mide cuánto pide un
    /// control, sino cómo reparte el sitio de una ventana de verdad. Dos pasadas porque Reflow se
    /// entera del ancho al colocar y recoloca entonces, y eso deja la maquetación por rehacer.
    /// </remarks>
    public static T Fill<T>(T control)
        where T : Control
    {
        _root.Content = control;
        Settle();
        Settle();
        return control;
    }

    /// <summary>Deja que la maquetación termine. Hace falta tras cambiar algo que mueve el árbol.</summary>
    public static void Settle()
    {
        Dispatcher.UIThread.RunJobs();
        _root.Measure(new Size(1456, 908));
        _root.Arrange(new Rect(0, 0, 1456, 908));
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Una pieza con nombre de dentro de la plantilla de un control, esté a los saltos que esté.</summary>
    public static T Part<T>(Visual root, string name)
        where T : Visual
    {
        ArgumentNullException.ThrowIfNull(root);

        foreach (var candidate in root.GetVisualDescendants().OfType<T>())
        {
            if (candidate is Control control && string.Equals(control.Name, name, StringComparison.Ordinal)) return candidate;
        }

        throw new InvalidOperationException($"no hay ninguna pieza «{name}» de tipo {typeof(T).Name} dentro de {root.GetType().Name}");
    }

    /// <summary>La primera pieza de un tipo, para las que la plantilla deja sin nombre.</summary>
    public static T First<T>(Visual root)
        where T : Visual
    {
        ArgumentNullException.ThrowIfNull(root);
        return root.GetVisualDescendants().OfType<T>().First();
    }

    /// <summary>
    /// La aplicación de la verificación: la misma pila de estilos que <c>VibeScreenApp</c>.
    /// </summary>
    /// <remarks>
    /// Copiada y no reutilizada porque <c>VibeScreenApp</c> arranca el enlace con el servicio, la
    /// bandeja y la ventana: aquí lo que importa es el orden de los estilos —Fluent primero y lo
    /// nuestro después— y nada más. Si esa pila cambia allí, hay que cambiarla aquí.
    /// </remarks>
    private sealed class ThemeApp : Application
    {
        public override void Initialize()
        {
            Styles.Add(new FluentTheme());
            Styles.Add(new StyleInclude(new Uri("avares://VibeScreen.App/"))
            {
                Source = new Uri("avares://Avalonia.Controls.ColorPicker/Themes/Fluent/Fluent.xaml"),
            });
            Styles.Add(Palette.Styles());
            Styles.Add(VibeScreen.App.Theme.ControlThemes.Styles());
        }
    }
}
