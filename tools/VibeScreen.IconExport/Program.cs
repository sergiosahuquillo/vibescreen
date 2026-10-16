using SkiaSharp;
using VibeScreen.App;

// El icono de los ficheros: el del ejecutable de la ventana, el del acceso directo y el del
// instalador. Lo usa packaging/windows/build.ps1 antes de publicar.
//
// En marcha, la ventana y la bandeja dibujan su icono con el realce elegido. Un fichero no puede
// hacer eso, así que se queda con el de fábrica en su tono oscuro, y por eso se genera aquí, con el
// mismo código, en vez de guardar un binario en el repositorio que se quedaría atrás al primer cambio
// de la marca.
//
//   dotnet run --project tools/VibeScreen.IconExport -- <carpeta>
//
// Deja en la carpeta:
//   vibescreen.ico             todos los tamaños, para el ejecutable y el instalador;
//   wizard-small-<n>.png       la marca sola, para la esquina del asistente a cada escala;
//   wizard-<n>.png             la marca centrada en el hueco vertical de la bienvenida.

if (args.Length != 1)
{
    Console.Error.WriteLine("Uso: VibeScreen.IconExport <carpeta>");
    return 2;
}

var folder = Directory.CreateDirectory(args[0]).FullName;
var color = SKColor.Parse(Palette.DefaultAccentOnDark);

File.WriteAllBytes(Path.Combine(folder, "vibescreen.ico"), AppIcon.Ico(color));

// Los tamaños que Inno Setup pide para cada escala: 55x55 y 164x314 al 100 %, y sus múltiplos hasta
// el 200 %. Con varios ficheros, el asistente escoge el que toca para el DPI de la pantalla en vez de
// estirar uno.
foreach (var (scale, suffix) in new[] { (1.0, "100"), (1.25, "125"), (1.5, "150"), (1.75, "175"), (2.0, "200") })
{
    var small = (int)Math.Round(55 * scale);
    File.WriteAllBytes(Path.Combine(folder, $"wizard-small-{suffix}.png"), AppIcon.Png(small, color));

    var width = (int)Math.Round(164 * scale);
    var height = (int)Math.Round(314 * scale);
    File.WriteAllBytes(Path.Combine(folder, $"wizard-{suffix}.png"), Centered(width, height, (int)Math.Round(112 * scale)));
}

Console.WriteLine($"Icono en {folder}");
return 0;

// La marca a un tamaño, en medio de un lienzo transparente más grande.
byte[] Centered(int width, int height, int mark)
{
    using var markImage = SKImage.FromEncodedData(AppIcon.Png(mark, color));
    using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
    surface.Canvas.Clear(SKColors.Transparent);
    surface.Canvas.DrawImage(markImage, (width - mark) / 2f, (height - mark) / 2f);

    using var image = surface.Snapshot();
    using var data = image.Encode(SKEncodedImageFormat.Png, 100);
    return data.ToArray();
}
