# ClearView design

Inherit the companion ClaerPlayer application's quiet, dark viewing interface: charcoal chrome, off-white labels, mint active controls, native Korean typography. Viewing is the main task; the image gets the canvas and controls sit in a compact bottom bar. Native window chrome is retained outside fullscreen.

Header: filename and dimensions. Footer: previous/next, counter, zoom, fit/100%, rotate, fullscreen, and a secondary menu. Fullscreen hides both bars after idle and restores them on pointer movement. Empty, loading, error, and detailed-loading messages remain readable without blocking interaction. No decorative animation or idle polling loop.

Use vector path icons, visible keyboard focus, accessible names and tooltips. Keep original aspect ratio. User changes to rotation or zoom are view-only. Default dark background makes photograph edges discernible while avoiding glare.
