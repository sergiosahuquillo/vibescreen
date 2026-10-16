using VibeScreen.Contracts;

namespace VibeScreen.App.Localization;

/// <summary>
/// Un texto para el usuario, sin resolver todavía: una clave del catálogo con sus argumentos, o un
/// dato que no se traduce.
/// </summary>
/// <remarks>
/// <para>
/// Se resuelve en el idioma que haya <b>al pintarlo</b>, no al crearlo. Eso es lo que deja cambiar de
/// idioma en caliente: <see cref="Loc.Set{T}"/> lo enlaza a la propiedad del control y lo vuelve a
/// resolver cada vez que el idioma cambia, esté la vista a la vista o escondida.
/// </para>
/// <para>
/// Los argumentos pueden ser otros <see cref="Text"/>, que se resuelven a la vez —«A prueba con
/// {0}», con el nombre del modo también traducido—, o cifras, que salen con el formato del idioma.
/// </para>
/// </remarks>
public readonly struct Text : IEquatable<Text>
{
    private readonly string? _key;
    private readonly string? _raw;
    private readonly object?[]? _args;
    private readonly long _count;
    private readonly Func<string>? _lazy;
    private readonly Notice? _notice;
    private readonly Kind _kind;
    private readonly TextCase _case;

    private enum Kind
    {
        Raw,
        Key,
        Plural,
        Lazy,
        Notice,
    }

    private Text(Kind kind, string? key, string? raw, object?[]? args, long count, Func<string>? lazy, TextCase textCase,
                 Notice? notice = null)
    {
        _kind = kind;
        _notice = notice;
        _key = key;
        _raw = raw;
        _args = args;
        _count = count;
        _lazy = lazy;
        _case = textCase;
    }

    /// <summary>Lo vacío: ni clave ni texto.</summary>
    public static Text Empty => default;

    /// <summary>Un dato que no se traduce: un nombre, una ruta, una cifra ya escrita.</summary>
    public static Text Raw(string? value) => new(Kind.Raw, null, value, null, 0, null, TextCase.None);

    /// <summary>
    /// Un literal suelto. Solo existe para migrar sin romperlo todo de golpe: lo que no sea un dato
    /// tiene que llegar con <see cref="Loc.L"/>.
    /// </summary>
    [Obsolete("Un texto para el usuario va con Loc.L(\"clave\"), y un dato con Text.Raw(valor).", error: true)]
    public static implicit operator Text(string? value) => Raw(value);

    internal static Text FromKey(string key, object?[]? args) =>
        new(Kind.Key, key, null, args is { Length: > 0 } ? args : null, 0, null, TextCase.None);

    internal static Text FromPlural(string key, long count, object?[]? args) =>
        new(Kind.Plural, key, null, args is { Length: > 0 } ? args : null, count, null, TextCase.None);

    /// <summary>
    /// Un texto que se compone con código cada vez que se pinta.
    /// </summary>
    /// <remarks>
    /// Para lo que no cabe en una plantilla —una lista de problemas, una duración que cambia de forma
    /// según su tamaño—. Lo que lleve dentro tiene que salir de <see cref="Loc"/>, o no cambiará con
    /// el idioma.
    /// </remarks>
    public static Text Of(Func<string> compose)
    {
        ArgumentNullException.ThrowIfNull(compose);
        return new(Kind.Lazy, null, null, null, 0, compose, TextCase.None);
    }

    /// <summary>
    /// Un aviso del servicio o de Core, redactado en el idioma que haya al pintarlo.
    /// </summary>
    /// <remarks>
    /// Sus textos no están en el catálogo de la ventana sino en el del contrato, que comparten la CLI
    /// y el servicio: ver <see cref="Notices"/>.
    /// </remarks>
    public static Text From(Notice notice)
    {
        ArgumentNullException.ThrowIfNull(notice);
        return new(Kind.Notice, null, null, null, 0, null, TextCase.None, notice);
    }

    /// <summary>Si es un dato fijo y no hay nada que volver a resolver al cambiar de idioma.</summary>
    public bool IsFixed => _kind == Kind.Raw;

    /// <summary>Si no dice nada.</summary>
    public bool IsEmpty => _kind == Kind.Raw && string.IsNullOrEmpty(_raw);

    /// <summary>El mismo texto, en versalitas: los títulos de tarjeta y las etiquetas de las cifras.</summary>
    public Text Upper() => new(_kind, _key, _raw, _args, _count, _lazy, TextCase.Upper, _notice);

    /// <summary>El mismo texto, en minúsculas: cuando va en mitad de otra frase.</summary>
    public Text Lower() => new(_kind, _key, _raw, _args, _count, _lazy, TextCase.Lower, _notice);

    /// <summary>Lo que dice en el idioma de ahora.</summary>
    public override string ToString()
    {
        string text = _kind switch
        {
            Kind.Raw => _raw ?? "",
            Kind.Key => Loc.Format(Loc.Template(_key!), _args),
            Kind.Plural => Loc.Format(Loc.Template(_key + (_count == 1 ? ".one" : ".other")), [_count, .. _args ?? []]),
            Kind.Notice => Notices.Render(_notice!, Loc.Current),
            _ => _lazy!(),
        };

        return _case switch
        {
            TextCase.Upper => Loc.Culture.TextInfo.ToUpper(text),
            TextCase.Lower => Loc.Culture.TextInfo.ToLower(text),
            _ => text,
        };
    }

    /// <summary>
    /// Iguales si dirían lo mismo en cualquier idioma: así enlazar dos veces el mismo texto a una
    /// propiedad —lo que hace el sondeo cada segundo— no rehace el enlace.
    /// </summary>
    public bool Equals(Text other)
    {
        if (_kind != other._kind || _case != other._case || _count != other._count) return false;
        if (!string.Equals(_key, other._key, StringComparison.Ordinal)) return false;
        if (!string.Equals(_raw, other._raw, StringComparison.Ordinal)) return false;
        if (!ReferenceEquals(_lazy, other._lazy)) return false;
        if (!Equals(_notice, other._notice)) return false;

        var mine = _args ?? [];
        var theirs = other._args ?? [];
        if (mine.Length != theirs.Length) return false;
        for (int i = 0; i < mine.Length; i++)
            if (!Equals(mine[i], theirs[i])) return false;
        return true;
    }

    public override bool Equals(object? obj) => obj is Text other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(_kind, _key, _raw, _count, _case);

    public static bool operator ==(Text left, Text right) => left.Equals(right);

    public static bool operator !=(Text left, Text right) => !left.Equals(right);
}

/// <summary>Cómo se escribe un texto ya resuelto.</summary>
public enum TextCase
{
    None,
    Upper,
    Lower,
}
