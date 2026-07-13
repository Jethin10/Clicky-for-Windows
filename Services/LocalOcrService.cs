using PDFtoImage;
using SkiaSharp;
using TesseractOCR;
using TesseractOCR.Enums;
using PixImage = TesseractOCR.Pix.Image;

namespace Clicky.Windows.Services;

public sealed class LocalOcrService : IDisposable
{
    public const int MaximumOcrPages = 50;
    public const int RasterDpi = 200;
    private readonly Engine _engine;

    public LocalOcrService()
    {
        var dataPath = Path.Combine(AppContext.BaseDirectory, "Assets", "tessdata");
        if (!File.Exists(Path.Combine(dataPath, "eng.traineddata")))
        {
            throw new InvalidOperationException("Clicky's local English OCR data is missing. Reinstall the application.");
        }

        _engine = new Engine(dataPath, Language.English, EngineMode.LstmOnly);
    }

    public string RecognizeImage(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var image = PixImage.LoadFromFile(path);
        using var page = _engine.Process(image, PageSegMode.Auto);
        cancellationToken.ThrowIfCancellationRequested();
        return page.Text.Trim();
    }

    public string RecognizePdfPage(string path, int zeroBasedPage, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var pdf = File.OpenRead(path);
        using var bitmap = Conversion.ToImage(
            pdf,
            new Index(zeroBasedPage),
            leaveOpen: false,
            options: new RenderOptions(Dpi: RasterDpi, Grayscale: true));
        using var encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        using var image = PixImage.LoadFromMemory(encoded.ToArray());
        using var page = _engine.Process(image, PageSegMode.Auto);
        cancellationToken.ThrowIfCancellationRequested();
        return page.Text.Trim();
    }

    public void Dispose() => _engine.Dispose();
}
