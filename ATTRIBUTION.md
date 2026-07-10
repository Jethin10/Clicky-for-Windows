# Attribution

This Windows reimplementation uses the public behavior and visual language of
[`farzaa/clicky`](https://github.com/farzaa/clicky), which is MIT licensed.

The original source's MIT license is included verbatim in
[`UPSTREAM_LICENSE.md`](./UPSTREAM_LICENSE.md). This project is independent and
is not affiliated with HeyClicky or Farza Majeed. The public upstream source was
used as the feature reference; proprietary HeyClicky features are not present in
that source and are not claimed here.

Local scanned-document support uses
[`TesseractOCR`](https://github.com/Sicos1977/TesseractOCR) and
[`Tesseract`](https://github.com/tesseract-ocr/tesseract) under Apache-2.0,
English model data from
[`tessdata_fast`](https://github.com/tesseract-ocr/tessdata_fast), and
[`PDFtoImage`](https://github.com/sungaila/PDFtoImage) under MIT. PDF rendering
is backed by PDFium and SkiaSharp. Their package licenses and notices remain in
the restored NuGet packages and published dependency metadata. The bundled
`eng.traineddata` asset has SHA-256
`7D4322BD2A7749724879683FC3912CB542F19906C83BCC1A52132556427170B2`.
