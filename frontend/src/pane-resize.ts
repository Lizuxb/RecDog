/** Pane widths are UI preferences; no project content is stored here. */
export function setupPaneResize() {
  for (const [selector, key, property, minimum, maximum, direction] of [
    ['.sidebar', 'sidebarWidth', '--sidebar-width', 190, 420, 1],
    ['.outline-panel', 'outlineWidth', '--outline-width', 180, 380, -1],
  ] as const) {
    const pane = document.querySelector<HTMLElement>(selector)!;
    const handle = document.createElement('div');
    handle.className = 'pane-resizer'; handle.tabIndex = 0;
    handle.setAttribute('role', 'separator'); handle.setAttribute('aria-orientation', 'vertical');
    handle.setAttribute('aria-label', direction === 1 ? '调整左侧栏宽度' : '调整右侧栏宽度');
    pane.append(handle);
    const apply = (value: number) => {
      const width = Math.round(Math.max(minimum, Math.min(maximum, value)));
      document.documentElement.style.setProperty(property, `${width}px`);
      handle.setAttribute('aria-valuenow', String(width));
      try { localStorage.setItem(key, String(width)); } catch { /* Optional preference. */ }
    };
    try { const saved = Number(localStorage.getItem(key)); if (saved) apply(saved); } catch { /* Defaults remain available. */ }
    handle.onpointerdown = event => {
      if (event.button !== 0) return;
      event.preventDefault(); handle.setPointerCapture(event.pointerId);
      const origin = event.clientX, width = pane.getBoundingClientRect().width;
      document.body.classList.add('resizing-pane');
      handle.onpointermove = move => apply(width + direction * (move.clientX - origin));
      const end = () => { handle.onpointermove = null; document.body.classList.remove('resizing-pane'); };
      handle.onpointerup = end; handle.onpointercancel = end; handle.onlostpointercapture = end;
    };
    handle.onkeydown = event => {
      if (!['ArrowLeft', 'ArrowRight'].includes(event.key)) return;
      event.preventDefault(); apply(pane.getBoundingClientRect().width + (event.key === 'ArrowRight' ? 10 : -10) * direction);
    };
  }
}
