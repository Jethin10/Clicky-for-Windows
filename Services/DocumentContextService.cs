using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace Clicky.Windows.Services;

public sealed class DocumentContextService
{
    public const long MaximumFileBytes = 25 * 1024 * 1024;
    public const int MaximumExtractedCharacters = 120_000;
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".json", ".csv", ".log", ".cs", ".xaml", ".xml", ".html", ".css", ".js", ".ts", ".py"
    };
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".bmp", ".gif", ".jpg", ".jpeg", ".png", ".tif", ".tiff", ".webp"
    };

    public async Task<DocumentContext> ExtractAsync(string path, CancellationToken cancellationToken)
    {
        var file = new FileInfo(path);
        if (!file.Exists)
        {
            throw new FileNotFoundException("The attached file no longer exists.", path);
        }
        if (file.Length > MaximumFileBytes)
        {
            throw new InvalidOperationException("Attachments are limited to 25 MB.");
        }

        var extension = file.Extension;
        return extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase)
            ? await Task.Run(() => ExtractPdf(file, cancellationToken), cancellationToken)
            : TextExtensions.Contains(extension)
                ? await ExtractTextAsync(file, cancellationToken)
                : ImageExtensions.Contains(extension)
                    ? await Task.Run(() => ExtractImage(file, cancellationToken), cancellationToken)
                    : throw new NotSupportedException("Attach a PDF, image, or supported text/code file.");
    }

    public static string AddToPrompt(string prompt, DocumentContext? document)
    {
        if (document is null)
        {
            return prompt;
        }

        return $"""
            {prompt}

            attached local document: {document.FileName}
            pages: {document.PageCount?.ToString() ?? "not applicable"}
            extracted locally: yes
            content{(document.WasTruncated ? " (truncated to the safe context limit)" : string.Empty)}:
            --- begin attached document ---
            {document.Text}
            --- end attached document ---
            """;
    }

    private static DocumentContext ExtractPdf(FileInfo file, CancellationToken cancellationToken)
    {
        string?[] pageTexts;
        bool[] pageWasOcr;
        int pageCount;
        using (var document = PdfDocument.Open(file.FullName))
        {
            pageCount = document.NumberOfPages;
            pageTexts = new string?[pageCount];
            pageWasOcr = new bool[pageCount];
            foreach (var page in document.GetPages())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var text = ContentOrderTextExtractor.GetText(page).Trim();
                pageTexts[page.Number - 1] = string.IsNullOrWhiteSpace(text) ? null : text;
            }
        }

        var pagesNeedingOcr = pageTexts.Count(text => string.IsNullOrWhiteSpace(text));
        if (pagesNeedingOcr > 0)
        {
            if (pagesNeedingOcr > LocalOcrService.MaximumOcrPages)
            {
                throw new InvalidOperationException($"Scanned PDFs are limited to {LocalOcrService.MaximumOcrPages} OCR pages per attachment.");
            }

            using var ocr = new LocalOcrService();
            for (var index = 0; index < pageTexts.Length; index++)
            {
                if (!string.IsNullOrWhiteSpace(pageTexts[index]))
                {
                    continue;
                }
                cancellationToken.ThrowIfCancellationRequested();
                var text = ocr.RecognizePdfPage(file.FullName, index, cancellationToken);
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }
                pageTexts[index] = text;
                pageWasOcr[index] = true;
            }
        }

        var builder = new StringBuilder();
        var truncated = false;
        for (var index = 0; index < pageTexts.Length; index++)
        {
            var text = pageTexts[index];
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }
            var provenance = pageWasOcr[index] ? ", local OCR" : string.Empty;
            if (!TryAppendBounded(builder, $"\n\n[page {index + 1}{provenance}]\n{text}", out truncated))
            {
                break;
            }
        }

        if (builder.Length == 0)
        {
            throw new InvalidOperationException("No readable text was found in this PDF after local OCR.");
        }

        return new DocumentContext(file.FullName, file.Name, builder.ToString().Trim(), pageCount, truncated, pageWasOcr.Any(value => value));
    }

    private static DocumentContext ExtractImage(FileInfo file, CancellationToken cancellationToken)
    {
        using var ocr = new LocalOcrService();
        var text = ocr.RecognizeImage(file.FullName, cancellationToken);
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException("No readable text was found in this image after local OCR.");
        }

        var truncated = text.Length > MaximumExtractedCharacters;
        if (truncated)
        {
            text = text[..MaximumExtractedCharacters];
        }
        return new DocumentContext(file.FullName, file.Name, text, 1, truncated, true);
    }

    private static async Task<DocumentContext> ExtractTextAsync(FileInfo file, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read, 16_384, useAsync: true);
        using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
        var buffer = new char[8_192];
        var builder = new StringBuilder();
        var truncated = false;
        while (true)
        {
            var count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (count == 0)
            {
                break;
            }

            if (!TryAppendBounded(builder, new string(buffer, 0, count), out truncated))
            {
                break;
            }
        }

        return new DocumentContext(file.FullName, file.Name, builder.ToString(), null, truncated);
    }

    private static bool TryAppendBounded(StringBuilder builder, string text, out bool truncated)
    {
        var remaining = MaximumExtractedCharacters - builder.Length;
        if (remaining <= 0)
        {
            truncated = true;
            return false;
        }

        if (text.Length > remaining)
        {
            builder.Append(text.AsSpan(0, remaining));
            truncated = true;
            return false;
        }

        builder.Append(text);
        truncated = false;
        return true;
    }
}

public sealed record DocumentContext(
    string FullPath,
    string FileName,
    string Text,
    int? PageCount,
    bool WasTruncated,
    bool UsedLocalOcr = false);
