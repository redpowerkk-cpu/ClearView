# ClearView

<!-- impeccable:product-schema 1 -->

## Platform
Windows 10/11 x64 desktop, .NET Framework 4.8.

## Purpose
The user wants a lightweight installable photo viewer optimized for large files and high-resolution images, with responsive navigation and fullscreen. ClearView is the user-provided name. The GitHub repository must be public.

## Implementation
C# WPF and the Windows Imaging Component; no browser runtime or bundled codec framework. Progressive screen-sized previews, one background decode worker, newest-request priority, neighbor prefetch, byte-budgeted cache, and viewport-only detail rendering on zoom. Decode work already inside a Windows codec cannot be interrupted, but stale results must never replace the current image.

## Supported scope
JPEG, PNG, BMP, TIFF, GIF first frame, ICO, and installed WIC codecs. HEIC/WebP/RAW availability depends on the Windows codec installation and is not claimed universally. Photo originals are read-only. Display rotation does not rewrite files.

## Validation
Generated high-resolution/high-file-size images, timings, cache budget, stale result suppression, exclusive file reopening, EXIF orientation, detail regions, navigation, zoom, rotation, and fullscreen. No universal zero-latency claim or monitor calibration guarantee.
