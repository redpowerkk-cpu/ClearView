# Performance observations

These are development measurements from the validation Windows x64 machine, not guaranteed limits or a comparison with other viewers. Fixtures were generated locally immediately before testing; filesystem caches were not flushed. All images are synthetic, not photographs from the user. Elapsed times include different portions of the pipeline as labeled below. No cold-disk benchmark is claimed.

## First validation run

| Fixture | Source size | File size | 2048px preview decode |
| --- | --- | --- | --- |
| JPEG | 6000×4000 (24 MP) | 18.0 MiB | 274 ms |
| PNG | 6000×4000 (24 MP) | 66.2 MiB | 176 ms |
| TIFF, uncompressed | 6000×4000 (24 MP) | 68.7 MiB | 46 ms |
| BMP, uncompressed | 12000×8000 (96 MP) | 274.7 MiB | 80 ms |

The UI test observed first pixels from the 24 MP JPEG in about 312 ms and its screen preview in 412 ms. The 96 MP BMP screen preview completed in about 161 ms. A 1200×800 detail region from the 96 MP BMP materialized in about 6 ms and occupied approximately 3.7 MiB of conservatively estimated pixel memory. These timings are affected by OS caching and image content.

The UI test process working set was about 96 MiB after startup and an internal screenshot, and 148 MiB after the image navigation/detail tests. These observations include .NET/WPF and rendering allocations and are not maximum memory usage. The retained preview cache was about 15.5 MiB at the end of that test, below its 128 MiB budget.

## Architecture and limits

- A single background decoder limits simultaneous expensive decodes. UI input and painting remain on the dispatcher thread.
- Newer queued navigation/detail requests supersede old ones. A codec call already underway completes, then stale results are ignored.
- Frozen, materialized images release input streams before publication. Opening an image does not hold an exclusive file lock.
- Previews use `BitmapImage.DecodePixelWidth`/`DecodePixelHeight`. Actual codec behavior varies by format.
- Zoom uses an orientation-corrected viewport crop. Output detail buffers are limited to 16 million pixels; very large source headers over 600 million pixels are rejected.
- The 128 MiB LRU budget covers retained preview pixels only; it does not cap native decoder scratch memory, current display images, GPU memory or the whole process.
- Embedded ICC color contexts are converted to sRGB when supported. HDR output and monitor-profile calibration have not been validated.
- The first version caps a folder listing at 30,000 supported filenames and displays only frame/page 1 of animated/multipage formats.

Microsoft references: [decode-size memory behavior](https://learn.microsoft.com/en-us/dotnet/api/system.windows.media.imaging.bitmapimage.decodepixelwidth), [WPF imaging](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/graphics-multimedia/imaging-overview), [WIC overview](https://learn.microsoft.com/en-us/windows/win32/wic/-wic-about-windows-imaging-codec).
