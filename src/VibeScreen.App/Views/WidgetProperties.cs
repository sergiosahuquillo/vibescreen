using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using SkiaSharp;
using VibeScreen.App.Localization;
using VibeScreen.App.Theme;
using VibeScreen.Core.Layout;
using VibeScreen.Core.Sensors;
using PixelRect = VibeScreen.Protocol.Display.PixelRect;

namespace VibeScreen.App.Views;

/// <summary>Una unidad tal como se ofrece en el desplegable: el símbolo primero, que es lo que se ve en el panel.</summary>
public sealed record UnitOption(SensorUnit Unit, Text Label) : ILabeled
{
    public override string ToString() => Label.ToString();
}

/// <summary>
/// Las propiedades del widget seleccionado. Se construye entera cada vez que cambia la selección:
/// un texto y un arco no tienen casi nada en común, y rellenar un formulario único a base de esconder
/// la mitad de las filas sale peor.
/// </summary>
/// <remarks>
/// Repartidas en secciones plegables, con una sola abierta: la del tipo de widget, que es a lo que se
/// viene. Un arco con valor dentro son treinta filas y en la columna caben once, así que de seguido no
/// cabría ni la mitad. Las secciones cerradas resumen sus valores al lado del título.
/// </remarks>
public sealed class WidgetProperties : UserControl
{
    private readonly LayoutWidget _widget;
    private readonly RemoteSensorProvider _sensors;
    private readonly Action _changed;

    /// <summary>
    /// Los formatos de reloj que se ofrecen hechos. No son todos los posibles —el patrón se puede
    /// escribir a mano— sino los que cubren lo que la gente pone en un panel.
    /// </summary>
    /// <remarks>
    /// El patrón también va con el idioma, y no solo su nombre: el orden de día y mes, y la palabra
    /// que los une, son de cada idioma. Se resuelve al elegirlo, así que lo que se escribe en el tema
    /// es el del idioma que hay puesto en ese momento.
    /// </remarks>
    private static readonly ClockPreset[] ClockPresets =
    [
        new(Loc.L("clock.preset.time"), Loc.Raw("HH:mm")),
        new(Loc.L("clock.preset.time-seconds"), Loc.Raw("HH:mm:ss")),
        new(Loc.L("clock.preset.time-12"), Loc.Raw("h:mm tt")),
        new(Loc.L("clock.preset.date"), Loc.L("clock.format.date")),
        new(Loc.L("clock.preset.day-month"), Loc.L("clock.format.day-month")),
        new(Loc.L("clock.preset.weekday"), Loc.Raw("dddd")),
        new(Loc.L("clock.preset.date-time"), Loc.L("clock.format.date-time")),
        new(Loc.L("clock.preset.all"), Loc.L("clock.format.all")),
    ];

    private readonly NumericUpDown _x = Forms.Number(0, 4096);
    private readonly NumericUpDown _y = Forms.Number(0, 4096);
    private readonly NumericUpDown _width = Forms.Number(1, 4096);
    private readonly NumericUpDown _height = Forms.Number(1, 4096);
    private readonly NumericUpDown _refresh = Forms.Number(LayoutLoader.MinRefreshMs, 600_000, 100);

    private readonly FormSection _box = new(Loc.L("props.box"));
    private readonly StackPanel _sections = new() { Spacing = InspectorPanel.BodySpacing };

    private bool _suspend;

    /// <summary>Reescribe los recorridos de la regla de color, que llevan la unidad del widget dentro.</summary>
    private Action _showRanges = () => { };

    /// <param name="idProblem">
    /// Qué le pasa a un identificador escrito a mano, o null si no le pasa nada. Lo sabe el editor,
    /// que es quien tiene los demás widgets del tema.
    /// </param>
    public WidgetProperties(
        LayoutWidget widget, RemoteSensorProvider sensors, Action changed, Action delete,
        Func<LayoutWidget, string, Text?> idProblem)
    {
        ArgumentNullException.ThrowIfNull(widget);
        ArgumentNullException.ThrowIfNull(sensors);
        ArgumentNullException.ThrowIfNull(changed);
        ArgumentNullException.ThrowIfNull(idProblem);

        _widget = widget;
        _sensors = sensors;
        _changed = changed;

        // El identificador, siempre a la vista: es lo que dice cuál de los treinta widgets se toca.
        var head = new FormGrid(96);
        var id = new TextBox { Text = widget.Id };
        var trouble = new TextBlock { Foreground = Palette.BadBrush, TextWrapping = TextWrapping.Wrap, IsVisible = false };
        id.LostFocus += (_, _) =>
        {
            string text = (id.Text ?? "").Trim();
            if (text.Length == 0 || text == widget.Id)
            {
                id.Text = widget.Id;
                trouble.IsVisible = false;
                return;
            }

            // Un repetido lo rechaza el cargador al guardar. Decirlo aquí, con la casilla delante, es
            // lo único que le sirve a quien lo acaba de escribir.
            if (idProblem(widget, text) is { } bad)
            {
                Loc.Set(trouble, TextBlock.TextProperty, bad);
                trouble.IsVisible = true;
                id.Text = widget.Id;
                return;
            }

            trouble.IsVisible = false;
            widget.Id = text;
            changed();
        };
        head.Add(Loc.L("props.id"), id);
        head.AddWide(trouble);

        BuildBox();

        switch (widget)
        {
            case TextWidget text:
                BuildText(text);
                break;
            case ClockWidget clock:
                BuildClock(clock);
                break;
            case GraphWidget graph:
                BuildGraph(graph);
                break;
        }

        var remove = Loc.Set(new Button
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            Foreground = Palette.BadBrush,
        }, ContentControl.ContentProperty, Loc.L("props.delete")).WithClick(delete);

        // Solo lo de dentro: la caja de Propiedades, con su título y el identificador a la derecha, la
        // pone el editor. Todo a la distancia del cuerpo de una caja del diseño, que es la que separa
        // el «Id» de la primera sección y cada sección de la siguiente.
        Content = new StackPanel
        {
            Spacing = InspectorPanel.BodySpacing,
            Children = { head, _sections, remove },
        };
    }

    /// <summary>Tras mover o estirar con el ratón, las casillas tienen que decir lo mismo que el lienzo.</summary>
    public void SyncRect()
    {
        _suspend = true;
        try
        {
            _x.Value = _widget.Rect.X;
            _y.Value = _widget.Rect.Y;
            _width.Value = _widget.Rect.Width;
            _height.Value = _widget.Rect.Height;
        }
        finally
        {
            _suspend = false;
        }

        ShowBoxSummary();
    }

    private void ApplyRect()
    {
        if (_suspend) return;

        _widget.Rect = new PixelRect(
            (int)(_x.Value ?? 0), (int)(_y.Value ?? 0),
            (int)(_width.Value ?? 1), (int)(_height.Value ?? 1));
        ShowBoxSummary();
        _changed();
    }

    // ---- la caja, que la tienen todos ----

    private void BuildBox()
    {
        // Una fila cada cosa y no cuatro: la posición son dos cifras de lo mismo y el tamaño otras
        // dos, que es como las pone el diseño. Cuatro filas con «X», «Y», «Ancho» y «Alto» ocupaban
        // el doble de alto para decir lo mismo.
        _box.Fields.AddFields(Loc.L("props.position"), _x, _y);
        _box.Fields.AddFields(Loc.L("props.size"), _width, _height);
        foreach (var field in new[] { _x, _y, _width, _height })
            field.ValueChanged += (_, _) => ApplyRect();

        var priority = Forms.Number(0, 100);
        priority.Value = _widget.Priority;
        priority.ValueChanged += (_, _) =>
        {
            _widget.Priority = (int)(priority.Value ?? 0);
            _changed();
        };

        _refresh.Value = _widget.RefreshMs;
        _refresh.ValueChanged += (_, _) =>
        {
            _widget.RefreshMs = (int)(_refresh.Value ?? 1000);
            ShowBoxSummary();
            _changed();
        };
        _box.Fields.AddPair(Loc.L("props.refresh"), _refresh, Loc.L("props.priority"), priority);

        SyncRect();
        _sections.Children.Add(_box);
    }

    private void ShowBoxSummary()
    {
        var r = _widget.Rect;
        _box.Summary = Loc.Raw($"{r.X},{r.Y} · {r.Width}×{r.Height} · {_widget.RefreshMs} ms");
    }

    private FormSection Section(Text title, bool open = false)
    {
        var section = new FormSection(title, open);
        _sections.Children.Add(section);
        return section;
    }

    // ---- texto ----

    private void BuildText(TextWidget text)
    {
        if (text.Sensor is null)
        {
            var fixedText = Section(Loc.L("props.text"), open: true);
            var content = new TextBox { Text = text.Text ?? "", AcceptsReturn = false };
            WhenTyped(content, () => text.Text ?? "", now =>
            {
                text.Text = now;
                _changed();
            });
            fixedText.Fields.Add(Loc.L("props.text"), content);
        }
        else
        {
            var value = Section(Loc.L("props.value"), open: true);
            value.Summary = Loc.Raw(text.Sensor);

            value.Fields.Add(Loc.L("props.sensor"), SensorField(text.Sensor, id =>
            {
                text.Sensor = id;
                value.Summary = Loc.Raw(id);
                _showRanges();
                _changed();
            }));

            var decimals = Forms.Number(0, TextComposer.MaxDecimals);
            decimals.Value = text.Decimals;
            decimals.ValueChanged += (_, _) =>
            {
                text.Decimals = (int)(decimals.Value ?? 0);
                _changed();
            };
            value.Fields.Add(Loc.L("props.decimals"), decimals);

            value.Fields.Add(Loc.L("props.unit"), UnitField(text.Unit, KindOf(text.Sensor), unit =>
            {
                text.Unit = unit;
                _showRanges();
                _changed();
            }));

            var show = Check(Loc.L("props.unit.show"), text.ShowUnit);
            show.IsCheckedChanged += (_, _) =>
            {
                text.ShowUnit = show.IsChecked == true;
                _changed();
            };
            value.Fields.AddWide(show);

            value.Fields.Add(Loc.L("props.unit.placement"), Forms.Enum(text.UnitPlacement, Labels.UnitPlacement, chosen =>
            {
                text.UnitPlacement = chosen;
                _changed();
            }));

            var space = Check(Loc.L("props.unit.space"), text.UnitSpace);
            space.IsCheckedChanged += (_, _) =>
            {
                text.UnitSpace = space.IsChecked == true;
                _changed();
            };
            value.Fields.AddWide(space);

            var prefix = new TextBox { Text = text.Prefix ?? "" };
            WhenTyped(prefix, () => text.Prefix ?? "", now =>
            {
                text.Prefix = Empty(now);
                _changed();
            });
            var suffix = new TextBox { Text = text.Suffix ?? "" };
            WhenTyped(suffix, () => text.Suffix ?? "", now =>
            {
                text.Suffix = Empty(now);
                _changed();
            });
            value.Fields.AddPair(Loc.L("props.prefix"), prefix, Loc.L("props.suffix"), suffix);
        }

        BuildLabel(Section(Loc.L("props.lettering")), text);

        // Un texto fijo no tiene valor que mirar: su color es el que se elige en «Letra» y ya está.
        if (text.Sensor is not null)
            BuildLevels(
                text.Levels,
                () => TextComposer.Unit(text.Unit, KindOf(text.Sensor)).Symbol(),
                () => TextRange(text),
                levels => text.Levels = levels);
    }

    // ---- fecha y hora ----

    private void BuildClock(ClockWidget clock)
    {
        var format = Section(Loc.L("props.format"), open: true);
        var pattern = new TextBox { Text = clock.Format };

        // Lo que se vería ahora mismo en el panel. Con un patrón a mano es la única forma de saber
        // si uno se ha equivocado de marca sin tener que mirar la vista previa con lupa.
        var example = Forms.OneLine();
        void ShowExample()
        {
            string now = ClockFormat.Compose(clock.Format, DateTimeOffset.Now, Loc.Current);
            Loc.Set(example, TextBlock.TextProperty, Loc.L("props.clock.now", now));
            format.Summary = Loc.Raw(now);
        }

        var presets = Forms.Labeled(new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch });
        Loc.Set(presets, ComboBox.PlaceholderTextProperty, Loc.L("props.clock.custom"));
        foreach (var preset in ClockPresets) presets.Items.Add(preset);
        presets.SelectedItem = Preset(clock.Format);

        presets.SelectionChanged += (_, _) =>
        {
            // Escribir en la casilla es lo que aplica el cambio, aquí y cuando lo teclea el usuario.
            if (presets.SelectedItem is ClockPreset chosen && chosen.Format.ToString() != clock.Format)
                pattern.Text = chosen.Format.ToString();
        };

        WhenTyped(pattern, () => clock.Format, now =>
        {
            clock.Format = now;
            // Un patrón que no es ninguno de la lista deja el desplegable en «Personalizado».
            presets.SelectedItem = Preset(clock.Format);
            ShowExample();
            _changed();
        });

        format.Fields.Add(Loc.L("props.format"), presets);
        format.Fields.Add(Loc.L("props.clock.pattern"), pattern);
        format.Fields.AddWide(example);
        format.Fields.AddWide(Forms.Label(Loc.L("props.clock.marks")));
        ShowExample();

        BuildLabel(Section(Loc.L("props.lettering")), clock);
    }

    /// <summary>El preset que escribe ese patrón en el idioma de ahora, o null si es uno a mano.</summary>
    private static ClockPreset? Preset(string format) =>
        ClockPresets.FirstOrDefault(p => p.Format.ToString() == format);

    // ---- lo que se escribe, sea la hora o un sensor ----

    private void BuildLabel(FormSection section, LabelWidget widget)
    {
        section.Summary = Describe(widget);

        section.Fields.Add(Loc.L("props.font"), FontField(widget.Font, family =>
        {
            widget.Font = family;
            section.Summary = Describe(widget);
            _changed();
        }));

        var size = Forms.Number(4, 200, 1, 1);
        size.Value = (decimal)widget.Size;
        size.ValueChanged += (_, _) =>
        {
            widget.Size = (float)(size.Value ?? 16);
            section.Summary = Describe(widget);
            _changed();
        };
        section.Fields.Add(Loc.L("props.size"), size);

        var bold = Check(Loc.L("props.bold"), widget.Bold);
        var italic = Check(Loc.L("props.italic"), widget.Italic);
        bold.IsCheckedChanged += (_, _) =>
        {
            widget.Bold = bold.IsChecked == true;
            section.Summary = Describe(widget);
            _changed();
        };
        italic.IsCheckedChanged += (_, _) =>
        {
            widget.Italic = italic.IsChecked == true;
            _changed();
        };
        section.Fields.AddWide(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, Children = { bold, italic } });

        section.Fields.Add(Loc.L("props.alignment"), Forms.Enum(widget.Align, Labels.TextAlignment, chosen =>
        {
            widget.Align = chosen;
            _changed();
        }));

        section.Fields.Add(Loc.L("props.color"), ColorField(widget.Color, color =>
        {
            widget.Color = color ?? "#FFFFFF";
            _changed();
        }, allowNone: false));
    }

    /// <summary>El resumen de una letra: el tamaño y si va marcada, que es lo que se reconoce de un vistazo.</summary>
    private static Text Describe(LabelWidget widget) =>
        widget.Bold ? Loc.L("props.lettering.summary.bold", widget.Size) : Loc.L("props.lettering.summary", widget.Size);

    // ---- gráficas ----

    private void BuildGraph(GraphWidget graph)
    {
        var source = Section(Loc.L("props.sensor"));
        source.Summary = Loc.Raw(graph.Sensor);

        source.Fields.Add(Loc.L("props.sensor"), SensorField(graph.Sensor, id =>
        {
            graph.Sensor = id;
            source.Summary = Loc.Raw(id);
            _showRanges();
            _changed();
        }));

        source.Fields.Add(Loc.L("props.unit"), UnitField(graph.Unit, KindOf(graph.Sensor), unit =>
        {
            graph.Unit = unit;
            _showRanges();
            _changed();
        }));

        var min = Forms.Number(-1_000_000, 1_000_000, 1, 2);
        var max = Forms.Number(-1_000_000, 1_000_000, 1, 2);
        min.Value = (decimal)graph.Min;
        max.Value = (decimal)graph.Max;
        min.ValueChanged += (_, _) =>
        {
            graph.Min = (float)(min.Value ?? 0);
            _changed();
        };
        max.ValueChanged += (_, _) =>
        {
            graph.Max = (float)(max.Value ?? 100);
            _changed();
        };
        source.Fields.AddPair(Loc.L("props.min"), min, Loc.L("props.max"), max);

        var shape = Section(graph switch
        {
            BarWidget => Loc.L("widget.kind.bar"),
            GaugeWidget => Loc.L("widget.kind.gauge"),
            _ => Loc.L("widget.kind.chart"),
        }, open: true);

        shape.Fields.Add(Loc.L("props.color"), ColorField(graph.Color, color =>
        {
            graph.Color = color ?? "#8AB4F8";
            _changed();
        }, allowNone: false));

        shape.Fields.Add(Loc.L("props.background"), ColorField(graph.Background, color =>
        {
            graph.Background = color;
            _changed();
        }, allowNone: true));

        shape.Fields.Add(Loc.L("props.outline"), ColorField(graph.Outline, color =>
        {
            graph.Outline = color;
            _changed();
        }, allowNone: true));

        var border = Forms.Number(0.5, 20, 0.5, 1);
        border.Value = (decimal)graph.OutlineWidth;
        border.ValueChanged += (_, _) =>
        {
            graph.OutlineWidth = (float)(border.Value ?? 1);
            _changed();
        };

        switch (graph)
        {
            case BarWidget bar:
                var radius = Forms.Number(0, 100, 1, 1);
                radius.Value = (decimal)bar.Radius;
                radius.ValueChanged += (_, _) =>
                {
                    bar.Radius = (float)(radius.Value ?? 0);
                    _changed();
                };
                shape.Fields.AddPair(Loc.L("props.border"), border, Loc.L("props.corners"), radius);

                shape.Fields.Add(Loc.L("props.grows"), Forms.Enum(bar.Direction, Labels.BarDirection, chosen =>
                {
                    bar.Direction = chosen;
                    _changed();
                }));
                break;

            case GaugeWidget gauge:
                var thickness = Forms.Number(1, 100, 1, 1);
                thickness.Value = (decimal)gauge.Thickness;
                thickness.ValueChanged += (_, _) =>
                {
                    gauge.Thickness = (float)(thickness.Value ?? 8);
                    _changed();
                };
                shape.Fields.AddPair(Loc.L("props.thickness"), thickness, Loc.L("props.border"), border);

                var start = Forms.Number(0, 360, 5, 0);
                var sweep = Forms.Number(5, 360, 5, 0);
                start.Value = (decimal)gauge.StartAngle;
                sweep.Value = (decimal)gauge.SweepAngle;
                start.ValueChanged += (_, _) =>
                {
                    gauge.StartAngle = (float)(start.Value ?? 135);
                    _changed();
                };
                sweep.ValueChanged += (_, _) =>
                {
                    gauge.SweepAngle = (float)(sweep.Value ?? 270);
                    _changed();
                };
                shape.Fields.AddPair(Loc.L("props.start"), start, Loc.L("props.sweep"), sweep);

                var round = Check(Loc.L("props.round"), gauge.Round);
                round.IsCheckedChanged += (_, _) =>
                {
                    gauge.Round = round.IsChecked == true;
                    _changed();
                };
                shape.Fields.AddWide(round);

                BuildGaugeLabel(gauge);
                break;

            case ChartWidget chart:
                var line = Forms.Number(1, 20, 1, 1);
                line.Value = (decimal)chart.Thickness;
                line.ValueChanged += (_, _) =>
                {
                    chart.Thickness = (float)(line.Value ?? 2);
                    _changed();
                };
                shape.Fields.AddPair(Loc.L("props.thickness"), line, Loc.L("props.border"), border);

                var samples = Forms.Number(LayoutLoader.MinChartSamples, 1000, 10);
                samples.Value = chart.Samples;
                samples.ValueChanged += (_, _) =>
                {
                    chart.Samples = (int)(samples.Value ?? 60);
                    _changed();
                };
                shape.Fields.Add(Loc.L("props.samples"), samples);

                shape.Fields.Add(Loc.L("props.fill"), ColorField(chart.Fill, color =>
                {
                    chart.Fill = color;
                    _changed();
                }, allowNone: true));
                break;
        }

        BuildLevels(
            graph.Levels,
            () => TextComposer.Unit(graph.Unit, KindOf(graph.Sensor)).Symbol(),
            () => (graph.Min, graph.Max),
            levels => graph.Levels = levels);
    }

    // ---- el valor escrito dentro del arco ----

    /// <summary>
    /// El valor que va en el hueco del arco. No pide sensor ni unidad: usa los del arco, que es lo
    /// que lo distingue de poner un texto encima y cuadrarlo a mano.
    /// </summary>
    private void BuildGaugeLabel(GaugeWidget gauge)
    {
        var section = Section(Loc.L("props.inside"));

        // La sección se construye una vez, así que el borrador vive aunque la casilla esté
        // desmarcada: quitar el valor y volver a ponerlo no pierde lo que el usuario había elegido.
        var inside = gauge.Label ?? new GaugeLabel();
        var fields = new FormGrid(96) { IsEnabled = gauge.Label is not null };

        void ShowSummary() =>
            section.Summary = gauge.Label is null
                ? Loc.L("props.inside.none")
                : Loc.L("props.inside.summary", inside.Decimals, inside.Size);

        var show = Check(Loc.L("props.inside.show"), gauge.Label is not null);
        show.IsCheckedChanged += (_, _) =>
        {
            bool on = show.IsChecked == true;
            gauge.Label = on ? inside : null;
            fields.IsEnabled = on;
            ShowSummary();
            _changed();
        };
        section.Fields.AddWide(show);

        var decimals = Forms.Number(0, TextComposer.MaxDecimals);
        decimals.Value = inside.Decimals;
        var size = Forms.Number(4, 200, 1, 1);
        size.Value = (decimal)inside.Size;
        decimals.ValueChanged += (_, _) =>
        {
            inside.Decimals = (int)(decimals.Value ?? 0);
            ShowSummary();
            _changed();
        };
        size.ValueChanged += (_, _) =>
        {
            inside.Size = (float)(size.Value ?? 20);
            ShowSummary();
            _changed();
        };
        fields.AddPair(Loc.L("props.decimals"), decimals, Loc.L("props.size"), size);

        var unit = Check(Loc.L("props.unit.show"), inside.ShowUnit);
        unit.IsCheckedChanged += (_, _) =>
        {
            inside.ShowUnit = unit.IsChecked == true;
            _changed();
        };
        fields.AddWide(unit);

        fields.Add(Loc.L("props.unit.placement"), Forms.Enum(inside.UnitPlacement, Labels.UnitPlacement, chosen =>
        {
            inside.UnitPlacement = chosen;
            _changed();
        }));

        var space = Check(Loc.L("props.unit.space"), inside.UnitSpace);
        space.IsCheckedChanged += (_, _) =>
        {
            inside.UnitSpace = space.IsChecked == true;
            _changed();
        };
        fields.AddWide(space);

        var prefix = new TextBox { Text = inside.Prefix ?? "" };
        WhenTyped(prefix, () => inside.Prefix ?? "", now =>
        {
            inside.Prefix = Empty(now);
            _changed();
        });
        var suffix = new TextBox { Text = inside.Suffix ?? "" };
        WhenTyped(suffix, () => inside.Suffix ?? "", now =>
        {
            inside.Suffix = Empty(now);
            _changed();
        });
        fields.AddPair(Loc.L("props.prefix"), prefix, Loc.L("props.suffix"), suffix);

        fields.Add(Loc.L("props.font"), FontField(inside.Font, family =>
        {
            inside.Font = family;
            _changed();
        }));

        var bold = Check(Loc.L("props.bold"), inside.Bold);
        var italic = Check(Loc.L("props.italic"), inside.Italic);
        bold.IsCheckedChanged += (_, _) =>
        {
            inside.Bold = bold.IsChecked == true;
            _changed();
        };
        italic.IsCheckedChanged += (_, _) =>
        {
            inside.Italic = italic.IsChecked == true;
            _changed();
        };
        fields.AddWide(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, Children = { bold, italic } });

        // Sin color propio hereda el del arco, que es lo que se quiere casi siempre.
        fields.Add(Loc.L("props.color"), ColorField(inside.Color, color =>
        {
            inside.Color = color;
            _changed();
        }, allowNone: true));

        section.Fields.AddWide(fields);
        ShowSummary();
    }

    // ---- el color según el valor ----

    /// <summary>
    /// De qué color va el widget según lo que marque su sensor: tres tramos y dos cortes.
    /// </summary>
    /// <remarks>
    /// Los tres colores y los dos cortes son del usuario, porque 40 y 80 grados pueden estar bien
    /// para una CPU y no para un disco: aquí no hay ningún umbral del programa. Lo único que pone
    /// VibeScreen es el punto de partida del botón, y está para retocarlo.
    /// </remarks>
    /// <param name="current">La regla que ya tiene el widget, o null si no tiene.</param>
    /// <param name="symbol">El símbolo de la unidad del widget, ahora mismo.</param>
    /// <param name="range">El recorrido del widget, ahora mismo: de él sale el preset.</param>
    /// <param name="set">Dónde se guarda la regla, o null al apagarla.</param>
    private void BuildLevels(
        ColorLevels? current, Func<string> symbol, Func<(float Min, float Max)> range, Action<ColorLevels?> set)
    {
        var section = Section(Loc.L("props.levels"));

        // El borrador vive aunque la casilla esté desmarcada, igual que el valor de dentro del arco:
        // apagar la regla y volver a encenderla no pierde ni los cortes ni los colores.
        var levels = current ?? Preset();
        var fields = new FormGrid(96) { IsEnabled = current is not null };
        bool on = current is not null;
        bool quiet = false;

        var medium = Forms.Number(-1_000_000, 1_000_000, 1, 2);
        var high = Forms.Number(-1_000_000, 1_000_000, 1, 2);
        // Con dos renglones y no con puntos suspensivos: un sensor de caudal llega a los millones de
        // B/s, y «menos de 5000000 B/s» cortado no dice nada. Lo que se mueve es la fila, no el resto
        // del formulario, porque debajo solo queda el botón.
        var ranges = new[] { Forms.Value(), Forms.Value(), Forms.Value() };

        ColorLevels Preset()
        {
            var (min, max) = range();
            return ValueColors.Traffic(min, max);
        }

        void Show()
        {
            string unit = symbol();
            for (int band = 0; band < ranges.Length; band++)
                Loc.Set(ranges[band], TextBlock.TextProperty, Labels.Range(levels, band, unit));

            // Con los cortes cruzados no hay tres tramos: hay dos y un hueco. Decirlo mientras se
            // teclea vale más que dejarlo salir al guardar, cuando ya no se sabe qué casilla fue.
            bool crossed = levels.MediumFrom >= levels.HighFrom;
            if (crossed) Loc.Set(ranges[1], TextBlock.TextProperty, Loc.L("props.levels.empty-band"));
            ranges[1].Foreground = crossed ? Palette.WarnBrush : Palette.TextBrush;

            section.Summary = on ? Labels.Range(levels, 1, unit) : Loc.L("props.levels.none");
        }

        void Touched()
        {
            if (quiet) return;

            Show();
            _changed();
        }

        medium.Value = (decimal)levels.MediumFrom;
        high.Value = (decimal)levels.HighFrom;
        medium.ValueChanged += (_, _) =>
        {
            levels.MediumFrom = (float)(medium.Value ?? 0);
            Touched();
        };
        high.ValueChanged += (_, _) =>
        {
            levels.HighFrom = (float)(high.Value ?? 0);
            Touched();
        };
        // Una por fila y no las dos juntas: un corte puede ser 12.500.000 B/s, y en media fila el
        // número se corta justo cuando más falta hace leerlo entero.
        fields.Add(Loc.L("props.levels.medium-from"), medium);
        fields.Add(Loc.L("props.levels.high-from"), high);

        // Más estrechos que los demás selectores: al lado va el recorrido, que es más largo que
        // cualquier otra cosa del formulario y no puede salir cortado.
        var low = Picker(levels.Low, hex => { levels.Low = hex; Touched(); }, width: 68);
        var middle = Picker(levels.Medium, hex => { levels.Medium = hex; Touched(); }, width: 68);
        var top = Picker(levels.High, hex => { levels.High = hex; Touched(); }, width: 68);

        fields.Add(Loc.L("props.levels.low"), Band(low, ranges[0]));
        fields.Add(Loc.L("props.levels.medium"), Band(middle, ranges[1]));
        fields.Add(Loc.L("props.levels.high"), Band(top, ranges[2]));

        fields.AddWide(Forms.Action(Loc.L("props.levels.traffic"), () =>
        {
            // El recorrido se lee ahora y no al construir el formulario: el mínimo y el máximo están
            // en la sección de al lado y lo normal es tocarlos antes de llegar aquí.
            var fresh = Preset();
            quiet = true;
            try
            {
                medium.Value = (decimal)fresh.MediumFrom;
                high.Value = (decimal)fresh.HighFrom;
                low.Color = Parse(fresh.Low) ?? low.Color;
                middle.Color = Parse(fresh.Medium) ?? middle.Color;
                top.Color = Parse(fresh.High) ?? top.Color;
            }
            finally
            {
                quiet = false;
            }

            Touched();
        }));

        fields.AddWide(Forms.Label(Loc.L("props.levels.help")));

        var show = Check(Loc.L("props.levels.show"), on);
        show.IsCheckedChanged += (_, _) =>
        {
            on = show.IsChecked == true;
            set(on ? levels : null);
            fields.IsEnabled = on;
            Show();
            _changed();
        };

        section.Fields.AddWide(show);
        section.Fields.AddWide(fields);
        Show();

        // Los recorridos llevan la unidad escrita, así que cambiarla —o cambiar de sensor con la
        // unidad en automático— los deja mintiendo hasta que alguien los reescribe.
        _showRanges = Show;

        static Control Band(Control picker, TextBlock text)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 8 };
            Grid.SetColumn(text, 1);
            row.Children.Add(picker);
            row.Children.Add(text);
            return row;
        }
    }

    /// <summary>
    /// El recorrido con el que se estrena la regla de un texto, que no tiene mínimo ni máximo
    /// propios: de cero al techo de su magnitud, los dos en la unidad que muestra el widget.
    /// </summary>
    private (float Min, float Max) TextRange(TextWidget text)
    {
        if (KindOf(text.Sensor) is not { } kind) return (0, 100);

        var native = kind.NativeUnit();
        var unit = TextComposer.Unit(text.Unit, kind);
        return ((float)SensorUnits.Convert(0, native, unit), (float)SensorUnits.Convert(kind.Ceiling(), native, unit));
    }

    // ---- campos ----

    /// <summary>La magnitud del sensor, o null si esta máquina no lo tiene y no hay forma de saberlo.</summary>
    private SensorKind? KindOf(string? sensor) =>
        sensor is not null && _sensors.Read(sensor) is { } reading ? reading.Kind : null;

    private Control SensorField(string current, Action<string> set)
    {
        var box = new AutoCompleteBox
        {
            FilterMode = AutoCompleteFilterMode.Contains,
            MinimumPrefixLength = 0,
            ItemsSource = _sensors.ReadAll().Select(s => s.Id).ToList(),
            Text = current,
        };

        // El campo tiene que recordar lo que hay ahora, no lo que había al construirlo: si no,
        // después de elegir un sensor en la lista, borrar el texto y salir reponía el de antes en la
        // casilla mientras el widget seguía con el nuevo. Tres sitios diciendo cosas distintas.
        void Apply(string id)
        {
            current = id;
            set(id);
        }

        // Solo se acepta un identificador que exista: cambiarlo a medio escribir dejaría el widget
        // apuntando a «cpu/0/temp» durante un instante y el aviso saltaría sin motivo.
        box.LostFocus += (_, _) =>
        {
            string id = (box.Text ?? "").Trim();
            if (id == current || _sensors.Read(id) is null) { box.Text = current; return; }
            Apply(id);
        };
        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedItem is string id && id != current) Apply(id);
        };
        return box;
    }

    /// <summary>
    /// Las unidades entre las que se puede elegir para ese sensor, con la nativa la primera. Siempre
    /// está «la del sensor»: es lo que vale cuando el layout no dice nada, y sin ella el desplegable
    /// aparecería vacío en los temas que no la fijan.
    /// </summary>
    private static Control UnitField(SensorUnit current, SensorKind? kind, Action<SensorUnit> set)
    {
        var options = new List<SensorUnit> { SensorUnit.Auto };
        options.AddRange(kind is { } known ? SensorUnits.For(known) : SensorUnits.CompatibleWith(current));
        if (current != SensorUnit.Auto && !options.Contains(current)) options.Add(current);

        Text Label(SensorUnit unit) => unit switch
        {
            SensorUnit.Auto when kind is { } k => Loc.L("props.unit.sensor-of", k.NativeUnit().Symbol()),
            SensorUnit.Auto => Loc.L("props.unit.sensor"),
            _ when unit.Symbol().Length == 0 => Loc.L("props.unit.none"),
            _ => Loc.Raw($"{unit.Symbol()}   ({unit.ToString().ToLowerInvariant()})"),
        };

        var box = Forms.Labeled(new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch });
        foreach (var unit in options) box.Items.Add(new UnitOption(unit, Label(unit)));
        box.SelectedItem = box.Items.OfType<UnitOption>().FirstOrDefault(o => o.Unit == current);

        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedItem is UnitOption option) set(option.Unit);
        };
        return box;
    }

    private static Control FontField(string? current, Action<string?> set)
    {
        // Las familias se piden a Skia y no al sistema de fuentes de Avalonia: Skia es quien va a
        // pintar el panel, así que es la lista que de verdad se puede usar. Y se piden a AppFonts,
        // que la guarda: este desplegable se construye en cada clic del lienzo.
        var box = Forms.Labeled(new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, MaxDropDownHeight = 320 });
        box.Items.Add(SystemFont);
        foreach (string family in AppFonts.Installed) box.Items.Add(family);

        box.SelectedItem = (object?)current ?? SystemFont;
        box.SelectionChanged += (_, _) => set(box.SelectedItem as string);
        return box;
    }

    /// <summary>
    /// La primera entrada del desplegable de letra, que no es una familia: es dejar la del sistema.
    /// </summary>
    /// <remarks>
    /// Un objeto y no una cadena, por lo mismo que el azar en la lista de temas: una familia puede
    /// llamarse como sea, y lo que dice esta entrada cambia con el idioma.
    /// </remarks>
    private sealed record SystemDefault : ILabeled
    {
        public Text Label => Loc.L("props.font.system");

        public override string ToString() => Label.ToString();
    }

    private static readonly SystemDefault SystemFont = new();

    /// <summary>Una casilla con su texto en el idioma de la ventana.</summary>
    private static CheckBox Check(Text label, bool on) =>
        Loc.Set(new CheckBox { IsChecked = on }, ContentControl.ContentProperty, label);

    /// <summary>El selector a secas, para cuando hace falta quedarse con la referencia.</summary>
    private static ColorPicker Picker(string? current, Action<string> set, double width = 108)
    {
        var picker = new ColorPicker
        {
            Color = Parse(current) ?? Colors.Black,
            Width = width,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        picker.ColorChanged += (_, e) => set(Hex(e.NewColor));
        return picker;
    }

    /// <summary>Un botón con el color, que abre el selector. Con <paramref name="allowNone"/>, se puede dejar sin color.</summary>
    public static Control ColorField(string? current, Action<string?> set, bool allowNone)
    {
        var picker = Picker(current, hex => set(hex));

        if (!allowNone) return picker;

        var none = Check(Loc.L("props.color.none"), current is null);
        picker.IsEnabled = current is not null;
        none.IsCheckedChanged += (_, _) =>
        {
            bool empty = none.IsChecked == true;
            picker.IsEnabled = !empty;
            set(empty ? null : Hex(picker.Color));
        };

        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { picker, none } };
    }

    private static Color? Parse(string? hex) =>
        hex is not null && SKColor.TryParse(hex, out var color) ? Color.FromRgb(color.Red, color.Green, color.Blue) : null;

    private static string Hex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    private static string? Empty(string? text) => string.IsNullOrEmpty(text) ? null : text;

    /// <summary>
    /// Escucha lo que se teclea en una casilla, sin creerse el primer aviso.
    /// </summary>
    /// <remarks>
    /// Avalonia dispara <c>TextChanged</c> también al enganchar la casilla en el árbol, con el mismo
    /// texto que se le acaba de poner y sin que nadie haya escrito nada. Como estos manejadores
    /// marcan el layout como modificado, bastaba seleccionar un widget de texto para que un tema
    /// recién abierto dijera «sin guardar» y para que «Nuevo» preguntara si descartar unos cambios
    /// que no existían. Se compara con lo que ya había: si dice lo mismo, no ha pasado nada.
    /// </remarks>
    private static void WhenTyped(TextBox box, Func<string> current, Action<string> apply)
    {
        box.TextChanged += (_, _) =>
        {
            string now = box.Text ?? "";
            if (now == current()) return;

            apply(now);
        };
    }
}
