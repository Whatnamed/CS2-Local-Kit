# Controller background UI probe

Run from the task worktree with a .NET 10 Windows SDK:

```powershell
dotnet run --project tests/CS2LocalKit.UiProbe -c Release -- <read-only-catalog-cache> <optional-read-only-image-cache>
```

Without arguments the small committed catalog fixture is used; use a full pinned catalog cache for visual evidence. No personal preset is consumed. The optional image cache is read only; validated cached image paths are served through an injected offline HttpMessageHandler, then written only to the sandbox. No network request is made by this carrier.

All service paths, Bot data root and game probes are explicitly synthetic. Output is `temp/ui-probe/`, with a fresh `sandbox-<guid>` each run. `--repo=<absolute-worktree>` supports running the probe from a separate copy of the published payload. `--quick` omits the additional match width/DPI matrix during development.

The carrier does not call Window.Show, Activate, SetForegroundWindow, SendInput, Clipboard or Explorer. A 1x1 WS_POPUP HWND at (-30000,-30000), with WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW and **without WS_VISIBLE**, hosts real WPF Loaded events; it is checked as invisible before/after. Main views are measured/arranged and captured with RenderTargetBitmap. Additional dialog/loading views are rendered directly without showing them. Owned windows/HWND are closed/disposed on completion; the process exits on failure.

Value, SelectionItem, Invoke, RangeValue and ExpandCollapse providers are exercised in process. List selection uses the actual realised item to respect WPF Selector identity. This is not a claim of cross-process desktop UIA or real mouse/keyboard coverage. Native ComboBox popups, file dialogs, OS chrome, modal activation and physical per-monitor DPI/focus remain manual checks.

DPI captures explicitly set WPF root DPI and encode matching pixel dimensions. The report records the hidden source's native DPI before overrides. 1000x610 additionally bounds the client area of a 1024x660 window with native chrome. Alpha coverage and non-flat pixels are checked to reject blank/transparent captures; actual screenshots still require visual inspection.

The suite exercises save/queue/activation and confirmation cancellation only against synthetic data. It never invokes Bot install/restore; disabled gates are checked. Existing Core tests cover the actual install/apply/restore semantics in their own fixtures.
